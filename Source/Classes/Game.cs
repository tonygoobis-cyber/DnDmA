using Microsoft.AspNetCore.Mvc.Razor.Internal;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Veldrid.Sdl2;
using vmmsharp;
using Vortice.Direct3D11;
using static DMAW_DND.Enums;

namespace DMAW_DND
{
    public class Game
    {
        public Game()
        { }

        private static ulong _clientBase;
        private static ulong _matchmakingBase;
        private static ulong _engineBase;
        private static ulong _inputBase;

        private static ulong _replayInterface;
        private static ulong _world;
        public static ulong GWorld { get => _world; }
        private static ulong _owningGameInstance;
        private static ulong _localPlayer;
        private static ulong _localPlayerController; //FCameraCacheEntry

        public static string _currentMapName = "<empty>";
        public static bool _inGame = false;
        private static string? _lastActivityLoggedMap;

        public static Stopwatch playerUpdateTimer = new();
        private static Semaphore playerLock = new(1, 1);
        private static Dictionary<int, string> FNameCache = new Dictionary<int, string>();

        private Dictionary<int, Player> _players = new Dictionary<int, Player>();
        public ulong PlayerController
        {
            get => _localPlayerController;
        }

        public Dictionary<int, Player> Players
        {
            get => _players;
        }
        public ulong World
        {
            get => _world;
        }
        public string CurrentMapName
        {
            get => _currentMapName;
        }
        public bool InGame
        {
            get
            {
                return _inGame;
            }
        }
        //local player location
        public Vector3 LocalPlayerLocation
        {
            get
            {
                return _players.FirstOrDefault(p => p.Value.Type == PlayerType.LocalPlayer).Value.Location;
            }
        }

        public void MapReadLoop()
        {
            if (_world == 0)
                return;
            try
            {
                var MapNameIndex = Memory.ReadValue<uint>(_world + Offsets.NameIndex);
                _currentMapName = Memory.ReadName(MapNameIndex);
            }
            catch (Exception ex)
            {
                // Transient DMA failures must not tear down the whole game loop (clears radar).
                Program.Log($"MapReadLoop: map name read failed, skipping cycle: {ex.Message}");
                return;
            }
            if (!string.Equals(_currentMapName, _lastActivityLoggedMap, StringComparison.Ordinal))
            {
                ActivityLog.Info("Game", $"Map name changed: '{_lastActivityLoggedMap ?? "<none>"}' -> '{_currentMapName}'");
                _lastActivityLoggedMap = _currentMapName;
            }
            // Keep activity log compact; map transitions are already logged above.
            if (_currentMapName == "<empty>" || _currentMapName == "None" || _currentMapName == null)
            {
                _inGame = false;
                Memory.GameStatus = Enums.GameStatus.Menu;
                _players.Clear();
                EntityManager.Items.Clear();
                ActivityLog.Warn("Game", "MapReadLoop: empty map name — cleared players/items, GameStatus=Menu");
                return;
            }
            UpdatePlayersList();
        }

        public void GameLoop()
        {
            try
            {
                if (!EntityManager.Running)
                {
                    EntityManager.Start();
                }
                if (playerUpdateTimer.ElapsedMilliseconds > 5000)
                {
                    MapReadLoop();
                    playerUpdateTimer.Restart();
                }
                UpdatePlayerPositions();
            }
            catch (Exception ex)
            {
                ActivityLog.CriticalException("Game", ex, "GameLoop");
                _players.Clear();
                throw;
            }
        }

        public void UpdatePlayersList()
        {
            try
            {
                //Console.Clear();
                //Program.Log($"Game is on map: {_currentMapName}");
                playerLock.WaitOne();
                Dictionary<int, Player> newPlayers = new();
                int statsReadOkCount = 0;
                int statsReadFailCount = 0;
                int missingNameCount = 0;
                int missingClassCount = 0;

                var gameStateBase = Memory.ReadPtr(_world + Offsets.GameStateBase); //AGameStateBase
                if (gameStateBase == 0)
                    ActivityLog.Warn("Game", "UpdatePlayersList: GameStateBase is 0 — player list will be empty (offsets/world).");
                var playerArray = Memory.ReadValue<TArray>(gameStateBase + Offsets.PlayerArray); //TArray<APlayerState*>

                var playerScatterMap = new ScatterReadMap(playerArray.Count);
                var playerStateRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerPawnPrivateRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerNickNameCachedRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerRootComponentRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerControllerRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerAbilitySystemComponentRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerSpawnedAttributesComponentRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerSkeletonMeshComponentRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerCompToWorldRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerAccountDataReplicationRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerLocationRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerRotationRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerEquipmentRound = playerScatterMap.AddRound(Memory.PID, false);
                var playerEquipmentArrayRound = playerScatterMap.AddRound(Memory.PID, false);

                //traverse the player array
                for (int i = 0; i < playerArray.Count; i++)
                {
                    var playerStateRoundEntry = playerStateRound.AddEntry<ulong>(i, 0, playerArray.Data, typeof(ulong), (uint)(i * sizeof(ulong)));
                    var playerPawnPrivateRoundEntry = playerPawnPrivateRound.AddEntry<ulong>(i, 1, playerStateRoundEntry, typeof(ulong), Offsets.PlayerState.PlayerPawnPrivate);
                    playerNickNameCachedRound.AddEntry<FNickname>(i, 2, playerPawnPrivateRoundEntry, typeof(FNickname), Offsets.PawnPrivate.NickNameCached);
                    var playerRootComponentRoundEntry = playerRootComponentRound.AddEntry<ulong>(i, 3, playerPawnPrivateRoundEntry, typeof(ulong), Offsets.PawnPrivate.RootComponent);
                    playerControllerRound.AddEntry<ulong>(i, 4, playerPawnPrivateRoundEntry, typeof(ulong), Offsets.PawnPrivate.PlayerController);
                    var playerAbilitySystemComponentRoundEntry = playerAbilitySystemComponentRound.AddEntry<ulong>(i, 5, playerPawnPrivateRoundEntry, typeof(ulong), Offsets.PawnPrivate.AbilitySystemComponent);
                    playerSpawnedAttributesComponentRound.AddEntry<TArray>(i, 6, playerAbilitySystemComponentRoundEntry, typeof(TArray), Offsets.PawnPrivate.SpawnedAttributes);
                    var playerSkeletonMeshComponentRoundEntry = playerSkeletonMeshComponentRound.AddEntry<ulong>(i, 7, playerPawnPrivateRoundEntry, typeof(ulong), Offsets.PawnPrivate.SkeletalMeshComponent);
                    // Mesh ComponentToWorld for bones / Z height; root C2W is often stale for the local pawn.
                    var playerCompToWorldRoundEntry = playerCompToWorldRound.AddEntry<FTransform>(i, 8, playerSkeletonMeshComponentRoundEntry, typeof(FTransform), Offsets.ComponentToWorld);
                    playerAccountDataReplicationRound.AddEntry<FAccountDataReplication>(i, 9, playerPawnPrivateRoundEntry, typeof(FAccountDataReplication), Offsets.PawnPrivate.AccountDataReplication);
                    playerLocationRound.AddEntry<FVector3>(i, 10, playerRootComponentRoundEntry, typeof(FVector3), Offsets.RootComponent.Location);
                    playerRotationRound.AddEntry<FRotator>(i, 11, playerRootComponentRoundEntry, typeof(FRotator), Offsets.RootComponent.Rotation);
                    var playerEquipmentRoundEntry = playerEquipmentRound.AddEntry<ulong>(i, 12, playerPawnPrivateRoundEntry, typeof(ulong), Offsets.PawnPrivate.EquipmentInventory);
                    playerEquipmentArrayRound.AddEntry<TArray>(i, 13, playerEquipmentRoundEntry, typeof(TArray), 0x110);
                }


                playerScatterMap.Execute(Memory.Mem);
                for (int i = 0; i < playerArray.Count; i++)
                {
                    if (playerScatterMap.Results[i][0].TryGetResult<ulong>(out var playerState) &&
                    playerScatterMap.Results[i][1].TryGetResult<ulong>(out var playerPawnPrivate) &&
                    playerPawnPrivate != 0)
                    {
                        playerScatterMap.Results[i][2].TryGetResult<FNickname>(out var playerNickNameCached);
                        playerScatterMap.Results[i][3].TryGetResult<ulong>(out var playerRootComponent);
                        playerScatterMap.Results[i][4].TryGetResult<ulong>(out var playerController);
                        playerScatterMap.Results[i][5].TryGetResult<ulong>(out var playerAbilitySystemComponent);
                        playerScatterMap.Results[i][6].TryGetResult<TArray>(out var playerSpawnedAttributes);
                        playerScatterMap.Results[i][7].TryGetResult<ulong>(out var playerSkeletonMeshComponent);
                        FTransform playerCompToWorld = default;
                        if (!playerScatterMap.Results[i][8].TryGetResult<FTransform>(out playerCompToWorld) && playerRootComponent != 0)
                            playerCompToWorld = Memory.ReadValue<FTransform>(playerRootComponent + Offsets.ComponentToWorld);
                        playerScatterMap.Results[i][9].TryGetResult<FAccountDataReplication>(out var playerAccountDataReplication);
                        playerScatterMap.Results[i][10].TryGetResult<FVector3>(out var playerLocation);
                        playerScatterMap.Results[i][11].TryGetResult<FRotator>(out var playerRotation);
                        playerScatterMap.Results[i][12].TryGetResult<ulong>(out var playerEquipmentInventory);
                        playerScatterMap.Results[i][13].TryGetResult<TArray>(out var playerEquipmentItemActorsArray);
                        //Program.Log($"Player Name: {Memory.ReadFString(playerNickNameCached.OriginalNickName)} SkeletonMesh : 0x{playerSkeletonMeshComponent:X}");
                        //// Bone test content
                        var playerBoneArray = Memory.ReadPtr(playerSkeletonMeshComponent + 0x5F8);
                        var playerBoneArrayCount = Memory.ReadValue<uint>(playerSkeletonMeshComponent + 0x5F8 + 0x8);
                        //Program.Log($"First Bone Array : 0x{playerBoneArray:X}, Bone Count: {playerBoneArrayCount}");
                        if (playerBoneArray == 0 || playerBoneArrayCount == 0 || playerBoneArrayCount == 1)
                        {
                            playerBoneArray = Memory.ReadPtr(playerSkeletonMeshComponent + 0x608);
                            playerBoneArrayCount = Memory.ReadValue<uint>(playerSkeletonMeshComponent + 0x608 + 0x8);
                            //Program.Log($"First Bone Array Empty: Second Bone Array : 0x{playerBoneArray:X}, Bone Count: {playerBoneArrayCount}");
                            while(playerBoneArray == 0)
                            {
                                playerBoneArray = Memory.ReadPtr(playerSkeletonMeshComponent + 0x608);
                            }
                        }
                        //Program.Log($" Final Bone Array : 0x{playerBoneArray:X}, Bone Count: {playerBoneArrayCount}");
                        HashSet<int> requiredBoneIndices = new HashSet<int>(BoneConnections.SelectMany(connection => new[] { (int)connection.Item1, (int)connection.Item2 }));
                        Dictionary<int, Bone> playerBones = new Dictionary<int, Bone>();
                        foreach (int index in requiredBoneIndices)
                        {
                            if (index < playerBoneArrayCount) // Ensure the index is within the array bounds
                            {
                                ulong boneAddress = playerBoneArray + (ulong)(index * 0x60); // Calculate address of the specific bone
                                var transform = Memory.ReadValue<FTransform>(boneAddress); // Read the transformation
                                playerBones[index] = new Bone
                                {
                                    Address = boneAddress,
                                    Transform = transform
                                };
                            }
                        }
                        //for (int j = 0; j < playerBoneArrayCount; j++)
                        //{
                        //    var transform = Memory.ReadValue<FTransform>(playerBoneArray + (ulong)(j * 0x60));                           
                        //    Bone bone = new Bone()
                        //    {
                        //        Address = playerBoneArray + (ulong)(j * 0x60),
                        //        Transform = transform,
                        //    };
                        //    playerBones.Add(j, bone);
                        //}
                        //// Bone end test content
                        var weaponLabel = "";
                        for (int j = 0; j < playerEquipmentItemActorsArray.Count; j++)
                        {
                            try
                            {
                                var playerEquipmentItemActor = Memory.ReadPtr(playerEquipmentItemActorsArray.Data + (ulong)(j * 0x8));
                                if (playerEquipmentItemActor == 0)
                                    continue;
                                var ItemInfo = Memory.ReadValue<FDCItemInfo>(playerEquipmentItemActor + 0x338);
                                var DesignDataItem = Memory.ReadValue<FDesignDataItem>(playerEquipmentItemActor + 0x4B0);
                                weaponLabel += $"{Memory.ReadName((uint)ItemInfo.ItemData.ItemId.PrimaryAssetName.ComparisonIndex).Replace("Id_Item_", "")} - {Memory.ReadName((uint)DesignDataItem.RarityType.TagName.ComparisonIndex).Replace("Type.Item.Rarity.", "")} ";
                            }
                            catch
                            {
                                // Bad pointer or stale FDCItemInfo / FDesignDataItem layout for this slot
                            }
                        }
                        // Read player stats defensively so one stale ASC pointer does not drop all visible player info.
                        var playerStats = default(FGamePlayAttributeDataSet);
                        bool statsReadOk = false;
                        ulong playerSpawnedAttributesData = 0;
                        try
                        {
                            if (playerSpawnedAttributes.Data != 0)
                            {
                                playerSpawnedAttributesData = Memory.ReadPtr(playerSpawnedAttributes.Data);
                                if (playerSpawnedAttributesData != 0)
                                {
                                    playerStats = Memory.ReadValue<FGamePlayAttributeDataSet>(playerSpawnedAttributesData);
                                    statsReadOk = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ActivityLog.Debug("Game", $"Stats read failed for player idx={i} pawn=0x{playerPawnPrivate:X} attrs=0x{playerSpawnedAttributesData:X}: {ex.Message}");
                        }
                        if (statsReadOk) statsReadOkCount++; else statsReadFailCount++;
                        _ = uint.TryParse(Memory.ReadFString(playerAccountDataReplication.PartyId), out uint partyIDInt) ? partyIDInt : 0;

                        // Radar position: USceneComponent::RelativeLocation at root+0x128 (× world scale). Fallback: mesh C2W translation.
                        Vector3 mapLocation;
                        if (playerScatterMap.Results[i][10].TryGetResult<FVector3>(out var relLoc) && playerRootComponent != 0)
                            mapLocation = new Vector3((float)relLoc.X * 0.08f, (float)relLoc.Y * 0.08f, (float)relLoc.Z * 0.08f);
                        else
                            mapLocation = new Vector3((float)playerCompToWorld.Translation.X * 0.08f, (float)playerCompToWorld.Translation.Y * 0.08f, (float)playerCompToWorld.Translation.Z * 0.08f);

                        string displayName = Memory.ReadFString(playerNickNameCached.OriginalNickName);
                        if (string.IsNullOrWhiteSpace(displayName))
                            displayName = Memory.ReadFString(playerAccountDataReplication.Nickname.OriginalNickName);
                        if (string.IsNullOrWhiteSpace(displayName) && playerState != 0)
                        {
                            try
                            {
                                var playerStateNamePrivate = Memory.ReadValue<FString>(playerState + (ulong)Offsets.PlayerState.PlayerNamePrivate);
                                displayName = Memory.ReadFString(playerStateNamePrivate);
                            }
                            catch
                            {
                                // ignore and keep fallback below
                            }
                        }
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            displayName = $"Player#{i}";
                            missingNameCount++;
                        }

                        string classLabel = Memory.ReadFString(playerNickNameCached.StreamingModeNickName);
                        if (string.IsNullOrWhiteSpace(classLabel))
                            classLabel = Memory.ReadFString(playerAccountDataReplication.Nickname.StreamingModeNickName);
                        if (!string.IsNullOrWhiteSpace(classLabel) && classLabel.Contains('#'))
                            classLabel = classLabel.Split('#')[0];
                        if (string.IsNullOrWhiteSpace(classLabel))
                        {
                            classLabel = "Unknown";
                            missingClassCount++;
                        }

                        newPlayers.Add(i, new Player
                        {
                            Type = playerController == _localPlayerController ? PlayerType.LocalPlayer : PlayerType.Default,
                            Name = displayName,
                            Weapon = weaponLabel,
                            Class = classLabel,
                            Health = statsReadOk ? playerStats.Health.CurrentValue : 0,
                            MaxHealth = statsReadOk ? playerStats.MaxHealth.CurrentValue : 0,
                            Strength = statsReadOk ? playerStats.Strength.CurrentValue : 0,
                            Vigor = statsReadOk ? playerStats.Vigor.CurrentValue : 0,
                            Agility = statsReadOk ? playerStats.Agility.CurrentValue : 0,
                            Dexterity = statsReadOk ? playerStats.Dexterity.CurrentValue : 0,
                            Will = statsReadOk ? playerStats.Will.CurrentValue : 0,
                            Knowledge = statsReadOk ? playerStats.Knowledge.CurrentValue : 0,
                            Resourcefulness = statsReadOk ? playerStats.Resourcefulness.CurrentValue : 0,
                            PhysicalDamageWeaponPrimary = statsReadOk ? playerStats.PhysicalDamageWeaponPrimary.EffectiveValue : 0,
                            PhysicalDamageBase = statsReadOk ? playerStats.PhysicalDamageBase.EffectiveValue : 0,
                            PhysicalPower = statsReadOk ? playerStats.PhysicalPower.EffectiveValue : 0,
                            ArmorRating = statsReadOk ? playerStats.ArmorRating.EffectiveValue : 0,
                            RootComponentPtr = playerRootComponent,
                            CompToWorld = playerCompToWorld,
                            SkeletonMeshPtr = playerSkeletonMeshComponent,
                            PawnPrivatePtr = playerPawnPrivate,
                            Location = mapLocation,
                            Rotation = new FRotator(playerRotation.Yaw, playerRotation.Pitch, playerRotation.Roll),
                            Level = playerAccountDataReplication.Level,
                            PartyID = partyIDInt,
                            Bones = playerBones,
                        });

                    }
                }
                _players = newPlayers.ToDictionary(x => x.Key, x => x.Value);

                var byType = newPlayers.Values.GroupBy(p => p.Type).Select(g => $"{g.Key}={g.Count()}");
                ActivityLog.Info("Game",
                    $"UpdatePlayersList: world=0x{_world:X} gameState=0x{gameStateBase:X} replicatedPlayerStates={playerArray.Count} builtPlayers={newPlayers.Count} statsOk={statsReadOkCount} statsFail={statsReadFailCount} missingName={missingNameCount} missingClass={missingClassCount} [{string.Join(", ", byType)}]");
            }
            catch (Exception ex)
            {
                ActivityLog.Exception("Game", ex, "UpdatePlayersList");
            }
            finally
            {
                playerLock.Release();
            }
        }

        public void UpdatePlayerPositions()
        {
            try
            {
                //Console.Clear();
                //update player positions
                playerLock.WaitOne();

                //get players dictionary
                foreach (var player in _players)
                {
                    var playerScatterMap = new ScatterReadMap(1);

                    var playerCompToWorldRound = playerScatterMap.AddRound(Memory.PID, false);
                    var playerLocationRound = playerScatterMap.AddRound(Memory.PID, false);
                    var playerRotationRound = playerScatterMap.AddRound(Memory.PID, false);

                    for (int i = 0; i < 1; i++)
                    {
                        ulong meshOrRoot = player.Value.SkeletonMeshPtr != 0 ? player.Value.SkeletonMeshPtr : player.Value.RootComponentPtr;
                        playerCompToWorldRound.AddEntry<FTransform>(i, 0, meshOrRoot, typeof(FTransform), Offsets.ComponentToWorld);
                        playerLocationRound.AddEntry<FVector3>(i, 1, player.Value.RootComponentPtr, typeof(FVector3), Offsets.RootComponent.Location);
                        playerRotationRound.AddEntry<FRotator>(i, 2, player.Value.RootComponentPtr, typeof(FRotator), Offsets.RootComponent.Rotation);
                    }
                    playerScatterMap.Execute(Memory.Mem);
                    for(int i = 0;i < 1; i++)
                    {
                        if (!playerScatterMap.Results[i][0].TryGetResult<FTransform>(out var playerCompToWorld) ||
                            !playerScatterMap.Results[i][2].TryGetResult<FRotator>(out var playerRotation))
                            continue;
                        FVector3 relLoc;
                        if (!playerScatterMap.Results[i][1].TryGetResult<FVector3>(out relLoc))
                        {
                            if (player.Value.RootComponentPtr == 0)
                                continue;
                            try
                            {
                                relLoc = Memory.ReadValue<FVector3>(player.Value.RootComponentPtr + Offsets.RootComponent.Location);
                            }
                            catch
                            {
                                continue;
                            }
                        }
                        //update bones
                        var newBones = new Dictionary<int, Bone>(player.Value.Bones);
                        for (int j = 0; j < player.Value.Bones.Count; j++)
                        {
                            if (player.Value.Bones.TryGetValue(j, out var bone))
                            {
                                var transform = Memory.ReadValue<FTransform>(bone.Address);
                                bone.Transform = transform;
                                newBones[j] = bone;
                            }
                        }
                        _players[player.Key].CompToWorld = playerCompToWorld;
                        _players[player.Key].Location = new Vector3((float)relLoc.X * 0.08f, (float)relLoc.Y * 0.08f, (float)relLoc.Z * 0.08f);
                        _players[player.Key].Rotation = new FRotator(playerRotation.Yaw, playerRotation.Pitch, playerRotation.Roll);
                        _players[player.Key].Bones = newBones;
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.Exception("Game", ex, "UpdatePlayerPositions");
            }
            finally
            {
                playerLock.Release();
            }
        }
        public void WaitForNewGame()
        {
            while (true)
            {
                //Console.Clear();
                _world = Memory.ReadPtr(Memory.ModuleBase + Offsets.GWorld);
                if (_world == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                _owningGameInstance = Memory.ReadPtr(_world + Offsets.OwningGameInstance);
                if (_owningGameInstance == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var localPlayersPtr = Memory.ReadPtr(_owningGameInstance + Offsets.LocalPlayers);
                if (localPlayersPtr == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var localPlayer = Memory.ReadPtr(localPlayersPtr);
                if (localPlayer == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var localPlayerController = Memory.ReadPtr(localPlayer + Offsets.PlayerController);
                if (localPlayerController == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var HUD = Memory.ReadPtr(localPlayerController + Offsets.PlayerHUD);
                if (HUD == 0)
                {
                    Thread.Sleep(100);
                    continue;
                }

                uint HUDnameIndex;
                try
                {
                    HUDnameIndex = Memory.ReadValue<uint>(HUD + Offsets.NameIndex);
                }
                catch (DMAException)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (Memory.ReadName(HUDnameIndex) == "BP_HUDDungeon_C")
                {
                    _inGame = true;
                    _localPlayer = localPlayer;
                    _localPlayerController = localPlayerController;
                    playerUpdateTimer.Start();
                    EntityManager.Start();
                    Memory.GameStatus = Enums.GameStatus.InGame;
                    ActivityLog.Info("Game", "Entered match: BP_HUDDungeon_C detected, GameStatus=InGame");
                    break;
                }

                Thread.Sleep(100);
            }
        }
       
    }
}

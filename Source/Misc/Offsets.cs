using Newtonsoft.Json;
using Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DMAW_DND
{
    /// <summary>
    /// Offsets sourced from UEDumper SDK (Dark And Darker), e.g.
    /// …\UEDumper-development\x64\Release\Dark And Darker\Dark-And-Darker\OffsetsInfo.json
    /// — version 10202 (updated_at 1774542253396): GNames=0xC2DA2C0, GObjects=0xC3BE160, GWorld=0xC53C440.
    /// UDCAttributeSet / FGameplayAttributeData: ClassesInfo.json + StructsInfo.json.
    /// </summary>
    internal static class Offsets
    {
        // OFFSET_* from OffsetsInfo.json (module-relative RVA)
        public const int GWorld = 0xC53C440;
        public const int GNames = 0xC2DA2C0;
        public const int GObjects = 0xC3BE160;

        // UWorld
        public const int GameStateBase = 0x160; // GameState (AGameStateBase*)
        public const int PlayerArray = 0x2C0; // AGameStateBase::PlayerArray
        public const int LevelsArray = 0x178; // TArray<ULevel*>
        public const int ActorsArray = 0x28; // ULevel::Actors
        public const int OwningGameInstance = 0x1D8; // UGameInstance*
        // UGameInstance
        public const int LocalPlayers = 0x38; // TArray<ULocalPlayer*>
        // UPlayer
        public const int PlayerController = 0x30; // APlayerController*
        // APlayerController
        public const int PlayerCameraManager = 0x360;
        public const int PlayerMinimalViewInfo = 0x1410; // APlayerCameraManager::CameraCachePrivate + POV
        public const int PlayerHUD = 0x358; // MyHUD
        // UObject
        public const int NameIndex = 0x18;
        // USceneComponent
        public const int ComponentToWorld = 0x188;
        // AActor
        public const int Instigator = 0x1A0;

        [StructLayout(LayoutKind.Explicit)]
        public struct ActorStatus
        {
            [FieldOffset(0x3F8)]
            public bool IsOpen;
        }

        public class PlayerState
        {
            public const int PlayerPawnPrivate = 0x320; // APlayerState (SDK)
            public const int PlayerNamePrivate = 0x340;
        }

        public class PawnPrivate
        {
            public const int SkeletalMeshComponent = 0x328; // ACharacter::Mesh (USkeletalMeshComponent*)
            public const int ComponentToWorld = 0x188;
            public const int AccountID = 0x7D8;
            public const int NickNameCached = 0x418; // not verified in SDK dump; re-check if names break
            public const int MeshDeformerInstance = 0x568;
            public const int RootComponent = 0x1B8; // AActor
            public const int PlayerController = 0x2D8; // APawn::Controller
            public const int AbilitySystemComponent = 0x708; // ADCCharacterBase
            public const int AccountDataReplication = 0x7F8;
            public const int EquipmentInventory = 0xA68; // ADCCharacterBase::InventoryComponentV2
            public const int SpawnedAttributes = 0x1088; // UAbilitySystemComponent
        }

        public class RootComponent
        {
            public const int Location = 0x128;
            public const int Rotation = 0x140;
        }
    }
}

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
    internal static class Offsets
    {
        public const int GWorld = 0xc53c440; //updated 
        public const int GNames = 0xc2da2c0; //updated
        public const int GameStateBase = 0x160; //Engine.World
        public const int PlayerArray = 0x2c0; //Engine.GameStateBase
        public const int LevelsArray = 0x178;  //Engine.World
        public const int ActorsArray = 0x28; //Engine.Level
        public const int OwningGameInstance = 0x1d8; //Engine.World
        public const int LocalPlayers = 0x38; //Engine.GameInstance
        public const int PlayerController = 0x30; //Engine.Player
        public const int PlayerCameraManager = 0x360; //Engine.PlayerController
                                                      //public const int PlayerMinimalViewInfo = 0x22B0;
        public const int PlayerMinimalViewInfo = 0x1410; //Engine.PlayerCameraManager CameraCachePrivate
        public const int PlayerHUD = 0x358; //Engine.PlayerController
        public const int NameIndex = 0x18; //uObject
        public const int ComponentToWorld = 0x188;
        public const int Instigator = 0x1a0; //Engine.Actor
                                             //public const int isOpen = 0x3F0;
                                             //public const int isHidden = 0x40C;
        [StructLayout(LayoutKind.Explicit)]
        public struct ActorStatus
        {
            [FieldOffset(0x3F8)]
            public bool IsOpen; // This will start at the base address + 0x3F0 // didnt update, not sure what this is, but it could be 0xd50 which is DCactorstatus
        }

        public class PlayerState
        {
            public const int PlayerPawnPrivate = 0x0328; //Engine.PlayerState
            public const int PlayerNamePrivate = 0x340; //Engine.PlayerState
        }
        public class PawnPrivate
        {
            public const int SkeletalMeshComponent = 0x408; //unable to find
            public const int ComponentToWorld = 0x188;
            public const int AccountID = 0x7d8; //fString  DungeonCrawler.DCCharacterBase
            public const int NickNameCached = 0x418; //fNickname //unable to find this, put my best guess, not sure what this is used for either.
            public const int MeshDeformerInstance = 0x568; //Engine.SkinnedMeshComponent
            public const int RootComponent = 0x1b8; //Engine.Actor
            public const int PlayerController = 0x2d8;
            public const int AbilitySystemComponent = 0x708; //DungeonCrawler.DCCharacterBase
            public const int AccountDataReplication = 0x7f8; //DungeonCrawler.DCCharacterBase
            public const int EquipmentInventory = 0xa68; //was also unable to find this, used my best guess. 
            public const int SpawnedAttributes = 0x1088;
        }
        public class RootComponent
        {
            public const int Location = 0x128; 
            public const int Rotation = 0x140;
        }
    }
}

using System;
using FlatWorld.Networking;

namespace FlatWorld.Spaceflight
{
    #region 太空联机意图与观察者
    [Serializable]
    public struct SpaceNetworkObserver
    {
        public bool InSpace;
        public uint RealmRevision;
        public SpaceSectorPosition Position;
        public SpaceVector2 Velocity;
        public string ShipId, WorldKey;
    }

    [Serializable]
    public struct SpacePassengerInput
    {
        public float MoveX, MoveY, Turn, Thrust;
    }

    public enum SpaceNetworkIntentKind
    {
        EnterConsole, ExitConsole, ToggleDoor, ToggleIgnition, RequestLanding,
        StartNavigation, CancelNavigation, ConfirmDock, DisconnectDock, Attack, AttackEnd, UseSkill, Respawn, CombatStateApplied
    }

    [Serializable]
    public struct SpaceNetworkIntent
    {
        public SpaceNetworkIntentKind Kind;
        public string ShipId, PieceId, TargetId;
        public int Action;
        public int SelectedSlot;
        public bool StartsGroundCombat;
        public double X, Y;
        public SpaceSectorId Sector;
    }

    public static class SpaceNetworkBridge
    {
        public static Func<string, SpacePassengerInput, bool> InputSender;
        public static Func<string, SpaceNetworkIntent, bool> IntentSender;
        public static Func<string, bool> CombatPreparationSender;
        public static bool SubmitInput(string profileId, SpacePassengerInput input)
            => GameNetwork.IsOnline && !GameNetwork.HasStateAuthority && InputSender?.Invoke(profileId, input) == true;
        public static bool SendIntent(string profileId, SpaceNetworkIntent intent)
            => GameNetwork.IsOnline && !GameNetwork.HasStateAuthority && IntentSender?.Invoke(profileId, intent) == true;
    }
    #endregion
}

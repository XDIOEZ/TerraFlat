using System;
using FlatWorld.Networking;
using UnityEngine;

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
        StartNavigation, CancelNavigation, ConfirmDock, DisconnectDock, Attack, AttackEnd, UseSkill, Respawn, CombatStateApplied, LaunchGroundShip, UsePlayerItem, HarvestNatural, SetTargetPressure, OffhandParry
    }

    [Serializable]
    public struct SpaceNetworkIntent
    {
        public SpaceNetworkIntentKind Kind;
        public string ShipId, PieceId, TargetId;
        public int Action;
        public int SelectedSlot;
        public uint RealmRevision;
        public bool StartsGroundCombat;
        public double X, Y;
        public SpaceSectorId Sector;
        public bool HasNaturalSource;
        public string NaturalSourceWorldKey;
        public int NaturalSourceChunkX, NaturalSourceChunkY;
    }

    public static class SpaceNetworkBridge
    {
        public const int LandingPreviewGridSize = 33;
        public static Func<string, Vector2, Vector2, Action<byte[], bool>, bool> LandingPreviewRequester;
        public static Action LandingPreviewCancel;
        public static Func<string, SpacePassengerInput, bool> InputSender;
        public static Func<string, SpaceNetworkIntent, bool> IntentSender;
        public static Func<string, bool> CombatPreparationSender;
        public static Action GroundStreamingRefreshRequested;
        public static void RequestGroundStreamingRefresh() => GroundStreamingRefreshRequested?.Invoke();
        public static Func<string, SpaceNetworkIntent, Action<bool, string>, bool> IntentWithReplySender;
        public static bool SubmitInput(string profileId, SpacePassengerInput input)
            => GameNetwork.IsOnline && !GameNetwork.HasStateAuthority && InputSender?.Invoke(profileId, input) == true;
        public static bool SendIntent(string profileId, SpaceNetworkIntent intent)
            => GameNetwork.IsOnline && !GameNetwork.HasStateAuthority && IntentSender?.Invoke(profileId, intent) == true;
        // 需要反馈的操作按请求序号等待服务器回执，不把成功发包当作执行成功。
        public static bool SendIntent(string profileId, SpaceNetworkIntent intent, Action<bool, string> completed)
            => GameNetwork.IsOnline && !GameNetwork.HasStateAuthority && IntentWithReplySender?.Invoke(profileId, intent, completed) == true;
    }
    #endregion
}

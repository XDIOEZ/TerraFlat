using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    #region 宇宙会话快照
    [Serializable]
    public sealed class SpaceSessionState
    {
        public int Version = 1;
        public UniverseState Universe;
        public List<ShipState> Ships = new();
        public List<ShipFlightState> Flights = new();
        public List<ShipDeviceState> Devices = new();
        public List<SpacePassengerState> Passengers = new();
        public List<ShipNavigationState> Navigation = new();
        public List<ShipDockingPairState> Docking = new();
        public List<SpaceLooseItemState> LooseItems = new();
        public long NavigationSequence;
    }
    public enum ShipFlightPhase { Landed, Floating, Lifting, Cruising, Descending, SurfaceDescending }
    [Serializable]
    public sealed class ShipFlightState
    {
        public string ShipId, BodyId, SurfaceWorldKey;
        public string RequestedLandingBodyId;
        public ShipFlightPhase Phase;
        public double SurfaceX, SurfaceY, HeightMeters, VerticalSpeed, SurfaceAngle;
        public SpaceMotionState Motion = new();
        public bool Ignited, TransferPending, LandingResolved, LandingSelected;
        public double LandingX, LandingY;
        public ulong LandingRandomState = 1;
        public string LandingEventId;
        public double ImpactSpeed;
        public SpaceVector2 EntrySurfaceVelocity;
    }
    [Serializable]
    public sealed class ShipDeviceState
    {
        public string PieceId;
        public ShipPartConfiguration Configuration = new();
        public bool Enabled = true;
        public double TargetPressureKPa = 101.325;
    }
    [Serializable]
    public sealed class SpacePassengerState
    {
        public string ProfileId, ShipId, ConsolePieceId;
        public double LocalX, LocalY;
        public SpaceMotionState FreeMotion = new();
        public SpaceVector2 FreeWalkVelocity;
        public bool Supported, IsInSpace;
        public bool DescentControlEntered, DescentDriving;
        public double LandingX, LandingY, LandingOriginX, LandingOriginY;
        public bool LandingSelected, LandingResolved, SurfaceDescending;
        public string LandingBodyId, LandingWorldKey;
        public double SurfaceHeight, SurfaceVerticalSpeed, WalkVelocityX, WalkVelocityY, SavedCameraSize;
        public double SurfaceHorizontalSpeed;
        public ulong LandingRandomState = 1;

        // 步行只替换自己的速度分量，继承的船速与外部碰撞冲量保持不变。
        public void UpdateFreeWalkVelocity(SpaceVector2 velocity)
        {
            FreeMotion.VelocityMetersPerSecond += velocity - FreeWalkVelocity;
            FreeWalkVelocity = velocity;
        }
    }
    [Serializable]
    public sealed class ShipNavigationState
    {
        public string ShipId, NavigationPieceId, ConsolePieceId;
        public double TargetX, TargetY;
        public long Sequence;
        public bool Active, NeedsManualRestart;
        public string Status = "待机";
    }
    #endregion
}

public partial class GameSaveData
{
    #region 太空权威快照
    // 宇宙数据随正式存档封装保存，场景只负责显示。
    public string SpaceStateJson;
    #endregion
}

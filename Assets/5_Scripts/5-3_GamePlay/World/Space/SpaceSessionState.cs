using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    #region 宇宙会话快照
    [Serializable]
    public sealed class SpaceSessionState
    {
        public int Version = 2;
        [JsonIgnore] public UniverseState Universe;
        public List<UniverseState> Sectors = new();
        public double SimulationSeconds;
        public SpaceStateCollection<ShipState> Ships = new();
        public SpaceStateCollection<ShipFlightState> Flights = new();
        public SpaceStateCollection<ShipDeviceState> Devices = new();
        public List<SpacePassengerState> Passengers = new();
        public List<ShipNavigationState> Navigation = new();
        public List<ShipDockingPairState> Docking = new();
        public List<SpaceLooseItemState> LooseItems = new();
        public List<SpaceOreAsteroidState> OreAsteroids = new();
        public List<SpaceSectorId> GeneratedOreAsteroidSectors = new();
        public List<GroundAtmosphereState> GroundAtmospheres = new();
        public List<SpaceElevatorState> Elevators = new();
        public List<SpaceLocalWorldNetworkState> LocalWorldStates = new();
        public long NavigationSequence;
    }

    [Serializable]
    public sealed class SpaceLocalWorldNetworkState
    {
        public string WorldKey;
        public ulong Revision;
        public byte[] Payload;
    }

    [Serializable]
    public sealed class GroundAtmosphereState
    {
        // 地面只借用气密几何与库存，不登记为飞船或创建飞行记录。
        public string WorldKey;
        public ShipState Geometry = new();
    }

    [Serializable]
    public sealed class SpaceStateCollection<T> : Collection<T>, IReadOnlyList<T>
    {
        #region 权威集合结构版本
        // 数组保存格式不变，增删或替换成员后才重建运行时身份索引。
        [JsonIgnore] public ulong Revision { get; private set; }
        protected override void InsertItem(int index, T item)
        { base.InsertItem(index, item); Revision++; }
        protected override void SetItem(int index, T item)
        { base.SetItem(index, item); Revision++; }
        protected override void RemoveItem(int index)
        { base.RemoveItem(index); Revision++; }
        protected override void ClearItems()
        { if (Count == 0) return; base.ClearItems(); Revision++; }
        public T Find(Predicate<T> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            for (int i = 0; i < Count; i++) if (predicate(this[i])) return this[i];
            return default;
        }
        public bool Exists(Predicate<T> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            for (int i = 0; i < Count; i++) if (predicate(this[i])) return true;
            return false;
        }
        public int RemoveAll(Predicate<T> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            int removed = 0;
            for (int i = Count - 1; i >= 0; i--)
                if (predicate(this[i])) { RemoveAt(i); removed++; }
            return removed;
        }
        public T[] ToArray()
        { var result = new T[Count]; CopyTo(result, 0); return result; }
        // 正式会话 foreach 使用值类型游标，避免基础 Collection 的枚举器装箱。
        public new Enumerator GetEnumerator() => new(this);
        public struct Enumerator : IDisposable
        {
            private readonly SpaceStateCollection<T> source;
            private readonly ulong revision;
            private int index;
            public T Current { get; private set; }
            internal Enumerator(SpaceStateCollection<T> owner)
            { source = owner; revision = owner.Revision; index = -1; Current = default; }
            public bool MoveNext()
            {
                if (source.Revision != revision) throw new InvalidOperationException("枚举期间会话集合发生变化。");
                index++;
                if (index < source.Count) { Current = source[index]; return true; }
                Current = default; return false;
            }
            public void Dispose() { }
        }
        #endregion
    }
    public enum ShipFlightPhase { Landed, Floating, Lifting, Cruising, SurfaceDescending }
    [Serializable]
    public sealed class ShipFlightState
    {
        public string ShipId, BodyId, SurfaceWorldKey;
        public string RequestedLandingBodyId;
        public ShipFlightPhase Phase;
        public double SurfaceX, SurfaceY, HeightMeters, VerticalSpeed, SurfaceAngle;
        public double SurfaceStageElapsedSeconds;
        public bool PoweredDescent;
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
        public float Rpm;
        public int EntryDirection = -1;
    }
    [Serializable]
    public sealed class SpacePassengerState
    {
        public string ProfileId, ShipId, ConsolePieceId;
        public uint RealmRevision;
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
        public ShipLandingReceipt LastShipLanding;

        // 步行只替换自己的速度分量，继承的船速与外部碰撞冲量保持不变。
        public void UpdateFreeWalkVelocity(SpaceVector2 velocity)
        {
            FreeMotion.VelocityMetersPerSecond += velocity - FreeWalkVelocity;
            FreeWalkVelocity = velocity;
        }
    }

    [Serializable]
    public readonly struct ShipLandingReceipt
    {
        #region 普通建筑移交完成凭证
        // 完成凭证随乘员快照保留，船记录移除和客户端换快照后仍能确认本次落地。
        public readonly string ShipId, EventId, WorldKey;
        public readonly bool IsWater;
        public readonly double SimulationSeconds;
        [JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(ShipId) &&
            !string.IsNullOrWhiteSpace(EventId) && !string.IsNullOrWhiteSpace(WorldKey);

        [JsonConstructor]
        public ShipLandingReceipt(string shipId, string eventId, string worldKey, bool isWater, double simulationSeconds)
        {
            ShipId = shipId; EventId = eventId; WorldKey = worldKey;
            IsWater = isWater; SimulationSeconds = simulationSeconds;
        }
        #endregion
    }

    [Serializable]
    public sealed class ShipNavigationState
    {
        public string ShipId, NavigationPieceId, ConsolePieceId;
        public double TargetX, TargetY;
        public SpaceSectorId TargetSector;
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

using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using MemoryPack;
using UnityEngine;

/// <summary>独立版本的掉落物快照，放在外层存档封装中。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DroppedItemArchive
{
    public const int CurrentVersion = 2;
    [MemoryPackOrder(0)] public int Version = CurrentVersion;
    [MemoryPackOrder(1)] public Dictionary<string, List<DroppedItemSaveRecord>> Worlds = new();
}

/// <summary>分离稳定物品载荷与实时数量、物质和运动状态。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DroppedItemSaveRecord
{
    #region 持久化状态

    [MemoryPackInclude, MemoryPackOrder(0)] private byte[] itemPayload;
    [MemoryPackOrder(1)] public int Guid;
    [MemoryPackOrder(2)] public float Amount;
    [MemoryPackOrder(3)] public bool CanBePickedUp;
    [MemoryPackOrder(4)] public float DurabilityFraction = 1f;
    [MemoryPackOrder(5)] public bool MatterInitialized;
    [MemoryPackOrder(6)] public float TemperatureCelsius = 20f;
    [MemoryPackOrder(7)] public float Moisture;
    [MemoryPackOrder(8)] public bool IsBurning;
    [MemoryPackOrder(9)] public float CombustionElapsedSeconds;
    [MemoryPackIgnore] public Vector2 Position;
    [MemoryPackInclude, MemoryPackOrder(10)] private float PositionX { get => Position.x; set => Position.x = value; }
    [MemoryPackInclude, MemoryPackOrder(11)] private float PositionY { get => Position.y; set => Position.y = value; }
    [MemoryPackIgnore] public Vector2 Scale;
    [MemoryPackInclude, MemoryPackOrder(12)] private float ScaleX { get => Scale.x; set => Scale.x = value; }
    [MemoryPackInclude, MemoryPackOrder(13)] private float ScaleY { get => Scale.y; set => Scale.y = value; }
    [MemoryPackOrder(14)] public float Rotation;
    [MemoryPackOrder(15)] public float VisualHeight;
    [MemoryPackOrder(16)] public float LiquidDepth;
    [MemoryPackOrder(17)] public byte WaterKind;
    [MemoryPackOrder(18)] public bool HasFlight;
    [MemoryPackIgnore] public Vector2 FlightStart;
    [MemoryPackInclude, MemoryPackOrder(19)] private float FlightStartX { get => FlightStart.x; set => FlightStart.x = value; }
    [MemoryPackInclude, MemoryPackOrder(20)] private float FlightStartY { get => FlightStart.y; set => FlightStart.y = value; }
    [MemoryPackIgnore] public Vector2 FlightEnd;
    [MemoryPackInclude, MemoryPackOrder(21)] private float FlightEndX { get => FlightEnd.x; set => FlightEnd.x = value; }
    [MemoryPackInclude, MemoryPackOrder(22)] private float FlightEndY { get => FlightEnd.y; set => FlightEnd.y = value; }
    [MemoryPackIgnore] public Vector2 FlightControl;
    [MemoryPackInclude, MemoryPackOrder(23)] private float FlightControlX { get => FlightControl.x; set => FlightControl.x = value; }
    [MemoryPackInclude, MemoryPackOrder(24)] private float FlightControlY { get => FlightControl.y; set => FlightControl.y = value; }
    [MemoryPackOrder(25)] public float FlightDuration;
    [MemoryPackOrder(26)] public float FlightElapsed;
    [MemoryPackOrder(27)] public float ArcHeight;
    [MemoryPackOrder(28)] public float RotationSpeed;
    [MemoryPackOrder(29)] public bool HasWaterTransition;
    [MemoryPackOrder(30)] public float WaterStart;
    [MemoryPackOrder(31)] public float WaterTarget;
    [MemoryPackOrder(32)] public float WaterDuration;
    [MemoryPackOrder(33)] public float WaterElapsed;
    [MemoryPackOrder(34)] public float PendingSurfaceMotionSeconds;
    [MemoryPackOrder(35)] public float PendingSurfaceDriftSeconds;

    [MemoryPackIgnore] private ItemData itemData;
    [MemoryPackIgnore] private DroppedItemPayloadCaptureCache mutablePayloadCache;
    [MemoryPackIgnore] internal byte[] EncodedItemPayload => itemPayload;
    [MemoryPackIgnore] internal bool HasItemPayload => itemPayload != null || itemData != null;

    // 后台世界按需取得独立冷物品，活动世界保存时无需还原或克隆它。
    [MemoryPackIgnore]
    public ItemData Data
    {
        get
        {
            if (itemData != null || itemPayload == null) return itemData;
            ItemInstanceSnapshot snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(itemPayload);
            itemData = snapshot.CreateColdData();
            itemData.Stack.Amount = Amount;
            itemData.Stack.CanBePickedUp = CanBePickedUp;
            itemData.Durability = itemData.MaxDurability * DurabilityFraction;
            itemData.inHand = false;
            itemData.MatterState = new ItemMatterState
            {
                Initialized = MatterInitialized, TemperatureCelsius = TemperatureCelsius, Moisture = Moisture,
                IsBurning = IsBurning, CombustionElapsedSeconds = CombustionElapsedSeconds
            };
            itemData.transform ??= new ItemTransform();
            itemData.transform.position = new Vector3(Position.x, Position.y, 0f);
            itemData.transform.rotation = Quaternion.Euler(0f, 0f, Rotation);
            itemData.transform.scale = new Vector3(Scale.x, Scale.y, 1f);
            mutablePayloadCache = new DroppedItemPayloadCaptureCache();
            mutablePayloadCache.SeedRestoredData(itemData, itemPayload);
            return itemData;
        }
        set
        {
            itemData = value;
            itemPayload = null;
            mutablePayloadCache = null;
            if (value != null) CaptureMutableItemState(value);
        }
    }

    [MemoryPackOnSerializing]
    private void FreezeMutableDataForSerialization()
    {
        if (itemData == null && ItemSnapshotSerialization.IsPublicPlayerStateCapture) _ = Data;
        if (itemData == null) return;
        CaptureMutableItemState(itemData);
        itemPayload = (mutablePayloadCache ??= new DroppedItemPayloadCaptureCache()).Capture(itemData);
    }

    private void CaptureMutableItemState(ItemData data)
    {
        Guid = data.Guid;
        Amount = data.Stack?.Amount ?? 1f;
        CanBePickedUp = data.Stack?.CanBePickedUp ?? true;
        DurabilityFraction = data.MaxDurability > 0f ? Mathf.Clamp01(data.Durability / data.MaxDurability) : 1f;
        ItemMatterState matter = data.MatterState;
        MatterInitialized = matter?.Initialized ?? false;
        TemperatureCelsius = matter?.TemperatureCelsius ?? 20f;
        Moisture = matter?.Moisture ?? 0f;
        IsBurning = matter?.IsBurning ?? false;
        CombustionElapsedSeconds = matter?.CombustionElapsedSeconds ?? 0f;
    }

    // 移交只复制轻量字段，共用的已冻结字节不可由运行态直接修改。
    internal DroppedItemSaveRecord CopyForWorldTransfer()
    {
        FreezeMutableDataForSerialization();
        var copy = (DroppedItemSaveRecord)MemberwiseClone();
        copy.itemData = null;
        copy.mutablePayloadCache = null;
        return copy;
    }

    #endregion

    #region 水态恢复与捕获

    internal LightweightDroppedBody RestoreBody(ItemData data, WorldTopologyDomain domain) => new()
    {
        Id = data.Guid, Amount = data.Stack.Amount, Position = domain.Normalize(Position),
        Scale = Scale, Rotation = Rotation, VisualHeight = VisualHeight,
        LiquidDepth = LiquidDepth, WaterKind = WaterKind, Pickable = 0,
        SubmergedProgress = WaterKind == LightweightDroppedBody.BottomWaterKind ? 1f :
            WaterKind == 2 && HasWaterTransition
                ? Mathf.Clamp01((WaterElapsed - WaterDuration) / WorldItemWaterRules.SubmergedRecedeDuration) : 0f
    };

    internal static DroppedItemSaveRecord Capture(ItemData payload, LightweightDroppedItemSimulation simulation, int id,
        DroppedItemPayloadCaptureCache cache = null)
    {
        LightweightDroppedBody body = simulation.Get(id);
        bool hasFlight = simulation.TryGetFlight(id, out LightweightDroppedFlight flight);
        bool hasWater = simulation.TryGetWater(id, out LightweightDroppedWaterTransition water);
        var record = new DroppedItemSaveRecord
        {
            itemPayload = (cache ?? new DroppedItemPayloadCaptureCache()).Capture(payload),
            Position = body.Position, Scale = body.Scale, Rotation = body.Rotation,
            VisualHeight = body.VisualHeight, LiquidDepth = body.LiquidDepth, WaterKind = body.WaterKind,
            HasFlight = hasFlight, FlightStart = flight.Start, FlightEnd = flight.End, FlightControl = flight.Control,
            FlightDuration = flight.Duration, FlightElapsed = flight.Elapsed, ArcHeight = flight.ArcHeight,
            RotationSpeed = flight.RotationSpeed, HasWaterTransition = hasWater,
            WaterStart = water.StartDepth, WaterTarget = water.TargetDepth, WaterDuration = water.Duration, WaterElapsed = water.Elapsed
        };
        record.CaptureMutableItemState(payload);
        record.Amount = body.Amount;
        record.CanBePickedUp = true;
        return record;
    }

    #endregion
}

/// <summary>只在稳定内容变化时重编码物品，并复用没有变化的模块快照。</summary>
internal sealed class DroppedItemPayloadCaptureCache
{
    #region 物品与模块快照缓存

    private sealed class ModuleState
    {
        public ulong Signature;
        public ModuleInstanceSnapshot Snapshot;
    }

    private readonly HashSet<ItemData> visiting = new();
    private readonly Dictionary<ModuleData, ModuleState> modules = new();
    private readonly Func<ModuleData, bool, ModuleInstanceSnapshot> captureModule;
    private ItemData source;
    private uint structureVersion;
    private ulong codecRevision;
    private ulong signature;
    private bool publicMode;
    private bool hasSignature;
    private byte[] payload;

    public DroppedItemPayloadCaptureCache() => captureModule = CaptureModule;

    public byte[] Capture(ItemData data)
    {
        bool isPublic = ItemSnapshotSerialization.IsPublicPlayerStateCapture;
        Prepare(data, isPublic);
        visiting.Clear();
        ulong signatureValue = 0;
        bool reusable = !isPublic && ItemRuntimeStateFingerprint.TryCalculate(data, false, false, visiting, out signatureValue,
            includeRootStackState: false, includeRootDynamicState: false);
        if (reusable && hasSignature && signature == signatureValue) return payload;
        ItemInstanceSnapshot snapshot = ItemSnapshotSerialization.Capture(data, includeTransform: false,
            captureModule: isPublic ? null : captureModule);
        snapshot.ResetSeparatelyPersistedQuantityDurabilityAndMatterState();
        payload = ItemSnapshotSerialization.SerializePayload(snapshot);
        signature = signatureValue;
        hasSignature = reusable;
        return payload;
    }

    public void SeedRestoredData(ItemData data, byte[] frozenPayload)
    {
        bool isPublic = ItemSnapshotSerialization.IsPublicPlayerStateCapture;
        Prepare(data, isPublic);
        visiting.Clear();
        hasSignature = !isPublic && ItemRuntimeStateFingerprint.TryCalculate(data, false, false, visiting, out signature,
            includeRootStackState: false, includeRootDynamicState: false);
        payload = frozenPayload;
    }

    private void Prepare(ItemData data, bool isPublic)
    {
        ulong revision = ModuleInstanceStateCodecs.Revision;
        if (ReferenceEquals(source, data) && structureVersion == data.ModuleStructureVersion && codecRevision == revision &&
            publicMode == isPublic) return;
        source = data;
        structureVersion = data.ModuleStructureVersion;
        codecRevision = revision;
        publicMode = isPublic;
        hasSignature = false;
        modules.Clear();
    }

    private ModuleInstanceSnapshot CaptureModule(ModuleData data, bool publicState)
    {
        visiting.Clear();
        bool reusable = ItemRuntimeStateFingerprint.TryCalculateModule(data, publicState, visiting, out ulong current);
        if (reusable && modules.TryGetValue(data, out ModuleState previous) && previous.Signature == current)
            return previous.Snapshot;
        ModuleInstanceSnapshot snapshot = ModuleInstanceSnapshot.Capture(data, publicState);
        if (reusable) modules[data] = new ModuleState { Signature = current, Snapshot = snapshot };
        else modules.Remove(data);
        return snapshot;
    }

    #endregion
}

public partial class GameSaveData
{
    /// <summary>仅内存桥接；真实字节由 CompactSaveEnvelope 独立保存。</summary>
    [MemoryPackIgnore]
    public DroppedItemArchive DroppedItems { get; set; } = new();
}

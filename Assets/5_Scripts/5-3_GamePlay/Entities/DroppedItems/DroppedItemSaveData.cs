using System.Collections.Generic;
using FlatWorld.DroppedItems;
using MemoryPack;
using UnityEngine;

/// <summary>独立版本的掉落物快照；放在外层存档封装中，不改变旧 GameSaveData 与 ItemData 的序列化布局。</summary>
[MemoryPackable]
public partial class DroppedItemArchive
{
    public int Version = 1;
    public Dictionary<string, List<DroppedItemSaveRecord>> Worlds = new();
}

/// <summary>库存载荷与短期运动状态的持久化副本；不序列化 Entity、GameObject 或表现节点。</summary>
[MemoryPackable]
public partial class DroppedItemSaveRecord
{
    public ItemData Data;
    public Vector2 Position;
    public Vector2 Scale;
    public float Rotation;
    public float VisualHeight;
    public float WaterDepth;
    public byte WaterKind;
    public bool HasFlight;
    public Vector2 FlightStart;
    public Vector2 FlightEnd;
    public Vector2 FlightControl;
    public float FlightDuration;
    public float FlightElapsed;
    public float ArcHeight;
    public float RotationSpeed;
    public bool HasWaterTransition;
    public float WaterStart;
    public float WaterTarget;
    public float WaterDuration;
    public float WaterElapsed;

    /// <summary>主线程复制库存数据；冷载荷中的数量不作为运行时第二份权威数量。</summary>
    internal static DroppedItemSaveRecord Capture(ItemData payload, DroppedItemSimulation simulation, int id)
    {
        DroppedBody body = simulation.Get(id);
        ItemData data = FastCloner.FastCloner.DeepClone(payload);
        data.Stack.Amount = body.Amount; data.Stack.CanBePickedUp = true; data.inHand = false;
        data.transform ??= new ItemTransform();
        data.transform.position = new Vector3(body.Position.x, body.Position.y, 0f);
        data.transform.rotation = Quaternion.Euler(0f, 0f, body.Rotation);
        data.transform.scale = new Vector3(body.Scale.x, body.Scale.y, 1f);
        bool hasFlight = simulation.TryGetFlight(id, out DroppedFlight flight);
        bool hasWater = simulation.TryGetWater(id, out DroppedWaterTransition water);
        return new DroppedItemSaveRecord
        {
            Data = data, Position = body.Position, Scale = body.Scale, Rotation = body.Rotation,
            VisualHeight = body.VisualHeight, WaterDepth = body.WaterDepth, WaterKind = body.WaterKind,
            HasFlight = hasFlight, FlightStart = flight.Start, FlightEnd = flight.End, FlightControl = flight.Control,
            FlightDuration = flight.Duration, FlightElapsed = flight.Elapsed, ArcHeight = flight.ArcHeight,
            RotationSpeed = flight.RotationSpeed, HasWaterTransition = hasWater,
            WaterStart = water.StartDepth, WaterTarget = water.TargetDepth, WaterDuration = water.Duration, WaterElapsed = water.Elapsed
        };
    }
}

public partial class GameSaveData
{
    /// <summary>仅内存桥接；真实字节由 CompactSaveEnvelope 独立保存，旧核心字段顺序保持不变。</summary>
    [MemoryPackIgnore]
    public DroppedItemArchive DroppedItems { get; set; } = new();
}

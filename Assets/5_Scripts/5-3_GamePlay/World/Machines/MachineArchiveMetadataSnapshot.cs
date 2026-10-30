using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

// 机械档案只存事件与删除身份，机器实例字节仍独立保存在各自记录中。
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MachineArchiveMetadataSnapshot
{
    #region 机械元数据字段
    [MemoryPackOrder(0)] public int Version = 2;
    [MemoryPackOrder(1)] public Dictionary<string, HashSet<int>> RemovedGenerated;
    [MemoryPackOrder(2)] public ulong NextPressureExplosionId;
    [MemoryPackOrder(3)] public List<PressureExplosionSnapshot> PressureExplosions;
    [MemoryPackOrder(4)] public List<string> RecentPressureExplosionSources;
    [MemoryPackOrder(5)] public List<PressureLiquidSpillSnapshot> PendingPressureLiquidSpills;

    public static MachineArchiveMetadataSnapshot Capture(MachineArchive archive)
    {
        var result = new MachineArchiveMetadataSnapshot
        {
            Version = archive.Version,
            RemovedGenerated = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal),
            NextPressureExplosionId = archive.NextPressureExplosionId,
            PressureExplosions = new List<PressureExplosionSnapshot>(),
            RecentPressureExplosionSources = archive.RecentPressureExplosionSources == null
                ? new List<string>() : new List<string>(archive.RecentPressureExplosionSources),
            PendingPressureLiquidSpills = new List<PressureLiquidSpillSnapshot>()
        };
        if (archive.RemovedGenerated != null)
            foreach (var pair in archive.RemovedGenerated)
                result.RemovedGenerated.Add(pair.Key, pair.Value == null ? null : new HashSet<int>(pair.Value));
        if (archive.PressureExplosions != null)
            foreach (var value in archive.PressureExplosions)
                result.PressureExplosions.Add(PressureExplosionSnapshot.Capture(value));
        if (archive.PendingPressureLiquidSpills != null)
            foreach (var value in archive.PendingPressureLiquidSpills)
                result.PendingPressureLiquidSpills.Add(PressureLiquidSpillSnapshot.Capture(value));
        return result;
    }

    public MachineArchive Restore()
    {
        var result = new MachineArchive
        {
            Version = Version, RemovedGenerated = RemovedGenerated ?? new Dictionary<string, HashSet<int>>(StringComparer.Ordinal),
            NextPressureExplosionId = NextPressureExplosionId,
            RecentPressureExplosionSources = RecentPressureExplosionSources ?? new List<string>()
        };
        if (PressureExplosions != null)
            foreach (var value in PressureExplosions)
                result.PressureExplosions.Add(value?.Restore());
        if (PendingPressureLiquidSpills != null)
            foreach (var value in PendingPressureLiquidSpills)
                result.PendingPressureLiquidSpills.Add(value?.Restore());
        return result;
    }
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PressureExplosionSnapshot
{
    #region 压力事件字段
    [MemoryPackOrder(0)] public ulong Id;
    [MemoryPackOrder(1)] public string WorldKey;
    [MemoryPackOrder(2)] public string SourceId;
    [MemoryPackOrder(3)] public string SpatialContextJson;
    [MemoryPackOrder(4)] public string PresentationWorldKey;
    [MemoryPackOrder(5)] public float CenterX;
    [MemoryPackOrder(6)] public float CenterY;
    [MemoryPackOrder(7)] public double Intensity;
    [MemoryPackOrder(8)] public double PressureEnergyJoules;
    [MemoryPackOrder(9)] public double ChemicalEnergyJoules;
    [MemoryPackOrder(10)] public double ProductHeatingJoules;
    [MemoryPackOrder(11)] public List<PressureExplosionMaterialSnapshot> Materials;

    public static PressureExplosionSnapshot Capture(PressureExplosionEvent value)
    {
        if (value == null) return null;
        var result = new PressureExplosionSnapshot
        {
            Id = value.Id, WorldKey = value.WorldKey, SourceId = value.SourceId,
            SpatialContextJson = value.SpatialContextJson, PresentationWorldKey = value.PresentationWorldKey,
            CenterX = value.Center.x, CenterY = value.Center.y, Intensity = value.Intensity,
            PressureEnergyJoules = value.PressureEnergyJoules, ChemicalEnergyJoules = value.ChemicalEnergyJoules,
            ProductHeatingJoules = value.ProductHeatingJoules, Materials = new List<PressureExplosionMaterialSnapshot>()
        };
        if (value.Materials != null)
            foreach (var material in value.Materials)
                result.Materials.Add(PressureExplosionMaterialSnapshot.Capture(material));
        return result;
    }

    public PressureExplosionEvent Restore()
    {
        var result = new PressureExplosionEvent
        {
            Id = Id, WorldKey = WorldKey, SourceId = SourceId, SpatialContextJson = SpatialContextJson,
            PresentationWorldKey = PresentationWorldKey, Center = new Vector2(CenterX, CenterY), Intensity = Intensity,
            PressureEnergyJoules = PressureEnergyJoules, ChemicalEnergyJoules = ChemicalEnergyJoules,
            ProductHeatingJoules = ProductHeatingJoules
        };
        if (Materials != null)
            foreach (var material in Materials) result.Materials.Add(material?.Restore());
        return result;
    }
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PressureExplosionMaterialSnapshot
{
    #region 气液物质字段
    [MemoryPackOrder(0)] public string FluidId;
    [MemoryPackOrder(1)] public string GasMoles;
    [MemoryPackOrder(2)] public string LiquidMoles;
    [MemoryPackOrder(3)] public double InternalEnergyJoules;

    public static PressureExplosionMaterialSnapshot Capture(PressureExplosionMaterial value) => value == null ? null : new PressureExplosionMaterialSnapshot
    {
        FluidId = value.FluidId, GasMoles = PersistentDecimalText.Format(value.GasMoles),
        LiquidMoles = PersistentDecimalText.Format(value.LiquidMoles), InternalEnergyJoules = value.InternalEnergyJoules
    };
    public PressureExplosionMaterial Restore() => new PressureExplosionMaterial
    {
        FluidId = FluidId, GasMoles = PersistentDecimalText.Parse(GasMoles), LiquidMoles = PersistentDecimalText.Parse(LiquidMoles),
        InternalEnergyJoules = InternalEnergyJoules
    };
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PressureLiquidSpillSnapshot
{
    #region 液体残余字段
    [MemoryPackOrder(0)] public string WorldKey;
    [MemoryPackOrder(1)] public string FluidId;
    [MemoryPackOrder(2)] public float CenterX;
    [MemoryPackOrder(3)] public float CenterY;
    [MemoryPackOrder(4)] public string LiquidMoles;
    [MemoryPackOrder(5)] public double InternalEnergyJoules;
    [MemoryPackOrder(6)] public int NextCellIndex;
    [MemoryPackOrder(7)] public string DetachedWorldKey;
    [MemoryPackOrder(8)] public float DetachedCenterX;
    [MemoryPackOrder(9)] public float DetachedCenterY;

    public static PressureLiquidSpillSnapshot Capture(PressureLiquidSpillState value) => value == null ? null : new PressureLiquidSpillSnapshot
    {
        WorldKey = value.WorldKey, FluidId = value.FluidId, CenterX = value.Center.x, CenterY = value.Center.y,
        LiquidMoles = PersistentDecimalText.Format(value.LiquidMoles), InternalEnergyJoules = value.InternalEnergyJoules,
        NextCellIndex = value.NextCellIndex, DetachedWorldKey = value.DetachedWorldKey,
        DetachedCenterX = value.DetachedCenter.x, DetachedCenterY = value.DetachedCenter.y
    };
    public PressureLiquidSpillState Restore() => new PressureLiquidSpillState
    {
        WorldKey = WorldKey, FluidId = FluidId, Center = new Vector2(CenterX, CenterY),
        LiquidMoles = PersistentDecimalText.Parse(LiquidMoles), InternalEnergyJoules = InternalEnergyJoules,
        NextCellIndex = NextCellIndex, DetachedWorldKey = DetachedWorldKey,
        DetachedCenter = new Vector2(DetachedCenterX, DetachedCenterY)
    };
    #endregion
}

using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>世界整份先换成真实十升，再按液体定义换算容器份数，抽取与回滚只走WorldLiquidSystem。</summary>
public sealed class WorldLiquidTransferPort : ContainerPortBase, ILiquidTransferPort, IQuantizedLiquidTransferPort
{
    #region 世界液体来源
    private readonly Vector2 worldPosition;
    private readonly WorldLiquidSourceTarget source;
    private readonly int maximumWorldUnits;
    private int pendingPumpedLevels;
    public WorldLiquidTransferPort(WorldLiquidSourceTarget source, int sceneHandle, int maximumWorldUnits = 4, float reach = 2f)
        : base($"world:{sceneHandle}:{source.WorldCell.x}:{source.WorldCell.y}", "world-liquid",
            new ContainerPortConfiguration { Id = "world-liquid", Type = "core:world_liquid", Direction = ContainerPortDirection.Output, Access = ContainerAccessKind.All, Reach = reach },
            () => source.Sample.Terrain != null && !source.Sample.Terrain.IsDisposed,
            position: (Vector2)source.WorldCell + Vector2.one * .5f, sceneHandle: sceneHandle)
    { this.source = source; worldPosition = (Vector2)source.WorldCell + Vector2.one * .5f; this.maximumWorldUnits = Math.Max(1, maximumWorldUnits); }
    private int Level => source.Sample.Terrain.GetLiquidLevel(source.Sample.LocalCell.x, source.Sample.LocalCell.y);
    private string LiquidId => source.Sample.Terrain.GetLiquidId(source.Sample.LocalCell.x, source.Sample.LocalCell.y);
    public override object StorageIdentity => source.Sample.Terrain;
    public override long StateVersion => unchecked((long)Level * 397 ^ (LiquidId?.GetHashCode() ?? 0));
    public bool PeekLiquid(out LiquidTransferBatch batch)
    {
        batch = default;
        if (!IsValid || ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var current) ||
            !ReferenceEquals(current.Terrain, source.Sample.Terrain) || !WorldLiquidSystem.TryGetDefinition(current, out var liquid)) return false;
        int worldUnits = Math.Min(maximumWorldUnits, Level);
        if (worldUnits <= 0) return false;
        batch = new(liquid.Id, WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(worldUnits, liquid.LitersPerServing), liquid.WorldWater.Temperature); return true;
    }
    public bool ReserveLiquid(out LiquidTransferBatch batch) => PeekLiquid(out batch);
    public float GetReceivableServings(string id, float requested) => 0f;
    public bool ExtractLiquid(LiquidTransferBatch batch, float servings)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(servings) || servings <= 0f ||
            !PeekLiquid(out var current) || current.LiquidId != batch.LiquidId || current.Servings < servings) return false;
        LiquidDefinition liquid = GameRes.ExistingInstance.GetLiquidDefinition(batch.LiquidId);
        int requested = WorldLiquidVolumeConversion.LiquidServingsToWholeWorldUnits(servings, liquid.LitersPerServing);
        if (requested <= 0 || WorldLiquidVolumeConversion.WorldUnitsToLiquidServings(requested, liquid.LitersPerServing) != servings) return false;
        int before = Level;
        if (!WorldLiquidSystem.TryPump(source.Sample, requested, out string id, out int removed)) return false;
        if (id != batch.LiquidId || removed != requested)
        { WorldLiquidSystem.TrySet(source.Sample, batch.LiquidId, before); return false; }
        pendingPumpedLevels += removed;
        return true;
    }
    public bool InsertLiquid(LiquidTransferBatch batch, float servings) => false;
    public float QuantizeTransferServings(string liquidId, float requested)
    {
        LiquidDefinition liquid = GameRes.ExistingInstance?.GetLiquidDefinition(liquidId);
        return liquid == null ? 0f : WorldLiquidVolumeConversion.QuantizeWholeWorldLiquidServings(requested, requested, liquid.LitersPerServing);
    }
    public override object CaptureState() => new Saved { Id = LiquidId, Level = Level, PendingPumpedLevels = pendingPumpedLevels };
    public override void RestoreState(object snapshot)
    {
        var saved = (Saved)snapshot;
        if (!WorldLiquidSystem.TrySet(source.Sample, saved.Id, saved.Level) &&
            (Level != saved.Level || !string.Equals(LiquidId, saved.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("世界液体事务回滚失败。");
        pendingPumpedLevels = saved.PendingPumpedLevels;
    }
    public override void PublishState()
    {
        if (pendingPumpedLevels <= 0) return;
        // 先清待发标记，订阅者重入时也不会重复发布同一笔取水。
        pendingPumpedLevels = 0;
        WorldLiquidSystem.PublishSurfaceLiquidPumped(source.Sample);
    }
    private sealed class Saved { public string Id; public int Level, PendingPumpedLevels; }
    #endregion
}

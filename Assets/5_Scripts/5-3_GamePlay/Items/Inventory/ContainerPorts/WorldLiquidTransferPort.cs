using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>世界液深以每份DepthPerServing显式换算，抽取与回滚只走WorldLiquidSystem。</summary>
public sealed class WorldLiquidTransferPort : ContainerPortBase, ILiquidTransferPort
{
    #region 世界液体来源
    private readonly Vector2 worldPosition;
    private readonly WorldLiquidSourceTarget source;
    private readonly int maximumServings;
    public WorldLiquidTransferPort(WorldLiquidSourceTarget source, int sceneHandle, int maximumServings = 4, float reach = 2f)
        : base($"world:{sceneHandle}:{source.WorldCell.x}:{source.WorldCell.y}", "world-liquid",
            new ContainerPortConfiguration { Id = "world-liquid", Type = "core:world_liquid", Direction = ContainerPortDirection.Output, Access = ContainerAccessKind.All, Reach = reach },
            () => source.Sample.Terrain != null && !source.Sample.Terrain.IsDisposed,
            position: (Vector2)source.WorldCell + Vector2.one * .5f, sceneHandle: sceneHandle)
    { this.source = source; worldPosition = (Vector2)source.WorldCell + Vector2.one * .5f; this.maximumServings = Math.Max(1, maximumServings); }
    private float Depth => source.Sample.Terrain.GetLiquidDepth(source.Sample.LocalCell.x, source.Sample.LocalCell.y);
    private string LiquidId => source.Sample.Terrain.GetLiquidId(source.Sample.LocalCell.x, source.Sample.LocalCell.y);
    public override object StorageIdentity => source.Sample.Terrain;
    public override long StateVersion => unchecked((long)Depth.GetHashCode() * 397 ^ (LiquidId?.GetHashCode() ?? 0));
    public bool PeekLiquid(out LiquidTransferBatch batch)
    {
        batch = default;
        if (!IsValid || ChunkMgr.ExistingInstance == null || !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(worldPosition, out var current) ||
            !ReferenceEquals(current.Terrain, source.Sample.Terrain) || !WorldLiquidSystem.TryGetDefinition(current, out var liquid)) return false;
        float servings = Mathf.Min(maximumServings, Depth / liquid.WorldWater.DepthPerServing);
        if (servings <= 0f) return false;
        batch = new(liquid.Id, servings, liquid.WorldWater.Temperature); return true;
    }
    public bool ReserveLiquid(out LiquidTransferBatch batch) => PeekLiquid(out batch);
    public float GetReceivableServings(string id, float requested) => 0f;
    public bool ExtractLiquid(LiquidTransferBatch batch, float servings)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(servings) || servings <= 0f || !PeekLiquid(out var current) || current.LiquidId != batch.LiquidId || current.Servings < servings) return false;
        float unitDepth = GameRes.ExistingInstance.GetLiquidDefinition(batch.LiquidId).WorldWater.DepthPerServing;
        float before = Depth;
        if (!WorldLiquidSystem.TryPump(source.Sample, servings * unitDepth, out string id, out float removed)) return false;
        if (id != batch.LiquidId || Math.Abs(removed - servings * unitDepth) > .000001f)
        { WorldLiquidSystem.TrySet(source.Sample, batch.LiquidId, before); return false; }
        return true;
    }
    public bool InsertLiquid(LiquidTransferBatch batch, float servings) => false;
    public override object CaptureState() => new Saved { Id = LiquidId, Depth = Depth };
    public override void RestoreState(object snapshot)
    { var saved = (Saved)snapshot; if (!WorldLiquidSystem.TrySet(source.Sample, saved.Id, saved.Depth) && Math.Abs(Depth - saved.Depth) > .000001f) throw new InvalidOperationException("世界液体事务回滚失败。"); }
    public override void PublishState() { }
    private sealed class Saved { public string Id; public float Depth; }
    #endregion
}

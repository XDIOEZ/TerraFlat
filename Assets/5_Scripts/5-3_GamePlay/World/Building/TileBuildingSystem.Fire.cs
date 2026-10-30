using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public static partial class TileBuildingSystem
{
    #region 格子建筑燃烧入口
    internal static bool BurnOutExposedSurface(in RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || sample.Terrain.IsDisposed ||
            !WorldFireSystem.TryGetTileFuel(sample.TopTileId, out var definition) || !definition.FireFuel.IsCombustible) return false;
        Vector2Int local = sample.LocalCell;
        if (TerrainSupportLayer.GetTileId(sample.Terrain, local.x, local.y) == sample.TopTileId)
        {
            TerrainSupportLayer.Set(sample.Terrain, local.x, local.y, 0, 0);
            SaveDataMgr.Instance?.RecordSupportCell(sample);
            RefreshSupportNavigation(sample.WorldCell);
            return true;
        }
        if (!GroundTileLayerSystem.TryGetUnderlying(sample.Terrain, local, out var underlying) ||
            !ChunkMgr.ExistingInstance.TryGetChunkRuntime(sample.Address, out var chunk)) return false;
        GroundTileLayerSystem.PopCover(sample.Terrain, local);
        sample.Terrain.SetCell(local.x, local.y, underlying.UnderlyingCell.ToTerrainCell());
        sample.Terrain.SetGrass(local.x, local.y, underlying.UnderlyingGrass);
        sample.Terrain.SetEnvironmentValue(RuntimeDamageLayerId, local.x, local.y, underlying.UnderlyingCell.AccumulatedDamage);
        SaveDataMgr.Instance?.RecordGroundLayerCell(sample);
        SaveDataMgr.Instance?.RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local,
            underlying.UnderlyingCell.GroundTileId, null));
        return true;
    }

    internal static void ApplyWorldFireExposure(in RuntimeTerrainTileSample sample, float exposureSeconds)
    {
        if (!GameNetwork.HasStateAuthority || exposureSeconds <= 0f || sample.Cell.BlockingTileId == 0 ||
            !WorldFireSystem.TryGetTileFuel(sample.Cell.BlockingTileId, out RuntimeTileDefinition definition) ||
            !definition.FireFuel.IsCombustible || definition.DamageProfile?.Damageable != true ||
            !ChunkMgr.ExistingInstance.TryGetChunkRuntime(sample.Address, out var chunk)) return;
        var target = new TileBuildingCell(chunk, sample.WorldCell, sample.LocalCell, definition.RuntimeTileId, definition.Id);
        var hit = new TileBuildingHitCandidate(target, (Vector2)sample.WorldCell + Vector2.one * .5f, 0f, 0f, 0f);
        float damage = definition.DamageProfile.MaxHealth * exposureSeconds / definition.FireFuel.BurnDurationSeconds;
        TryDamageRuntime(hit, null, out _, damage, spawnDestructionDrops: false);
    }
    #endregion
}

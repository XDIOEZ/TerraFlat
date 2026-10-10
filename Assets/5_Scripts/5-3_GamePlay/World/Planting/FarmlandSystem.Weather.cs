using FlatWorld.Networking;
using UnityEngine;

public static partial class FarmlandSystem
{
    #region 通用地表补水

    // 来源方只提供水量，土壤统一维护容量、水肥层和持久化。
    public static bool SupplySurfaceWater(RuntimeTerrainTileSample sample, float waterPoints, bool persist = true)
    {
        if (!GameNetwork.HasStateAuthority || !float.IsFinite(waterPoints) || waterPoints <= 0f ||
            !CanUseSoilCell(sample))
            return false;

        bool hasSoil = HasSoilState(sample);
        bool hasMoisture = sample.Terrain.TryGetEnvironmentValue("moisture", sample.LocalCell.x,
            sample.LocalCell.y, out float moisture);
        if (!hasSoil)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null || !resources.TryGetTileDefinition(sample.Cell.GroundTileId,
                    out RuntimeTileDefinition definition) || !definition.NaturalPlantable ||
                (!hasMoisture && !IsFarmland(sample.Cell)))
                return false;
        }

        var template = (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
        float maximum = Mathf.Max(0.01f, template.maxWater);
        float water = hasSoil ? Read(sample.Terrain, sample.LocalCell, WaterLayer) : Mathf.Clamp01(moisture) * maximum;
        float nextWater = Mathf.Min(maximum, water + waterPoints);
        if (nextWater <= water)
            return false;

        float fertility = hasSoil ? Read(sample.Terrain, sample.LocalCell, FertilityLayer) : template.Fertility;
        if (!hasSoil && sample.Terrain.TryGetEnvironmentValue("fertility", sample.LocalCell.x,
                sample.LocalCell.y, out float generatedFertility))
            fertility = generatedFertility;

        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        if (!hasSoil)
        {
            sample.Terrain.SetEnvironmentValue(SourceLayer, x, y, sample.Cell.GroundTileId);
            sample.Terrain.SetEnvironmentValue(FertilityLayer, x, y, fertility);
        }
        sample.Terrain.SetEnvironmentValue(WaterLayer, x, y, nextWater);
        SyncSoilEnvironment(sample.Terrain, x, y, nextWater, fertility);
        if (persist)
            SaveDataMgr.Instance.RecordAgricultureCell(sample);
        return true;
    }

    #endregion
}

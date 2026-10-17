using System;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestWorldRuntimeProbe
{
    #region 播种资格与食用优先级

    // 使用真实种子、土壤和作物占格采样资格，不接管玩家输入或重算种植规则。
    private static JObject CapturePlantingTarget(Player player, JObject args)
    {
        Site site = FindSite(player, sample =>
            FarmlandSystem.GetBlockId(sample.Cell.GroundTileId) is "Tile_Grass" or "Tile_Soil");
        Item seed = null;
        ChunkAgricultureRenderer agriculture = null;
        NaturalEntityHandle crop = default;
        var result = new JObject();
        try
        {
            ItemData seedData = GameRes.ExistingInstance.CreateItemData("Seed_Radish");
            seedData.Stack.Amount = 3f;
            seed = ItemMgr.Instance.InstantiateHeldItem(seedData, player, player.transform);
            seed.Load();
            Mod_Plantable plantable = seed.GetComponentInChildren<Mod_Plantable>(true) ??
                throw new InvalidOperationException("真实萝卜种子缺少种植模块。");
            plantable.maxPlantingDistance = 16f;
            result["ordinaryGroundAvailable"] = plantable.CanPlantAt(site.Center);
            result["ordinaryGroundPriority"] = plantable.ShouldPrioritizePlantingAt(site.Center);

            if (!FarmlandSystem.TryTill(site.Center, site.Authority, 16f, 1f, out bool completed) || !completed ||
                !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(site.Center, out RuntimeTerrainTileSample sample))
                throw new InvalidOperationException("播种回归没有生成正式耕地。");
            TileData_Farmland soil = FarmlandSystem.ReadSoilSnapshot(sample);
            soil.waterValue = (int)GameplayTestSystemProbe.Number(args, "water", 0);
            soil.fertilityValue = new GameValue_float((float)GameplayTestSystemProbe.Number(args, "fertility", 0));
            FarmlandSystem.CommitSoil(soil);
            result["water"] = soil.waterValue;
            result["fertility"] = soil.Fertility;
            result["emptyFarmlandAvailable"] = plantable.CanPlantAt(site.Center);
            result["emptyFarmlandPriority"] = plantable.ShouldPrioritizePlantingAt(site.Center);

            if (!ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(sample.Address, out ChunkView view) ||
                (agriculture = view.GetComponent<ChunkAgricultureRenderer>()) == null ||
                !GameRes.ExistingInstance.TryGetItemDefinition(plantable.cropItemId, out RuntimeItemDefinition definition) ||
                !definition.UsesResourceEntities)
                throw new InvalidOperationException("萝卜作物缺少正式 Entity 农业入口。");
            crop = agriculture.CreateEntityCrop(sample.WorldCell, definition);
            result["cropPresent"] = FarmlandSystem.HasWorldPlant(sample.WorldCell);
            result["occupiedAvailable"] = plantable.CanPlantAt(site.Center);
            result["occupiedPriority"] = plantable.ShouldPrioritizePlantingAt(site.Center);
            agriculture.RollbackEntityCrop(crop);
            crop = default;
            result["availableAfterCropRemoved"] = plantable.CanPlantAt(site.Center);

            plantable.maxPlantingDistance = 0f;
            result["outOfReachAvailable"] = plantable.CanPlantAt(site.Center);
            result["outOfReachPriority"] = plantable.ShouldPrioritizePlantingAt(site.Center);
            plantable.maxPlantingDistance = 16f;
            TileData_Farmland after = FarmlandSystem.ReadSoilSnapshot(sample);
            result["waterAfter"] = after.waterValue;
            result["fertilityAfter"] = after.Fertility;
            result["seedAmountAfter"] = seedData.Stack.Amount;
        }
        finally
        {
            if (crop.IsValid && agriculture != null) agriculture.RollbackEntityCrop(crop);
            if (seed != null) ItemMgr.Instance.DespawnItem(seed, saveData: false);
            site.Dispose();
            result["restored"] = site.IsRestored();
        }
        return result;
    }

    #endregion
}

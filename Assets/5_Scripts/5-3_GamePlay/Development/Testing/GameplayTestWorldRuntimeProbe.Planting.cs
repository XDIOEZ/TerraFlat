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

    #region 磷肥真实手持补给与拒绝
    private static JObject CapturePhosphateFertilizerHeldUseAndRejection(Player player, JObject args)
    {
        static bool SourceGround(RuntimeTerrainTileSample sample) =>
            FarmlandSystem.GetBlockId(sample.Cell.GroundTileId) is "Tile_Grass" or "Tile_Soil";
        Vector3 originalPlayerPosition = player.transform.position;
        Site near = FindSite(player, SourceGround);
        Site distant = null;
        Item held = null;
        var result = new JObject();
        try
        {
            // 陈列厅出生点可能处于大片石地，先在自有草土旁准备隔离玩家位置。
            player.transform.position = WorldLocalPresentation.ProjectPosition((Vector2)near.Center);
            distant = FindSite(player, sample => SourceGround(sample) && sample.WorldCell != near.Sample.WorldCell &&
                !FarmlandSystem.IsWithinReach(player.transform.position, sample.WorldCell, 2f));
            result["nearWithinReach"] = FarmlandSystem.IsWithinReach(player.transform.position, near.Sample.WorldCell, 2f);
            result["distantWithinReach"] = FarmlandSystem.IsWithinReach(player.transform.position, distant.Sample.WorldCell, 2f);
            ItemData source = GameRes.ExistingInstance.CreateItemData("Fertilizer_Phosphate");
            source.Stack.Amount = 3;
            held = ItemMgr.Instance.InstantiateHeldItem(source, player, player.transform);
            held.Load();
            Mod_FarmlandSupply supply = held.GetComponentInChildren<Mod_FarmlandSupply>(true) ??
                throw new InvalidOperationException("磷肥没有装配正式耕地补给模块。");
            result["held"] = held.InHand && ReferenceEquals(held.Owner, player);
            result["ordinaryAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            result["ordinaryAmount"] = source.Stack.Amount;
            TileData_Farmland Prepare(Site site)
            {
                if (!FarmlandSystem.TryTill(site.Center, site.Authority, 16f, 1, out bool complete) || !complete ||
                    !FarmlandSystem.TryReadSoil(site.Sample.WorldCell, out TileData_Farmland soil))
                    throw new InvalidOperationException("磷肥回归未能准备自有耕地。");
                soil.waterValue = 4;
                soil.fertilityValue = new GameValue_float(.2f);
                FarmlandSystem.CommitSoil(soil);
                return soil;
            }
            Prepare(near);
            Prepare(distant);
            source.Stack.Amount = 0;
            result["emptyAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            FarmlandSystem.TryReadSoil(near.Sample.WorldCell, out TileData_Farmland afterEmpty);
            result["emptyFertility"] = afterEmpty.Fertility;
            result["emptyAmount"] = source.Stack.Amount;
            source.Stack.Amount = 3;
            result["distantAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(distant.Center);
            FarmlandSystem.TryReadSoil(distant.Sample.WorldCell, out TileData_Farmland afterDistant);
            result["distantFertility"] = afterDistant.Fertility;
            result["distantAmount"] = source.Stack.Amount;
            held.SetInHand(false);
            result["notHeldAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            result["notHeldAmount"] = source.Stack.Amount;
            held.SetInHand(true);
            result["accepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            FarmlandSystem.TryReadSoil(near.Sample.WorldCell, out TileData_Farmland normal);
            result["fertility"] = normal.Fertility;
            result["water"] = normal.waterValue;
            result["amount"] = source.Stack.Amount;
            normal.fertilityValue = new GameValue_float(.9f);
            FarmlandSystem.CommitSoil(normal);
            result["nearlyFullAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            FarmlandSystem.TryReadSoil(near.Sample.WorldCell, out TileData_Farmland full);
            result["fullFertility"] = full.Fertility;
            result["fullWater"] = full.waterValue;
            result["nearlyFullAmount"] = source.Stack.Amount;
            result["fullAccepted"] = supply.TrySupplyFarmlandAtWorldPosition(near.Center);
            result["fullAmount"] = source.Stack.Amount;
        }
        finally
        {
            try
            {
                if (held != null) ItemMgr.Instance.DespawnItem(held, saveData: false);
            }
            finally
            {
                try { distant?.Dispose(); }
                finally
                {
                    try { near.Dispose(); }
                    finally { player.transform.position = originalPlayerPosition; }
                }
            }
            result["restored"] = near.IsRestored() && (distant == null || distant.IsRestored());
            result["playerRestored"] = Vector3.Distance(player.transform.position, originalPlayerPosition) < .001f;
            result["fertilizerReleased"] = held == null || held.IsInPool || held.DestructionHandled;
        }
        return result;
    }
    #endregion
}

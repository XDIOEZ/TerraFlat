using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Building;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>真实世界探针只修改自己选中的空格与自有物品，并通过正式事务采集实际效果。</summary>
public static partial class GameplayTestWorldRuntimeProbe
{
    #region 操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntimeOperations() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorOperations() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("agriculture", "tillage_irrigation", CaptureTillage);
        GameplayTestRuntimeProbe.Register("agriculture", "planting_target", CapturePlantingTarget);
        GameplayTestRuntimeProbe.Register("agriculture", "pour_absorption", CapturePourAbsorption);
        GameplayTestRuntimeProbe.Register("building", "placement", CapturePlacement);
        GameplayTestRuntimeProbe.Register("building", "facility_visual_scale", CaptureFacilityVisualScale);
        GameplayTestRuntimeProbe.Register("building", "host_placement_receipt", CaptureHostPlacementReceipt);
        GameplayTestRuntimeProbe.Register("fishing", "rig_roundtrip", CaptureFishingRig);
    }
    #endregion

    #region 自有空格与恢复
    private sealed class Site : IDisposable
    {
        public readonly RuntimeTerrainTileSample Sample;
        public readonly ChunkRuntime Chunk;
        public readonly TerrainCell Original;
        public readonly Vector3 Center;
        public readonly Vector3 Authority;
        private readonly Player player;
        private readonly GrassCellState? originalGrassDelta;
        private readonly bool originalLiquidChanged;
        private readonly Dictionary<string, float> environment = new(StringComparer.Ordinal);

        public Site(Player player, RuntimeTerrainTileSample sample, ChunkRuntime chunk, Vector3 authority)
        {
            this.player = player;
            Sample = sample; Chunk = chunk; Authority = authority;
            originalGrassDelta = SaveDataMgr.TestExistingInstance.CaptureTestRuntimeGrassDelta(player, sample);
            originalLiquidChanged = sample.Terrain.IsLiquidChanged(sample.LocalCell.x, sample.LocalCell.y);
            Original = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
            Center = new Vector3(sample.WorldCell.x + .5f, sample.WorldCell.y + .5f);
            foreach (string layer in new[] { FarmlandSystem.SourceLayer, FarmlandSystem.WaterLayer,
                         FarmlandSystem.WaterSupplyRemainderLayer, FarmlandSystem.WaterConsumptionRemainderLayer,
                         FarmlandSystem.FertilityLayer, "moisture", "fertility", TileBuildingSystem.RuntimeDamageLayerId })
            {
                sample.Terrain.TryGetEnvironmentValue(layer, sample.LocalCell.x, sample.LocalCell.y, out float value);
                environment.Add(layer, value);
            }
        }

        public void Dispose()
        {
            ChunkTerrainData terrain = Sample.Terrain;
            if (terrain == null || terrain.IsDisposed) throw new InvalidOperationException("测试空格所在区块已卸载，无法恢复。");
            TerrainCell current = terrain.GetCell(Sample.LocalCell.x, Sample.LocalCell.y);
            SaveDataMgr saves = SaveDataMgr.TestExistingInstance;
            if (current.BackTileId != Original.BackTileId || current.BlockingTileId != Original.BlockingTileId ||
                (current.GroundTileId != Original.GroundTileId && !FarmlandSystem.IsFarmland(current)) ||
                TerrainSupportLayer.GetTileId(terrain, Sample.LocalCell.x, Sample.LocalCell.y) != 0 ||
                saves.GetAgricultureCells(Sample.Address).Any(value => value.LocalPosition == Sample.LocalCell && value.Crop != null))
                throw new InvalidOperationException("测试空格出现其他来源的建筑或作物，拒绝覆盖非自有状态。");
            FarmlandSystem.ClearTillingProgress(Sample.Address, Sample.LocalCell);
            WorldLiquidSystem.TrySet(Sample, null, 0);
            saves.RecordLiquidChange(Sample);
            terrain.SetCell(Sample.LocalCell.x, Sample.LocalCell.y, Original);
            foreach (var entry in environment)
                terrain.SetEnvironmentValue(entry.Key, Sample.LocalCell.x, Sample.LocalCell.y, entry.Value);
            saves.RecordRuntimeTerrainChange(new TileBuildingCell(Chunk, Sample.WorldCell, Sample.LocalCell,
                Original.GroundTileId, FarmlandSystem.GetBlockId(Original.GroundTileId)));
            if (!saves.RestoreTestRuntimeGrassDelta(player, Sample, originalGrassDelta))
                throw new InvalidOperationException("自有空格草差量无法恢复，拒绝改写其他来源的草状态。");
            if (!saves.TryRemoveUninitializedAgricultureCell(Sample))
                throw new InvalidOperationException("自有空格农业差量无法移除，拒绝留下未清理现场。");
            UnityEngine.Object.FindObjectOfType<WorldNavigationManager>()?.QueueNavigationRegion(
                new RectInt(Sample.WorldCell.x, Sample.WorldCell.y, 1, 1));
        }

        public bool IsRestored() => Sample.Terrain.GetCell(Sample.LocalCell.x, Sample.LocalCell.y).Equals(Original) &&
            environment.All(entry => ReadEnvironment(Sample, entry.Key) == entry.Value) &&
            SaveDataMgr.TestExistingInstance.CaptureTestRuntimeGrassDelta(player, Sample) == originalGrassDelta &&
            Sample.Terrain.GetLiquidLevel(Sample.LocalCell.x, Sample.LocalCell.y) == 0 &&
            Sample.Terrain.IsLiquidChanged(Sample.LocalCell.x, Sample.LocalCell.y) == originalLiquidChanged &&
            !SaveDataMgr.TestExistingInstance.GetAgricultureCells(Sample.Address).Any(value => value.LocalPosition == Sample.LocalCell);
    }

    private static float ReadEnvironment(RuntimeTerrainTileSample sample, string layer)
    {
        sample.Terrain.TryGetEnvironmentValue(layer, sample.LocalCell.x, sample.LocalCell.y, out float value);
        return value;
    }

    private static Site FindSite(Player player, Func<RuntimeTerrainTileSample, bool> accepts)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        ChunkMgr manager = ChunkMgr.ExistingInstance ?? throw new InvalidOperationException("真实区块管理器未就绪。");
        Vector3 authority = WorldLocalPresentation.ToLogical(player.transform.position);
        Vector2Int origin = Vector2Int.FloorToInt(authority);
        for (int radius = 2; radius <= 8; radius++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
            Vector2Int cell = WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(dx, dy));
            Vector2 center = (Vector2)cell + Vector2.one * .5f;
            if (!manager.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample sample) || !FarmlandSystem.IsOpen(sample) ||
                sample.Terrain.GetGrass(sample.LocalCell.x, sample.LocalCell.y) == ChunkTerrainData.GrassPresent || FarmlandSystem.HasWorldPlant(cell) ||
                ReadEnvironment(sample, FarmlandSystem.SourceLayer) != 0f ||
                FarmlandSystem.GetTillingVisualProgress(sample.Address, sample.LocalCell, sample.Cell.GroundTileId) > 0f ||
                SaveDataMgr.TestExistingInstance.GetAgricultureCells(sample.Address).Any(value => value.LocalPosition == sample.LocalCell) ||
                !manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk) ||
                !manager.TryGetRuntimeChunkView(sample.Address, out var view) || view == null || !view.gameObject.activeInHierarchy ||
                !accepts(sample)) continue;
            bool dryNeighborhood = true;
            for (int ny = -1; ny <= 1 && dryNeighborhood; ny++)
            for (int nx = -1; nx <= 1; nx++)
                if (!manager.TryGetRuntimeTerrainTile(center + new Vector2(nx, ny), out RuntimeTerrainTileSample neighbor) ||
                    neighbor.Terrain.GetLiquidDepth(neighbor.LocalCell.x, neighbor.LocalCell.y) > 0f)
                { dryNeighborhood = false; break; }
            if (dryNeighborhood) return new Site(player, sample, chunk, authority);
        }
        throw new InvalidOperationException("附近没有满足正式资格且可独立恢复的空格；需要准备干燥、无草、无作物的隔离场地。");
    }
    #endregion

    #region 正式耕作与灌溉
    private static JObject CaptureTillage(Player player, JObject args)
    {
        Site site = FindSite(player, sample =>
        {
            string id = FarmlandSystem.GetBlockId(sample.Cell.GroundTileId);
            return id == "Tile_Grass" || id == "Tile_Soil";
        });
        var result = new JObject();
        try
        {
            bool partial = FarmlandSystem.TryTill(site.Center, site.Authority, 16f, .25f, out bool partialCompleted);
            int partialGround = site.Sample.Terrain.GetCell(site.Sample.LocalCell.x, site.Sample.LocalCell.y).GroundTileId;
            var tillingCells = new List<Vector2Int>();
            FarmlandSystem.CollectTillingVisualCells(site.Sample.Address, tillingCells);
            result["partialVisualIndexed"] = tillingCells.Contains(site.Sample.LocalCell);
            bool completed = FarmlandSystem.TryTill(site.Center, site.Authority, 16f, .75f, out bool tillCompleted);
            tillingCells.Clear();
            FarmlandSystem.CollectTillingVisualCells(site.Sample.Address, tillingCells);
            result["completedVisualRemoved"] = !tillingCells.Contains(site.Sample.LocalCell);
            bool soilReady = FarmlandSystem.TryReadSoil(site.Sample.WorldCell, out TileData_Farmland soil);
            if (!soilReady || soil == null) throw new InvalidOperationException("正式耕作没有产生可读取的真实土壤状态。");
            soil.ConsumeWater(soil.maxWater);
            FarmlandSystem.CommitSoil(soil);
            var behavior = new Tile_Farmland();
            behavior.Irrigate(soil, 3);
            FarmlandSystem.CommitSoil(soil);
            float afterIrrigation;
            using (FarmlandSystem.SoilSamplingBatch batch = FarmlandSystem.CreateSoilSamplingBatch())
            {
                if (!batch.TryRead(site.Sample.WorldCell, out TileData_Farmland watered,
                        out RuntimeTerrainTileSample sampled, allowNaturalSoil: false))
                    throw new InvalidOperationException("农业批次未取得真实耕地。");
                afterIrrigation = watered.waterValue;
                behavior.ConsumeWater(watered, 1);
                watered.AccumulateWaterSupply(.25f);
                watered.AccumulateWaterConsumption(.5f);
                batch.CommitSampled(sampled, watered, allowNaturalSoil: false);
                result["sampledCommit"] = true;
            }
            FarmlandSystem.TryReadSoil(site.Sample.WorldCell, out TileData_Farmland consumed);
            AgricultureCellSaveData saved = SaveDataMgr.TestExistingInstance.GetAgricultureCells(site.Sample.Address)
                .FirstOrDefault(value => value.LocalPosition == site.Sample.LocalCell);
            result["partialAccepted"] = partial; result["partialCompleted"] = partialCompleted;
            result["partialGroundPreserved"] = partialGround == site.Original.GroundTileId;
            result["completionAccepted"] = completed; result["tillCompleted"] = tillCompleted;
            result["groundAfter"] = FarmlandSystem.GetBlockId(site.Sample.Terrain.GetCell(site.Sample.LocalCell.x, site.Sample.LocalCell.y).GroundTileId);
            result["afterIrrigation"] = afterIrrigation; result["afterConsumption"] = consumed?.waterValue;
            result["savedWater"] = saved?.Water; result["savedSource"] = saved?.SourceTileId;
            result["savedSupplyRemainder"] = saved?.WaterSupplyRemainder;
            result["savedConsumptionRemainder"] = saved?.WaterConsumptionRemainder;
            result["supplyRemainder"] = consumed?.waterSupplyRemainder;
            result["consumptionRemainder"] = consumed?.waterConsumptionRemainder;
            result["currentSource"] = ReadEnvironment(site.Sample, FarmlandSystem.SourceLayer);
            result["savedCropAbsent"] = saved != null && saved.Crop == null;
            return result;
        }
        finally
        {
            site.Dispose();
            result["restored"] = site.IsRestored();
        }
    }

    // 两格各自保留独立现场，倒水时只让目标格吸收，其余有水耕地保持不变。
    private static JObject CapturePourAbsorption(Player player, JObject args)
    {
        static bool TillingSource(RuntimeTerrainTileSample sample) =>
            FarmlandSystem.GetBlockId(sample.Cell.GroundTileId) is "Tile_Grass" or "Tile_Soil";
        Site targetSite = FindSite(player, TillingSource);
        Site untouchedSite = null;
        var result = new JObject();
        try
        {
            untouchedSite = FindSite(player, sample => sample.WorldCell != targetSite.Sample.WorldCell && TillingSource(sample));
            RuntimeTerrainTileSample Prepare(Site site)
            {
                if (!FarmlandSystem.TryTill(site.Center, site.Authority, 16f, 1f, out bool completed) || !completed ||
                    !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(site.Center, out RuntimeTerrainTileSample sample))
                    throw new InvalidOperationException("倒水回归没有生成正式耕地。");
                TileData_Farmland soil = FarmlandSystem.ReadSoilSnapshot(sample);
                soil.ConsumeWater(soil.maxWater);
                FarmlandSystem.CommitSoil(soil);
                return sample;
            }
            RuntimeTerrainTileSample target = Prepare(targetSite);
            RuntimeTerrainTileSample untouched = Prepare(untouchedSite);
            if (!WorldLiquidSystem.TrySet(untouched, "core:pure_water", 2))
                throw new InvalidOperationException("无法给不应触发吸收的对照格放水。");
            result["targetPourAccepted"] = WorldLiquidSystem.TryPour(targetSite.Center, "core:pure_water", 3, out int firstAdded);
            result["firstAdded"] = firstAdded;
            result["targetWater"] = FarmlandSystem.ReadWaterLevel(target.Terrain, target.LocalCell);
            result["targetLiquid"] = target.LiquidLevel;
            result["untouchedWater"] = FarmlandSystem.ReadWaterLevel(untouched.Terrain, untouched.LocalCell);
            result["untouchedLiquid"] = untouched.LiquidLevel;

            TileData_Farmland nearlyFull = FarmlandSystem.ReadSoilSnapshot(target);
            nearlyFull.waterValue = 9;
            FarmlandSystem.CommitSoil(nearlyFull);
            result["overflowPourAccepted"] = WorldLiquidSystem.TryPour(targetSite.Center, "core:pure_water", 3, out int overflowAdded);
            result["overflowAdded"] = overflowAdded;
            result["fullWater"] = FarmlandSystem.ReadWaterLevel(target.Terrain, target.LocalCell);
            result["overflowLiquid"] = target.LiquidLevel;
            result["fullPourAccepted"] = WorldLiquidSystem.TryPour(targetSite.Center, "core:pure_water", 1, out int fullAdded);
            result["fullAdded"] = fullAdded;
            result["fullWaterAfterExtra"] = FarmlandSystem.ReadWaterLevel(target.Terrain, target.LocalCell);
            result["extraLiquid"] = target.LiquidLevel;

            WorldLiquidSystem.TrySet(target, null, 0);
            TileData_Farmland dry = FarmlandSystem.ReadSoilSnapshot(target);
            dry.ConsumeWater(dry.maxWater);
            FarmlandSystem.CommitSoil(dry);
            result["lavaPourAccepted"] = WorldLiquidSystem.TryPour(targetSite.Center, "core:lava", 1, out int lavaAdded);
            result["lavaAdded"] = lavaAdded;
            result["waterAfterLava"] = FarmlandSystem.ReadWaterLevel(target.Terrain, target.LocalCell);
            result["lavaLiquid"] = target.LiquidLevel;
            return result;
        }
        finally
        {
            untouchedSite?.Dispose();
            targetSite.Dispose();
            result["restored"] = targetSite.IsRestored() && (untouchedSite == null || untouchedSite.IsRestored());
        }
    }
    #endregion

    #region 正式放置扣料与失败回滚
    private static JObject CapturePlacement(Player player, JObject args)
    {
        GameRes resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("正式资源目录未就绪。");
        string id = GameplayTestSystemProbe.Text(args, "summonerId", "Wall_Wood_Summoner");
        ItemData source = resources.CreateItemData(id);
        source.Stack.Amount = 2f;
        if (!Mod_Building.TryReadBuildingData(source, out _, out Mod_Building.Building_Data state) || string.IsNullOrEmpty(state.TileBlockId))
            throw new InvalidOperationException("测试召唤器没有正式格子建筑入口：" + id);
        bool support = TileBuildingSystem.IsGroundPlacement(state.TileBlockId);
        bool water = resources.GetTileBlock(state.TileBlockId).groundPlacement?.LiquidRequirement == GroundPlacementLiquidRequirement.Present;
        Site site = FindSite(player, sample => water || Mod_Building.ValidateTileDataPlacement(source,
            new Vector3(sample.WorldCell.x + .5f, sample.WorldCell.y + .5f), out _));
        int ReadPlacedTile() => support
            ? TerrainSupportLayer.GetTileId(site.Sample.Terrain, site.Sample.LocalCell.x, site.Sample.LocalCell.y)
            : site.Sample.Terrain.GetCell(site.Sample.LocalCell.x, site.Sample.LocalCell.y).BlockingTileId;
        var slot = new ItemSlot(0) { itemData = source };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "独立建筑材料样本");
        TileBuildingCell? ownPlacement = null;
        int placementWrites = 0, tileAtRejectedPublish = 0;
        float amountAtRejectedPublish = -1f;
        var result = new JObject();
        Action<TileBuildingCell> capture = cell =>
        {
            if (cell.Position != site.Sample.WorldCell) return;
            ownPlacement = cell;
            placementWrites++;
        };
        BuildingPlacementRequest Request(bool publish) => new()
        {
            SourceData = slot.itemData, SourceInventory = inventory, SourceSlot = slot,
            Position = site.Center, AuthorityPosition = site.Authority, MaximumDistance = 16f,
            Publish = candidate =>
            {
                result["publishHasTile"] = candidate.TileCell.HasValue;
                if (!publish)
                {
                    amountAtRejectedPublish = slot.itemData?.Stack?.Amount ?? 0f;
                    tileAtRejectedPublish = ReadPlacedTile();
                }
                return publish;
            }
        };
        // 使用实际 Tile 后端，只在事务发布阶段注入拒绝，不伪造放置或库存结果。
        TileBuildingSystem.CellPlaced += capture;
        try
        {
            if (water && !WorldLiquidSystem.TrySet(site.Sample, "core:pure_water", 8))
                throw new InvalidOperationException("隔离平台测试无法准备真实水格。");
            bool rejected = BuildingPlacementService.TryPlace(Request(false), out _, out string rejection);
            float amountAfterRejection = slot.itemData?.Stack?.Amount ?? 0f;
            int writesAfterRejection = placementWrites;
            bool terrainAfterRejection = site.Sample.Terrain.GetCell(site.Sample.LocalCell.x, site.Sample.LocalCell.y).Equals(site.Original) &&
                TerrainSupportLayer.GetTileId(site.Sample.Terrain, site.Sample.LocalCell.x, site.Sample.LocalCell.y) == 0;
            bool placed = BuildingPlacementService.TryPlace(Request(true), out BuildingPlacementResult placement, out string reason);
            int installedTile = ReadPlacedTile();
            float amountAfterPlacement = slot.itemData?.Stack?.Amount ?? 0f;
            bool overlap = BuildingPlacementService.TryPlace(Request(true), out _, out string overlapReason);
            result["rejectedAccepted"] = rejected; result["rejectionReason"] = rejection;
            result["writesAfterRejection"] = writesAfterRejection;
            result["amountAtRejectedPublish"] = amountAtRejectedPublish; result["tileAtRejectedPublish"] = tileAtRejectedPublish;
            result["amountAfterRejection"] = amountAfterRejection; result["terrainAfterRejection"] = terrainAfterRejection;
            result["placed"] = placed; result["placementReason"] = reason;
            result["installedTile"] = installedTile; result["expectedTile"] = resources.GetTileBlock(state.TileBlockId)?.RuntimeTileId;
            result["placedId"] = placement?.BuildingId; result["expectedBuildingId"] = state.BuildingPrefabId;
            result["amountAfterPlacement"] = amountAfterPlacement; result["remainingAmount"] = placement?.RemainingAmount;
            result["overlapAccepted"] = overlap; result["overlapReason"] = overlapReason;
            result["amountAfterOverlap"] = slot.itemData?.Stack?.Amount ?? 0f;
            result["placementWrites"] = placementWrites;
            result["liquidAfterPlacement"] = site.Sample.Terrain.GetLiquidLevel(site.Sample.LocalCell.x, site.Sample.LocalCell.y);
            result["expectedLiquid"] = water ? 8 : 0;
            result["returnedTile"] = placement?.TileCell?.RuntimeTileId ?? 0;
            result["returnedSupport"] = placement?.TileCell?.ReplacedGroundCell.HasValue ?? false;
            result["expectedSupport"] = support;
            return result;
        }
        finally
        {
            TileBuildingSystem.CellPlaced -= capture;
            int current = ReadPlacedTile();
            if (ownPlacement.HasValue && current == ownPlacement.Value.RuntimeTileId && current != 0 &&
                !TileBuildingSystem.TryRemove(ownPlacement.Value, false, out string removeReason))
                throw new InvalidOperationException("自有格子建筑清理失败：" + removeReason);
            site.Dispose();
            result["restored"] = site.IsRestored();
        }
    }
    #endregion

    #region 落地设施主体与阴影尺寸
    // 实走材料扣除和机器落地，从正式绘制提交采样，避免只检查预览配置。
    private static JObject CaptureFacilityVisualScale(Player player, JObject args)
    {
        GameRes resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("正式资源目录未就绪。");
        string id = GameplayTestSystemProbe.Text(args, "buildingId", "BlastFurnace");
        if (!resources.TryGetItemDefinition(id, out RuntimeItemDefinition definition))
            throw new InvalidOperationException("缺少设施本体定义：" + id);
        ItemData source = resources.CreateItemData(id + "_Summoner");
        source.Stack.Amount = 2f;
        var slot = new ItemSlot(0) { itemData = source };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "独立设施材料样本");
        Site site = FindSite(player, sample => MachineWorld.GetAt(sample.WorldCell, 0) == null);
        BuildingPlacementResult placement = null;
        var result = new JObject();
        try
        {
            var request = new BuildingPlacementRequest
            {
                SourceData = source, SourceInventory = inventory, SourceSlot = slot,
                Position = site.Center, AuthorityPosition = site.Authority, MaximumDistance = 16f
            };
            if (!BuildingPlacementService.TryPlace(request, out placement, out string reason) || placement.Machine == null)
                throw new InvalidOperationException("正式设施放置失败：" + reason);
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(site.Sample.Address, out var view) || view == null)
                throw new InvalidOperationException("落地设施所属区块没有真实表现。");
            ChunkTilemapRenderer owner = view.GetComponentInChildren<ChunkTilemapRenderer>(true);
            if (owner == null) throw new InvalidOperationException("落地设施所属区块没有正式绘制器。");
            Vector3 origin = site.Center + owner.DepthPresentationOffset;
            MechanicalShadowRegistry.Entry shadow = MechanicalShadowRegistry.Entries.FirstOrDefault(entry =>
                entry.Active && entry.PrimarySprite == definition.Sprite && entry.Parts.Any(part =>
                    part != null && part.Active && part.Sprite == definition.Sprite && (part.Origin - origin).sqrMagnitude < .000001f));
            MechanicalShadowRegistry.Part body = shadow?.Parts.FirstOrDefault(part =>
                part != null && part.Active && part.Sprite == definition.Sprite);
            if (body == null) throw new InvalidOperationException("落地设施没有提交真实主体与阴影部件。");
            body.ResolveTransform(out Vector3 position, out Quaternion rotation);
            Bounds bounds = ShadowFootprintResolver.MeasureVisibleWorldBounds(body.Sprite, Matrix4x4.TRS(position, rotation, body.Scale));
            Vector3 configuredScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
            Vector3 configuredOffset = definition.Visual?.RendererLocalPosition ?? Vector3.zero;
            Vector3 expectedFoot = ShadowFootprintResolver.ResolveFoot(Matrix4x4.TRS(position, rotation, body.Scale), bounds,
                definition.Visual?.Shadows?.FootLocalPosition, 0f, definition.Visual?.Shadows?.FootOverlap);
            result["placedId"] = placement.BuildingId;
            result["amountAfterPlacement"] = slot.itemData?.Stack?.Amount ?? 0f;
            result["scaleX"] = body.Scale.x; result["scaleY"] = body.Scale.y;
            result["configuredScaleX"] = configuredScale.x; result["configuredScaleY"] = configuredScale.y;
            result["offsetY"] = body.Offset.y; result["configuredOffsetY"] = configuredOffset.y;
            result["visibleWidth"] = bounds.size.x; result["visibleHeight"] = bounds.size.y;
            result["shadowFootError"] = Vector3.Distance(shadow.Foot, expectedFoot);
            return result;
        }
        finally
        {
            try
            {
                if (placement?.Machine != null && !MachineWorld.DiscardPlacementCandidate(placement.Machine.Id))
                    throw new InvalidOperationException("自有设施清理失败。");
            }
            finally
            {
                site.Dispose();
                result["restored"] = site.IsRestored() &&
                    (placement?.Machine == null || MachineWorld.GetById(placement.Machine.Id) == null);
            }
        }
    }
    #endregion

    #region Host 共享库存建造回执
    // 独立载体走正式安装入口和放置事务，只替换传输桥来控制成功回执到达时机。
    private static JObject CaptureHostPlacementReceipt(Player player, JObject args)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority)
            throw new InvalidOperationException("Host 建造回执采样需要世界权威。");
        ItemData source = GameRes.ExistingInstance.CreateItemData("Wall_Wood_Summoner");
        source.Stack.Amount = (float)GameplayTestSystemProbe.Number(args, "amount", 1);
        if (!Mod_Building.TryReadBuildingData(source, out Ex_ModData stored, out Mod_Building.Building_Data state))
            throw new InvalidOperationException("Host 建造回执采样缺少建筑模块。");
        Site site = FindSite(player, sample => Mod_Building.ValidateTileDataPlacement(source,
            new Vector3(sample.WorldCell.x + .5f, sample.WorldCell.y + .5f), out _));
        var root = new GameObject("[HostPlacementReceipt]");
        root.SetActive(false);
        var slot = new ItemSlot(0) { itemData = source };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "独立Host建造材料");
        var previousBridge = ItemNetworkStateSerialization.TryBeginNetworkBuilding;
        TileBuildingCell? ownPlacement = null;
        BuildingPlacementResult placement = null;
        var errors = new JArray();
        var result = new JObject();
        void CaptureCell(TileBuildingCell cell) { if (cell.Position == site.Sample.WorldCell) ownPlacement = cell; }
        void CaptureLog(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        TileBuildingSystem.CellPlaced += CaptureCell;
        Application.logMessageReceived += CaptureLog;
        try
        {
            root.transform.position = WorldLocalPresentation.ProjectPosition(site.Center);
            GameItem owner = root.AddComponent<GameItem>();
            owner.BindData(new Data_GeneralItem { IDName = "test:host-placement-owner" });
            var carrierRoot = new GameObject("carrier");
            carrierRoot.transform.SetParent(root.transform, false);
            GameItem carrier = carrierRoot.AddComponent<GameItem>();
            carrier.BindData(source);
            carrier.Owner = owner;
            carrier.SetInHand(true);
            Mod_Building building = carrierRoot.AddComponent<Mod_Building>();
            building.ModuleInit(carrier, stored);
            building.Data = state;
            ItemNetworkStateSerialization.TryBeginNetworkBuilding = (pending, position) =>
            {
                if (!ReferenceEquals(pending, building)) throw new InvalidOperationException("回执采样收到其他建筑请求。");
                var request = new BuildingPlacementRequest
                {
                    SourceData = source, SourceInventory = inventory, SourceSlot = slot,
                    Position = position, AuthorityPosition = site.Center, MaximumDistance = 16f
                };
                if (!BuildingPlacementService.TryPlace(request, out placement, out string reason))
                    throw new InvalidOperationException(reason);
                return true;
            };
            building.Install();
            if (placement == null || !building.IsPlacementPending)
                throw new InvalidOperationException("正式安装入口没有形成待确认的建造事务。");
            result["afterServer"] = slot.itemData?.Stack?.Amount ?? 0f;
            int receiptWrites = 0;
            inventory.Event_OnDataChanged += _ => receiptWrites++;
            building.CompleteNetworkPlacement(placement.RemainingAmount);
            building.CompleteNetworkPlacement(placement.RemainingAmount);
            result["afterReceipt"] = slot.itemData?.Stack?.Amount ?? 0f;
            result["remainingAmount"] = placement.RemainingAmount;
            result["pending"] = building.IsPlacementPending;
            result["receiptWrites"] = receiptWrites;
            result["installed"] = ownPlacement.HasValue;
            result["errors"] = errors;
            return result;
        }
        finally
        {
            ItemNetworkStateSerialization.TryBeginNetworkBuilding = previousBridge;
            Application.logMessageReceived -= CaptureLog;
            TileBuildingSystem.CellPlaced -= CaptureCell;
            UnityEngine.Object.DestroyImmediate(root);
            if (ownPlacement.HasValue && !TileBuildingSystem.TryRemove(ownPlacement.Value, false, out string reason))
                throw new InvalidOperationException("Host 回执采样撤墙失败：" + reason);
            site.Dispose();
            result["restored"] = site.IsRestored();
        }
    }
    #endregion

    #region 正式钓具加载保存与恢复
    private static JObject CaptureFishingRig(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        GameRes resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("正式资源目录未就绪。");
        ItemMgr manager = ItemMgr.Instance ?? throw new InvalidOperationException("真实物品管理器未就绪。");
        Item first = null, restored = null;
        var result = new JObject();
        try
        {
            string rodId = GameplayTestSystemProbe.Text(args, "rodId", "FishingRod");
            ItemData hook = resources.CreateItemData(GameplayTestSystemProbe.Text(args, "hookId", "FishingHook_Bone"));
            ItemData bait = null;
            foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values
                         .Where(value => Mod_FishingRod.FoodTags.Any(value.HasTag))
                         .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                ItemData candidate = resources.CreateItemData(definition.Id);
                if (candidate.Tags?.ContainsAnyTag(Mod_FishingRod.FoodTags) == true && candidate.ModuleDataDic.Values
                        .Any(module => module is ModData_FoodData food && food.EnsureFoodData()?.nutrition != null))
                { bait = candidate; break; }
            }
            if (bait == null) throw new InvalidOperationException("正式目录没有可作为鱼饵的食品，不能伪造鱼饵。");
            first = manager.InstantiateItem(rodId, player.transform.position + Vector3.right * 2f);
            first.Load();
            Mod_FishingRod[] rods = first.GetComponentsInChildren<Mod_FishingRod>(true);
            if (rods.Length != 1 || rods[0].RigData?.itemSlots.Count != 2)
                throw new InvalidOperationException("正式钓竿没有初始化唯一钓具模块和两槽库存。");
            Mod_FishingRod rod = rods[0];
            if (!rod.RigData.TrySetSlotItemAmount(rod.RigData.itemSlots[0], hook, 1f) ||
                !rod.RigData.TrySetSlotItemAmount(rod.RigData.itemSlots[1], bait, 1f))
                throw new InvalidOperationException("钓具真实库存事务没有写入鱼钩/鱼饵。");
            float initialHookDurability = hook.Durability;
            if (!rod.RigData.TryConsumeDurability(hook, 1f, out bool hookBroke) || hookBroke)
                throw new InvalidOperationException("正式鱼钩无法提交一次耐久消耗。");
            first.Save();
            uint beforeGeneration = first.RuntimeGeneration;
            bool saved = ItemNetworkStateSerialization.TrySerializeItemData(first.itemData, out byte[] payload);
            if (!saved) throw new InvalidOperationException("正式钓具快照封包失败。");
            manager.DespawnItem(first, false); first = null;
            bool decoded = ItemNetworkStateSerialization.TryDeserializeItemData(payload, out ItemData data);
            if (!decoded) throw new InvalidOperationException("正式钓具快照恢复失败。");
            restored = manager.InstantiateItem(data, player.transform.position + Vector3.right * 2f);
            restored.Load();
            Mod_FishingRod loaded = restored.GetComponentInChildren<Mod_FishingRod>(true);
            if (loaded?.RigData?.itemSlots.Count != 2) throw new InvalidOperationException("恢复钓具丢失真实库存布局。");
            ItemData loadedHook = loaded.RigData.itemSlots[0].itemData, loadedBait = loaded.RigData.itemSlots[1].itemData;
            result["saved"] = saved; result["decoded"] = decoded; result["payloadBytes"] = payload.Length;
            result["loaded"] = restored.IsInitialized && loaded.IsRuntimeLoaded;
            result["generationChanged"] = restored.RuntimeGeneration != beforeGeneration;
            result["hookId"] = loadedHook?.IDName; result["expectedHookId"] = hook.IDName;
            result["baitId"] = loadedBait?.IDName; result["expectedBaitId"] = bait.IDName;
            result["hookAmount"] = loadedHook?.Stack?.Amount; result["baitAmount"] = loadedBait?.Stack?.Amount;
            result["initialHookDurability"] = initialHookDurability;
            result["hookDurability"] = loadedHook?.Durability;
            result["hookMaxDurability"] = loadedHook?.MaxDurability;
            result["hookGuidPreserved"] = loadedHook != null && loadedHook.Guid == hook.Guid;
            result["baitGuidPreserved"] = loadedBait != null && loadedBait.Guid == bait.Guid;
            result["hookCapacity"] = loaded.RigData.itemSlots[0].SlotMaxVolume;
            result["baitCapacity"] = loaded.RigData.itemSlots[1].SlotMaxVolume;
            return result;
        }
        finally
        {
            if (first != null && !first.DestructionHandled) manager.DespawnItem(first, false);
            if (restored != null && !restored.DestructionHandled) manager.DespawnItem(restored, false);
            result["fixturesReleased"] = (first == null || first.IsInPool || first.DestructionHandled) &&
                (restored == null || restored.IsInPool || restored.DestructionHandled);
        }
    }
    #endregion
}

using System;
using System.Collections.Generic;
using FlatWorld.AIECS;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.NaturalEntities
{
    #region 无外壳的配置描述

    internal sealed partial class NaturalEntityEcsProfile
    {
        public string CropModuleName, StockModuleName, ProductionModuleName, CanopyModuleName;
        public CropRuntimeData CropDefaults;
        public ResourceCollectionDefinition Collection;
        public ResourceCanopyDefinition Canopy;
        public ResourceCropVisualDefinition CropVisual;
        public ResourceTemperatureYieldDefinition TemperatureYield;
        public List<CropYieldEntry> HarvestOutputs;
        public List<Mod_Production.ItemProductionData> Production;
        public float ProductionSpeed = 1f;
        public bool ProductionUsesGrowthDifficulty, AllowCultivatedHarvest;
        public string TreeFoodId, TreeSeedId;
        public int TreeFoodMinimum, TreeFoodMaximum;
        public EntityPlantSoil Soil;
        public JObject HealthParameters;
        public bool HasCrop => CropDefaults != null;
    }

    internal sealed class ResourceCollectionDefinition
    {
        public string CollectItemId = "Berry";
        public int MaxStock = 12, NaturalInitialStockMin = 1, NaturalInitialStockMax = 2;
        public float SpawnRadius = 1.2f, ThrowDuration = 0.5f, ThrowBezierOffset = 0.8f, ThrowArcHeight = 0.6f;
        public List<Vector3> IndicatorLocalPositions = new() { new(0.256f, 0.549f), new(-0.119f, 0.7f), new(-0.289f, 0.389f) };
        public float IndicatorScale = 0.2859f;
    }

    internal sealed class ResourceCropVisualDefinition
    {
        public float buriedClip = 0.5f, seedlingScale = 0.25f, matureScale = 1f, growingVisualThreshold = 0.34f;
    }

    internal sealed class ResourceTemperatureYieldDefinition
    {
        public string OutputItemId;
        public float ColdTemperatureCelsius, WarmTemperatureCelsius = 25f, ColdMultiplier = 1f, WarmMultiplier = 4f;
    }

    internal sealed class ResourceCanopyDefinition
    {
        public CanopyFruitSettings Settings = new();
        public string FruitItemId = "Coconut_Green", SplitItemId = "Coconut_Half";
        public float BluntDamage = 10f, CollisionRadius = 0.12f, FruitWidth = 0.28f, SmallScale = 0.2f, DropScatterRadius = 0.85f;
        public double SplitChance = 0.5;
        public bool UseNormalizedCrownAnchor;
        public Vector2 CrownCenter = new(0f, 2.25f), CrownSpread = new(0.28f, 0.14f);
        public Vector2 CrownAnchorUV = new(0.5f, 0.625f), CrownSpreadUV = new(0.048f, 0.02f);
    }

    #endregion

    internal static partial class NaturalEntityEcsProfileCompiler
    {
        #region 能力配置编译

        private static bool TryCompilePlantModule(RuntimeItemModuleDefinition module, NaturalEntityEcsProfile profile,
            out bool recognized, out string reason)
        {
            reason = null;
            recognized = true;
            string id = module.PrefabId;
            switch (id)
            {
                case "Module_Crop":
                {
                    if (profile.HasGrowth) { reason = "同一植物只能有一个成长权威。"; return false; }
                    JObject p = ParseParameters(module, out reason);
                    if (p == null) return false;
                    float duration = p.Value<float?>("growthDurationSeconds") ?? 500f;
                    if (!FinitePositive(duration)) { reason = "作物成熟时间必须为有限正数。"; return false; }
                    profile.CropModuleName = module.StableName;
                    profile.CropDefaults = DeserializeConfiguration<CropRuntimeData>(p["Data"]) ?? new CropRuntimeData();
                    profile.GrowthDefaults = new GrowData
                    {
                        MaxGrowProgress = 1f, GrowSpeed = 1f / duration,
                        GrowProgress = profile.CropDefaults.stage == CropStage.Mature ? 1f : profile.CropDefaults.normalizedGrowth,
                        growState_Value = new List<float> { 0f, 1f }, growState_Scale = new List<float> { 1f, 1f }
                    };
                    profile.GrowthHealthRatios = new[] { 1f, 1f };
                    profile.RainGrowthBonus = p.Value<float?>("rainGrowthBonus") ?? 0.15f;
                    profile.Soil = ReadSoil(p);
                    profile.Capabilities |= NaturalEntityCapability.Growth;
                    return true;
                }
                case "Module_CropYield":
                    profile.HarvestOutputs = DeserializeConfiguration<List<CropYieldEntry>>(ParseParameters(module, out reason)?["outputs"]);
                    return profile.HarvestOutputs != null;
                case "Module_CropVisual":
                    profile.CropVisual = DeserializeConfiguration<ResourceCropVisualDefinition>(ParseParameters(module, out reason));
                    return profile.CropVisual != null;
                case "Module_Collectable":
                    if (profile.Collection != null) { reason = "资源库存能力重复。"; return false; }
                    profile.Collection = DeserializeConfiguration<ResourceCollectionDefinition>(ParseParameters(module, out reason));
                    profile.StockModuleName = module.StableName;
                    return profile.Collection != null;
                case "Module_Production":
                {
                    if (profile.Production != null) { reason = "生产能力重复。"; return false; }
                    JObject p = ParseParameters(module, out reason);
                    profile.Production = DeserializeConfiguration<List<Mod_Production.ItemProductionData>>(p?["ProductionList"]);
                    profile.ProductionSpeed = p?.Value<float?>("ProductionSpeed") ?? 1f;
                    profile.ProductionUsesGrowthDifficulty = p?.Value<bool?>("UseCropGrowthMultiplier") ?? false;
                    profile.ProductionModuleName = module.StableName;
                    return profile.Production != null;
                }
                case "Module_CanopyFruit":
                    profile.Canopy = DeserializeConfiguration<ResourceCanopyDefinition>(ParseParameters(module, out reason));
                    profile.CanopyModuleName = module.StableName;
                    return profile.Canopy != null;
                case "Module_TemperatureYield":
                    profile.TemperatureYield = DeserializeConfiguration<ResourceTemperatureYieldDefinition>(ParseParameters(module, out reason));
                    return profile.TemperatureYield != null;
                default:
                    recognized = false;
                    return false;
            }
        }

        private static EntityPlantSoil ReadSoil(JObject p) => new()
        {
            MinimumWaterMultiplier = p.Value<float?>("minimumWaterGrowthMultiplier") ?? 0.5f,
            MinimumFertilityMultiplier = p.Value<float?>("minimumFertilityGrowthMultiplier") ?? 0.5f,
            WaterPerSecond = p.Value<float?>("waterConsumePerSecond") ?? 0.02f,
            FertilityPerSecond = p.Value<float?>("fertilityConsumePerSecond") ?? 0.00035f,
            RainWaterPerSecond = p.Value<float?>("rainWaterPerSecond") ?? 0.08f
        };

        private static void ReadTreeExtras(JObject p, NaturalEntityEcsProfile profile)
        {
            profile.Soil = ReadSoil(p);
            profile.AllowCultivatedHarvest = p.Value<bool?>("allowCultivatedHarvest") ?? true;
            profile.TreeFoodId = p.Value<string>("harvestFoodItemId");
            profile.TreeSeedId = p.Value<string>("harvestSeedItemId");
            profile.TreeFoodMinimum = p.Value<int?>("harvestFoodMin") ?? 2;
            profile.TreeFoodMaximum = p.Value<int?>("harvestFoodMax") ?? 4;
        }

        private static bool ValidatePlantComposition(NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if (profile.Definition.Material == null) { reason = "资源实体缺少共享材质。"; return false; }
            var collider = profile.Definition.Visual?.Collider;
            if (collider?.Enabled != false && collider != null &&
                !string.Equals(collider.Type, "BoxCollider2D", StringComparison.Ordinal))
            { reason = "资源实体当前使用显式矩形几何；其它形状须提供相应 Entity 几何能力。"; return false; }
            if (!Finite(profile.ProductionSpeed) || profile.ProductionSpeed < 0f ||
                !Finite(profile.RainGrowthBonus) || profile.RainGrowthBonus < 0f)
            { reason = "生长或生产倍率无效。"; return false; }
            if (profile.HasGrowth)
            {
                var soil = profile.Soil;
                if (!Unit(soil.MinimumWaterMultiplier) || !Unit(soil.MinimumFertilityMultiplier) ||
                    !NonNegative(soil.WaterPerSecond) || !NonNegative(soil.FertilityPerSecond) || !NonNegative(soil.RainWaterPerSecond))
                { reason = "植物水肥规则必须是有限非负数。"; return false; }
            }
            if (profile.Collection is { } stock && (string.IsNullOrWhiteSpace(stock.CollectItemId) ||
                stock.MaxStock < 1 || stock.NaturalInitialStockMin < 0 || stock.NaturalInitialStockMax < stock.NaturalInitialStockMin ||
                stock.NaturalInitialStockMax > stock.MaxStock))
            { reason = "可采集库存的产物、容量或初始范围无效。"; return false; }
            if (profile.Collection is { } collection && (!NonNegative(collection.SpawnRadius) ||
                !NonNegative(collection.ThrowDuration) || !Finite(collection.ThrowBezierOffset) ||
                !Finite(collection.ThrowArcHeight) || !FinitePositive(collection.IndicatorScale) ||
                collection.IndicatorLocalPositions == null || collection.IndicatorLocalPositions.Count > 64))
            { reason = "采集掉落或果实提示配置无效。"; return false; }
            if (profile.CropVisual is { } visual)
            {
                if (!Unit(visual.buriedClip) || !Unit(visual.growingVisualThreshold) ||
                    !FinitePositive(visual.seedlingScale) || !FinitePositive(visual.matureScale))
                { reason = "作物成长表现配置无效。"; return false; }
                bool seed = profile.Definition.TryGetVisualStateSprite("seedling", out _);
                bool grow = profile.Definition.TryGetVisualStateSprite("growing", out _);
                bool mature = profile.Definition.TryGetVisualStateSprite("mature", out _);
                if ((seed || grow || mature) && !(seed && grow && mature))
                { reason = "作物阶段图必须同时提供 seedling/growing/mature。"; return false; }
            }
            if (profile.HasCrop && profile.Collection == null && (profile.HarvestOutputs == null || profile.HarvestOutputs.Count == 0))
            { reason = "作物缺少收获产物或持续采集库存。"; return false; }
            if (profile.HarvestOutputs != null)
                foreach (CropYieldEntry output in profile.HarvestOutputs)
                    if (output == null || string.IsNullOrWhiteSpace(output.itemId) || output.minAmount < 1 ||
                        output.maxAmount < output.minAmount || output.maxAmount == int.MaxValue ||
                        !Finite(output.probability) || output.probability < 0f || output.probability > 1f)
                    { reason = "收获产物配置无效。"; return false; }
            if (profile.Production != null)
                foreach (Mod_Production.ItemProductionData rule in profile.Production)
                    if (rule == null || !rule.StoreInModule || rule.DestroySelf || profile.Collection == null ||
                        !string.Equals(rule.itemName, profile.Collection.CollectItemId, StringComparison.OrdinalIgnoreCase) ||
                        !FinitePositive(rule.MaxProductionTime) || rule.itemCountMin < 1 || rule.itemCountMax < rule.itemCountMin ||
                        rule.itemCountMax == int.MaxValue || !Finite(rule.SpawnProbability) || rule.SpawnProbability < 0f ||
                        rule.SpawnProbability > 1f || !NonNegative(rule.Random_ProductionTime.x) ||
                        !NonNegative(rule.Random_ProductionTime.y) || rule.Random_ProductionTime.y < rule.Random_ProductionTime.x)
                    { reason = "库存生产需要匹配的接收模块和有效规则；其它生产模式必须显式提供 Entity 能力。"; return false; }
            if (profile.Canopy != null)
            {
                profile.Canopy.Settings.Validate();
                if (profile.HealthDefaults == null || !profile.HasGrowth || !FinitePositive(profile.Canopy.FruitWidth) ||
                    !FinitePositive(profile.Canopy.CollisionRadius) || !Unit(profile.Canopy.SmallScale) ||
                    !NonNegative(profile.Canopy.BluntDamage) || !NonNegative(profile.Canopy.DropScatterRadius) ||
                    !(profile.Canopy.SplitChance >= 0d && profile.Canopy.SplitChance <= 1d))
                { reason = "树冠结果需要生命、生长能力和有效表现/碰撞配置。"; return false; }
            }
            if (profile.TemperatureYield is { } yield &&
                (string.IsNullOrWhiteSpace(yield.OutputItemId) || !Finite(yield.ColdTemperatureCelsius) ||
                 !Finite(yield.WarmTemperatureCelsius) || yield.WarmTemperatureCelsius <= yield.ColdTemperatureCelsius ||
                 !NonNegative(yield.ColdMultiplier) || !NonNegative(yield.WarmMultiplier)))
            { reason = "温度产量规则无效。"; return false; }
            return true;
        }

        private static bool NonNegative(float value) => Finite(value) && value >= 0f;
        private static bool Unit(float value) => NonNegative(value) && value <= 1f;

        #endregion
    }
}

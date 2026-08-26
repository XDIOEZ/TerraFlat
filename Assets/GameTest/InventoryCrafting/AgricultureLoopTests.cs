using NUnit.Framework;
using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.InventoryCrafting
{
    /// <summary>单一苹果作物闭环测试：保护播种、成长、成熟、收获与存档边界。</summary>
    public sealed class AgricultureLoopTests
    {
        private const string SeedPath = "Assets/2_Prefabs/Gameplay/Items/Seeds/Seed.prefab";
        private const string FoodPath = "Assets/2_Prefabs/Gameplay/Items/Food/Apple.prefab";
        private const string FertilizerPath = "Assets/2_Prefabs/Gameplay/Items/Food/Fertilizer.prefab";

        [Test]
        [Category("InventoryCrafting.Agriculture")]
        public void AppleLoopUsesSingleAuthoritativeGrowthPath()
        {
            GameObject seed = AssetDatabase.LoadAssetAtPath<GameObject>(SeedPath);
            GameObject food = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPath);
            GameObject fertilizer = AssetDatabase.LoadAssetAtPath<GameObject>(FertilizerPath);
            ItemDefinitionDto crop = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions()
                .Single(definition => definition.Id == "AppleTree");

            Assert.That(seed, Is.Not.Null);
            Assert.That(food, Is.Not.Null);
            Assert.That(fertilizer, Is.Not.Null);
            Assert.That(seed.GetComponentInChildren<Mod_Plantable>(true), Is.Not.Null, "Seed_Apple 必须通过统一种植模块进入播种入口。");
            Assert.That(food.GetComponentInChildren<Mod_Plantable>(true), Is.Null, "Apple 只能作为食物，不能形成第二条播种入口。");

            ItemModuleDefinitionDto[] growthModules = crop.Modules.Values
                .Where(module => module.Prefab == "Module_Growth")
                .ToArray();
            Assert.That(growthModules, Has.Length.EqualTo(1), "AppleTree JSON 必须只声明一个生长模块。");
            Assert.That(crop.Modules.Values.Any(module => module.Prefab == "Module_Production"), Is.False,
                "AppleTree 不得继续无限自动生产。");
            Assert.That(growthModules[0].Parameters["$collider2D"], Is.Not.Null,
                "成熟作物必须声明可供交互发送器检测的 Collider。");
            Assert.That(fertilizer.GetComponentInChildren<Mod_FarmlandSupply>(true), Is.Not.Null, "肥料必须能补充耕地水肥。");

            Assert.That(growthModules[0].Parameters.Value<bool>("allowCultivatedHarvest"), Is.True);
            Assert.That(growthModules[0].Parameters.Value<string>("harvestSeedItemId"), Is.EqualTo("Seed_Apple"));
            Assert.That(growthModules[0].Parameters.Value<string>("harvestFoodItemId"), Is.EqualTo("Apple"));
        }

        [Test]
        [Category("InventoryCrafting.Agriculture")]
        public void GrowthFormulaAppliesEachMultiplierExactlyOnce()
        {
            float delta = Mod_Grow.CalculateGrowthDelta(
                baseSpeed: 2f,
                farmlandMultiplier: 0.5f,
                weatherMultiplier: 1.2f,
                difficultyMultiplier: 1.5f,
                deltaTime: 4f);

            Assert.That(delta, Is.EqualTo(7.2f).Within(0.0001f));
        }

        [Test]
        [Category("InventoryCrafting.Agriculture")]
        public void FarmlandStopsAtZeroAndCanBeSuppliedAgain()
        {
            var farmland = new TileData_Farmland
            {
                fertilityValue = new GameValue_float(0f),
                waterValue = 0f,
                maxWater = 100f
            };

            Assert.That(Mod_Grow.CalculateFarmlandGrowthMultiplier(farmland), Is.Zero);

            farmland.AddWater(25f);
            farmland.AddFertility(0.5f);
            Assert.That(farmland.waterValue, Is.EqualTo(25f));
            Assert.That(farmland.Fertility, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Mod_Grow.CalculateFarmlandGrowthMultiplier(farmland), Is.GreaterThan(0f));

            farmland.ConsumeWater(1000f);
            farmland.ConsumeFertility(1000f);
            Assert.That(farmland.waterValue, Is.Zero);
            Assert.That(farmland.Fertility, Is.Zero.Within(0.0001f));
        }

        [Test]
        [Category("InventoryCrafting.Agriculture")]
        public void GrowthMaturityAndHarvestStateRoundTripThroughModuleData()
        {
            var source = new GrowData
            {
                growState = Mod_Grow.GrowState.成熟,
                GrowProgress = 100f,
                MaxGrowProgress = 100f,
                GrowSpeed = 0.1f,
                isCultivatedCrop = true,
                plantedTilePos = new Vector2Int(14, -7),
                isMature = true,
                isHarvested = true,
                growthStatus = Mod_Grow.GrowthStatus.Harvested,
                environmentInitialized = true,
                environmentGrowthMultiplier = 1f
            };
            var moduleData = new Ex_ModData_MemoryPackable();
            moduleData.WriteData(source);

            GrowData restored = new GrowData();
            moduleData.ReadData(ref restored);

            Assert.That(restored.GrowProgress, Is.EqualTo(100f));
            Assert.That(restored.growState, Is.EqualTo(Mod_Grow.GrowState.成熟));
            Assert.That(restored.plantedTilePos, Is.EqualTo(new Vector2Int(14, -7)));
            Assert.That(restored.isCultivatedCrop, Is.True);
            Assert.That(restored.isMature, Is.True);
            Assert.That(restored.isHarvested, Is.True);
            Assert.That(restored.growthStatus, Is.EqualTo(Mod_Grow.GrowthStatus.Harvested));
        }
    }
}

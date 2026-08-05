using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FlatWorld.GameTest.ItemModule
{
    /// <summary>Item/Module 基础冒烟测试：保护实体、模块和管理器入口。</summary>
    public sealed class ItemModuleSmokeTests
    {
        [Test]
        [Category("ItemModule.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/ItemMgr.cs", "ItemMgr");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Item/Item.cs", "Item");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Item/Module.cs", "Module");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Item", "t:Prefab");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Module", "t:Prefab");
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-3_GamePlay/Item/Definitions/ItemDefinitionCatalogLoader.cs",
                "ItemDefinitionCatalogLoader");
            Assert.That(File.Exists(Path.Combine(
                Application.dataPath,
                "StreamingAssets/GameConfig/Items/items.json")), Is.True, "缺少本体 ItemDefinition JSON");
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void KnifeDefinitionsShareOneShellAndUseNamedModuleDictionary()
        {
            string path = Path.Combine(Application.dataPath, "StreamingAssets/GameConfig/Items/items.json");
            var definitions = ItemDefinitionCatalogLoader.ResolveDefinitions(File.ReadAllText(path));
            var knives = definitions.Where(definition =>
                !definition.Abstract && definition.Tags?.Contains("Knife") == true).ToList();

            Assert.That(knives.Count, Is.EqualTo(4));
            Assert.That(knives.Select(definition => definition.ShellPrefab).Distinct().Single(), Is.EqualTo("Dagger_Stone"));

            ItemDefinitionDto copper = knives.Single(definition => definition.Id == "Dagger_Copper");
            Assert.That(copper.Modules.Keys, Is.EquivalentTo(new[] { "animation", "damage" }));
            Assert.That(copper.Modules["damage"].Prefab, Is.EqualTo("Mod_Damage"));
            Assert.That(copper.Modules["damage"].Parameters["Damage"]?["BaseValue"]?.ToObject<float>(), Is.EqualTo(10f));
            Assert.That(copper.Modules["damage"].Parameters["Damage"]?["MultiplicativeModifier"]?.ToObject<float>(), Is.EqualTo(1f));
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void MigratedFamiliesUseSevenShellsAndKeepModulePrefabSeparateFromGameplayId()
        {
            string path = Path.Combine(Application.dataPath, "StreamingAssets/GameConfig/Items/items.json");
            var definitions = ItemDefinitionCatalogLoader.ResolveDefinitions(File.ReadAllText(path));
            var concrete = definitions.Where(definition => !definition.Abstract).ToList();

            Assert.That(concrete.Count, Is.EqualTo(61));
            Assert.That(concrete.Select(definition => definition.ShellPrefab).Distinct().Count(), Is.EqualTo(7));
            Assert.That(concrete.Single(definition => definition.Id == "Axe_Iron").ShellPrefab, Is.EqualTo("Axe_Stone"));
            Assert.That(concrete.Single(definition => definition.Id == "Pickaxe_Iron").ShellPrefab, Is.EqualTo("Pickaxe_Stone"));
            Assert.That(concrete.Single(definition => definition.Id == "Spear_Iron").ShellPrefab, Is.EqualTo("Spear_Stone"));
            Assert.That(concrete.Single(definition => definition.Id == "Ingot_Steel").ShellPrefab, Is.EqualTo("Bone"));
            Assert.That(concrete.Single(definition => definition.Id == "Meat_Cooked").ShellPrefab, Is.EqualTo("Bone"));

            ItemDefinitionDto leather = concrete.Single(definition => definition.Id == "Leather");
            ItemModuleDefinitionDto fuel = leather.Modules.Values.Single();
            Assert.That(fuel.Prefab, Is.EqualTo("Module_Fuel"));
            Assert.That(fuel.Id, Is.EqualTo("燃料模块"));
            Assert.That(fuel.Data, Is.Not.Null);
            Assert.That(leather.ItemData, Is.Not.Null);

            Assert.That(ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths().Count, Is.EqualTo(54));
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void ItemDataTemplateCreationDoesNotRunSpecializedItemLifecycle()
        {
            var gameObject = new GameObject(nameof(ItemTemplateLifecycleProbe));
            try
            {
                ItemTemplateLifecycleProbe probe = gameObject.AddComponent<ItemTemplateLifecycleProbe>();
                ItemTemplateLifecycleProbe.ResetCounters();

                ItemData result = probe.Get_NewItemData();

                Assert.That(result, Is.Not.Null);
                Assert.That(result.IDName, Is.EqualTo(ItemTemplateLifecycleProbe.ItemId));
                Assert.That(result, Is.Not.SameAs(probe.itemData));
                Assert.That(ItemTemplateLifecycleProbe.LoadCalls, Is.Zero);
                Assert.That(ItemTemplateLifecycleProbe.SaveCalls, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void PersistedModuleIdFallsBackToThePrefabChildIdentity()
        {
            var root = new GameObject("LegacyEntity");
            var child = new GameObject("Module_AI_Chicken");
            child.transform.SetParent(root.transform);

            try
            {
                ModuleIdentityProbe module = child.AddComponent<ModuleIdentityProbe>();
                module.Data.ID = "AI";
                module.Data.Name = "RuntimeAI";

                var modules = new ItemMods();
                modules.AddMod(module);

                Assert.That(modules.GetMod_ByID("Module_AI_Chicken"), Is.Null);
                Assert.That(
                    modules.FindModByPersistedId("Module_AI_Chicken"),
                    Is.SameAs(module));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void ProductionEnvironmentAdjustmentUsesPrecipitation()
        {
            GameObject gameObject = new GameObject("ProductionPrecipitationTest");
            try
            {
                Mod_Production production = gameObject.AddComponent<Mod_Production>();
                var layers = new EnvironmentLayers();
                layers.EnsureSize(1, 1);
                layers.SetCell(0, 0, 0.5f, 20f, 0f, 0.5f);

                production.AdjustByEnvironment(layers, Vector2Int.zero);
                float drySpeed = production.ProductionSpeed;

                layers.SetPrecipitation(0, 0, 1f);
                production.AdjustByEnvironment(layers, Vector2Int.zero);
                float wetSpeed = production.ProductionSpeed;

                Assert.That(drySpeed, Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(wetSpeed, Is.EqualTo(1.1f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        public void TileMapItemDataUnionRoundTripsNewStackAndInitializesFromFiveEnvironmentLayers()
        {
            var source = new Data_TileMap
            {
                IDName = "TileMap_Union_Test",
                position = new Vector2Int(5, -3)
            };
            source.EnsureTileStorage(1, 1);
            source.EnsureEnvironmentStorage(1, 1);
            source.SetEnvironmentAtLocal(0, 0, 0.6f, 30f, 0.8f, 0.25f);
            source.SetLightAtLocal(0, 0, 0.7f);
            var water = new TileData_Water
            {
                ID = "Tile_Water_Test",
                Name = "Tile_Water_Test",
                salt = 0f,
                IsWalkable = false
            };
            water.Initialize_Env(source.EnvironmentLayers, 0, 0);
            source.SetBaseTile(source.position, water);

            var container = new Ex_ModData_MemoryPackable();
            container.WriteData<ItemData>(source);
            ItemData restoredBase = container.GetData<ItemData>();

            Assert.That(restoredBase, Is.TypeOf<Data_TileMap>());
            Data_TileMap restored = (Data_TileMap)restoredBase;
            Assert.That(restored.GetLayerCount(source.position), Is.EqualTo(1));
            Assert.That(restored.GetTopTile(source.position), Is.TypeOf<TileData_Water>());
            TileData_Water restoredWater = (TileData_Water)restored.GetTopTile(source.position);
            Assert.That(restoredWater.deepValue, Is.EqualTo(0.5f).Within(0.000001f));
            Assert.That(restored.EnvironmentLayers.Temperature[0, 0], Is.EqualTo(0.6f));
            Assert.That(restored.EnvironmentLayers.TemperatureCelsius[0, 0], Is.EqualTo(30f));
            Assert.That(restored.EnvironmentLayers.Precipitation[0, 0], Is.EqualTo(0.8f));
            Assert.That(restored.EnvironmentLayers.Height[0, 0], Is.EqualTo(0.25f));
            Assert.That(restored.EnvironmentLayers.Light[0, 0], Is.EqualTo(0.7f));
        }
    }

    public sealed class ItemTemplateLifecycleProbe : Item
    {
        public const string ItemId = "ItemTemplateLifecycleProbe";
        public static int LoadCalls { get; private set; }
        public static int SaveCalls { get; private set; }

        [SerializeField]
        private Data_GeneralItem data = new Data_GeneralItem
        {
            IDName = ItemId,
            Stack = new ItemStack()
        };

        public override ItemData itemData
        {
            get => data;
            set => data = (Data_GeneralItem)value;
        }

        public static void ResetCounters()
        {
            LoadCalls = 0;
            SaveCalls = 0;
        }

        public override void Load()
        {
            LoadCalls++;
            base.Load();
        }

        public override void Save()
        {
            SaveCalls++;
            base.Save();
        }
    }

    public sealed class ModuleIdentityProbe : Module
    {
        public Ex_ModData_MemoryPackable Data = new();

        public override ModuleData _Data
        {
            get => Data;
            set => Data = (Ex_ModData_MemoryPackable)value;
        }

        public override void Load()
        {
        }

        public override void Save()
        {
        }
    }
}

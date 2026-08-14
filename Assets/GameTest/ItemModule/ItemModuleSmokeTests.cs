using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using FlatWorld.GameTest.Shared;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.ItemModule
{
    /// <summary>Item/Module 基础冒烟测试：保护实体、模块和管理器入口。</summary>
    public sealed class ItemModuleSmokeTests
    {
        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void BuiltInManifestLoadsEnabledShellPackages()
        {
            List<ItemDefinitionDto> definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
            Assert.That(definitions, Is.Not.Empty);

            var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (ItemDefinitionDto definition in definitions)
            {
                Assert.That(ids.Add(definition.Id), Is.True, $"重复物品 ID：{definition.Id}");
                Assert.That(definition.ShellPrefab, Is.Not.Null.And.Not.Empty,
                    $"物品 {definition.Id} 没有解析出 shellPrefab");

                if (definition.Abstract)
                    continue;

                Assert.That(definition.GameName, Is.Not.Null.And.Not.Empty,
                    $"物品 {definition.Id} 没有 JSON 显示名");
                Assert.That(definition.Visual?.SpriteAddress, Is.Not.Null.And.Not.Empty,
                    $"物品 {definition.Id} 没有 JSON 显示贴图地址");
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void BuiltInManifestUsesStableItemCategoryFileNames()
        {
            ItemDefinitionManifestDto manifest = ItemDefinitionCatalogLoader.DeserializeManifest(
                File.ReadAllText(ItemDefinitionCatalogLoader.BuiltInManifestPath));
            ItemDefinitionCatalogLoader.ValidateManifest(manifest);

            string[] expectedCategories =
            {
                "basic_items", "tools", "weapons", "equipment", "seeds", "building_summoners"
            };
            string[] actualCategories = manifest.Packages
                .Where(package => package.Enabled)
                .Select(package => package.Id)
                .OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(actualCategories, Is.EquivalentTo(expectedCategories),
                "物品 JSON 应按稳定玩法类别分包，不应退回具体物品或 Prefab 名称");
            foreach (ItemDefinitionPackageDto package in manifest.Packages)
            {
                Assert.That(package.Path, Is.EqualTo($"shells/{package.Id}.json"));
                Assert.That(package.ShellPrefab, Is.Null.Or.Empty,
                    $"类别包 {package.Id} 可包含多个运行时外壳，不应声明单一 shellPrefab 约束");
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void MeatrackModuleShellResolvesRuntimeComponent()
        {
            const string scriptPath = "Assets/5_Scripts/5-3_GamePlay/Items/Food/Meatrack.cs";
            const string prefabPath = "Assets/2_Prefabs/World/Buildings/Meatrack.prefab";

            GameTestAssertions.AssertScriptType(scriptPath, nameof(Meatrack));
            AssetDatabase.ImportAsset(
                prefabPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少晾肉架模块外壳：{prefabPath}");
            Assert.That(prefab.GetComponentInChildren<Meatrack>(true), Is.Not.Null,
                "晾肉架模块外壳没有解析出 Meatrack 运行时组件");
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void ScarecrowShellResolvesEquipmentModule()
        {
            const string scriptPath = "Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Mod_Equipment.cs";
            const string prefabPath = "Assets/2_Prefabs/World/Buildings/Summoners/Scarecrow_Summoner.prefab";

            GameTestAssertions.AssertScriptType(scriptPath, nameof(Mod_Equipment));
            AssetDatabase.ImportAsset(
                prefabPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少稻草人物品外壳：{prefabPath}");
            Assert.That(prefab.GetComponentInChildren<Mod_Equipment>(true), Is.Not.Null,
                "稻草人物品外壳没有解析出 Mod_Equipment 运行时组件");
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void WorkBenchShellResolvesMakeTableModule()
        {
            const string scriptPath = "Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Mod_MakeTable.cs";
            const string prefabPath = "Assets/2_Prefabs/World/Buildings/Summoners/WorkBench_Summoner.prefab";

            GameTestAssertions.AssertScriptType(scriptPath, nameof(Mod_MakeTable));
            AssetDatabase.ImportAsset(
                prefabPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少工作台物品外壳：{prefabPath}");
            Assert.That(prefab.GetComponentInChildren<Mod_MakeTable>(true), Is.Not.Null,
                "工作台物品外壳没有解析出 Mod_MakeTable 运行时组件");
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void PrefabAliasCannotReplaceItemShellWithNonItemPrefab()
        {
            var managerObject = new GameObject("GameResAliasProbe");
            managerObject.SetActive(false);
            var itemObject = new GameObject("Dagger");
            var moduleObject = new GameObject("Dagger");

            try
            {
                GameRes gameRes = managerObject.AddComponent<GameRes>();
                itemObject.AddComponent<ItemTemplateLifecycleProbe>();
                MethodInfo registerAlias = typeof(GameRes).GetMethod(
                    "RegisterPrefabAlias",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(registerAlias, Is.Not.Null);

                registerAlias.Invoke(gameRes, new object[] { "Dagger", itemObject });
                registerAlias.Invoke(gameRes, new object[] { "Dagger", moduleObject });

                Assert.That(gameRes.AllPrefabs["Dagger"], Is.SameAs(itemObject),
                    "非 Item 的同名 Prefab 不得覆盖 JSON 使用的 Item 外壳");
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(itemObject);
                Object.DestroyImmediate(moduleObject);
            }
        }

        [Test]
        [Category("ItemModule.Smoke")]
        [Category("Smoke")]
        public void ModuleLoadMigratesLegacyPrefabIdentityToCanonicalId()
        {
            const string legacyId = "LegacyModule";
            const string canonicalId = "CanonicalModule";
            var root = new GameObject("LegacyEntity");
            var child = new GameObject(legacyId);
            child.transform.SetParent(root.transform);

            try
            {
                ItemTemplateLifecycleProbe item = root.AddComponent<ItemTemplateLifecycleProbe>();
                ModuleIdentityProbe module = child.AddComponent<ModuleIdentityProbe>();
                module.Data.ID = canonicalId;
                module.Data.Name = "RuntimeModule";

                var persisted = new Ex_ModData_MemoryPackable
                {
                    ID = legacyId,
                    Name = "RuntimeModule"
                };
                item.itemData.ModuleDataDic.Clear();
                item.itemData.ModuleDataDic[persisted.Name] = persisted;

                item.ModuleLoad();

                Assert.That(persisted.ID, Is.EqualTo(canonicalId));
                Assert.That(item.itemMods.GetMod_ByID(canonicalId), Is.SameAs(module));
                Assert.That(item.itemMods.GetMod_ByID(legacyId), Is.Null);

                var replacement = new Data_GeneralItem
                {
                    IDName = ItemTemplateLifecycleProbe.ItemId,
                    Stack = new ItemStack()
                };
                item.BindData(replacement);

                Assert.That(item.itemData, Is.SameAs(replacement));
                Assert.Throws<System.ArgumentException>(() => item.BindData(new Data_Player()));
                Assert.That(item.itemData, Is.SameAs(replacement),
                    "类型校验失败时不应替换已绑定数据");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        public override ItemData itemData => data;

        protected override void SetItemData(ItemData value)
        {
            data = RequireData<Data_GeneralItem>(value);
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

using NUnit.Framework;
using System.Collections.Generic;
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

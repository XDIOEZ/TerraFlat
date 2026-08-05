using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.GameTest.InventoryCrafting
{
    /// <summary>背包制作基础冒烟测试：保护库存、装备和配方入口。</summary>
    public sealed class InventoryCraftingSmokeTests
    {
        [Test]
        [Category("InventoryCrafting.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Inventory/Mod_Inventory.cs", "Mod_Inventory");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Equipment/Mod_Equipment.cs", "Mod_Equipment");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/RecipeDto.cs", "RecipeDto");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/RuntimeRecipe.cs", "RuntimeRecipe");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/CraftingRecipeMatcher.cs", "CraftingRecipeMatcher");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/CraftingTransaction.cs", "CraftingTransaction");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/CraftingResult.cs", "CraftingResult");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/CraftingService.cs", "CraftingService");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/crafting/survival.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/crafting/tools.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/crafting/weapons.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/crafting/buildings.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/cooking/basic_food.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/cooking/advanced_food.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/smelting/ores.json");
            GameTestAssertions.AssertAssetExists("Assets/StreamingAssets/GameConfig/Recipes/smelting/alloys.json");
            GameTestAssertions.AssertAssetExists("Assets/GameConfig/Excel/RecipeConfig.xlsx");
            GameTestAssertions.AssertFolderContainsAsset("Assets/4_ScriptObjects/4-6_InventoryInit", "t:ScriptableObject");
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        public void InventoryAndCraftingRuntimeVisualsExistInPrefabs()
        {
            const string bagPath = "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_Bag.prefab";
            GameObject bag = AssetDatabase.LoadAssetAtPath<GameObject>(bagPath);
            Assert.That(bag, Is.Not.Null, $"缺少背包 Prefab：{bagPath}");
            Assert.That(
                Array.Exists(bag.GetComponentsInChildren<Button>(true), button => button.name == "整理"),
                Is.True,
                "UI_Bag.prefab 缺少预制的整理按钮。");

            const string slotPath = "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_Slot.prefab";
            GameObject slot = AssetDatabase.LoadAssetAtPath<GameObject>(slotPath);
            Assert.That(slot, Is.Not.Null, $"缺少槽位 Prefab：{slotPath}");
            AssertCraftingPreviewLayers(slot.GetComponent<ItemSlot_UI>(), slotPath);

            string[] craftingPanelPaths =
            {
                "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_HandCraftTable.prefab",
                "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_MakerTable.prefab",
                "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_FireDrill.prefab",
                "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_FlintStrike.prefab"
            };

            foreach (string craftingPanelPath in craftingPanelPaths)
            {
                GameObject panel = AssetDatabase.LoadAssetAtPath<GameObject>(craftingPanelPath);
                Assert.That(panel, Is.Not.Null, $"缺少制作面板 Prefab：{craftingPanelPath}");

                ItemSlot_UI[] panelSlots = panel.GetComponentsInChildren<ItemSlot_UI>(true);
                ItemSlot_UI[] outputSlots = Array.FindAll(
                    panelSlots,
                    panelSlot => panelSlot != null && panelSlot.name.StartsWith("输出_", StringComparison.Ordinal));
                Assert.That(outputSlots, Is.Not.Empty, $"{craftingPanelPath} 未找到输出槽");

                foreach (ItemSlot_UI outputSlot in outputSlots)
                    AssertCraftingPreviewLayers(outputSlot, craftingPanelPath);
            }
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        public void HotbarSelectionBoxMovesWithoutChangingItsParent()
        {
            GameObject hotbarObject = new("HotbarSelection_Test");
            GameObject firstSlotObject = new("HotbarSlot_0_Test");
            GameObject secondSlotObject = new("HotbarSlot_1_Test");
            GameObject selectionObject = new("HotbarSelectionBox_Test");

            try
            {
                Inventory_HotBar hotbar = hotbarObject.AddComponent<Inventory_HotBar>();
                ItemSlot_UI firstSlot = firstSlotObject.AddComponent<ItemSlot_UI>();
                ItemSlot_UI secondSlot = secondSlotObject.AddComponent<ItemSlot_UI>();
                firstSlotObject.transform.position = new Vector3(-50f, 20f, 0f);
                secondSlotObject.transform.position = new Vector3(125f, 20f, 0f);

                selectionObject.transform.SetParent(firstSlotObject.transform, false);
                Transform originalParent = selectionObject.transform.parent;

                hotbar.SelectBox = selectionObject;
                hotbar.SelectBoxChangeDuration = 0.1f;
                hotbar.RuntimeInventory.itemSlot_UI.Add(firstSlot);
                hotbar.RuntimeInventory.itemSlot_UI.Add(secondSlot);

                MethodInfo moveSelection = typeof(Inventory_HotBar).GetMethod(
                    "MoveSelectBox",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(moveSelection, Is.Not.Null, "快捷栏选中框必须提供移动入口。");

                MethodInfo resolveTargetPosition = typeof(Inventory_HotBar).GetMethod(
                    "GetSelectBoxTargetPosition",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(resolveTargetPosition, Is.Not.Null, "快捷栏选中框必须使用目标槽位的位置。");

                Vector3 targetPosition = (Vector3)resolveTargetPosition.Invoke(
                    null,
                    new object[] { secondSlotObject.transform });
                Assert.That(targetPosition, Is.EqualTo(secondSlotObject.transform.position));

                moveSelection.Invoke(hotbar, new object[] { 1 });

                Assert.That(selectionObject.transform.parent, Is.EqualTo(originalParent), "切换快捷栏位不应改变选中框层级。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hotbarObject);
                UnityEngine.Object.DestroyImmediate(selectionObject);
                UnityEngine.Object.DestroyImmediate(firstSlotObject);
                UnityEngine.Object.DestroyImmediate(secondSlotObject);
            }
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        public void HotbarSelectionBoxStartsCenteredOnFirstSlot()
        {
            GameObject hotbarObject = new("HotbarInitialSelection_Test");
            GameObject firstSlotObject = new("HotbarSlot_0_Test", typeof(RectTransform));
            GameObject selectionObject = new("HotbarSelectionBox_Test", typeof(RectTransform));

            try
            {
                Inventory_HotBar hotbar = hotbarObject.AddComponent<Inventory_HotBar>();
                ItemSlot_UI firstSlot = firstSlotObject.AddComponent<ItemSlot_UI>();
                RectTransform slotRect = firstSlotObject.GetComponent<RectTransform>();
                RectTransform selectionRect = selectionObject.GetComponent<RectTransform>();
                selectionRect.SetParent(slotRect, false);
                selectionRect.anchoredPosition = new Vector2(-80f, -40f);

                hotbar.SelectBox = selectionObject;
                hotbar.RuntimeInventory.itemSlot_UI.Add(firstSlot);

                MethodInfo snapSelection = typeof(Inventory_HotBar).GetMethod(
                    "SnapSelectBoxToSlot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(snapSelection, Is.Not.Null, "快捷栏首次显示必须提供无补间定位入口。");

                snapSelection.Invoke(hotbar, new object[] { 0 });

                Assert.That(selectionRect.parent, Is.EqualTo(slotRect));
                Assert.That(selectionRect.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(selectionRect.position, Is.EqualTo(slotRect.position));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hotbarObject);
                UnityEngine.Object.DestroyImmediate(firstSlotObject);
            }
        }

        private static void AssertCraftingPreviewLayers(ItemSlot_UI slot, string prefabPath)
        {
            Assert.That(slot, Is.Not.Null, $"{prefabPath} 缺少 ItemSlot_UI");
            Assert.That(slot.image, Is.Not.Null, $"{prefabPath}/{slot.name} 缺少物品图标引用");

            Image[] images = slot.GetComponentsInChildren<Image>(true);
            Image ghost = Array.Find(images, image => image != null && image.name == "Crafting Output Ghost");
            Image reveal = Array.Find(images, image => image != null && image.name == "Crafting Output Reveal");
            string context = $"{prefabPath}/{slot.name}";

            Assert.That(ghost, Is.Not.Null, $"{context} 缺少 Crafting Output Ghost");
            Assert.That(reveal, Is.Not.Null, $"{context} 缺少 Crafting Output Reveal");
            Assert.That(ghost.gameObject.activeSelf, Is.False, $"{context} Ghost 默认应隐藏");
            Assert.That(reveal.gameObject.activeSelf, Is.False, $"{context} Reveal 默认应隐藏");
            Assert.That(ghost.raycastTarget, Is.False, $"{context} Ghost 不应拦截射线");
            Assert.That(reveal.raycastTarget, Is.False, $"{context} Reveal 不应拦截射线");
            Assert.That(ghost.preserveAspect, Is.True, $"{context} Ghost 应保持宽高比");
            Assert.That(reveal.preserveAspect, Is.True, $"{context} Reveal 应保持宽高比");
            Assert.That(reveal.type, Is.EqualTo(Image.Type.Filled), $"{context} Reveal 类型错误");
            Assert.That(reveal.fillMethod, Is.EqualTo(Image.FillMethod.Vertical), $"{context} Reveal 填充方向错误");
            Assert.That(reveal.fillOrigin, Is.EqualTo((int)Image.OriginVertical.Bottom), $"{context} Reveal 应从下方填充");
            Assert.That(ghost.transform.parent, Is.EqualTo(slot.image.transform.parent), $"{context} Ghost 层级错误");
            Assert.That(reveal.transform.parent, Is.EqualTo(slot.image.transform.parent), $"{context} Reveal 层级错误");
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        public void RecipeJsonContainsMigratedValidRecipes()
        {
            string root = Path.GetFullPath("Assets/StreamingAssets/GameConfig/Recipes");
            RecipeManifestDto manifest = RecipeCatalogLoader.DeserializeManifest(
                File.ReadAllText(Path.Combine(root, RecipeCatalogLoader.ManifestFileName)));
            RecipeCatalogLoader.ValidateManifest(manifest);
            Assert.That(manifest.Packages, Has.Count.EqualTo(8));

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int durabilityActions = 0;
            int recipeCount = 0;
            foreach (RecipePackageDto package in manifest.Packages)
            {
                string packagePath = RecipeCatalogLoader.ResolvePackagePath(root, package.Path);
                Assert.That(File.Exists(packagePath), Is.True, $"缺少配方分包：{package.Path}");
                RecipeCatalogDto catalog = RecipeRuntimeFactory.Deserialize(File.ReadAllText(packagePath));
                Assert.That(catalog.SchemaVersion, Is.EqualTo(1));
                foreach (RecipeDto recipe in catalog.Recipes)
                {
                    recipeCount++;
                    Assert.That(ids.Add(recipe.Id), Is.True, $"跨分包重复配方 ID：{recipe.Id}");
                    foreach (RecipeIngredientDto input in recipe.Inputs)
                    {
                        bool hasItem = !string.IsNullOrWhiteSpace(input.ItemId);
                        bool hasTag = !string.IsNullOrWhiteSpace(input.Tag);
                        Assert.That(input.Amount, Is.GreaterThanOrEqualTo(0), $"配方输入数量不能为负数：{recipe.Id}");
                        if (input.Match == "tag")
                            Assert.That(hasTag, Is.True, $"标签输入缺少 tag：{recipe.Id}");
                        else if (input.Amount > 0 || hasItem)
                            Assert.That(hasItem, Is.True, $"物品输入缺少 itemId：{recipe.Id}");
                        else
                            Assert.That(input.Amount, Is.Zero, $"空输入槽数量必须为 0：{recipe.Id}");
                    }

                    Assert.That(recipe.Outputs, Is.Not.Empty, $"配方没有产物：{recipe.Id}");
                    foreach (RecipeOutputDto output in recipe.Outputs)
                    {
                        Assert.That(output.ItemId, Is.Not.Empty, $"配方产物 ID 为空：{recipe.Id}");
                        Assert.That(output.Amount, Is.GreaterThan(0), $"配方产物数量无效：{recipe.Id}");
                    }

                    foreach (RecipeActionDto action in recipe.Actions)
                    {
                        if (action.Type != "change_durability")
                            continue;
                        durabilityActions++;
                        Assert.That(action.TargetRole, Is.Not.Empty, $"耐久动作缺少工具角色：{recipe.Id}");
                        Assert.That(action.Value, Is.GreaterThan(0f), $"耐久动作消耗无效：{recipe.Id}");
                    }
                }
            }

            Assert.That(recipeCount, Is.EqualTo(39));
            Assert.That(durabilityActions, Is.EqualTo(3));
        }
    }
}

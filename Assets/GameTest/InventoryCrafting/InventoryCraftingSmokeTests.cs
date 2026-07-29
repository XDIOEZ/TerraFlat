using System;
using System.Collections.Generic;
using System.IO;
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
            Image[] images = slot.GetComponentsInChildren<Image>(true);
            Assert.That(Array.Exists(images, image => image.name == "Crafting Output Ghost"), Is.True);
            Assert.That(Array.Exists(images, image => image.name == "Crafting Output Reveal"), Is.True);
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

            Assert.That(recipeCount, Is.EqualTo(38));
            Assert.That(durabilityActions, Is.EqualTo(3));
        }
    }
}

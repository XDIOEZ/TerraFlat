using FlatWorld.GameTest.Shared;
using NUnit.Framework;

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
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Crafting/Recipe.cs", "Recipe");
            GameTestAssertions.AssertFolderContainsAsset("Assets/4_ScriptObjects/4-6_InventoryInit", "t:ScriptableObject");
        }
    }
}

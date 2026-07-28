using FlatWorld.GameTest.Shared;
using NUnit.Framework;

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
        }
    }
}

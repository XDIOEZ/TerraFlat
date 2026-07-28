using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.UI
{
    /// <summary>UI 基础冒烟测试：保护 UI 管理器、面板和根 Prefab 入口。</summary>
    public sealed class UISmokeTests
    {
        [Test]
        [Category("UI.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-5_UI/UIManager.cs", "UIManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-5_UI/BasePanel.cs", "BasePanel");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/2-1_UI/UIRoot.prefab");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/2-1_UI", "t:Prefab");
        }
    }
}

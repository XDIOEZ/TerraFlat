using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.Core
{
    /// <summary>核心生命周期基础冒烟测试：保护全局入口与启动场景。</summary>
    public sealed class CoreSmokeTests
    {
        [Test]
        [Category("Core.Smoke")]
        public void RequiredEntryPointsAndScenesExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs", "GameManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs", "GameRes");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/SceneMgr.cs", "SceneMgr");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/GameStartScene.unity");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/Manager.unity");
        }
    }
}

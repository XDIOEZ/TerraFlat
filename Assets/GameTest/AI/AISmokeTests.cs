using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.AI
{
    /// <summary>AI 基础冒烟测试：保护状态机、感知和生物资源入口。</summary>
    public sealed class AISmokeTests
    {
        [Test]
        [Category("AI.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/AI/AI_StateMachineRunner.cs", "AI_StateMachineRunner");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/AI/Mod_ItemDetector.cs", "Mod_ItemDetector");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Config/SpawnerConfig.asset");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Entity_AI", "t:Prefab");
        }
    }
}

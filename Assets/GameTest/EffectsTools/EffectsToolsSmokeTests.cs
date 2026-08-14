using System.IO;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;

namespace FlatWorld.GameTest.EffectsTools
{
    /// <summary>特效与工具基础冒烟测试：保护视觉入口和粒子资源。</summary>
    public sealed class EffectsToolsSmokeTests
    {
        [Test]
        [Category("EffectsTools.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Management/VisualEffectManager.cs", "VisualEffectManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Presentation/Effects/Runtime/Text/DamageTextEffect.cs", "DamageTextEffect");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Effects", "t:Prefab");
            GameTestAssertions.AssertFolderContainsAsset("Assets/Shaders", "t:Shader");

            string navigationSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Development/Debug/GMReflectionConsole.Navigation.cs");
            string questPageSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Development/Debug/GMReflectionConsole.Quests.cs");
            Assert.That(navigationSource, Does.Contain("BuildQuestPage();"));
            Assert.That(
                navigationSource,
                Does.Contain("CreateTab(content.transform, GmPageId.Quests, \"任务\""));
            Assert.That(questPageSource, Does.Contain("definition.DebugOnly"));
            Assert.That(questPageSource, Does.Contain("runtime.AcceptQuest"));
            Assert.That(questPageSource, Does.Contain("runtime.ClaimQuest"));
            Assert.That(questPageSource, Does.Contain("runtime.Refresh();"));
            Assert.That(questPageSource, Does.Contain("QuestChanged += HandleGmQuestChanged"));
            Assert.That(questPageSource, Does.Not.Contain("private void Update()"));
        }
    }
}

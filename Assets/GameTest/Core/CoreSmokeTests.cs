using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using System.IO;

namespace FlatWorld.GameTest.Core
{
    /// <summary>核心生命周期基础冒烟测试：保护全局入口与启动场景。</summary>
    public sealed class CoreSmokeTests
    {
        [Test]
        [Category("Core.Smoke")]
        [Category("Smoke")]
        public void RequiredEntryPointsScenesAndOptionalNamesRemainValid()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.cs", "GameManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Manager/NewWorldCreationRequest.cs", "NewWorldCreationRequest");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs", "GameRes");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Manager/SceneMgr.cs", "SceneMgr");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/GameStartScene.unity");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/Manager.unity");

            var request = new NewWorldCreationRequest(
                " ",
                null,
                "12345",
                new PlanetData { Name = "测试世界" },
                new TimeData());

            Assert.That(request.SaveName, Does.Match("^[0-9]{8}$"));
            Assert.That(request.PlayerName, Is.EqualTo(request.SaveName));
            Assert.That(request.TryValidate(out string error), Is.True, error);
        }

        [Test]
        [Category("Core.Smoke")]
        public void ExitLifecycleCanExplicitlySkipDiskSave()
        {
            const string gameManagerPath =
                "Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.cs";
            string source = File.ReadAllText(gameManagerPath);

            Assert.That(source, Does.Contain("bool saveCurrentGame = true"));
            Assert.That(source, Does.Contain("if (saveCurrentGame)"));
            Assert.That(source, Does.Contain("Save_And_WriteToDiskAndRecordExitTime();"));
        }
    }
}

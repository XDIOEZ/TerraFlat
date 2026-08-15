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
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs", "GameManager");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/NewWorldCreationRequest.cs", "NewWorldCreationRequest");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameRes.cs", "GameRes");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/SceneMgr.cs", "SceneMgr");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/GameStartScene.unity");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/Manager.unity");

            var request = new NewWorldCreationRequest(
                " ",
                null,
                "12345",
                new PlanetData { Name = "测试世界" },
                new TimeData());

            Assert.That(request.SaveName, Is.Not.Null.And.Not.Empty);
            Assert.That(request.PlayerName, Is.Not.Null.And.Not.Empty);
            if (GameRes.ExistingInstance?.TextLibraries?.IsReady == true)
            {
                Assert.That(request.SaveName, Does.Not.Match("^World_[0-9]{8}$"));
                Assert.That(request.PlayerName, Does.Not.Match("^Player_[0-9]{8}$"));
            }
            else
            {
                Assert.That(request.SaveName, Does.Match("^World_[0-9]{8}$"));
                Assert.That(request.PlayerName, Does.Match("^Player_[0-9]{8}$"));
                Assert.That(
                    request.SaveName.Substring("World_".Length),
                    Is.EqualTo(request.PlayerName.Substring("Player_".Length)));
            }
            Assert.That(request.TryValidate(out string error), Is.True, error);
        }

        [Test]
        [Category("Core.Smoke")]
        public void BuiltInTextLibraryConfigCanGenerateNames()
        {
            string configPath =
                "Assets/StreamingAssets/GameConfig/TextLibraries/text-library.json";
            string json = File.ReadAllText(configPath);
            TextLibraryService service = TextLibraryCatalogLoader.Deserialize(json);

            Assert.That(service.IsReady, Is.True);
            Assert.That(service.EntryCount, Is.GreaterThan(0));
            Assert.That(
                service.TryGenerate(TextLibraryKeys.PlayerName, out string playerName),
                Is.True);
            Assert.That(playerName, Is.Not.Null.And.Not.Empty);
            Assert.That(
                service.TryGenerate(TextLibraryKeys.SaveName, out string saveName),
                Is.True);
            Assert.That(saveName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        [Category("Core.Smoke")]
        public void ExitLifecycleCanExplicitlySkipDiskSave()
        {
            const string gameManagerPath =
                "Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs";
            string source = File.ReadAllText(gameManagerPath);

            Assert.That(source, Does.Contain("bool saveCurrentGame = true"));
            Assert.That(source, Does.Contain("if (saveCurrentGame)"));
            Assert.That(source, Does.Contain("Save_And_WriteToDiskAndRecordExitTime();"));
        }
    }
}

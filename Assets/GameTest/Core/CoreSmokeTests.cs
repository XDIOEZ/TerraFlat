using System.IO;
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

        [Test]
        [Category("Core.Smoke")]
        public void NewAndExistingWorldEntryUsePrefabLoadingView()
        {
            const string managerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs";
            const string uiPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs";
            string managerSource = File.ReadAllText(managerPath);
            string uiSource = File.ReadAllText(uiPath);

            Assert.That(managerSource, Does.Contain(
                "BeginWorldEntryLoading(\"正在创建新世界\""));
            Assert.That(managerSource, Does.Contain(
                "BeginWorldEntryLoading(\"正在进入存档\""));
            Assert.That(managerSource, Does.Contain("yield return null;"),
                "加载 Prefab 必须先获得渲染帧，再执行同步世界准备。 ");
            Assert.That(uiSource, Does.Contain(
                "InstantiatePrefab(RuntimeUIPrefabKeys.WorldLoading)"));
            Assert.That(uiSource, Does.Contain("DontDestroyOnLoad(worldLoadingView)"));
            Assert.That(uiSource, Does.Contain("ChunkMgr.Instance.HasPendingChunkLoads"));
            Assert.That(uiSource, Does.Not.Contain("new GameObject"),
                "世界加载界面不得由运行时代码硬编码创建视觉节点。 ");
        }

        [Test]
        [Category("Core.Smoke")]
        public void NewPlayerSpawnSearchDoesNotSynchronouslyGenerateEveryVisitedChunk()
        {
            const string managerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs";
            string managerSource = File.ReadAllText(managerPath);

            Assert.That(managerSource, Does.Contain("FindNewPlayerSpawnCoroutine"),
                "New-player placement must wait for one candidate chunk at a time.");
            Assert.That(managerSource, Does.Contain("TryFindNearestLandInChunk"),
                "Spawn search must scan bounded, ready chunk data instead of the whole world radius.");
            Assert.That(managerSource, Does.Not.Contain("ChunkMgr.Instance.LoadChunk_By_Position(chunkPos)"),
                "A tile probe must never synchronously create a chunk; this queued hundreds of chunks while creating a world.");
            Assert.That(managerSource, Does.Not.Contain("SaveAllChunks();"),
                "GameManager must not save every chunk before SaveDataMgr repeats the same scan.");
        }
    }
}

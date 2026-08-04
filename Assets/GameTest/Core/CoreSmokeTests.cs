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
        public void NewPlayerSpawnUsesPureSeedSamplingBeforeChunkStreaming()
        {
            const string managerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs";
            const string chunkLoaderPath = "Assets/5_Scripts/5-3_GamePlay/Chunk/Mod_ChunkLoader.cs";
            string managerSource = File.ReadAllText(managerPath);
            string chunkLoaderSource = File.ReadAllText(chunkLoaderPath);

            Assert.That(managerSource, Does.Contain("TryFindWalkableTerrainNear("));
            Assert.That(managerSource, Does.Contain("spawnTerrainSampleBudget"));
            Assert.That(managerSource, Does.Contain("GetActiveMapCorePrefabId()"));
            Assert.That(managerSource, Does.Contain("GetPrefab(mapCorePrefabId, logError: false)"));
            Assert.That(managerSource, Does.Not.Contain("RandomDropInMap(player.gameObject"),
                "出生搜索失败时不得将玩家随机投放到可能是水面的坐标。");
            Assert.That(managerSource, Does.Not.Contain("LoadChunk_By_Position("),
                "出生定位阶段不得创建 Chunk；Chunk 必须在玩家坐标设置后由流送模块加载。 ");
            Assert.That(managerSource, Does.Not.Contain("WaitForSpawnChunkTerrain("));

            int teleportIndex = managerSource.IndexOf("player.transform.position = spawnPosition;", System.StringComparison.Ordinal);
            int worldEnterIndex = managerSource.IndexOf("Event_PlayerEnterWorld?.Invoke(player);", teleportIndex, System.StringComparison.Ordinal);
            Assert.That(teleportIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(worldEnterIndex, Is.GreaterThan(teleportIndex),
                "必须先设置出生坐标，再触发玩家进入世界事件。 ");
            Assert.That(chunkLoaderSource, Does.Contain("GameManager.Event_PlayerEnterWorld += OnPlayerEnterWorld"));
            Assert.That(chunkLoaderSource, Does.Contain("RefreshChunksAroundPlayer();"));
        }

        [Test]
        [Category("Core.Smoke")]
        public void GameManagerDelegatesChunkPersistenceToSaveDataManager()
        {
            const string managerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs";
            string managerSource = File.ReadAllText(managerPath);

            Assert.That(managerSource, Does.Not.Contain("SaveAllChunks();"),
                "GameManager 不应在 SaveDataMgr 写盘前重复扫描并保存同一批区块。 ");
            Assert.That(managerSource, Does.Contain("Save_And_WriteToDisk()"));
            Assert.That(managerSource, Does.Contain("Save_And_WriteToDiskAndRecordExitTime()"));
        }
    }
}

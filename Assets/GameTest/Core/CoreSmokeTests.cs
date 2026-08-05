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
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/NewWorldCreationRequest.cs", "NewWorldCreationRequest");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs", "GameRes");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Manager/SceneMgr.cs", "SceneMgr");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/GameStartScene.unity");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/Manager.unity");
        }

        [Test]
        [Category("Core.Smoke")]
        public void WorldCreationCoreIsUiIndependentAndLoadingViewIsAnObserver()
        {
            const string managerPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs";
            const string uiPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs";
            const string worldEntryPath = "Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.WorldEntry.cs";
            string managerSource = File.ReadAllText(managerPath);
            string uiSource = File.ReadAllText(uiPath);
            string worldEntrySource = File.ReadAllText(worldEntryPath);

            Assert.That(typeof(GameManager).GetMethod(
                nameof(GameManager.CreateNewWorld),
                new[] { typeof(NewWorldCreationRequest) }), Is.Not.Null);
            Assert.That(managerSource, Does.Contain(
                "CreateNewWorld(NewWorldCreationRequest request)"));
            Assert.That(managerSource, Does.Not.Contain("UIManager"));
            Assert.That(managerSource, Does.Not.Contain("TryBuildNewWorldCreationRequest"));
            Assert.That(managerSource, Does.Contain("yield return null;"),
                "异步世界准备必须先交还一帧，避免在同一帧阻塞启动。 ");

            Assert.That(uiSource, Does.Contain("TryBuildNewWorldCreationRequest"));
            Assert.That(uiSource, Does.Contain("CreateNewWorld(request)"));
            Assert.That(uiSource, Does.Contain(
                "WorldEntryProgressChanged += OnWorldEntryProgressChanged"));
            Assert.That(uiSource, Does.Contain(
                "InstantiatePrefab(RuntimeUIPrefabKeys.WorldLoading)"));
            Assert.That(uiSource, Does.Contain("DontDestroyOnLoad(worldLoadingView)"));
            Assert.That(worldEntrySource, Does.Contain("ChunkMgr.Instance.HasPendingChunkLoads"));
            Assert.That(uiSource, Does.Not.Contain("new GameObject"),
                "世界加载界面不得由运行时代码硬编码创建视觉节点。 ");
        }

        [Test]
        [Category("Core.Smoke")]
        public void NewWorldCreationRequestOwnsAValidatedSnapshot()
        {
            var sourcePlanet = new PlanetData
            {
                Name = "CoreSmokeWorld",
                Radius = PlanetData.DefaultRadius,
                NoiseScale = PlanetData.DefaultNoiseScale
            };
            var sourceTime = new TimeData();
            var request = new NewWorldCreationRequest(
                "CoreSmokeSave",
                "CoreSmokePlayer",
                "12345",
                sourcePlanet,
                sourceTime);

            sourcePlanet.Name = "MutatedAfterConstruction";

            Assert.That(request.TryValidate(out string error), Is.True, error);
            Assert.That(request.PlanetData.Name, Is.EqualTo("CoreSmokeWorld"),
                "请求必须保存输入快照，不能被 UI 或调用方随后修改。 ");
            Assert.That(request.TimeData, Is.Not.SameAs(sourceTime));
            Assert.That(request.TimeData.LightParams, Is.Not.SameAs(sourceTime.LightParams),
                "AnimationCurve 持有 Unity 原生资源，必须显式重建。 ");
            Assert.That(request.TimeData.dayNightGradient, Is.Not.SameAs(sourceTime.dayNightGradient),
                "Gradient 持有 Unity 原生资源，必须显式重建。 ");
            Assert.That(
                request.TimeData.dayNightGradient.Evaluate(0.5f),
                Is.EqualTo(sourceTime.dayNightGradient.Evaluate(0.5f)));
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

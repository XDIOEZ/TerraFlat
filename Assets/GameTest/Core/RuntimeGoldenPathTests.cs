using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.Core
{
    /// <summary>
    /// 从真实启动场景进入游戏，只通过公开行为 API 覆盖创建世界、玩家移动和 Chunk 流送。
    /// </summary>
    public sealed class RuntimeGoldenPathTests
    {
        private const string StartScenePath = "Assets/3_Scenes/GameStartScene.unity";
        private const float StartupTimeoutSeconds = 90f;
        private const float WorldEntryTimeoutSeconds = 180f;
        private const float MoveTimeoutSeconds = 20f;

        private GameManager gameManager;
        private SaveDataMgr saveDataManager;
        private Player player;
        private string originalSavePath;
        private string temporarySaveDirectory;
        private Action<WorldEntryProgressInfo> progressHandler;

        [UnityTest]
        [Category("Runtime.GoldenPath")]
        [Timeout(300000)]
        public IEnumerator CreateWorld_MoveAcrossChunksTwice_AndExitWithoutErrors()
        {
            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                StartScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(loadOperation, Is.Not.Null, $"无法加载启动场景：{StartScenePath}");
            while (!loadOperation.isDone)
                yield return null;

            yield return WaitForCondition(
                () => GameRes.Instance != null &&
                      GameRes.Instance.isLoadFinish &&
                      ModRuntimeManager.Instance != null &&
                      ModRuntimeManager.Instance.IsReady,
                StartupTimeoutSeconds,
                "游戏资源或 MOD 框架未在限定时间内就绪。 ");

            gameManager = GameManager.Instance;
            saveDataManager = SaveDataMgr.Instance;
            Assert.That(gameManager, Is.Not.Null);
            Assert.That(saveDataManager, Is.Not.Null);

            originalSavePath = saveDataManager.UserSavePath;
            temporarySaveDirectory = Path.GetFullPath(Path.Combine(
                "Library",
                "FlatWorldSkillTests",
                "RuntimeGoldenPath",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(temporarySaveDirectory);
            saveDataManager.UserSavePath = temporarySaveDirectory;

            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var request = new NewWorldCreationRequest(
                $"GoldenPathSave_{suffix}",
                $"GoldenPathPlayer_{suffix}",
                "424242",
                new PlanetData
                {
                    Name = $"GoldenPathWorld_{suffix}",
                    Radius = 256,
                    NoiseScale = PlanetData.DefaultNoiseScale,
                    ChunkSize = new Vector2Int(16, 16),
                    AutoGenerateMap = true
                },
                new TimeData(),
                GameDifficultyId.Simple);

            WorldEntryProgressState? terminalState = null;
            progressHandler = progress =>
            {
                if (progress.State != WorldEntryProgressState.Running)
                    terminalState = progress.State;
            };
            gameManager.WorldEntryProgressChanged += progressHandler;

            Assert.That(gameManager.CreateNewWorld(request), Is.True,
                "公开世界创建入口拒绝了合法请求。 ");

            yield return WaitForCondition(
                () => gameManager.IsInGameWorld &&
                      !gameManager.IsWorldEntryInProgress &&
                      ItemMgr.Instance != null &&
                      ItemMgr.Instance.User_Player != null &&
                      ChunkMgr.Instance != null &&
                      IsChunkWindowReady(ChunkMgr.Instance),
                WorldEntryTimeoutSeconds,
                "世界、玩家或初始 Chunk 窗口未在限定时间内完成。 ");

            Assert.That(terminalState, Is.EqualTo(WorldEntryProgressState.Completed),
                "世界进入生命周期没有正常完成。 ");
            player = ItemMgr.Instance.User_Player;
            Assert.That(player.Data.CurrentSceneName, Is.EqualTo(SceneManager.GetActiveScene().name));

            Mover mover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
            Mod_ChunkLoader chunkLoader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
            Assert.That(mover, Is.Not.Null, "玩家缺少公开移动模块 Mover。 ");
            Assert.That(mover.rb, Is.Not.Null, "Mover 缺少 Rigidbody2D。 ");
            Assert.That(mover.Speed, Is.Not.Null, "Mover 缺少速度数据。 ");
            Assert.That(chunkLoader, Is.Not.Null, "玩家缺少自动 Chunk 加载模块。 ");

            foreach (Collider2D collider in player.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            float originalBaseSpeed = mover.Speed.BaseValue;
            const float maximumTestSpeed = 24f;
            Vector2 startPosition = mover.rb.position;
            Vector2 chunkSize = ChunkMgr.GetChunkSize();
            float horizontalSpan = Mathf.Max(4f, chunkSize.x * 1.25f);
            float verticalSpan = Mathf.Max(4f, chunkSize.y * 1.25f);
            Vector2[] lap =
            {
                startPosition + new Vector2(horizontalSpan, 0f),
                startPosition + new Vector2(horizontalSpan, verticalSpan),
                startPosition + new Vector2(0f, verticalSpan),
                startPosition
            };

            var visitedChunks = new HashSet<Vector2Int>
            {
                Chunk.GetChunkPosition(startPosition, chunkSize)
            };

            for (int lapIndex = 0; lapIndex < 2; lapIndex++)
            {
                foreach (Vector2 waypoint in lap)
                {
                    yield return MovePlayerTo(mover, waypoint, maximumTestSpeed);
                    yield return null;

                    Vector2Int expectedChunk = Chunk.GetChunkPosition(mover.rb.position, chunkSize);
                    yield return WaitForCondition(
                        () => IsChunkReadyAt(ChunkMgr.Instance, expectedChunk),
                        WorldEntryTimeoutSeconds,
                        $"玩家移动到 {waypoint} 后，Chunk {expectedChunk} 未完成自动流送。 ");

                    visitedChunks.Add(expectedChunk);
                    AssertChunkDictionariesAreHealthy(ChunkMgr.Instance);
                }
            }

            mover.Speed.BaseValue = originalBaseSpeed;
            mover.Move(mover.rb.position, Mathf.Max(Time.deltaTime, 0.02f));

            Assert.That(visitedChunks.Count, Is.GreaterThanOrEqualTo(4),
                "两圈移动没有覆盖足够多的 Chunk。 ");
            Assert.That(
                Chunk.GetChunkPosition(mover.rb.position, chunkSize),
                Is.EqualTo(Chunk.GetChunkPosition(startPosition, chunkSize)),
                "两圈结束后玩家没有回到初始 Chunk。 ");

        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (gameManager != null && progressHandler != null)
                gameManager.WorldEntryProgressChanged -= progressHandler;

            if (gameManager != null && gameManager.IsInGameWorld)
            {
                Player activePlayer = player;
                if (activePlayer == null && ItemMgr.Instance != null)
                    activePlayer = ItemMgr.Instance.User_Player;

                yield return gameManager.BackToHelloScene_Coroutine(activePlayer);
            }

            if (saveDataManager != null && originalSavePath != null)
                saveDataManager.UserSavePath = originalSavePath;

            DeleteTemporarySaveDirectory();
            yield return null;
        }

        private static IEnumerator MovePlayerTo(Mover mover, Vector2 target, float maximumSpeed)
        {
            float deadline = Time.realtimeSinceStartup + MoveTimeoutSeconds;
            while (Vector2.Distance(mover.rb.position, target) > 0.2f &&
                   Time.realtimeSinceStartup < deadline)
            {
                float distance = Vector2.Distance(mover.rb.position, target);
                mover.Speed.BaseValue = Mathf.Clamp(distance * 8f, 2f, maximumSpeed);
                mover.Move(target, Mathf.Max(Time.deltaTime, 0.02f));
                yield return null;
            }

            mover.Move(mover.rb.position, Mathf.Max(Time.deltaTime, 0.02f));
            Assert.That(Vector2.Distance(mover.rb.position, target), Is.LessThanOrEqualTo(0.35f),
                $"Mover.Move 未在限定时间内到达 {target}。 ");
        }

        private static IEnumerator WaitForCondition(
            Func<bool> condition,
            float timeoutSeconds,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(condition(), Is.True, failureMessage);
        }

        private static bool IsChunkWindowReady(ChunkMgr chunkManager)
        {
            if (chunkManager == null ||
                chunkManager.HasPendingChunkLoads ||
                chunkManager.Chunk_Dic_Active_ByPos.Count == 0)
            {
                return false;
            }

            foreach (Chunk chunk in chunkManager.Chunk_Dic_Active_ByPos.Values)
            {
                if (chunk == null || !chunk.IsReady)
                    return false;
            }

            return true;
        }

        private static bool IsChunkReadyAt(ChunkMgr chunkManager, Vector2Int chunkPosition)
        {
            return IsChunkWindowReady(chunkManager) &&
                   chunkManager.TryGetActiveChunkByPos(chunkPosition, out Chunk chunk) &&
                   chunk != null &&
                   chunk.IsReady;
        }

        private static void AssertChunkDictionariesAreHealthy(ChunkMgr chunkManager)
        {
            Assert.That(chunkManager, Is.Not.Null);
            Assert.That(chunkManager.HasPendingChunkLoads, Is.False);
            Assert.That(chunkManager.Chunk_Dic_Active_ByPos.Count, Is.GreaterThan(0));

            foreach (KeyValuePair<Vector2Int, Chunk> entry in chunkManager.Chunk_Dic_Active_ByPos)
            {
                Assert.That(entry.Value, Is.Not.Null, $"激活 Chunk 字典包含空对象：{entry.Key}");
                Assert.That(entry.Value.IsReady, Is.True,
                    $"激活 Chunk 尚未 Ready：{entry.Key}, state={entry.Value.LifecycleState}");
            }
        }

        private void DeleteTemporarySaveDirectory()
        {
            if (string.IsNullOrWhiteSpace(temporarySaveDirectory) ||
                !Directory.Exists(temporarySaveDirectory))
            {
                return;
            }

            string allowedRoot = Path.GetFullPath(Path.Combine("Library", "FlatWorldSkillTests"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string resolvedTarget = Path.GetFullPath(temporarySaveDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            Assert.That(resolvedTarget.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase), Is.True,
                $"拒绝删除测试目录之外的路径：{resolvedTarget}");

            Directory.Delete(temporarySaveDirectory, recursive: true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 在项目真实启动场景中执行创建世界、移动玩家和 Chunk 流送验证。
    /// 该命令不经过 Unity Test Framework，因此不会创建 InitTestScene。
    /// </summary>
    [InitializeOnLoad]
    internal static class FlatWorldGoldenPathCommand
    {
        private const string StartScenePath = "Assets/3_Scenes/GameStartScene.unity";
        private const string RequestPrefix = "golden-request-";
        private const string RunningPrefix = "golden-running-";
        private const string PendingPrefix = "golden-pending-";
        private const string ResultPrefix = "golden-result-";
        private const string ActiveIdSessionKey = "FlatWorldGoldenPath.ActiveId";
        private const string StageSessionKey = "FlatWorldGoldenPath.Stage";
        private const string SettingsCapturedSessionKey = "FlatWorldGoldenPath.SettingsCaptured";
        private const string PlayModeSettingsCapturedSessionKey =
            "FlatWorldGoldenPath.PlayModeSettingsCaptured";
        private const string PreviousIdleTimeSessionKey = "FlatWorldGoldenPath.PreviousIdleTime";
        private const string PreviousInteractionModeSessionKey = "FlatWorldGoldenPath.PreviousInteractionMode";
        private const string PreviousEnterPlayModeOptionsEnabledSessionKey =
            "FlatWorldGoldenPath.PreviousEnterPlayModeOptionsEnabled";
        private const string PreviousEnterPlayModeOptionsSessionKey =
            "FlatWorldGoldenPath.PreviousEnterPlayModeOptions";
        private const string ApplicationIdleTimeKey = "ApplicationIdleTime";
        private const string InteractionModeKey = "InteractionMode";
        private const double PollIntervalSeconds = 0.2d;
        private const double RuntimePollIntervalSeconds = 0.01d;
        private const double StartupTimeoutSeconds = 90d;
        private const double WorldEntryTimeoutSeconds = 180d;
        private const double MoveTimeoutSeconds = 20d;
        private const float MaximumTestSpeed = 24f;

        private static readonly string CommandDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "FlatWorldSkillTests"));

        private static GoldenPathRequest _activeRequest;
        private static string _runningPath;
        private static double _nextPollTime;
        private static RuntimePhase _runtimePhase;
        private static double _runtimeStartedAt;
        private static double _phaseDeadline;
        private static string _runtimeError;
        private static GameManager _gameManager;
        private static SaveDataMgr _saveDataManager;
        private static string _originalSavePath;
        private static Player _player;
        private static Mover _mover;
        private static float _originalMoveSpeed;
        private static Vector2 _startPosition;
        private static Vector2 _chunkSize;
        private static Vector2[] _waypoints;
        private static int _waypointIndex;
        private static Vector2Int _expectedChunk;
        private static HashSet<Vector2Int> _visitedChunks;
        private static WorldEntryProgressState? _terminalWorldEntryState;
        private static string _terminalWorldEntryStatus;
        private static Action<WorldEntryProgressInfo> _worldEntryHandler;

        static FlatWorldGoldenPathCommand()
        {
            Directory.CreateDirectory(CommandDirectory);
            RecoverState();
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
                return;

            string stage = _activeRequest == null
                ? string.Empty
                : SessionState.GetString(StageSessionKey, string.Empty);
            double pollInterval = string.Equals(stage, "runtime", StringComparison.Ordinal)
                ? RuntimePollIntervalSeconds
                : PollIntervalSeconds;
            _nextPollTime = EditorApplication.timeSinceStartup + pollInterval;

            if (_activeRequest == null)
            {
                TryStartNextRequest();
                return;
            }

            string pendingPath = GetPath(PendingPrefix, _activeRequest.id);
            if (File.Exists(pendingPath))
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                    return;
                }

                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    FinalizeResult(pendingPath);
                }
                return;
            }

            if (string.Equals(stage, "entering", StringComparison.Ordinal))
            {
                if (EditorApplication.isPlaying)
                {
                    SessionState.SetString(StageSessionKey, "runtime");
                    BeginRuntimeExecution();
                }
                else if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                         !EditorApplication.isCompiling &&
                         !EditorApplication.isUpdating)
                {
                    EnterDefaultStartScene();
                }
                return;
            }

            if (string.Equals(stage, "runtime", StringComparison.Ordinal))
            {
                if (!EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        WriteFailureBeforePlay("Unity 在黄金路径完成前退出了 PlayMode。");
                    return;
                }

                if (_runtimePhase == RuntimePhase.None)
                    BeginRuntimeExecution();
                TickRuntimeExecution();
                return;
            }

            WriteFailureBeforePlay("黄金路径命令状态丢失，无法继续执行。");
        }

        private static void TryStartNextRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string requestPath = Directory.GetFiles(CommandDirectory, RequestPrefix + "*.json")
                .OrderBy(File.GetCreationTimeUtc)
                .FirstOrDefault();
            if (requestPath == null)
                return;

            // Unity 可能在 Refresh/Domain Reload 时把当前内存偏好写回 ProjectSettings。
            // 必须先锁定磁盘上的用户原值，黄金路径结束后再恢复。
            CaptureSerializedEnterPlayModeSettings();

            // AI 可能在 Editor 失焦或关闭 Auto Refresh 时修改了场景脚本。
            // 在接管请求前同步导入并编译；若触发 Domain Reload，请求文件保留供新 Domain 继续处理。
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            string id = Path.GetFileNameWithoutExtension(requestPath).Substring(RequestPrefix.Length);
            string runningPath = GetPath(RunningPrefix, id);
            try
            {
                File.Move(requestPath, runningPath);
                GoldenPathRequest request = ReadRequest(id, runningPath);
                SetActiveRequest(request, runningPath);

                if (EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Unity 脚本编译失败，请先修复编译错误。");

                SessionState.SetString(ActiveIdSessionKey, id);
                SessionState.SetString(StageSessionKey, "entering");
                CaptureNoThrottleSettings();
                EnterDefaultStartScene();
            }
            catch (Exception exception)
            {
                WriteFailureBeforePlay(exception.Message);
            }
        }

        private static void EnterDefaultStartScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isDirty)
                throw new InvalidOperationException(
                    $"当前场景 {activeScene.path} 有未保存改动，自动化不会覆盖它。");

            if (!string.Equals(activeScene.path, StartScenePath, StringComparison.OrdinalIgnoreCase))
            {
                Scene openedScene = EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Single);
                if (!openedScene.IsValid())
                    throw new InvalidOperationException($"无法打开默认启动场景：{StartScenePath}");
            }

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.EnterPlaymode();
        }

        private static void BeginRuntimeExecution()
        {
            ResetRuntimeReferences();
            FlatWorldGoldenPathScenarios.Reset();
            _runtimePhase = RuntimePhase.WaitForStartup;
            _runtimeStartedAt = EditorApplication.timeSinceStartup;
            _phaseDeadline = _runtimeStartedAt + StartupTimeoutSeconds;
            Application.logMessageReceived -= OnRuntimeLog;
            Application.logMessageReceived += OnRuntimeLog;
            Debug.Log("[FlatWorldGoldenPath] 已在 GameStartScene 启动代码级黄金路径。");
        }

        private static void TickRuntimeExecution()
        {
            EditorApplication.QueuePlayerLoopUpdate();

            if (!string.IsNullOrEmpty(_runtimeError))
            {
                FinishRuntime(false, _runtimeError);
                return;
            }

            try
            {
                switch (_runtimePhase)
                {
                    case RuntimePhase.WaitForStartup:
                        TickWaitForStartup();
                        break;
                    case RuntimePhase.WaitForWorld:
                        TickWaitForWorld();
                        break;
                    case RuntimePhase.MoveToWaypoint:
                        TickMoveToWaypoint();
                        break;
                    case RuntimePhase.WaitForChunk:
                        TickWaitForChunk();
                        break;
                }
            }
            catch (Exception exception)
            {
                FinishRuntime(false, exception.Message, exception.ToString());
            }
        }

        private static void TickWaitForStartup()
        {
            bool ready = GameRes.Instance != null &&
                         GameRes.Instance.isLoadFinish &&
                         ModRuntimeManager.Instance != null &&
                         ModRuntimeManager.Instance.IsReady;
            if (!ready)
            {
                ThrowIfTimedOut("游戏资源或 MOD 框架未在限定时间内就绪。");
                return;
            }

            _gameManager = GameManager.Instance;
            _saveDataManager = SaveDataMgr.Instance;
            if (_gameManager == null || _saveDataManager == null)
                throw new InvalidOperationException("GameStartScene 缺少 GameManager 或 SaveDataMgr。");

            _originalSavePath = _saveDataManager.UserSavePath;
            string temporarySaveDirectory = GetTemporarySaveDirectory(_activeRequest.id);
            Directory.CreateDirectory(temporarySaveDirectory);
            _saveDataManager.UserSavePath = temporarySaveDirectory;

            string suffix = _activeRequest.id.Substring(0, Math.Min(8, _activeRequest.id.Length));
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

            _worldEntryHandler = progress =>
            {
                if (progress.State == WorldEntryProgressState.Running)
                    return;
                _terminalWorldEntryState = progress.State;
                _terminalWorldEntryStatus = progress.Status;
            };
            _gameManager.WorldEntryProgressChanged += _worldEntryHandler;

            if (!_gameManager.CreateNewWorld(request))
                throw new InvalidOperationException("公开世界创建入口拒绝了合法请求。");

            _runtimePhase = RuntimePhase.WaitForWorld;
            _phaseDeadline = EditorApplication.timeSinceStartup + WorldEntryTimeoutSeconds;
        }

        private static void TickWaitForWorld()
        {
            if (_terminalWorldEntryState == WorldEntryProgressState.Failed)
                throw new InvalidOperationException(
                    $"世界进入生命周期失败：{_terminalWorldEntryStatus ?? "未知错误"}");

            bool ready = _gameManager != null &&
                         _gameManager.IsInGameWorld &&
                         !_gameManager.IsWorldEntryInProgress &&
                         ItemMgr.Instance != null &&
                         ItemMgr.Instance.User_Player != null &&
                         ChunkMgr.Instance != null &&
                         IsChunkWindowReady(ChunkMgr.Instance);
            if (!ready)
            {
                ThrowIfTimedOut("世界、玩家或初始 Chunk 窗口未在限定时间内完成。");
                return;
            }

            if (_terminalWorldEntryState != WorldEntryProgressState.Completed)
                throw new InvalidOperationException("世界进入生命周期没有报告 Completed。");

            _player = ItemMgr.Instance.User_Player;
            if (_player.Data.CurrentSceneName != SceneManager.GetActiveScene().name)
                throw new InvalidOperationException("玩家保存的当前场景与实际场景不一致。");

            _mover = _player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
            Mod_ChunkLoader chunkLoader =
                _player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
            if (_mover == null || _mover.rb == null || _mover.Speed == null)
                throw new InvalidOperationException("玩家缺少可用的公开移动模块 Mover。");
            if (chunkLoader == null)
                throw new InvalidOperationException("玩家缺少自动 Chunk 加载模块。");

            foreach (Collider2D collider in _player.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            _originalMoveSpeed = _mover.Speed.BaseValue;
            _startPosition = _mover.rb.position;
            _chunkSize = ChunkMgr.GetChunkSize();
            float horizontalSpan = Mathf.Max(4f, _chunkSize.x * 1.25f);
            float verticalSpan = Mathf.Max(4f, _chunkSize.y * 1.25f);
            Vector2[] lap =
            {
                _startPosition + new Vector2(horizontalSpan, 0f),
                _startPosition + new Vector2(horizontalSpan, verticalSpan),
                _startPosition + new Vector2(0f, verticalSpan),
                _startPosition
            };
            _waypoints = lap.Concat(lap).ToArray();
            _waypointIndex = 0;
            _visitedChunks = new HashSet<Vector2Int>
            {
                Chunk.GetChunkPosition(_startPosition, _chunkSize)
            };
            FlatWorldGoldenPathScenarios.OnWorldReady(CreateScenarioContext());
            _runtimePhase = RuntimePhase.MoveToWaypoint;
            _phaseDeadline = EditorApplication.timeSinceStartup + MoveTimeoutSeconds;
        }

        private static void TickMoveToWaypoint()
        {
            if (_mover == null || _mover.rb == null)
                throw new InvalidOperationException("移动过程中 Mover 或 Rigidbody2D 被销毁。");

            Vector2 target = _waypoints[_waypointIndex];
            FlatWorldGoldenPathScenarios.OnTraversalTick(CreateScenarioContext());
            float distance = Vector2.Distance(_mover.rb.position, target);
            if (distance > 0.2f)
            {
                ThrowIfTimedOut($"Mover.Move 未在限定时间内到达 {target}。");
                _mover.Speed.BaseValue = Mathf.Clamp(distance * 8f, 2f, MaximumTestSpeed);
                _mover.Move(target, Mathf.Max(Time.deltaTime, 0.02f));
                return;
            }

            _mover.Move(_mover.rb.position, Mathf.Max(Time.deltaTime, 0.02f));
            _expectedChunk = Chunk.GetChunkPosition(_mover.rb.position, _chunkSize);
            _runtimePhase = RuntimePhase.WaitForChunk;
            _phaseDeadline = EditorApplication.timeSinceStartup + WorldEntryTimeoutSeconds;
        }

        private static void TickWaitForChunk()
        {
            if (!IsChunkReadyAt(ChunkMgr.Instance, _expectedChunk))
            {
                ThrowIfTimedOut($"玩家移动后，Chunk {_expectedChunk} 未完成自动流送。");
                return;
            }

            ValidateChunkDictionaries(ChunkMgr.Instance);
            _visitedChunks.Add(_expectedChunk);
            FlatWorldGoldenPathScenarios.OnChunkReady(CreateScenarioContext());
            _waypointIndex++;
            if (_waypointIndex < _waypoints.Length)
            {
                _runtimePhase = RuntimePhase.MoveToWaypoint;
                _phaseDeadline = EditorApplication.timeSinceStartup + MoveTimeoutSeconds;
                return;
            }

            if (_visitedChunks.Count < 4)
                throw new InvalidOperationException("两圈移动没有覆盖至少 4 个 Chunk。");
            if (Chunk.GetChunkPosition(_mover.rb.position, _chunkSize) !=
                Chunk.GetChunkPosition(_startPosition, _chunkSize))
            {
                throw new InvalidOperationException("两圈结束后玩家没有回到初始 Chunk。");
            }

            FlatWorldGoldenPathScenarios.BeforeWorldExit(CreateScenarioContext());
            FinishRuntime(true, "创建世界、移动两圈及 Chunk 流送验证全部通过。");
        }

        private static void ThrowIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup >= _phaseDeadline)
                throw new TimeoutException(message);
        }

        private static bool IsChunkWindowReady(ChunkMgr chunkManager)
        {
            if (chunkManager == null || chunkManager.HasPendingChunkLoads ||
                chunkManager.Chunk_Dic_Active_ByPos.Count == 0)
            {
                return false;
            }

            return chunkManager.Chunk_Dic_Active_ByPos.Values.All(chunk =>
                chunk != null && chunk.IsReady);
        }

        private static bool IsChunkReadyAt(ChunkMgr chunkManager, Vector2Int chunkPosition)
        {
            return IsChunkWindowReady(chunkManager) &&
                   chunkManager.TryGetActiveChunkByPos(chunkPosition, out Chunk chunk) &&
                   chunk != null &&
                   chunk.IsReady;
        }

        private static void ValidateChunkDictionaries(ChunkMgr chunkManager)
        {
            if (chunkManager == null || chunkManager.HasPendingChunkLoads ||
                chunkManager.Chunk_Dic_Active_ByPos.Count == 0)
            {
                throw new InvalidOperationException("Chunk 激活字典尚未稳定。");
            }

            foreach (KeyValuePair<Vector2Int, Chunk> entry in chunkManager.Chunk_Dic_Active_ByPos)
            {
                if (entry.Value == null)
                    throw new InvalidOperationException($"激活 Chunk 字典包含空对象：{entry.Key}");
                if (!entry.Value.IsReady)
                {
                    throw new InvalidOperationException(
                        $"激活 Chunk 尚未 Ready：{entry.Key}, state={entry.Value.LifecycleState}");
                }
            }
        }

        private static void FinishRuntime(bool passed, string message, string stackTrace = "")
        {
            if (_runtimePhase == RuntimePhase.Finishing)
                return;
            _runtimePhase = RuntimePhase.Finishing;

            try
            {
                FlatWorldGoldenPathScenarios.Cleanup(CreateScenarioContext());
            }
            catch (Exception cleanupException)
            {
                passed = false;
                message = string.IsNullOrWhiteSpace(message)
                    ? $"黄金路径场景清理失败：{cleanupException.Message}"
                    : $"{message} 场景清理同时失败：{cleanupException.Message}";
                stackTrace = string.IsNullOrWhiteSpace(stackTrace)
                    ? cleanupException.ToString()
                    : stackTrace + Environment.NewLine + cleanupException;
            }

            Application.logMessageReceived -= OnRuntimeLog;
            if (_gameManager != null && _worldEntryHandler != null)
                _gameManager.WorldEntryProgressChanged -= _worldEntryHandler;
            if (_mover != null && _mover.Speed != null)
            {
                _mover.Speed.BaseValue = _originalMoveSpeed;
                if (_mover.rb != null)
                    _mover.Move(_mover.rb.position, Mathf.Max(Time.deltaTime, 0.02f));
            }
            if (_saveDataManager != null && _originalSavePath != null)
                _saveDataManager.UserSavePath = _originalSavePath;

            var response = CreateResponse(passed, message, stackTrace);
            AtomicWrite(GetPath(PendingPrefix, _activeRequest.id), JsonUtility.ToJson(response, true));
            SessionState.SetString(StageSessionKey, "exiting");
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
        }

        private static FlatWorldGoldenPathScenarioContext CreateScenarioContext()
        {
            Vector2 currentPosition = _mover != null && _mover.rb != null
                ? _mover.rb.position
                : default;
            Vector2 targetPosition = _waypoints != null &&
                                     _waypointIndex >= 0 &&
                                     _waypointIndex < _waypoints.Length
                ? _waypoints[_waypointIndex]
                : currentPosition;

            return new FlatWorldGoldenPathScenarioContext(
                _gameManager,
                _saveDataManager,
                _player,
                _mover,
                _waypointIndex,
                _waypoints?.Length ?? 0,
                currentPosition,
                targetPosition,
                _expectedChunk);
        }

        private static GoldenPathResponse CreateResponse(bool passed, string message, string stackTrace)
        {
            return new GoldenPathResponse
            {
                id = _activeRequest.id,
                state = "completed",
                outcome = passed ? "Passed" : "Failed",
                mode = "PlayMode",
                categories = new[] { "Runtime.GoldenPath" },
                testNames = new[] { "FlatWorld.DefaultStartScene.CreateWorldMoveTwoLaps" },
                startedUtc = _activeRequest.createdUtc ?? string.Empty,
                finishedUtc = DateTime.UtcNow.ToString("O"),
                durationSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - _runtimeStartedAt),
                total = 1,
                passed = passed ? 1 : 0,
                failed = passed ? 0 : 1,
                skipped = 0,
                inconclusive = 0,
                failures = passed
                    ? new List<GoldenPathFailure>()
                    : new List<GoldenPathFailure>
                    {
                        new()
                        {
                            fullName = "FlatWorld.DefaultStartScene.CreateWorldMoveTwoLaps",
                            resultState = "Failed",
                            message = message ?? "黄金路径失败。",
                            stackTrace = stackTrace ?? string.Empty,
                            durationSeconds = Math.Max(
                                0d,
                                EditorApplication.timeSinceStartup - _runtimeStartedAt)
                        }
                    },
                message = passed ? message ?? string.Empty : string.Empty
            };
        }

        private static void OnRuntimeLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            if (!string.IsNullOrEmpty(_runtimeError))
                return;

            _runtimeError = string.IsNullOrWhiteSpace(stackTrace)
                ? condition
                : condition + Environment.NewLine + stackTrace;
        }

        private static void RecoverState()
        {
            string pendingPath = Directory.GetFiles(CommandDirectory, PendingPrefix + "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (pendingPath != null)
            {
                string id = Path.GetFileNameWithoutExtension(pendingPath).Substring(PendingPrefix.Length);
                string runningPath = GetPath(RunningPrefix, id);
                if (File.Exists(runningPath))
                    SetActiveRequest(ReadRequest(id, runningPath), runningPath);
                return;
            }

            string running = Directory.GetFiles(CommandDirectory, RunningPrefix + "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (running == null)
                return;

            string runningId = Path.GetFileNameWithoutExtension(running).Substring(RunningPrefix.Length);
            if (string.Equals(
                    SessionState.GetString(ActiveIdSessionKey, string.Empty),
                    runningId,
                    StringComparison.Ordinal))
            {
                SetActiveRequest(ReadRequest(runningId, running), running);
                return;
            }

            GoldenPathRequest staleRequest = ReadRequest(runningId, running);
            SetActiveRequest(staleRequest, running);
            WriteFailureBeforePlay("Unity 在上一次黄金路径执行中退出，已清理中断请求。");
        }

        private static GoldenPathRequest ReadRequest(string id, string path)
        {
            GoldenPathRequest request = JsonUtility.FromJson<GoldenPathRequest>(File.ReadAllText(path));
            if (request == null || !string.Equals(request.id, id, StringComparison.Ordinal))
                throw new InvalidDataException("黄金路径请求 JSON 无效或 ID 不匹配。");
            return request;
        }

        private static void SetActiveRequest(GoldenPathRequest request, string runningPath)
        {
            _activeRequest = request;
            _runningPath = runningPath;
        }

        private static void WriteFailureBeforePlay(string message)
        {
            if (_activeRequest == null)
                return;

            _runtimeStartedAt = EditorApplication.timeSinceStartup;
            GoldenPathResponse response = CreateResponse(false, message, string.Empty);
            AtomicWrite(GetPath(PendingPrefix, _activeRequest.id), JsonUtility.ToJson(response, true));
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                FinalizeResult(GetPath(PendingPrefix, _activeRequest.id));
        }

        private static void FinalizeResult(string pendingPath)
        {
            GoldenPathResponse response;
            try
            {
                response = JsonUtility.FromJson<GoldenPathResponse>(File.ReadAllText(pendingPath));
                if (response == null || string.IsNullOrWhiteSpace(response.id))
                    throw new InvalidDataException("黄金路径结果 JSON 无效。");

                try
                {
                    DeleteTemporarySaveDirectory(response.id);
                }
                catch (Exception cleanupException)
                {
                    response.outcome = "Failed";
                    response.passed = 0;
                    response.failed = 1;
                    response.failures ??= new List<GoldenPathFailure>();
                    response.failures.Add(new GoldenPathFailure
                    {
                        fullName = "FlatWorld.DefaultStartScene.Cleanup",
                        resultState = "Failed",
                        message = cleanupException.Message,
                        stackTrace = cleanupException.ToString(),
                        durationSeconds = 0d
                    });
                }

                AtomicWrite(GetPath(ResultPrefix, response.id), JsonUtility.ToJson(response, true));
            }
            finally
            {
                DeleteIfExists(pendingPath);
                DeleteIfExists(_runningPath);
                RestoreNoThrottleSettings();
                SessionState.EraseString(ActiveIdSessionKey);
                SessionState.EraseString(StageSessionKey);
                ResetRuntimeReferences();
                _activeRequest = null;
                _runningPath = null;
            }
        }

        private static void CaptureNoThrottleSettings()
        {
            if (!SessionState.GetBool(SettingsCapturedSessionKey, false))
            {
                SessionState.SetInt(
                    PreviousIdleTimeSessionKey,
                    EditorPrefs.GetInt(ApplicationIdleTimeKey, 4));
                SessionState.SetInt(
                    PreviousInteractionModeSessionKey,
                    EditorPrefs.GetInt(InteractionModeKey, 0));

                CaptureSerializedEnterPlayModeSettings();
                SessionState.SetBool(SettingsCapturedSessionKey, true);
            }

            // 黄金路径必须从全新的静态域启动，避免连续 PlayMode 运行残留单例和事件。
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorPrefs.SetInt(ApplicationIdleTimeKey, 0);
            EditorPrefs.SetInt(InteractionModeKey, 1);
            ApplyInteractionSettings();
        }

        private static void RestoreNoThrottleSettings()
        {
            bool settingsCaptured = SessionState.GetBool(SettingsCapturedSessionKey, false);
            bool playModeSettingsCaptured = SessionState.GetBool(
                PlayModeSettingsCapturedSessionKey,
                false);
            if (!settingsCaptured && !playModeSettingsCaptured)
                return;

            if (settingsCaptured)
            {
                EditorPrefs.SetInt(
                    ApplicationIdleTimeKey,
                    SessionState.GetInt(PreviousIdleTimeSessionKey, 4));
                EditorPrefs.SetInt(
                    InteractionModeKey,
                    SessionState.GetInt(PreviousInteractionModeSessionKey, 0));
                ApplyInteractionSettings();
            }

            if (playModeSettingsCaptured)
            {
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(
                    PreviousEnterPlayModeOptionsSessionKey,
                    0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(
                    PreviousEnterPlayModeOptionsEnabledSessionKey,
                    false);
            }

            SessionState.EraseBool(SettingsCapturedSessionKey);
            SessionState.EraseBool(PlayModeSettingsCapturedSessionKey);
            SessionState.EraseInt(PreviousIdleTimeSessionKey);
            SessionState.EraseInt(PreviousInteractionModeSessionKey);
            SessionState.EraseBool(PreviousEnterPlayModeOptionsEnabledSessionKey);
            SessionState.EraseInt(PreviousEnterPlayModeOptionsSessionKey);
        }

        private static void CaptureSerializedEnterPlayModeSettings()
        {
            if (SessionState.GetBool(PlayModeSettingsCapturedSessionKey, false))
                return;

            bool enterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions enterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            string settingsPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "ProjectSettings",
                "EditorSettings.asset"));
            if (File.Exists(settingsPath))
            {
                foreach (string line in File.ReadLines(settingsPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("m_EnterPlayModeOptionsEnabled:", StringComparison.Ordinal) &&
                        int.TryParse(
                            trimmed.Substring(trimmed.IndexOf(':') + 1).Trim(),
                            out int enabledValue))
                    {
                        enterPlayModeOptionsEnabled = enabledValue != 0;
                    }
                    else if (trimmed.StartsWith("m_EnterPlayModeOptions:", StringComparison.Ordinal) &&
                             int.TryParse(
                                 trimmed.Substring(trimmed.IndexOf(':') + 1).Trim(),
                                 out int optionsValue))
                    {
                        enterPlayModeOptions = (EnterPlayModeOptions)optionsValue;
                    }
                }
            }

            SessionState.SetBool(
                PreviousEnterPlayModeOptionsEnabledSessionKey,
                enterPlayModeOptionsEnabled);
            SessionState.SetInt(
                PreviousEnterPlayModeOptionsSessionKey,
                (int)enterPlayModeOptions);
            SessionState.SetBool(PlayModeSettingsCapturedSessionKey, true);
        }

        private static void ApplyInteractionSettings()
        {
            try
            {
                MethodInfo method = typeof(EditorApplication).GetMethod(
                    "UpdateInteractionModeSettings",
                    BindingFlags.Static | BindingFlags.NonPublic);
                method?.Invoke(null, null);
            }
            catch
            {
                // 反射 API 在不同 Unity 小版本可能不存在；EditorPrefs 本身仍然有效。
            }
        }

        private static void DeleteTemporarySaveDirectory(string id)
        {
            string target = GetTemporarySaveDirectory(id);
            if (!Directory.Exists(target))
                return;

            string allowedRoot = Path.GetFullPath(Path.Combine(CommandDirectory, "RuntimeGoldenPath"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string resolvedTarget = Path.GetFullPath(target)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!resolvedTarget.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"拒绝删除测试目录之外的路径：{resolvedTarget}");

            Directory.Delete(target, true);
        }

        private static string GetTemporarySaveDirectory(string id)
        {
            return Path.GetFullPath(Path.Combine(CommandDirectory, "RuntimeGoldenPath", id));
        }

        private static string GetPath(string prefix, string id)
        {
            return Path.Combine(CommandDirectory, prefix + id + ".json");
        }

        private static void AtomicWrite(string path, string contents)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, contents);
            DeleteIfExists(path);
            File.Move(temporaryPath, path);
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }

        private static void ResetRuntimeReferences()
        {
            Application.logMessageReceived -= OnRuntimeLog;
            _runtimePhase = RuntimePhase.None;
            _runtimeError = null;
            _gameManager = null;
            _saveDataManager = null;
            _originalSavePath = null;
            _player = null;
            _mover = null;
            _originalMoveSpeed = 0f;
            _waypoints = null;
            _waypointIndex = 0;
            _visitedChunks = null;
            _terminalWorldEntryState = null;
            _terminalWorldEntryStatus = null;
            _worldEntryHandler = null;
        }

        private enum RuntimePhase
        {
            None,
            WaitForStartup,
            WaitForWorld,
            MoveToWaypoint,
            WaitForChunk,
            Finishing
        }

        [Serializable]
        private sealed class GoldenPathRequest
        {
            public string id;
            public string createdUtc;
        }

        [Serializable]
        private sealed class GoldenPathResponse
        {
            public string id;
            public string state;
            public string outcome;
            public string mode;
            public string[] categories;
            public string[] testNames;
            public string startedUtc;
            public string finishedUtc;
            public double durationSeconds;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public List<GoldenPathFailure> failures;
            public string message;
        }

        [Serializable]
        private sealed class GoldenPathFailure
        {
            public string fullName;
            public string resultState;
            public string message;
            public string stackTrace;
            public double durationSeconds;
        }
    }
}

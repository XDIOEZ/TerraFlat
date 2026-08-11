using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
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
        private static readonly string CommandDirectory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Library", "FlatWorldSkillTests"));

        private static GoldenPathRequest _activeRequest;
        private static FlatWorldGoldenPathExecutor _executor;
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
        private static Coroutine _movementDriverCoroutine;
        private static bool _movementDriveActive;
        private static Vector2 _movementDriveTarget;
        private static float _originalMoveSpeed;
        private static Vector2 _startPosition;
        private static Vector2 _chunkSize;
        private static Vector2 _travelDirection;
        private static float _plannedTravelDistance;
        private static Vector2[] _waypoints;
        private static int _waypointIndex;
        private static Vector2Int _expectedChunk;
        private static HashSet<Vector2Int> _visitedChunks;
        private static HashSet<Vector2Int> _observedChunks;
        private static List<string> _screenshotPaths;
        private static string _pendingScreenshotPath;
        private static int _screenshotCaptureAfterFrame;
        private static double _screenshotCaptureNotBefore;
        private static bool _screenshotRequested;
        private static bool _screenshotViewApplied;
        private static ScreenshotContinuation _screenshotContinuation;
        private static WorldEntryProgressState? _terminalWorldEntryState;
        private static string _terminalWorldEntryStatus;
        private static Action<WorldEntryProgressInfo> _worldEntryHandler;
        private static bool _worldSetupApplied;
        private static bool _worldExitCompleted;
        private static string _completionMessage;
        private static string _reentrySaveName;
        private static string _reentryPlayerName;
        private static string _reentryWorldKey;

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

            if (!EnsureRequiredMonoScriptsReady())
                return;
            RefreshRequiredPrefabComponents();
            EnsureAddressablesUseAssetDatabase();

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
                request.ownerProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
                AtomicWrite(runningPath, JsonUtility.ToJson(request, true));
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

        /// <summary>修复并验证实际路径依赖的 MonoScript 类型映射。</summary>
        private static bool EnsureRequiredMonoScriptsReady()
        {
            (string Path, Type Type)[] requiredScripts =
            {
                ("Assets/5_Scripts/5-3_GamePlay/Entities/Item/GameItem.cs", typeof(GameItem)),
                ("Assets/5_Scripts/5-3_GamePlay/Entities/Item/Mod_Furnace.cs", typeof(Mod_Furnace)),
                ("Assets/5_Scripts/5-3_GamePlay/Items/Food/Meatrack.cs", typeof(Meatrack)),
                ("Assets/5_Scripts/5-3_GamePlay/Items/Equipment/Mod_Equipment.cs", typeof(Mod_Equipment)),
                ("Assets/5_Scripts/5-3_GamePlay/World/Map/SO/Tile_Block.cs", typeof(Tile_Block)),
                ("Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkTilePaletteSO.cs", typeof(ChunkTilePaletteSO)),
                ("Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Configuration/ChunkGenerationProfileSO.cs", typeof(ChunkGenerationProfileSO)),
                ("Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureCatalogSO.cs", typeof(StructureCatalogSO)),
                ("Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureDefinitionSO.cs", typeof(StructureDefinitionSO)),
                ("Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureTemplateSO.cs", typeof(StructureTemplateSO)),
                ("Assets/5_Scripts/5-3_GamePlay/World/Map/SO/BiomeData.cs", typeof(BiomeData))
            };

            bool requestedImport = false;
            foreach ((string path, Type expectedType) in requiredScripts)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == expectedType)
                    continue;

                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                requestedImport = true;
            }

            string[] prefabPaths = GetRuntimeAddressablePrefabPaths();
            string[] dependencies = prefabPaths.Length > 0
                ? AssetDatabase.GetDependencies(prefabPaths, true)
                : Array.Empty<string>();
            foreach (string scriptPath in dependencies
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (script != null && script.GetClass() != null)
                    continue;

                AssetDatabase.ImportAsset(
                    scriptPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                requestedImport = true;
            }

            // 脚本重导入可能触发编译/Domain Reload；下一轮 Poll 再做硬校验并接管请求。
            if (requestedImport)
                return false;

            return true;
        }

        /// <summary>脚本映射恢复后，重导入仍缓存为 Missing Script 的关键 Prefab。</summary>
        private static void RefreshRequiredPrefabComponents()
        {
            (string Path, Type Type)[] requiredPrefabs =
            {
                ("Assets/2_Prefabs/Building/Meatrack.prefab", typeof(Meatrack)),
                ("Assets/2_Prefabs/Building/Summoners/Scarecrow_Summoner.prefab", typeof(Mod_Equipment))
            };

            foreach ((string path, Type expectedType) in requiredPrefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponentInChildren(expectedType, true) != null)
                    continue;

                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            foreach (string path in GetRuntimeAddressablePrefabPaths())
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && !HasMissingMonoScript(prefab))
                    continue;

                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
        }

        /// <summary>收集当前运行时 Prefab 标签实际会加载的 Addressables 资源。</summary>
        private static string[] GetRuntimeAddressablePrefabPaths()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return Array.Empty<string>();

            return settings.groups
                .Where(group => group != null)
                .SelectMany(group => group.entries)
                .Where(entry => entry != null &&
                                entry.labels.Contains("Prefab") &&
                                entry.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.AssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>检查 Prefab 根节点及全部子节点是否仍含 Missing Script。</summary>
        private static bool HasMissingMonoScript(GameObject prefab)
        {
            foreach (Transform node in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) > 0)
                    return true;
            }

            return false;
        }

        /// <summary>Golden Path 必须读取当前 AssetDatabase，不能复用可能陈旧的 Addressables Bundle。</summary>
        private static void EnsureAddressablesUseAssetDatabase()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Golden Path 找不到 Addressables 设置。");

            for (int index = 0; index < settings.DataBuilders.Count; index++)
            {
                if (settings.DataBuilders[index] is not BuildScriptFastMode)
                    continue;

                settings.ActivePlayModeDataBuilderIndex = index;
                return;
            }

            throw new InvalidOperationException("Golden Path 找不到 Addressables Fast Mode 数据构建器。");
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
            _runtimeStartedAt = EditorApplication.timeSinceStartup;
            Application.logMessageReceived -= OnRuntimeLog;
            Application.logMessageReceived += OnRuntimeLog;

            try
            {
                _executor = new FlatWorldGoldenPathExecutor(
                    _activeRequest.configuration ?? FlatWorldGoldenPathConfiguration.CreateDefault());
                _activeRequest.configuration = _executor.Configuration;
                _runtimePhase = RuntimePhase.WaitForStartup;
                _phaseDeadline = _runtimeStartedAt +
                                 _executor.Configuration.execution.startupTimeoutSeconds;
                Debug.Log(
                    $"[FlatWorldGoldenPath] Effective configuration: " +
                    JsonUtility.ToJson(_executor.Configuration));
                Debug.Log("[FlatWorldGoldenPath] 已在 GameStartScene 启动代码级黄金路径。");
            }
            catch (Exception exception)
            {
                FinishRuntime(false, exception.Message, exception.ToString());
            }
        }

        private static void TickRuntimeExecution()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            RecordObservedActiveChunks(ChunkMgr.Instance);

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
                    case RuntimePhase.WaitForWorldReadyScenarios:
                        TickWaitForWorldReadyScenarios();
                        break;
                    case RuntimePhase.VerifyWorldWrap:
                        TickVerifyWorldWrap();
                        break;
                    case RuntimePhase.RestoreWorldWrap:
                        TickRestoreWorldWrap();
                        break;
                    case RuntimePhase.MoveWorldModelAway:
                        TickMoveWorldModelAway();
                        break;
                    case RuntimePhase.WaitWorldModelAway:
                        TickWaitWorldModelAway();
                        break;
                    case RuntimePhase.MoveWorldModelReturn:
                        TickMoveWorldModelReturn();
                        break;
                    case RuntimePhase.WaitWorldModelReturn:
                        TickWaitWorldModelReturn();
                        break;
                    case RuntimePhase.MoveToWaypoint:
                        TickMoveToWaypoint();
                        break;
                    case RuntimePhase.WaitForChunk:
                        TickWaitForChunk();
                        break;
                    case RuntimePhase.WaitForScreenshot:
                        TickWaitForScreenshot();
                        break;
                    case RuntimePhase.WaitForWorldExit:
                        TickWaitForWorldExit();
                        break;
                    case RuntimePhase.WaitForWorldReentry:
                        TickWaitForWorldReentry();
                        break;
                    case RuntimePhase.WaitForFinalWorldExit:
                        TickWaitForFinalWorldExit();
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
            NewWorldCreationRequest request = _executor.CreateWorldRequest(suffix);

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
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
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
                         ChunkMgr.Instance != null;
            if (!ready)
            {
                ThrowIfTimedOut("世界、玩家或初始 Chunk 窗口未在限定时间内完成。");
                return;
            }

            if (!_worldSetupApplied)
            {
                Player configuredPlayer = ItemMgr.Instance.User_Player;
                Mover configuredMover = configuredPlayer.itemMods.GetMod_ByID<Mover>(ModText.Mover);
                Mod_ChunkLoader configuredChunkLoader =
                    configuredPlayer.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);
                if (configuredMover == null || configuredMover.rb == null || configuredMover.Speed == null)
                    throw new InvalidOperationException("GoldenPath player has no usable Mover.");
                if (configuredChunkLoader == null)
                    throw new InvalidOperationException("GoldenPath player has no Chunk loader.");

                _player = configuredPlayer;
                _mover = configuredMover;
                _originalMoveSpeed = configuredMover.Speed.BaseValue;
                foreach (Collider2D collider in configuredPlayer.GetComponentsInChildren<Collider2D>(true))
                    collider.enabled = false;
                _executor.ConfigurePlayer(configuredPlayer, configuredChunkLoader);
                _worldSetupApplied = true;
                _phaseDeadline = EditorApplication.timeSinceStartup +
                                 _executor.Configuration.execution.worldEntryTimeoutSeconds;
                return;
            }

            if (!IsChunkWindowReady(ChunkMgr.Instance))
            {
                ThrowIfTimedOut("Configured camera Chunk window did not become Ready.");
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
            GoldenPathPlayerConfiguration playerConfig = _executor.Configuration.player;
            _travelDirection = SelectDeterministicDirection(_executor.Configuration.world.seed);
            float waypointStep = Mathf.Max(_chunkSize.x, _chunkSize.y) *
                                 playerConfig.waypointStepChunks;
            _plannedTravelDistance = waypointStep * playerConfig.waypointCount;
            _waypoints = new Vector2[playerConfig.waypointCount];
            Vector2 routePosition = _startPosition;
            for (int index = 0; index < _waypoints.Length; index++)
            {
                routePosition = WorldTopologyRuntime.NormalizePosition(
                    routePosition + _travelDirection * waypointStep);
                _waypoints[index] = routePosition;
            }
            _waypointIndex = 0;
            _visitedChunks = new HashSet<Vector2Int>
            {
                ChunkMgr.Instance.ResolveRuntimeChunkOrigin(_startPosition)
            };
            _observedChunks = new HashSet<Vector2Int>();
            _screenshotPaths = new List<string>(3);
            RecordObservedActiveChunks(ChunkMgr.Instance);
            FlatWorldGoldenPathScenarios.OnWorldReady(CreateScenarioContext());
            _runtimePhase = RuntimePhase.WaitForWorldReadyScenarios;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
        }

        /// <summary>给帧末批处理的表现层留下真实帧，再继续截图、移动和自动保存。</summary>
        private static void TickWaitForWorldReadyScenarios()
        {
            if (!FlatWorldGoldenPathScenarios.TickWorldReadyScenarios(CreateScenarioContext()))
            {
                ThrowIfTimedOut("世界就绪后的跨帧表现断言未在限定时间内完成。");
                return;
            }

            if (_executor.Configuration.scenarios.worldWrap)
            {
                FlatWorldGoldenPathScenarios.BeginWorldWrapScenario(CreateScenarioContext());
                _runtimePhase = RuntimePhase.VerifyWorldWrap;
                _phaseDeadline = EditorApplication.timeSinceStartup +
                                 _executor.Configuration.execution.worldEntryTimeoutSeconds;
            }
            else
            {
                BeginInitialScreenshot();
            }
        }

        private static void TickVerifyWorldWrap()
        {
            if (!FlatWorldGoldenPathScenarios.TickWorldWrapScenario(CreateScenarioContext()))
            {
                ThrowIfTimedOut("玩家右边界环绕、目标 Chunk Ready 或原位置恢复超时。");
                return;
            }

            PrepareDaylightForVisualCapture();
            Debug.Log(
                $"[FlatWorldGoldenPath] 直线路线 direction={_travelDirection}, " +
                $"waypoints={_waypoints.Length}, distance={_plannedTravelDistance:0.##}。");
            BeginScreenshotCapture("initial", ScreenshotContinuation.BeginTraversal);
        }

        private static void BeginInitialScreenshot()
        {
            PrepareDaylightForVisualCapture();
            Debug.Log(
                $"[FlatWorldGoldenPath] route direction={_travelDirection}, " +
                $"waypoints={_waypoints.Length}, distance={_plannedTravelDistance:0.##}.");
            BeginScreenshotCapture("initial", ScreenshotContinuation.BeginTraversal);
        }

        private static void TickRestoreWorldWrap()
        {
            if (!FlatWorldGoldenPathScenarios.TickWorldWrapRestoration(CreateScenarioContext()))
            {
                ThrowIfTimedOut("玩家右边界环绕后的原位置恢复或目标 Chunk Ready 超时。");
                return;
            }

            BeginWorldModelExcursion();
        }

        private static void BeginWorldModelExcursion()
        {
            FlatWorldGoldenPathScenarios.BeginWorldModelExcursion(_travelDirection, _chunkSize);
            _runtimePhase = RuntimePhase.MoveWorldModelAway;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.moveTimeoutSeconds;
        }

        private static void TickMoveWorldModelAway()
        {
            MoveWorldModelPlayer(FlatWorldGoldenPathScenarios.WorldModelAwayPosition,
                RuntimePhase.WaitWorldModelAway);
        }

        private static void TickWaitWorldModelAway()
        {
            if (!FlatWorldGoldenPathScenarios.TickWorldModelAway())
            {
                ThrowIfTimedOut("起始无头区块未在 destroyDistance 内进入休眠并解绑表现。");
                return;
            }
            _runtimePhase = RuntimePhase.MoveWorldModelReturn;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.moveTimeoutSeconds;
        }

        private static void TickMoveWorldModelReturn()
        {
            MoveWorldModelPlayer(FlatWorldGoldenPathScenarios.WorldModelStartPosition,
                RuntimePhase.WaitWorldModelReturn);
        }

        private static void MoveWorldModelPlayer(Vector2 target, RuntimePhase completedPhase)
        {
            if (_mover == null || _mover.rb == null)
                throw new InvalidOperationException("无头模型往返时玩家 Mover 已销毁。");
            float distance = WorldTopologyRuntime.Distance(_mover.rb.position, target);
            if (distance > 0.2f)
            {
                ThrowIfTimedOut(
                    $"无头模型往返未在限定时间内到达 {target}；" +
                    $"当前位置={_mover.rb.position}，剩余={distance:0.###}，" +
                    $"速度={_mover.rb.velocity}，MoverSpeed={_mover.Speed.Value:0.###}，" +
                    $"Base={_mover.Speed.BaseValue:0.###}，" +
                    $"Multiplier={_mover.Speed.MultiplicativeModifier:0.###}，" +
                    $"BodyType={_mover.rb.bodyType}，Simulated={_mover.rb.simulated}，" +
                    $"TimeScale={Time.timeScale:0.###}。");
                _mover.Speed.BaseValue = Mathf.Clamp(distance * 8f, 2f,
                    _executor.Configuration.player.maximumMoveSpeed);
                SetMovementDrive(target);
                _mover.Move(target, Mathf.Max(Time.deltaTime, 0.02f));
                return;
            }
            StopMovementDrive();
            _expectedChunk = ChunkMgr.NormalizeChunkPosition(
                ChunkMgr.Instance.ResolveRuntimeChunkOrigin(_mover.rb.position));
            _runtimePhase = completedPhase;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
        }

        private static void TickWaitWorldModelReturn()
        {
            if (!IsChunkReadyAt(ChunkMgr.Instance, _expectedChunk) ||
                !FlatWorldGoldenPathScenarios.TickWorldModelReturn())
            {
                ThrowIfTimedOut("返回起点后区块模型或表现未完成无重复重绑。");
                return;
            }
            _visitedChunks.Add(_expectedChunk);
            BeginNextWaypoint();
        }

        private static void TickMoveToWaypoint()
        {
            if (_mover == null || _mover.rb == null)
                throw new InvalidOperationException("移动过程中 Mover 或 Rigidbody2D 被销毁。");

            Vector2 target = _waypoints[_waypointIndex];
            FlatWorldGoldenPathScenarios.OnTraversalTick(CreateScenarioContext());
            float distance = WorldTopologyRuntime.Distance(_mover.rb.position, target);
            if (distance > 0.2f)
            {
                ThrowIfTimedOut($"Mover.Move 未在限定时间内到达 {target}。");
                _mover.Speed.BaseValue = Mathf.Clamp(
                    distance * 8f,
                    2f,
                    _executor.Configuration.player.maximumMoveSpeed);
                SetMovementDrive(target);
                _mover.Move(target, Mathf.Max(Time.deltaTime, 0.02f));
                return;
            }

            StopMovementDrive();
            _expectedChunk = ChunkMgr.NormalizeChunkPosition(
                ChunkMgr.Instance.ResolveRuntimeChunkOrigin(_mover.rb.position));
            _runtimePhase = RuntimePhase.WaitForChunk;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
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
            RecordObservedActiveChunks(ChunkMgr.Instance);
            FlatWorldGoldenPathScenarios.OnChunkReady(CreateScenarioContext());
            bool captureMiddle = _waypointIndex ==
                                 _executor.Configuration.player.middleScreenshotWaypointIndex;
            bool captureFinal = _waypointIndex == _waypoints.Length - 1;
            _waypointIndex++;
            if (captureFinal)
            {
                BeginScreenshotCapture("final", ScreenshotContinuation.CompleteTraversal);
                return;
            }

            if (captureMiddle)
            {
                BeginScreenshotCapture("middle", ScreenshotContinuation.ContinueTraversal);
                return;
            }

            BeginNextWaypoint();
        }

        private static void BeginNextWaypoint()
        {
            _runtimePhase = RuntimePhase.MoveToWaypoint;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.moveTimeoutSeconds;
        }

        private static void BeginScreenshotCapture(
            string name,
            ScreenshotContinuation continuation)
        {
            StopMovementDrive();

            _executor.ConfigureScreenshotView();
            _screenshotViewApplied = true;

            string directory = GetScreenshotDirectory(_activeRequest.id);
            Directory.CreateDirectory(directory);
            _pendingScreenshotPath = Path.GetFullPath(Path.Combine(directory, name + ".png"));
            DeleteIfExists(_pendingScreenshotPath);
            _screenshotContinuation = continuation;
            _screenshotRequested = false;
            _screenshotCaptureAfterFrame = Time.frameCount +
                                           _executor.Configuration.execution.screenshotSettleFrames;
            _screenshotCaptureNotBefore =
                EditorApplication.timeSinceStartup +
                _executor.Configuration.execution.screenshotSettleSeconds;
            _runtimePhase = RuntimePhase.WaitForScreenshot;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.screenshotTimeoutSeconds;
        }

        private static void TickWaitForScreenshot()
        {
            StopMovementDrive();

            if (!_screenshotRequested)
            {
                Camera gameCamera = Camera.main;
                bool canCapture = Time.frameCount >= _screenshotCaptureAfterFrame &&
                                  EditorApplication.timeSinceStartup >= _screenshotCaptureNotBefore &&
                                  Screen.width > 0 &&
                                  Screen.height > 0 &&
                                  gameCamera != null &&
                                  gameCamera.isActiveAndEnabled &&
                                  IsChunkWindowReady(ChunkMgr.Instance);
                if (!canCapture)
                {
                    ThrowIfTimedOut("Game View 相机未在截图超时前就绪。");
                    return;
                }

                ScreenCapture.CaptureScreenshot(_pendingScreenshotPath);
                _screenshotRequested = true;
                Debug.Log($"[FlatWorldGoldenPath] 已请求 Game View 截图：{_pendingScreenshotPath}");
                return;
            }

            if (!IsValidPng(_pendingScreenshotPath))
            {
                ThrowIfTimedOut($"Game View 截图未在限定时间内完整写入：{_pendingScreenshotPath}");
                return;
            }

            _screenshotPaths.Add(_pendingScreenshotPath);
            Debug.Log($"[FlatWorldGoldenPath] Game View 截图已写入：{_pendingScreenshotPath}");
            ScreenshotContinuation continuation = _screenshotContinuation;
            _pendingScreenshotPath = null;
            _screenshotRequested = false;
            _screenshotContinuation = ScreenshotContinuation.None;
            RestoreTraversalViewAfterScreenshot();

            switch (continuation)
            {
                case ScreenshotContinuation.BeginTraversal:
                    if (_executor.Configuration.scenarios.worldWrap)
                    {
                        FlatWorldGoldenPathScenarios.BeginWorldWrapRestoration();
                        _runtimePhase = RuntimePhase.RestoreWorldWrap;
                        _phaseDeadline = EditorApplication.timeSinceStartup +
                                         _executor.Configuration.execution.worldEntryTimeoutSeconds;
                    }
                    else
                    {
                        BeginWorldModelExcursion();
                    }
                    break;
                case ScreenshotContinuation.ContinueTraversal:
                    BeginNextWaypoint();
                    break;
                case ScreenshotContinuation.CompleteTraversal:
                    CompleteTraversal();
                    break;
                default:
                    throw new InvalidOperationException("截图完成后缺少有效的黄金路径继续阶段。");
            }
        }

        private static void CompleteTraversal()
        {
            int minimumVisited = _executor.Configuration.execution.minimumVisitedChunks;
            int minimumObserved = _executor.Configuration.execution.minimumObservedChunks;
            float positionTolerance = _executor.Configuration.execution.positionTolerance;
            if (_visitedChunks.Count < minimumVisited)
            {
                throw new InvalidOperationException(
                    $"直线移动仅覆盖 {_visitedChunks.Count} 个玩家 Chunk，" +
                    $"少于要求的 {minimumVisited} 个。");
            }

            if (_observedChunks.Count < minimumObserved)
            {
                throw new InvalidOperationException(
                    $"直线流送累计仅观察到 {_observedChunks.Count} 个活动 Chunk，" +
                    $"少于要求的 {minimumObserved} 个。");
            }

            Vector2 finalPosition = _mover.rb.position;
            bool wrapped = WorldTopologyRuntime.TryGetActiveBounds(out _);
            Vector2 displacement = finalPosition - _startPosition;
            float forwardDistance = Vector2.Dot(displacement, _travelDirection);
            float lateralError = Mathf.Abs(
                displacement.x * _travelDirection.y - displacement.y * _travelDirection.x);
            if (!wrapped && Mathf.Abs(forwardDistance - _plannedTravelDistance) > positionTolerance)
            {
                throw new InvalidOperationException(
                    $"直线移动距离异常：计划 {_plannedTravelDistance:0.###}，" +
                    $"实际投影 {forwardDistance:0.###}。");
            }
            if (!wrapped && lateralError > positionTolerance)
            {
                throw new InvalidOperationException(
                    $"直线移动横向偏差 {lateralError:0.###} 超过容差 {positionTolerance:0.###}。");
            }

            Vector2Int startChunk = ChunkMgr.NormalizeChunkPosition(
                ChunkMgr.Instance.ResolveRuntimeChunkOrigin(_startPosition));
            Vector2Int finalChunk = ChunkMgr.NormalizeChunkPosition(
                ChunkMgr.Instance.ResolveRuntimeChunkOrigin(finalPosition));
            if (finalChunk == startChunk && _visitedChunks.Count < 2)
                throw new InvalidOperationException("直线长距离移动结束后玩家仍位于初始 Chunk。");

            if (_screenshotPaths.Count != 3 || _screenshotPaths.Any(path => !IsValidPng(path)))
                throw new InvalidOperationException("起点、中点、终点三张 Game View PNG 未全部有效写入。");

            FlatWorldGoldenPathScenarios.BeforeWorldExit(CreateScenarioContext());
            CaptureWorldReentryExpectation();
            _completionMessage =
                $"创建世界、无头区块休眠重绑、随机直线移动、保存退出、重进及 Chunk 流送验证全部通过；" +
                $"玩家 Chunk={_visitedChunks.Count}，累计活动 Chunk={_observedChunks.Count}。";
            _worldExitCompleted = false;
            _runtimePhase = RuntimePhase.WaitForWorldExit;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
            _gameManager.StartCoroutine(_gameManager.BackToHelloScene_Coroutine(
                _player, () => _worldExitCompleted = true));
        }

        private static void TickWaitForWorldExit()
        {
            if (!_worldExitCompleted ||
                !string.Equals(SceneManager.GetActiveScene().name, "GameStartScene",
                    StringComparison.Ordinal))
            {
                ThrowIfTimedOut("完整保存退出流程未在限定时间内返回 GameStartScene。");
                return;
            }

            VerifyWorldExitCleanup();
            BeginWorldReentry();
        }

        /// <summary>记录退出前可从磁盘恢复的存档、玩家和动态世界键。</summary>
        private static void CaptureWorldReentryExpectation()
        {
            if (_saveDataManager?.SaveData == null || _player?.Data == null)
                throw new InvalidOperationException("退出重进回归缺少当前存档或玩家数据。");

            _reentrySaveName = _saveDataManager.SaveData.saveName;
            _reentryPlayerName = _player.Data.Name_User;
            _reentryWorldKey = _player.Data.CurrentSceneName;
            if (string.IsNullOrWhiteSpace(_reentrySaveName) ||
                string.IsNullOrWhiteSpace(_reentryPlayerName) ||
                string.IsNullOrWhiteSpace(_reentryWorldKey))
            {
                throw new InvalidOperationException("退出重进回归缺少有效的存档名、玩家名或世界键。");
            }
        }

        /// <summary>验证退出已移除动态世界与玩家运行时注册，避免下一次创建同名场景失败。</summary>
        private static void VerifyWorldExitCleanup()
        {
            Scene oldWorldScene = SceneManager.GetSceneByName(_reentryWorldKey);
            if (oldWorldScene.IsValid() && oldWorldScene.isLoaded)
            {
                throw new InvalidOperationException(
                    $"退出后动态世界场景仍处于已加载状态：{_reentryWorldKey}。");
            }

            ItemMgr itemManager = ItemMgr.Instance;
            if (itemManager == null)
                throw new InvalidOperationException("退出后 ItemMgr 不可用，无法验证运行时注册表。");

            if (itemManager.Player_DIC.ContainsKey(_reentryPlayerName))
            {
                throw new InvalidOperationException("退出后旧玩家仍保留在 Player_DIC 中。");
            }

            foreach (Item item in itemManager.WorldRunTimeItems.Values)
            {
                if (item == null)
                    throw new InvalidOperationException("退出后 ItemMgr 运行时注册表仍包含已销毁对象。");
            }
        }

        /// <summary>用刚写入的隔离存档重新进入同一世界，覆盖退出→重进回归。</summary>
        private static void BeginWorldReentry()
        {
            string savePath = Path.Combine(_saveDataManager.UserSavePath, _reentrySaveName + ".bytes");
            if (!File.Exists(savePath))
                throw new InvalidOperationException($"退出重进回归找不到存档文件：{savePath}");

            _saveDataManager.LoadSaveByDisk(savePath);
            if (_saveDataManager.SaveData == null ||
                !_saveDataManager.SaveData.PlayerData_Dict.ContainsKey(_reentryPlayerName))
            {
                throw new InvalidOperationException("退出重进回归未能从磁盘恢复原玩家存档。");
            }

            _terminalWorldEntryState = null;
            _terminalWorldEntryStatus = null;
            _gameManager.ContinueGame(_reentryPlayerName);
            _runtimePhase = RuntimePhase.WaitForWorldReentry;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
        }

        /// <summary>等待重新进入完成并验证同名动态场景可被重新创建。</summary>
        private static void TickWaitForWorldReentry()
        {
            if (_terminalWorldEntryState == WorldEntryProgressState.Failed)
            {
                throw new InvalidOperationException(
                    $"退出后重进世界失败：{_terminalWorldEntryStatus ?? "未知错误"}");
            }

            ItemMgr itemManager = ItemMgr.Instance;
            bool ready = _gameManager != null &&
                         _gameManager.IsInGameWorld &&
                         !_gameManager.IsWorldEntryInProgress &&
                         itemManager != null &&
                         itemManager.User_Player != null &&
                         ChunkMgr.Instance != null;
            if (!ready || !IsChunkWindowReady(ChunkMgr.Instance) ||
                !FlatWorldGoldenPathScenarios.TickWorldModelAiReentry())
            {
                ThrowIfTimedOut("退出后重进世界、AI 实体或初始 Chunk 窗口未在限定时间内完成。");
                return;
            }

            Player reenteredPlayer = itemManager.User_Player;
            if (reenteredPlayer.Data == null ||
                !string.Equals(reenteredPlayer.Data.Name_User, _reentryPlayerName, StringComparison.Ordinal) ||
                !string.Equals(SceneManager.GetActiveScene().name, _reentryWorldKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("退出后重进没有恢复到原玩家或原动态世界场景。");
            }

            Scene reenteredWorldScene = SceneManager.GetSceneByName(_reentryWorldKey);
            if (!reenteredWorldScene.IsValid() || !reenteredWorldScene.isLoaded)
                throw new InvalidOperationException("退出后重进未创建有效的动态世界场景。");

            _player = reenteredPlayer;
            _mover = reenteredPlayer.itemMods.GetMod_ByID<Mover>(ModText.Mover);
            _worldExitCompleted = false;
            _runtimePhase = RuntimePhase.WaitForFinalWorldExit;
            _phaseDeadline = EditorApplication.timeSinceStartup +
                             _executor.Configuration.execution.worldEntryTimeoutSeconds;
            _gameManager.StartCoroutine(_gameManager.BackToHelloScene_Coroutine(
                reenteredPlayer, () => _worldExitCompleted = true));
        }

        /// <summary>第二次退出只负责还原测试环境，确保运行结束时仍回到主菜单。</summary>
        private static void TickWaitForFinalWorldExit()
        {
            if (!_worldExitCompleted ||
                !string.Equals(SceneManager.GetActiveScene().name, "GameStartScene",
                    StringComparison.Ordinal))
            {
                ThrowIfTimedOut("退出重进回归后的最终退出未在限定时间内返回 GameStartScene。");
                return;
            }

            FinishRuntime(true, _completionMessage);
        }

        private static Vector2 SelectDeterministicDirection(int seed)
        {
            Vector2[] directions =
            {
                Vector2.right,
                new Vector2(1f, 1f).normalized,
                Vector2.up,
                new Vector2(-1f, 1f).normalized,
                Vector2.left,
                new Vector2(-1f, -1f).normalized,
                Vector2.down,
                new Vector2(1f, -1f).normalized
            };

            uint value = unchecked((uint)seed);
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return directions[value % (uint)directions.Length];
        }

        private static void PrepareDaylightForVisualCapture()
        {
            DayTimeSystem dayTimeSystem = DayTimeSystem.Instance;
            string sceneName = SceneManager.GetActiveScene().name;
            if (dayTimeSystem == null ||
                !dayTimeSystem.TryGetResolvedTimeData(
                    sceneName,
                    out _,
                    out TimeData timeData) ||
                timeData == null)
            {
                throw new InvalidOperationException(
                    $"无法为 Game View 截图准备确定性日间光照：{sceneName}");
            }

            dayTimeSystem.JumpToTime(sceneName, Mathf.Max(1f, timeData.DayLength) * 0.5f);
            dayTimeSystem.SetCurrentSceneLighting(sceneName);
            Debug.Log("[FlatWorldGoldenPath] 截图光照已通过公开时间 API 固定到正午。");
        }

        private static void RecordObservedActiveChunks(ChunkMgr chunkManager)
        {
            if (chunkManager == null || _observedChunks == null)
                return;

            foreach (KeyValuePair<FlatWorld.WorldModel.WorldAddress,
                         FlatWorld.WorldModel.ChunkRuntime> pair in chunkManager.Chunks)
            {
                if (pair.Value.SimulationStatus ==
                    FlatWorld.WorldModel.ChunkSimulationStatus.Active)
                    _observedChunks.Add(new Vector2Int(pair.Key.ChunkOrigin.X,
                        pair.Key.ChunkOrigin.Y));
            }
        }

        private static bool IsValidPng(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                if (stream.Length < 8)
                    return false;

                byte[] signature = new byte[8];
                if (stream.Read(signature, 0, signature.Length) != signature.Length)
                    return false;

                return signature[0] == 0x89 &&
                       signature[1] == 0x50 &&
                       signature[2] == 0x4E &&
                       signature[3] == 0x47 &&
                       signature[4] == 0x0D &&
                       signature[5] == 0x0A &&
                       signature[6] == 0x1A &&
                       signature[7] == 0x0A;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void ThrowIfTimedOut(string message)
        {
            if (EditorApplication.timeSinceStartup >= _phaseDeadline)
                throw new TimeoutException(message);
        }

        private static bool IsChunkWindowReady(ChunkMgr chunkManager)
        {
            if (chunkManager == null || chunkManager.HasPendingChunkDataLoads ||
                chunkManager.Chunks.Count == 0)
            {
                return false;
            }

            int activeCount = 0;
            foreach (KeyValuePair<FlatWorld.WorldModel.WorldAddress,
                         FlatWorld.WorldModel.ChunkRuntime> entry in chunkManager.Chunks)
            {
                FlatWorld.WorldModel.ChunkRuntime runtime = entry.Value;
                if (runtime.SimulationStatus != FlatWorld.WorldModel.ChunkSimulationStatus.Active)
                    continue;
                activeCount++;
                if (runtime.DataStatus != FlatWorld.WorldModel.ChunkDataStatus.Ready ||
                    runtime.PresentationStatus != FlatWorld.WorldModel.ChunkPresentationStatus.Bound ||
                    !runtime.HasNavigationLease ||
                    !chunkManager.TryGetRuntimeChunkView(entry.Key, out ChunkView view))
                    return false;
                UnityEngine.Tilemaps.Tilemap ground = view.transform.Find("Ground")?.
                    GetComponent<UnityEngine.Tilemaps.Tilemap>();
                if (ground == null || ground.GetUsedTilesCount() == 0)
                    return false;
            }
            return activeCount > 0;
        }

        private static bool IsChunkReadyAt(ChunkMgr chunkManager, Vector2Int chunkPosition)
        {
            if (!IsChunkWindowReady(chunkManager))
                return false;
            FlatWorld.WorldModel.WorldAddress address =
                chunkManager.ResolveWorldAddress(chunkPosition);
            return chunkManager.TryGetChunkRuntime(address, out var chunk) &&
                   chunk.DataStatus == FlatWorld.WorldModel.ChunkDataStatus.Ready &&
                   chunk.SimulationStatus == FlatWorld.WorldModel.ChunkSimulationStatus.Active &&
                   chunkManager.TryGetRuntimeChunkView(address, out _);
        }

        private static void ValidateChunkDictionaries(ChunkMgr chunkManager)
        {
            if (chunkManager == null || chunkManager.HasPendingChunkDataLoads ||
                chunkManager.Chunks.Count == 0)
            {
                throw new InvalidOperationException("Chunk 模型窗口尚未稳定。");
            }

            int activeCount = 0;
            foreach (KeyValuePair<FlatWorld.WorldModel.WorldAddress,
                         FlatWorld.WorldModel.ChunkRuntime> entry in chunkManager.Chunks)
            {
                if (entry.Value.SimulationStatus !=
                    FlatWorld.WorldModel.ChunkSimulationStatus.Active)
                    continue;
                activeCount++;
                if (entry.Value.DataStatus != FlatWorld.WorldModel.ChunkDataStatus.Ready ||
                    entry.Value.PresentationStatus !=
                    FlatWorld.WorldModel.ChunkPresentationStatus.Bound)
                {
                    throw new InvalidOperationException(
                        $"激活 Chunk 模型尚未完整就绪：{entry.Key}, " +
                        $"data={entry.Value.DataStatus}, view={entry.Value.PresentationStatus}");
                }
            }
            if (activeCount == 0)
                throw new InvalidOperationException("没有活动 Chunk 模型。");
        }

        private static void FinishRuntime(bool passed, string message, string stackTrace = "")
        {
            if (_runtimePhase == RuntimePhase.Finishing)
                return;
            _runtimePhase = RuntimePhase.Finishing;

            try
            {
                RestoreTraversalViewAfterScreenshot();
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
            StopMovementDriver();
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

        #region 真实帧移动驱动

        /// <summary>在游戏 Update 完成后持续重放正式移动入口，避免玩家输入模块覆盖自动化速度。</summary>
        private static IEnumerator DriveMovementAfterUpdate()
        {
            while (_activeRequest != null && EditorApplication.isPlaying)
            {
                yield return null;
                if (!_movementDriveActive || _mover == null || _mover.rb == null)
                    continue;

                _mover.Move(_movementDriveTarget, Mathf.Max(Time.deltaTime, 0.02f));
            }

            _movementDriverCoroutine = null;
        }

        /// <summary>设置当前自动化移动目标，并确保真实帧驱动已运行。</summary>
        private static void SetMovementDrive(Vector2 target)
        {
            _movementDriveTarget = target;
            _movementDriveActive = true;
            if (_movementDriverCoroutine == null && _gameManager != null)
                _movementDriverCoroutine = _gameManager.StartCoroutine(DriveMovementAfterUpdate());
        }

        /// <summary>停止目标驱动并通过正式移动入口把刚体速度归零。</summary>
        private static void StopMovementDrive()
        {
            _movementDriveActive = false;
            if (_mover != null && _mover.rb != null)
                _mover.Move(_mover.rb.position, Mathf.Max(Time.deltaTime, 0.02f));
        }

        /// <summary>结束测试时彻底注销移动协程。</summary>
        private static void StopMovementDriver()
        {
            StopMovementDrive();
            if (_movementDriverCoroutine != null && _gameManager != null)
                _gameManager.StopCoroutine(_movementDriverCoroutine);
            _movementDriverCoroutine = null;
        }

        #endregion

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
                _expectedChunk,
                _executor?.Configuration ??
                _activeRequest?.configuration ??
                FlatWorldGoldenPathConfiguration.CreateDefault());
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
                testNames = new[] { "FlatWorld.DefaultStartScene.CreateWorldMoveStraight" },
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
                            fullName = "FlatWorld.DefaultStartScene.CreateWorldMoveStraight",
                            resultState = "Failed",
                            message = message ?? "黄金路径失败。",
                            stackTrace = stackTrace ?? string.Empty,
                            durationSeconds = Math.Max(
                                0d,
                                EditorApplication.timeSinceStartup - _runtimeStartedAt)
                        }
                    },
                message = passed ? message ?? string.Empty : string.Empty,
                screenshotPaths = _screenshotPaths?.ToArray() ?? Array.Empty<string>(),
                configuration = _activeRequest.configuration ??
                                FlatWorldGoldenPathConfiguration.CreateDefault()
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
            GoldenPathRequest runningRequest = ReadRequest(runningId, running);
            bool activeSessionMatches = string.Equals(
                SessionState.GetString(ActiveIdSessionKey, string.Empty),
                runningId,
                StringComparison.Ordinal);
            bool currentProcessOwnsRequest = runningRequest.ownerProcessId ==
                                             System.Diagnostics.Process.GetCurrentProcess().Id;
            if (activeSessionMatches || currentProcessOwnsRequest)
            {
                SetActiveRequest(runningRequest, running);
                if (!activeSessionMatches)
                {
                    // EditorSettings 或 PlayMode 引发的 Domain Reload 可能早于 SessionState 恢复。
                    SessionState.SetString(ActiveIdSessionKey, runningId);
                    SessionState.SetString(
                        StageSessionKey,
                        EditorApplication.isPlaying ? "runtime" : "entering");
                }
                return;
            }

            SetActiveRequest(runningRequest, running);
            WriteFailureBeforePlay("Unity 在上一次黄金路径执行中退出，已清理中断请求。");
        }

        private static GoldenPathRequest ReadRequest(string id, string path)
        {
            GoldenPathRequest request = JsonUtility.FromJson<GoldenPathRequest>(File.ReadAllText(path));
            if (request == null || !string.Equals(request.id, id, StringComparison.Ordinal))
                throw new InvalidDataException("黄金路径请求 JSON 无效或 ID 不匹配。");
            request.configuration ??= FlatWorldGoldenPathConfiguration.CreateDefault();
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

        private static string GetScreenshotDirectory(string id)
        {
            return Path.GetFullPath(Path.Combine(CommandDirectory, "GoldenPathCaptures", id));
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
            _executor?.Dispose();
            _executor = null;
            _runtimePhase = RuntimePhase.None;
            _runtimeError = null;
            _gameManager = null;
            _saveDataManager = null;
            _originalSavePath = null;
            _player = null;
            _mover = null;
            _movementDriverCoroutine = null;
            _movementDriveActive = false;
            _movementDriveTarget = default;
            _originalMoveSpeed = 0f;
            _startPosition = default;
            _chunkSize = default;
            _travelDirection = default;
            _plannedTravelDistance = 0f;
            _waypoints = null;
            _waypointIndex = 0;
            _expectedChunk = default;
            _visitedChunks = null;
            _observedChunks = null;
            _screenshotPaths = null;
            _pendingScreenshotPath = null;
            _screenshotCaptureAfterFrame = 0;
            _screenshotCaptureNotBefore = 0d;
            _screenshotRequested = false;
            _screenshotViewApplied = false;
            _screenshotContinuation = ScreenshotContinuation.None;
            _terminalWorldEntryState = null;
            _terminalWorldEntryStatus = null;
            _worldEntryHandler = null;
            _worldSetupApplied = false;
            _worldExitCompleted = false;
            _completionMessage = null;
            _reentrySaveName = null;
            _reentryPlayerName = null;
            _reentryWorldKey = null;
        }

        private static void RestoreTraversalViewAfterScreenshot()
        {
            if (!_screenshotViewApplied)
                return;
            _screenshotViewApplied = false;
            _executor?.RestoreTraversalView();
        }

        private enum RuntimePhase
        {
            None,
            WaitForStartup,
            WaitForWorld,
            WaitForWorldReadyScenarios,
            VerifyWorldWrap,
            RestoreWorldWrap,
            MoveWorldModelAway,
            WaitWorldModelAway,
            MoveWorldModelReturn,
            WaitWorldModelReturn,
            MoveToWaypoint,
            WaitForChunk,
            WaitForScreenshot,
            WaitForWorldExit,
            WaitForWorldReentry,
            WaitForFinalWorldExit,
            Finishing
        }

        private enum ScreenshotContinuation
        {
            None,
            BeginTraversal,
            ContinueTraversal,
            CompleteTraversal
        }

        [Serializable]
        private sealed class GoldenPathRequest
        {
            public string id;
            public string createdUtc;
            public int ownerProcessId;
            public FlatWorldGoldenPathConfiguration configuration;
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
            public string[] screenshotPaths;
            public FlatWorldGoldenPathConfiguration configuration;
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

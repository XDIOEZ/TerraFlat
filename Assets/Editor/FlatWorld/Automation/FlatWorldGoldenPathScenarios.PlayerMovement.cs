using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Automation
{
    /// <summary>通过真实玩家 API 验证出生、交互重试、奔跑输入与管理员能力。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        #region 新玩家安全出生点

        private static bool _initialSpawnLandScenarioCompleted;

        private static void ResetInitialSpawnLandScenario()
        {
            _initialSpawnLandScenarioCompleted = false;
        }

        /// <summary>确认新玩家首次进入世界后，运行时位置与存档位置都落在同一块可走陆地。</summary>
        private static void VerifyInitialPlayerSpawnLand(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player?.Data == null)
                throw new InvalidOperationException("出生点场景找不到真实玩家数据。");

            ChunkMgr chunkManager = ChunkMgr.Instance;
            Vector2 runtimePosition = context.Player.transform.position;
            if (chunkManager == null ||
                !chunkManager.TryGetRuntimeTerrainTile(
                    runtimePosition, out RuntimeTerrainTileSample sample))
            {
                throw new InvalidOperationException(
                    $"出生点场景无法读取玩家脚下的权威地形：position={runtimePosition}。");
            }

            bool isWater = (sample.Cell.Flags &
                            FlatWorld.WorldModel.TerrainCellFlags.Water) != 0;
            bool isWalkable = sample.Terrain.IsWalkable(
                sample.LocalCell.x, sample.LocalCell.y);
            if (isWater || !isWalkable)
            {
                throw new InvalidOperationException(
                    $"新玩家出生格不是安全陆地：cell={sample.WorldCell}, " +
                    $"water={isWater}, walkable={isWalkable}。");
            }

            Vector2 savedPosition = context.Player.Data.transform.position;
            if (WorldTopologyRuntime.Distance(runtimePosition, savedPosition) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"新玩家运行时位置与存档位置不一致：runtime={runtimePosition}, " +
                    $"saved={savedPosition}。");
            }

            _initialSpawnLandScenarioCompleted = true;
            Debug.Log($"[GoldenPath][Player] 新玩家安全陆地出生验证通过：{sample.WorldCell}。");
        }

        private static void AssertInitialSpawnLandScenarioCompleted()
        {
            if (!_initialSpawnLandScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未验证新玩家安全陆地出生。");
        }

        private static void CleanupInitialSpawnLandScenario()
        {
            _initialSpawnLandScenarioCompleted = false;
        }

        #endregion

        #region 同目标交互重试

        private static Mod_InteractSender _interactionRetrySender;
        private static GameObject _interactionRetryProbeObject;
        private static GoldenPathInteractionRetryProbe _interactionRetryProbe;
        private static float _interactionRetryOriginalDistance;
        private static bool _interactionRetryDistanceCaptured;
        private static bool _interactionRetryScenarioCompleted;

        private static void ResetPlayerInteractionRetryScenario()
        {
            _interactionRetrySender = null;
            _interactionRetryProbeObject = null;
            _interactionRetryProbe = null;
            _interactionRetryOriginalDistance = 0f;
            _interactionRetryDistanceCaptured = false;
            _interactionRetryScenarioCompleted = false;
        }

        /// <summary>验证同一目标第一次临时拒绝后，下一次交互请求仍会再次触发。</summary>
        private static void RunPlayerInteractionRetryScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null)
                throw new InvalidOperationException("交互重试场景找不到真实玩家。");

            _interactionRetrySender =
                context.Player.GetComponentInChildren<Mod_InteractSender>(true);
            if (_interactionRetrySender?.interactCollider == null)
                throw new InvalidOperationException("真实玩家缺少可用的交互发送器或探测碰撞体。");

            _interactionRetryOriginalDistance = _interactionRetrySender.maxInteractDistance;
            _interactionRetryDistanceCaptured = true;
            _interactionRetrySender.CancelCurrentInteraction();
            _interactionRetrySender.maxInteractDistance = 0.25f;

            _interactionRetryProbeObject = new GameObject("GoldenPath Interaction Retry Probe");
            _interactionRetryProbeObject.transform.position = context.Player.transform.position;
            CircleCollider2D collider = _interactionRetryProbeObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.05f;
            _interactionRetryProbe =
                _interactionRetryProbeObject.AddComponent<GoldenPathInteractionRetryProbe>();
            Physics2D.SyncTransforms();

            try
            {
                if (!_interactionRetrySender.TryInteractAtCurrentPosition() ||
                    _interactionRetryProbe.StartCount != 1)
                {
                    throw new InvalidOperationException("第一次主动交互没有触发当前目标。");
                }

                // 不离开范围、不清除目标，直接模拟玩家再次按 E。
                if (!_interactionRetrySender.TryInteractAtCurrentPosition() ||
                    _interactionRetryProbe.StartCount != 2)
                {
                    throw new InvalidOperationException(
                        "同一交互目标被缓存后没有响应第二次请求。");
                }

                _interactionRetryScenarioCompleted = true;
                Debug.Log("[GoldenPath][Player] 同一目标连续两次交互重试验证通过。");
            }
            finally
            {
                CleanupPlayerInteractionRetryScenario();
            }
        }

        private static void AssertPlayerInteractionRetryScenarioCompleted()
        {
            if (!_interactionRetryScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成同目标交互重试验证。");
        }

        private static void CleanupPlayerInteractionRetryScenario()
        {
            if (_interactionRetrySender != null)
            {
                _interactionRetrySender.CancelCurrentInteraction();
                if (_interactionRetryDistanceCaptured)
                {
                    _interactionRetrySender.maxInteractDistance =
                        _interactionRetryOriginalDistance;
                }
            }

            if (_interactionRetryProbeObject != null)
            {
                _interactionRetryProbeObject.SetActive(false);
                UnityEngine.Object.Destroy(_interactionRetryProbeObject);
            }

            _interactionRetrySender = null;
            _interactionRetryProbeObject = null;
            _interactionRetryProbe = null;
            _interactionRetryDistanceCaptured = false;
        }

        #endregion

        #region 主世界出生点复活

        private static Player _respawnScenarioPlayer;
        private static Mod_PlayerDeathState _respawnScenarioDeathState;
        private static DamageReceiver _respawnScenarioDamageReceiver;
        private static Rigidbody2D _respawnScenarioBody;
        private static Vector3 _respawnScenarioOriginalPosition;
        private static float _respawnScenarioOriginalHp;
        private static string _respawnScenarioOriginalProfileName;
        private static string _respawnScenarioOriginalPlayerName;
        private static bool _respawnScenarioApplied;
        private static bool _respawnScenarioCompleted;

        private static void ResetPlayerRespawnScenario()
        {
            _respawnScenarioPlayer = null;
            _respawnScenarioDeathState = null;
            _respawnScenarioDamageReceiver = null;
            _respawnScenarioBody = null;
            _respawnScenarioOriginalPosition = default;
            _respawnScenarioOriginalHp = 0f;
            _respawnScenarioOriginalProfileName = null;
            _respawnScenarioOriginalPlayerName = null;
            _respawnScenarioApplied = false;
            _respawnScenarioCompleted = false;
        }

        /// <summary>用真实死亡与重生 API 验证玩家会回到存档中的主世界初始出生点。</summary>
        private static void RunPlayerRespawnScenario(FlatWorldGoldenPathScenarioContext context)
        {
            _respawnScenarioPlayer = context.Player;
            if (_respawnScenarioPlayer?.Data == null || _respawnScenarioPlayer.itemMods == null)
                throw new InvalidOperationException("主世界复活场景找不到真实玩家数据。");

            _respawnScenarioDeathState = _respawnScenarioPlayer.itemMods
                .GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
            _respawnScenarioDamageReceiver = _respawnScenarioPlayer.itemMods
                .GetMod_ByID<DamageReceiver>(ModText.Hp);
            _respawnScenarioBody = _respawnScenarioPlayer.GetComponent<Rigidbody2D>();
            if (_respawnScenarioDeathState == null || _respawnScenarioDamageReceiver == null)
                throw new InvalidOperationException("主世界复活场景缺少死亡或生命模块。");

            if (!PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(
                    _respawnScenarioPlayer.Data,
                    out Vector3 mainWorldSpawn,
                    out string mainWorldKey))
            {
                throw new InvalidOperationException("玩家存档中没有主世界出生点。");
            }

            WorldAddress activeAddress = WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
            if (!activeAddress.IsSurface ||
                !string.Equals(activeAddress.PlanetId, mainWorldKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"黄金路径复活场景必须在主世界执行：active={activeAddress.WorldKey}, " +
                    $"spawnWorld={mainWorldKey}。");
            }

            WorldAddress mainWorldAddress = new WorldAddress(
                mainWorldKey,
                WorldAddress.SurfaceDimensionId);
            WorldAddress caveAddress = mainWorldAddress.WithDimension(
                WorldAddress.CaveDimensionId);
            if (!Mod_PlayerDeathState.RequiresWorldTransition(caveAddress, mainWorldAddress) ||
                Mod_PlayerDeathState.RequiresWorldTransition(mainWorldAddress, mainWorldAddress))
            {
                throw new InvalidOperationException(
                    "玩家死亡复活路由没有把矿洞识别为跨维度返回地表。");
            }

            _respawnScenarioOriginalPosition = _respawnScenarioPlayer.transform.position;
            _respawnScenarioOriginalHp = _respawnScenarioDamageReceiver.Hp;
            _respawnScenarioOriginalProfileName = _respawnScenarioPlayer.ProfileName;
            _respawnScenarioOriginalPlayerName = _respawnScenarioPlayer.Data.Name_User;
            _respawnScenarioApplied = true;

            Vector3 deathPosition = mainWorldSpawn + new Vector3(3f, 1f, 0f);
            _respawnScenarioPlayer.transform.position = deathPosition;
            _respawnScenarioPlayer.Data.transform.position = deathPosition;
            if (_respawnScenarioBody != null)
            {
                _respawnScenarioBody.position = deathPosition;
                _respawnScenarioBody.velocity = Vector2.zero;
            }

            _respawnScenarioDamageReceiver.Hp = _respawnScenarioDamageReceiver.MaxHp;
            _respawnScenarioDamageReceiver.Data.AttackersUIDs.Clear();
            _respawnScenarioDamageReceiver.ForceHurt(
                _respawnScenarioDamageReceiver.MaxHp * 2f + 1f);
            if (!_respawnScenarioDeathState.IsInDyingState)
                throw new InvalidOperationException("致死伤害后玩家没有进入濒死状态。");

            _respawnScenarioDeathState.RespawnFromDying();
            if (_respawnScenarioDeathState.IsInDyingState ||
                WorldTopologyRuntime.Distance(_respawnScenarioPlayer.transform.position, mainWorldSpawn) >
                0.001f ||
                Mathf.Abs(_respawnScenarioDamageReceiver.Hp -
                          _respawnScenarioDamageReceiver.MaxHp) > 0.001f ||
                !string.Equals(
                    _respawnScenarioPlayer.ProfileName,
                    _respawnScenarioOriginalProfileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _respawnScenarioPlayer.Data.Name_User,
                    _respawnScenarioOriginalPlayerName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"玩家没有回到主世界出生点：actual={_respawnScenarioPlayer.transform.position}, " +
                    $"expected={mainWorldSpawn}, hp={_respawnScenarioDamageReceiver.Hp}, " +
                    $"profile={_respawnScenarioPlayer.ProfileName}, " +
                    $"name={_respawnScenarioPlayer.Data.Name_User}。");
            }

            RestorePlayerRespawnScenario();
            _respawnScenarioCompleted = true;
            Debug.Log("[GoldenPath][Player] 玩家死亡后已回到存档中的主世界初始出生点。");
        }

        private static void AssertPlayerRespawnScenarioCompleted()
        {
            if (!_respawnScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成主世界出生点复活验证。");
        }

        private static void CleanupPlayerRespawnScenario()
        {
            RestorePlayerRespawnScenario();
            _respawnScenarioPlayer = null;
            _respawnScenarioDeathState = null;
            _respawnScenarioDamageReceiver = null;
            _respawnScenarioBody = null;
        }

        private static void RestorePlayerRespawnScenario()
        {
            if (!_respawnScenarioApplied)
                return;

            if (_respawnScenarioDeathState?.IsInDyingState == true)
                _respawnScenarioDeathState.RespawnFromDying();

            if (_respawnScenarioPlayer != null)
            {
                _respawnScenarioPlayer.transform.position = _respawnScenarioOriginalPosition;
                if (_respawnScenarioPlayer.Data != null)
                    _respawnScenarioPlayer.Data.transform.position = _respawnScenarioOriginalPosition;
            }

            if (_respawnScenarioBody != null)
            {
                _respawnScenarioBody.position = _respawnScenarioOriginalPosition;
                _respawnScenarioBody.velocity = Vector2.zero;
            }

            if (_respawnScenarioDamageReceiver != null)
            {
                _respawnScenarioDamageReceiver.Hp = Mathf.Clamp(
                    _respawnScenarioOriginalHp,
                    0f,
                    _respawnScenarioDamageReceiver.MaxHp);
                _respawnScenarioDamageReceiver.Data.AttackersUIDs.Clear();
                if (_respawnScenarioDamageReceiver.IsPanelVisible())
                    _respawnScenarioDamageReceiver.RefreshUI();
            }

            _respawnScenarioApplied = false;
        }

        #endregion

        #region 奔跑输入状态

        private const float RunInputSpeedTolerance = 0.001f;
        // 速度过渡验证的容差与固定模拟步长。
        private const float RunInputVelocityTolerance = 0.05f;
        private const float RunInputSimulationStep = 0.02f;

        private static Mover _runInputMover;
        private static float _runInputOriginalModifier;
        // 清理时恢复测试前的物理与动画移动状态。
        private static Vector2 _runInputOriginalVelocity;
        private static bool _runInputOriginalIsMoving;
        private static bool _runInputScenarioCompleted;

        private static void ResetPlayerRunInputScenario()
        {
            _runInputMover = null;
            _runInputOriginalModifier = 1f;
            _runInputOriginalVelocity = Vector2.zero;
            _runInputOriginalIsMoving = false;
            _runInputScenarioCompleted = false;
        }

        /// <summary>验证按住奔跑并在松开后恢复普通移动。</summary>
        private static void RunPlayerRunInputScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            _runInputMover = context.Mover;
            if (_runInputMover?.Speed == null || _runInputMover.rb == null)
                throw new InvalidOperationException("奔跑输入场景找不到真实玩家 Mover。");
            if (_runInputMover.IsRunning)
                throw new InvalidOperationException("奔跑输入场景开始前玩家已处于奔跑状态。");

            _runInputOriginalModifier = _runInputMover.Speed.MultiplicativeModifier;
            _runInputOriginalVelocity = _runInputMover.rb.velocity;
            _runInputOriginalIsMoving = _runInputMover.IsMoving;
            _runInputMover.rb.velocity = Vector2.zero;

            Vector2 movementTarget = _runInputMover.rb.position + Vector2.right;
            float walkSpeed = _runInputMover.Speed.Value;
            _runInputMover.Move(movementTarget, RunInputSimulationStep);
            AssertVelocityBetween(
                _runInputMover.rb.velocity.magnitude,
                0f,
                walkSpeed,
                "走路起步速度未经过平滑加速。");
            AdvanceMover(
                _runInputMover,
                movementTarget,
                RunInputSimulationStep,
                GetTransitionStepCount(_runInputMover.speedTransitionDuration));
            AssertVelocityApproximately(
                _runInputMover.rb.velocity.magnitude,
                walkSpeed,
                "走路速度未在过渡窗口内达到目标速度。");

            _runInputMover.HandleHoldRunInputPressed();

            float expectedRunModifier = _runInputOriginalModifier * _runInputMover.RunSpeedRate;
            if (!_runInputMover.IsRunning ||
                Mathf.Abs(_runInputMover.Speed.MultiplicativeModifier - expectedRunModifier) >
                RunInputSpeedTolerance)
            {
                throw new InvalidOperationException(
                    "按下奔跑输入后未进入奔跑状态或速度倍率异常。");
            }

            float runSpeed = _runInputMover.Speed.Value;
            _runInputMover.Move(movementTarget, RunInputSimulationStep);
            AssertVelocityBetween(
                _runInputMover.rb.velocity.magnitude,
                walkSpeed,
                runSpeed,
                "进入奔跑后速度没有平滑提升。");
            AdvanceMover(
                _runInputMover,
                movementTarget,
                RunInputSimulationStep,
                GetTransitionStepCount(_runInputMover.speedTransitionDuration));
            AssertVelocityApproximately(
                _runInputMover.rb.velocity.magnitude,
                runSpeed,
                "奔跑速度未在过渡窗口内达到目标速度。");

            _runInputMover.HandleHoldRunInputReleased();
            if (_runInputMover.IsRunning ||
                Mathf.Abs(_runInputMover.Speed.MultiplicativeModifier - _runInputOriginalModifier) >
                RunInputSpeedTolerance)
            {
                throw new InvalidOperationException("松开奔跑输入后未恢复普通移动。");
            }

            _runInputMover.Move(movementTarget, RunInputSimulationStep);
            AssertVelocityBetween(
                _runInputMover.rb.velocity.magnitude,
                walkSpeed,
                runSpeed,
                "退出奔跑后速度没有平滑降低。");
            AdvanceMover(
                _runInputMover,
                movementTarget,
                RunInputSimulationStep,
                GetTransitionStepCount(_runInputMover.speedTransitionDuration));
            AssertVelocityApproximately(
                _runInputMover.rb.velocity.magnitude,
                walkSpeed,
                "退出奔跑后未恢复走路目标速度。");

            _runInputMover.Move(_runInputMover.rb.position, RunInputSimulationStep);
            if (_runInputMover.rb.velocity.sqrMagnitude <= RunInputVelocityTolerance * RunInputVelocityTolerance)
            {
                throw new InvalidOperationException("松开方向输入后速度被立即清零，未保留短暂惯性。");
            }

            AdvanceMover(
                _runInputMover,
                _runInputMover.rb.position,
                RunInputSimulationStep,
                GetTransitionStepCount(_runInputMover.stopTransitionDuration));
            if (_runInputMover.rb.velocity.sqrMagnitude > RunInputVelocityTolerance * RunInputVelocityTolerance)
            {
                throw new InvalidOperationException("松开方向输入后的惯性未在短暂减速窗口内停止。");
            }

            _runInputScenarioCompleted = true;
            Debug.Log("[GoldenPath][Player] 走路、奔跑与松开惯性速度过渡验证通过。");
        }

        private static void AssertPlayerRunInputScenarioCompleted()
        {
            if (!_runInputScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成奔跑输入验证。");
        }

        private static void CleanupPlayerRunInputScenario()
        {
            if (_runInputMover != null)
            {
                if (_runInputMover.IsRunning)
                    _runInputMover.SetRunState(false);
                if (_runInputMover.rb != null)
                    _runInputMover.rb.velocity = _runInputOriginalVelocity;
                _runInputMover.IsMoving = _runInputOriginalIsMoving;
            }

            _runInputMover = null;
        }

        /// <summary>用公开 Mover API 推进固定次数，不依赖真实设备输入。</summary>
        private static void AdvanceMover(Mover mover, Vector2 target, float deltaTime, int steps)
        {
            for (int index = 0; index < steps; index++)
                mover.Move(target, deltaTime);
        }

        /// <summary>按固定模拟步长换算足以完成速度过渡的次数。</summary>
        private static int GetTransitionStepCount(float duration)
        {
            return Mathf.CeilToInt(Mathf.Max(0.01f, duration) / RunInputSimulationStep) + 2;
        }

        /// <summary>断言当前速度严格处于两个目标速度之间。</summary>
        private static void AssertVelocityBetween(
            float actual,
            float lowerExclusive,
            float upperExclusive,
            string errorMessage)
        {
            if (actual <= lowerExclusive + RunInputVelocityTolerance ||
                actual >= upperExclusive - RunInputVelocityTolerance)
            {
                throw new InvalidOperationException(
                    $"{errorMessage} actual={actual:0.###}, " +
                    $"range=({lowerExclusive:0.###}, {upperExclusive:0.###})。");
            }
        }

        /// <summary>断言当前速度已稳定到预期目标。</summary>
        private static void AssertVelocityApproximately(
            float actual,
            float expected,
            string errorMessage)
        {
            if (Mathf.Abs(actual - expected) > RunInputVelocityTolerance)
            {
                throw new InvalidOperationException(
                    $"{errorMessage} actual={actual:0.###}, expected={expected:0.###}。");
            }
        }

        #endregion

        #region 管理员移动速度

        private const float GoldenPathMoveSpeedMultiplier = 2.75f;
        private const float MoveSpeedTolerance = 0.001f;

        private static PlayerAdminController _moveSpeedAdminController;
        private static float _moveSpeedOriginalAdminMultiplier;
        private static float _moveSpeedOriginalModifier;
        private static bool _moveSpeedScenarioApplied;
        private static bool _moveSpeedScenarioCompleted;

        private static void ResetPlayerMoveSpeedScenario()
        {
            _moveSpeedAdminController = null;
            _moveSpeedOriginalAdminMultiplier = 1f;
            _moveSpeedOriginalModifier = 1f;
            _moveSpeedScenarioApplied = false;
            _moveSpeedScenarioCompleted = false;
        }

        private static void RunPlayerMoveSpeedScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || context.Mover?.Speed == null)
                throw new InvalidOperationException("移动速度倍率场景找不到真实玩家 Mover。");

            _moveSpeedAdminController =
                context.Player.GetComponentInChildren<PlayerAdminController>(true);
            if (_moveSpeedAdminController == null)
                throw new InvalidOperationException("真实玩家缺少 PlayerAdminController。");

            _moveSpeedOriginalAdminMultiplier =
                _moveSpeedAdminController.AdminMoveSpeedMultiplier;
            _moveSpeedOriginalModifier = context.Mover.Speed.MultiplicativeModifier;
            if (!_moveSpeedAdminController.TrySetAdminMoveSpeedMultiplier(
                    GoldenPathMoveSpeedMultiplier,
                    out float appliedMultiplier))
            {
                throw new InvalidOperationException("真实玩家拒绝了合法的移动速度倍率。");
            }

            _moveSpeedScenarioApplied = true;
            float expectedModifier = _moveSpeedOriginalModifier /
                                     Mathf.Max(0.01f, _moveSpeedOriginalAdminMultiplier) *
                                     GoldenPathMoveSpeedMultiplier;
            if (Mathf.Abs(appliedMultiplier - GoldenPathMoveSpeedMultiplier) > MoveSpeedTolerance ||
                Mathf.Abs(context.Mover.Speed.MultiplicativeModifier - expectedModifier) >
                MoveSpeedTolerance)
            {
                throw new InvalidOperationException(
                    $"管理员移速倍率未正确应用：applied={appliedMultiplier}, " +
                    $"modifier={context.Mover.Speed.MultiplicativeModifier}, expected={expectedModifier}。");
            }

            RestorePlayerMoveSpeedScenario();
            if (Mathf.Abs(context.Mover.Speed.MultiplicativeModifier - _moveSpeedOriginalModifier) >
                MoveSpeedTolerance)
            {
                throw new InvalidOperationException("管理员移速倍率恢复后破坏了原有速度修饰值。");
            }

            _moveSpeedScenarioCompleted = true;
            Debug.Log(
                $"[GoldenPath][Player] 自定义移速倍率 {GoldenPathMoveSpeedMultiplier:0.##}x " +
                "应用与恢复均通过。");
        }

        private static void AssertPlayerMoveSpeedScenarioCompleted()
        {
            if (!_moveSpeedScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成自定义移速倍率验证。");
        }

        private static void CleanupPlayerMoveSpeedScenario()
        {
            RestorePlayerMoveSpeedScenario();
            _moveSpeedAdminController = null;
        }

        private static void RestorePlayerMoveSpeedScenario()
        {
            if (!_moveSpeedScenarioApplied || _moveSpeedAdminController == null)
                return;

            if (!_moveSpeedAdminController.TrySetAdminMoveSpeedMultiplier(
                    _moveSpeedOriginalAdminMultiplier,
                    out _))
            {
                throw new InvalidOperationException("清理自定义移速倍率时无法恢复原倍率。");
            }

            _moveSpeedScenarioApplied = false;
        }

        #endregion

        #region 管理员无敌开关

        private const float AdminInvincibilityDamage = 1f;

        private static Player _adminInvincibilityPlayer;
        private static PlayerAdminController _adminInvincibilityController;
        private static DamageReceiver _adminInvincibilityDamageReceiver;
        private static Mod_PlayerDeathState _adminInvincibilityDeathState;
        private static string _adminInvincibilityOriginalPlayerName;
        private static float _adminInvincibilityOriginalHp;
        private static bool _adminInvincibilityOriginalSetting;
        private static bool _adminInvincibilityScenarioApplied;
        private static bool _adminInvincibilityScenarioCompleted;

        private static void ResetPlayerAdminInvincibilityScenario()
        {
            _adminInvincibilityPlayer = null;
            _adminInvincibilityController = null;
            _adminInvincibilityDamageReceiver = null;
            _adminInvincibilityDeathState = null;
            _adminInvincibilityOriginalPlayerName = null;
            _adminInvincibilityOriginalHp = 0f;
            _adminInvincibilityOriginalSetting = true;
            _adminInvincibilityScenarioApplied = false;
            _adminInvincibilityScenarioCompleted = false;
        }

        /// <summary>验证管理员可关闭无敌受伤，并可重新开启以拦截致死伤害。</summary>
        private static void RunPlayerAdminInvincibilityScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            _adminInvincibilityPlayer = context.Player;
            if (_adminInvincibilityPlayer?.Data == null ||
                _adminInvincibilityPlayer.itemMods == null)
            {
                throw new InvalidOperationException("管理员无敌场景找不到真实玩家或模块容器。");
            }

            _adminInvincibilityController =
                _adminInvincibilityPlayer.GetComponentInChildren<PlayerAdminController>(true);
            _adminInvincibilityDamageReceiver =
                _adminInvincibilityPlayer.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
            _adminInvincibilityDeathState =
                _adminInvincibilityPlayer.itemMods.GetMod_ByID<Mod_PlayerDeathState>(
                    Mod_PlayerDeathState.ModuleId);
            if (_adminInvincibilityController == null ||
                _adminInvincibilityDamageReceiver == null ||
                _adminInvincibilityDeathState == null)
            {
                throw new InvalidOperationException("真实玩家缺少管理员无敌所需模块。");
            }

            _adminInvincibilityOriginalPlayerName = _adminInvincibilityPlayer.Data.Name_User;
            _adminInvincibilityOriginalHp = _adminInvincibilityDamageReceiver.Hp;
            _adminInvincibilityOriginalSetting = PlayerAdminController.AdminInvincibilityEnabled;
            _adminInvincibilityScenarioApplied = true;

            if (_adminInvincibilityOriginalSetting)
            {
                throw new InvalidOperationException(
                    "管理员无敌默认值应为关闭，避免 Player Prefab 的默认管理员名称阻止正常死亡。");
            }

            if (!_adminInvincibilityController.TryEnableAdministrator() ||
                !_adminInvincibilityController.TrySetAdminInvincibilityEnabled(false) ||
                _adminInvincibilityController.IsAdminInvincibilityEnabled)
            {
                throw new InvalidOperationException("管理员无敌无法关闭。");
            }

            float hpBeforeDamage = _adminInvincibilityDamageReceiver.Hp;
            _adminInvincibilityDamageReceiver.ForceHurt(AdminInvincibilityDamage);
            if (_adminInvincibilityDamageReceiver.Hp >= hpBeforeDamage - MoveSpeedTolerance)
            {
                throw new InvalidOperationException("关闭管理员无敌后，玩家没有受到正常伤害。");
            }

            if (!_adminInvincibilityController.TrySetAdminInvincibilityEnabled(true) ||
                !_adminInvincibilityController.IsAdminInvincibilityEnabled ||
                Mathf.Abs(
                    _adminInvincibilityDamageReceiver.Hp -
                    _adminInvincibilityDamageReceiver.MaxHp) > MoveSpeedTolerance)
            {
                throw new InvalidOperationException("重新开启管理员无敌后未立即恢复满生命。");
            }

            _adminInvincibilityDamageReceiver.ForceHurt(
                _adminInvincibilityDamageReceiver.MaxHp * 2f + 1f);
            if (_adminInvincibilityDamageReceiver.Hp <
                _adminInvincibilityDamageReceiver.MaxHp - MoveSpeedTolerance ||
                _adminInvincibilityDeathState.IsInDyingState)
            {
                throw new InvalidOperationException("管理员无敌未拦截致死伤害或濒死状态。");
            }

            RestorePlayerAdminInvincibilityScenario();
            _adminInvincibilityScenarioCompleted = true;
            Debug.Log("[GoldenPath][Player] 管理员无敌关闭、恢复与致死伤害拦截验证通过。");
        }

        private static void AssertPlayerAdminInvincibilityScenarioCompleted()
        {
            if (!_adminInvincibilityScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成管理员无敌开关验证。");
        }

        private static void CleanupPlayerAdminInvincibilityScenario()
        {
            RestorePlayerAdminInvincibilityScenario();
            _adminInvincibilityPlayer = null;
            _adminInvincibilityController = null;
            _adminInvincibilityDamageReceiver = null;
            _adminInvincibilityDeathState = null;
        }

        private static void RestorePlayerAdminInvincibilityScenario()
        {
            if (!_adminInvincibilityScenarioApplied)
                return;

            if (_adminInvincibilityController != null &&
                _adminInvincibilityController.IsAdministrator)
            {
                _adminInvincibilityController.TrySetAdminInvincibilityEnabled(
                    _adminInvincibilityOriginalSetting);
            }

            if (_adminInvincibilityDamageReceiver != null)
            {
                _adminInvincibilityDamageReceiver.Hp = Mathf.Clamp(
                    _adminInvincibilityOriginalHp,
                    0f,
                    _adminInvincibilityDamageReceiver.MaxHp);
            }

            if (_adminInvincibilityPlayer?.Data != null)
                _adminInvincibilityPlayer.Data.Name_User = _adminInvincibilityOriginalPlayerName;

            _adminInvincibilityScenarioApplied = false;
        }

        #endregion
    }

    /// <summary>黄金路径专用交互目标，只记录公开 IInteractable 调用次数。</summary>
    internal sealed class GoldenPathInteractionRetryProbe : MonoBehaviour, IInteractable
    {
        internal int StartCount { get; private set; }

        public void OnInteractStart(Item playerItem)
        {
            if (playerItem == null)
                throw new InvalidOperationException("交互重试探针收到空玩家。");

            StartCount++;
        }

        public void OnInteractCancel(Item playerItem)
        {
        }
    }
}

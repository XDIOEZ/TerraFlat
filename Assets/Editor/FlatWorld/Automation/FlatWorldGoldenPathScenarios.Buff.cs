using System;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实玩家移动和 Chunk 流送期间验证正式 Buff 生命周期。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const double BurningTickTimeoutSeconds = 4d;
        private const float HealthChangeTolerance = 0.001f;

        private static BuffManager _burningBuffManager;
        private static DamageReceiver _burningDamageReceiver;
        private static ActorStatusVisualEffectController _burningVisualController;
        private static float _healthBeforeBurning;
        private static float _observedBurningDamage;
        private static double _burningTickDeadline;
        private static bool _burningHealthCaptured;
        private static bool _burningApplied;
        private static bool _burningTickObserved;
        private static bool _burningScenarioCompleted;

        private static void ResetBurningBuffScenario()
        {
            _burningBuffManager = null;
            _burningDamageReceiver = null;
            _burningVisualController = null;
            _healthBeforeBurning = 0f;
            _observedBurningDamage = 0f;
            _burningTickDeadline = 0d;
            _burningHealthCaptured = false;
            _burningApplied = false;
            _burningTickObserved = false;
            _burningScenarioCompleted = false;
        }

        private static void TickBurningBuffScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (!_burningApplied)
                ApplyBurningBuff(context);

            ObserveBurningTick();
            if (!_burningTickObserved && EditorApplication.timeSinceStartup >= _burningTickDeadline)
            {
                throw new TimeoutException(
                    $"燃烧 Buff 在 {BurningTickTimeoutSeconds:0.#} 秒内没有产生 Tick 伤害。");
            }
        }

        private static void ApplyBurningBuff(FlatWorldGoldenPathScenarioContext context)
        {
            Player player = context.Player;
            if (player == null || player.itemMods == null)
                throw new InvalidOperationException("燃烧 Buff 场景找不到真实玩家模块容器。");

            BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(BurningBuffIds.Burning);
            if (definition == null)
                throw new InvalidOperationException($"燃烧 Buff 未注册：{BurningBuffIds.Burning}");
            if (definition.TickEffects.Count != 1 ||
                definition.TickEffects[0].TypeId != BuffEffectTypeIds.TrueDamage ||
                definition.TickEffects[0].Value <= 0f)
            {
                throw new InvalidOperationException("燃烧 Buff 没有配置唯一且有效的真实伤害 Tick。");
            }

            _burningBuffManager = player.itemMods.GetMod_ByID<BuffManager>(ModText.BuffManager);
            _burningDamageReceiver = player.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
            _burningVisualController = player.GetComponentInChildren<ActorStatusVisualEffectController>(true);
            if (_burningBuffManager == null)
                throw new InvalidOperationException("真实玩家缺少 BuffManager，无法测试燃烧 Buff。");
            if (_burningDamageReceiver == null)
                throw new InvalidOperationException("真实玩家缺少 DamageReceiver，无法观察燃烧伤害。");
            if (_burningVisualController == null ||
                !_burningVisualController.IsStatusVisualConfigured(BurningBuffIds.Burning) ||
                _burningVisualController.GetStatusVisualFrameCount(BurningBuffIds.Burning) != 8)
            {
                throw new InvalidOperationException("真实玩家缺少有效的八帧燃烧状态表现配置。");
            }
            if (_burningBuffManager.HasBuff(BurningBuffIds.Burning))
                throw new InvalidOperationException("黄金路径开始前玩家已存在燃烧 Buff，测试前置状态不干净。");

            _healthBeforeBurning = _burningDamageReceiver.Hp;
            _burningHealthCaptured = true;
            if (_healthBeforeBurning <= definition.TickEffects[0].Value + HealthChangeTolerance)
                throw new InvalidOperationException("玩家生命值不足以安全执行一次燃烧 Tick。");

            _burningDamageReceiver.OnDamageReceived += OnBurningDamageReceived;
            if (!_burningBuffManager.AddBuff(BurningBuffIds.Burning) ||
                !_burningBuffManager.TryGetBuff(BurningBuffIds.Burning, out BuffInstance runtime) ||
                runtime?.Definition != definition)
            {
                throw new InvalidOperationException("通过 BuffManager.AddBuff 施加燃烧 Buff 失败。");
            }

            _burningVisualController.RefreshStatusVisuals();
            if (!_burningVisualController.IsStatusVisualActive(BurningBuffIds.Burning))
                throw new InvalidOperationException("燃烧 Buff 已生效，但角色火焰表现没有同步启用。");

            _burningApplied = true;
            _burningTickDeadline = EditorApplication.timeSinceStartup + BurningTickTimeoutSeconds;
            Debug.Log($"[GoldenPath][Buff] 已在移动阶段施加 {BurningBuffIds.Burning}，等待真实 Tick 伤害。");
        }

        private static void ObserveBurningTick()
        {
            if (!_burningApplied || _burningTickObserved || _burningDamageReceiver == null)
                return;

            if (_observedBurningDamage > HealthChangeTolerance ||
                _burningDamageReceiver.Hp < _healthBeforeBurning - HealthChangeTolerance)
            {
                _burningTickObserved = true;
                Debug.Log(
                    $"[GoldenPath][Buff] 已观察到 {BurningBuffIds.Burning} Tick：" +
                    $"伤害 {_observedBurningDamage:0.###}，" +
                    $"HP {_healthBeforeBurning:0.###} → {_burningDamageReceiver.Hp:0.###}。");
            }
        }

        private static void OnBurningDamageReceived(DamageReceiverDamageInfo damageInfo)
        {
            if (!_burningApplied || damageInfo == null || damageInfo.DamageValue <= HealthChangeTolerance)
                return;

            _observedBurningDamage = Mathf.Max(_observedBurningDamage, damageInfo.DamageValue);
        }

        private static void VerifyBurningBuffAtChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            _ = context;
            if (!_burningApplied)
                throw new InvalidOperationException("到达 Chunk Ready 时尚未施加燃烧 Buff。");

            ObserveBurningTick();
            if (!_burningTickObserved)
            {
                if (EditorApplication.timeSinceStartup >= _burningTickDeadline)
                    throw new TimeoutException("Chunk Ready 后仍未观察到燃烧 Buff Tick 伤害。");
                return;
            }

            if (GameRes.Instance?.GetBuffDefinition(BurningBuffIds.Burning) == null)
                throw new InvalidOperationException("Chunk 流送期间燃烧 Buff 注册丢失。");

            _burningScenarioCompleted = true;
            CleanupBurningBuffScenario();
        }

        private static void AssertBurningBuffScenarioCompleted()
        {
            if (!_burningScenarioCompleted)
                throw new InvalidOperationException("完整移动流程结束前未完成燃烧 Buff Tick 验证。");
        }

        private static void CleanupBurningBuffScenario()
        {
            if (_burningBuffManager != null)
                _burningBuffManager.RemoveBuff(BurningBuffIds.Burning);

            if (_burningVisualController != null)
            {
                _burningVisualController.RefreshStatusVisuals();
                if (_burningVisualController.IsStatusVisualActive(BurningBuffIds.Burning))
                    throw new InvalidOperationException("燃烧 Buff 清理后角色火焰表现仍处于启用状态。");
            }

            if (_burningHealthCaptured && _burningDamageReceiver != null)
            {
                _burningDamageReceiver.OnDamageReceived -= OnBurningDamageReceived;
                _burningDamageReceiver.Hp = Mathf.Clamp(
                    _healthBeforeBurning,
                    0f,
                    _burningDamageReceiver.MaxHp);
            }

            _burningBuffManager = null;
            _burningDamageReceiver = null;
            _burningVisualController = null;
            _burningHealthCaptured = false;
        }
    }
}

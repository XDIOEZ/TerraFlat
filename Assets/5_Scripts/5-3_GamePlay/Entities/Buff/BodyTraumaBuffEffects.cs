using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 创伤 Buff 的实际数值/输入效果；每个 BuffInstance 是唯一来源，左右肢体及其它疾病独立撤销。
/// 移速接入 GameValue 修饰器，攻速/方向在使用端查询组合值，换武器与读档不会把临时倍率写入基础值。
/// </summary>
public static class BodyTraumaBuffEffects
{
    #region 来源修饰
    public const string Move = "core:trauma_move";
    public const string Attack = "core:trauma_attack";
    public const string Confusion = "core:trauma_confusion";
    public const string Blur = "core:trauma_blur";
    public const string RestoreDurability = "core:body_durability_restore";

    /// <summary>限时恢复只作用于指定部位，不回复总生命，远端表现副本不重复结算。</summary>
    public static void ApplyDurabilityRecovery(BuffEffectDefinition effect, BuffInstance runtime)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || runtime.Receiver == null) return;
        Mod_DamageReceiver receiver = runtime.Receiver.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        float amount = effect.Value;
        // 自然到期结算最后不足一个周期的余量；主动移除/预留回滚绝不额外治疗。
        if (effect.Phase == BuffEffectPhase.Stop)
        {
            if (!runtime.IsExpired || runtime.Definition.TickIntervalSeconds <= 0f) return;
            amount *= Mathf.Clamp01(runtime.TickElapsedSeconds / runtime.Definition.TickIntervalSeconds);
        }
        if (receiver != null && receiver.Hp > 0f && receiver.TryGetBodyPart(effect.BodyPartTarget, out _))
            receiver.RestoreBodyPartDurability(effect.BodyPartTarget, amount);
    }
    private sealed class Entry
    {
        public string Type;
        public float Value;
        public GameValue_float Speed;
        public TraumaScreenEffect Screen;
    }
    private static readonly Dictionary<BuffInstance, Dictionary<string, Entry>> Sources = new();
    private readonly struct ReceiverModifiers
    {
        public readonly float Attack, Confusion;
        public ReceiverModifiers(float attack, float confusion)
        {
            Attack = attack;
            Confusion = confusion;
        }
    }
    private static readonly Dictionary<Item, ReceiverModifiers> receiverModifiers = new();

    public static void Apply(BuffEffectDefinition effect, BuffInstance runtime)
    {
        if (effect.Phase == BuffEffectPhase.Stop) { Remove(runtime, effect.TypeId); return; }
        if (effect.Phase != BuffEffectPhase.Start || effect.Value <= 0f || float.IsInfinity(effect.Value) || float.IsNaN(effect.Value))
            throw new InvalidOperationException("创伤效果只允许 start/stop，数值必须为有限正数。");
        Remove(runtime, effect.TypeId);
        if (!Sources.TryGetValue(runtime, out Dictionary<string, Entry> entries))
            Sources.Add(runtime, entries = new());
        var entry = new Entry { Type = effect.TypeId, Value = effect.Value };
        if (effect.TypeId == Move)
        {
            entry.Speed = runtime.Receiver.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.Speed;
            if (entry.Speed != null) entry.Speed.MultiplicativeModifier *= entry.Value;
        }
        if (effect.TypeId == Blur && runtime.Receiver is Player player && player.IsLocalProfile)
        {
            entry.Screen = new TraumaScreenEffect(runtime, effect.Value);
            ScreenPostProcessManager.Instance.RegisterEffect(entry.Screen);
        }
        entries.Add(effect.TypeId, entry);
        if (effect.TypeId == Attack || effect.TypeId == Confusion)
            RefreshReceiverModifiers(runtime.Receiver);
    }

    private static void Remove(BuffInstance runtime, string type)
    {
        if (!Sources.TryGetValue(runtime, out Dictionary<string, Entry> entries) || !entries.Remove(type, out Entry entry)) return;
        if (entry.Speed != null) entry.Speed.MultiplicativeModifier /= entry.Value;
        if (entry.Screen != null) ScreenPostProcessManager.ExistingInstance?.UnregisterEffect(entry.Screen);
        if (entries.Count == 0) Sources.Remove(runtime);
        if (type == Attack || type == Confusion)
            RefreshReceiverModifiers(runtime.Receiver);
    }

    /// <summary>Buff 生效或结束时重算拥有者汇总，移动和武器热路径只做一次字典查询。</summary>
    private static void RefreshReceiverModifiers(Item owner)
    {
        if (ReferenceEquals(owner, null)) return;
        float attack = 1f, confusion = 0f;
        foreach (var pair in Sources)
        {
            if (!ReferenceEquals(pair.Key.Receiver, owner)) continue;
            if (pair.Value.TryGetValue(Attack, out Entry attackEntry)) attack *= attackEntry.Value;
            if (pair.Value.TryGetValue(Confusion, out Entry confusionEntry)) confusion += confusionEntry.Value;
        }
        if (attack == 1f && confusion <= 0f)
            receiverModifiers.Remove(owner);
        else
            receiverModifiers[owner] = new ReceiverModifiers(attack, confusion);
    }

    public static float GetAttackMultiplier(Item owner) =>
        !ReferenceEquals(owner, null) && receiverModifiers.TryGetValue(owner, out ReceiverModifiers values)
            ? values.Attack : 1f;

    /// <summary>在输入源仲裁之后反转移动；不累加反转次数，零输入和 UI/输入锁语义保持不变。</summary>
    public static Vector2 TransformMoveInput(Item owner, Vector2 input, float time)
    {
        return !ReferenceEquals(owner, null) &&
            receiverModifiers.TryGetValue(owner, out ReceiverModifiers values) && values.Confusion > 0f
            ? -input : input;
    }
    #endregion

    #region 屏幕视觉
    private sealed class TraumaScreenEffect : IScreenPostProcessEffect, IScreenPostProcessLowQualityEffect
    {
        private readonly BuffInstance runtime;
        private readonly float strength;
        public TraumaScreenEffect(BuffInstance runtime, float strength) { this.runtime = runtime; this.strength = strength; }
        public string EffectId => "trauma:" + runtime.SourceKey;
        public int Priority => 30;
        public bool IsValid => runtime.Receiver is Player player && player.IsLocalProfile;
        public void Apply(ScreenPostProcessFrame frame, float deltaTime) => frame.AddBlur(strength);
    }
    #endregion
}

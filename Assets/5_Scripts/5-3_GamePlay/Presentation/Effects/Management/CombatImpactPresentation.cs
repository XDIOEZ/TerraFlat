using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>消费已经结算的命中结果，只生成表现，不查询碰撞或修改生命。</summary>
public static class CombatImpactPresentation
{
    #region 命中特效选择与播放
    public static void CollectEffects(CombatImpactEffectSet impactEffects, IReadOnlyList<GameEffect> effects,
        CombatDamageKind kind, bool includeDamageNumber, List<GameEffect> result)
    {
        result.Clear();
        bool hasNumber = !includeDamageNumber;
        AddEffect(impactEffects != null ? impactEffects.GetPrefab(kind) : null, result, ref hasNumber);
        if (effects == null) return;
        for (int i = 0; i < effects.Count; i++) AddEffect(effects[i], result, ref hasNumber);
    }

    private static void AddEffect(GameEffect effect, List<GameEffect> result, ref bool hasNumber)
    {
        if (effect == null || result.Contains(effect)) return;
        // 一笔命中至多选择一个数字，重复配置不应重复播放。
        if (effect is DamageTextEffect)
        {
            if (hasNumber) return;
            hasNumber = true;
        }
        result.Add(effect);
    }

    public static void Play(Transform source, CombatImpactEffectSet impactEffects, IReadOnlyList<GameEffect> effects,
        Vector2 hitPoint, float damage, CombatDamageKind kind, bool includeDamageNumber)
    {
        if (!float.IsFinite(damage) || damage < 0f) return;
        using var scope = ListPool<GameEffect>.Get(out var selected);
        CollectEffects(impactEffects, effects, kind, includeDamageNumber, selected);
        if (selected.Count == 0) return;
        WorldRenderingManager manager = WorldRenderingManager.Instance;
        foreach (GameEffect prefab in selected)
        {
            GameEffect effect = manager != null ? manager.GetGameEffectFromPool(prefab) : Object.Instantiate(prefab);
            if (effect == null) continue;
            effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
            object data = effect is DamageTextEffect ? BuildDamageNumber(damage, kind) : damage;
            effect.Effect(source, data);
        }
    }

    public static DamageTextEffectData BuildDamageNumber(float damage, CombatDamageKind kind)
    {
        DamageTextStyle style = kind switch
        {
            CombatDamageKind.Cutting or CombatDamageKind.Chopping => DamageTextStyle.Cutting,
            CombatDamageKind.Piercing => DamageTextStyle.Piercing,
            CombatDamageKind.Blunt => DamageTextStyle.Blunt,
            _ => DamageTextStyle.Normal
        };
        return new DamageTextEffectData(damage, style);
    }
    #endregion
}

/// <summary>接收侧可显式接管自己的伤害数字，攻击者仍保留非数字命中特效。</summary>
public interface ICombatDamageNumberPresenter
{
    bool CanPresentDamageNumber { get; }
}

public static class CombatDamageNumberOwnership
{
    #region 伤害数字归属
    private static readonly Dictionary<Mod_DamageReceiver, ICombatDamageNumberPresenter> owners = new();

    public static void Register(Mod_DamageReceiver receiver, ICombatDamageNumberPresenter presenter)
    {
        if (receiver != null && presenter != null) owners[receiver] = presenter;
    }

    public static void Unregister(Mod_DamageReceiver receiver, ICombatDamageNumberPresenter presenter)
    {
        if (!ReferenceEquals(receiver, null) && owners.TryGetValue(receiver, out var current) &&
            ReferenceEquals(current, presenter)) owners.Remove(receiver);
    }

    public static bool OwnsNumber(Mod_DamageReceiver receiver, float damage)
        => float.IsFinite(damage) && damage > 0f && HasPresenter(receiver);

    public static bool HasPresenter(Mod_DamageReceiver receiver)
        => receiver != null && owners.TryGetValue(receiver, out var presenter) && presenter.CanPresentDamageNumber;

    public static bool IsOwner(Mod_DamageReceiver receiver, ICombatDamageNumberPresenter presenter)
        => receiver != null && owners.TryGetValue(receiver, out var current) && ReferenceEquals(current, presenter);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => owners.Clear();
    #endregion
}

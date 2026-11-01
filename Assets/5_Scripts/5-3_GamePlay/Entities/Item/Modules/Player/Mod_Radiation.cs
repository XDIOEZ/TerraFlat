using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>读取脚下辐射层，按生物与装备的安全范围结算周期伤害。</summary>
public sealed class Mod_Radiation : Module, IItemModuleDependencyBinder, IModuleJsonParameterValidator, IPlayerStateAuthorityModule
{
    #region 配置与运行态

    public const string ModuleId = "辐射生存模块";
    private const string RadiationDeathCauseId = "radiation";
    private const float RadiationDamagePerSecond = 1f;
    private static readonly CombatDamage RadiationDamage = new() { Radiation = RadiationDamagePerSecond };

    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .25f;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }

    public float SafeRadiationMin;
    public float SafeRadiationMax;
    public float CurrentRadiation { get; private set; }
    public bool HasCurrentSample { get; private set; }
    public bool IsInDanger { get; private set; }

    private readonly Dictionary<object, Vector2> rangeModifiers = new();
    private Mod_DamageReceiver health;
    private float radiationDamageClock;

    #endregion

    #region 生命周期与安全范围

    public void BindModuleDependencies(ItemMods modules)
        => health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);

    protected override void OnLoad()
    {
        radiationDamageClock = 0f;
        CurrentRadiation = 0f;
        HasCurrentSample = false;
        IsInDanger = false;
        if (health != null) health.DeathStarted += OnDeath;
    }

    protected override void OnSave() { }

    protected override void OnUnload()
    {
        if (health != null) health.DeathStarted -= OnDeath;
        rangeModifiers.Clear();
        health = null;
        radiationDamageClock = 0f;
        CurrentRadiation = 0f;
        HasCurrentSample = false;
        IsInDanger = false;
    }

    private void OnDestroy() => Unload();

    private void OnDeath(Mod_DamageReceiver _)
    {
        radiationDamageClock = 0f;
        IsInDanger = false;
    }

    /// <summary>装备按自身来源独立扩展耐受范围，移除时可精确撤销。</summary>
    public void SetSafeRadiationRangeModifier(object source, float lowerToleranceIncrease, float upperToleranceIncrease)
    {
        if (source == null || !Valid(lowerToleranceIncrease) || !Valid(upperToleranceIncrease))
            throw new ArgumentException("辐射耐受修正必须是有限非负数。");
        rangeModifiers[source] = new Vector2(lowerToleranceIncrease, upperToleranceIncrease);
    }

    public void RemoveSafeRadiationRangeModifier(object source)
    {
        if (source != null) rangeModifiers.Remove(source);
    }

    public void GetSafeRadiationRange(out float minimum, out float maximum)
    {
        double safeMinimum = SafeRadiationMin;
        double safeMaximum = SafeRadiationMax;
        foreach (Vector2 modifier in rangeModifiers.Values)
        {
            safeMinimum -= modifier.x;
            safeMaximum += modifier.y;
        }
        minimum = (float)Math.Max(0d, safeMinimum);
        maximum = (float)Math.Min(float.MaxValue, safeMaximum);
    }

    public void ValidateJsonParameters(JObject parameters)
    {
        float minimum = parameters.Value<float?>(nameof(SafeRadiationMin)) ?? SafeRadiationMin;
        float maximum = parameters.Value<float?>(nameof(SafeRadiationMax)) ?? SafeRadiationMax;
        if (!Valid(minimum) || !Valid(maximum) || maximum < minimum)
            throw new InvalidOperationException("生物安全辐射范围无效。");
    }

    private static bool Valid(float value) => float.IsFinite(value) && value >= 0f;

    #endregion

    #region 辐射暴露与伤害

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || item == null || deltaTime <= 0f)
            return;

        Vector2 logicalPosition = (Vector2)WorldLocalPresentation.ToLogical(item.transform.position);
        HasCurrentSample = ContaminationSystem.TryGetValue(logicalPosition, ContaminationIds.Radiation, out float radiation);
        CurrentRadiation = HasCurrentSample ? radiation : 0f;
        if (health == null || health.Hp <= 0f)
        {
            radiationDamageClock = 0f;
            IsInDanger = false;
            return;
        }

        GetSafeRadiationRange(out float minimum, out float maximum);
        var exposure = new SurvivalNumericBatch.RadiationExposure
        {
            Radiation = CurrentRadiation,
            SafeMinimum = minimum,
            SafeMaximum = maximum,
            DamageClock = radiationDamageClock,
            Seconds = deltaTime,
            HasSample = HasCurrentSample
        };
        SurvivalNumericBatch.Advance(ref exposure);
        radiationDamageClock = exposure.DamageClock;
        IsInDanger = exposure.IsDangerous;

        ulong firstEventId = GameplayFixedStepSimulation.TickIndex + 1UL;
        for (int i = 0; i < exposure.DamageTicks && health.Hp > 0f; i++)
        {
            health.HurtEnvironmentalDamage(
                firstEventId + (ulong)i,
                logicalPosition,
                RadiationDamage,
                RadiationDeathCauseId,
                isTrueDamage: true);
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>气压危险共用生命权威，装备只按来源扩展安全范围。</summary>
public sealed partial class Mod_Pressure : Module, IItemModuleDependencyBinder, IModuleJsonParameterValidator
{
    #region 配置与真实状态
    public const string ModuleId = "气压生存模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => .2f;
    public Ex_ModData_MemoryPackable modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData_MemoryPackable)value; }
    public float SafePressureMinKPa = 50f;
    public float SafePressureMaxKPa = 150f;
    public float DangerBufferSeconds = 30f;
    public float BufferRecoveryPerSecond = 1f;
    public float DamageIntervalSeconds = 5f;
    public float LowPressureDamage = 5f;
    public float HighPressureDamage = 5f;
    [MemoryPackable, Serializable]
    public partial class PressureState
    {
        public bool Initialized;
        public float RemainingBufferSeconds;
    }
    public PressureState State = new();
    public double EnvironmentPressureKPa { get; private set; }
    public int Danger { get; private set; } // -1 低压，1 高压，0 安全。
    public event Action Changed;
    private readonly Dictionary<object, Vector2> modifiers = new();
    private Mod_DamageReceiver health;
    private float lowDamageClock, highDamageClock;
    #endregion

    #region 生命周期与范围
    public void BindModuleDependencies(ItemMods modules) => health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
    protected override void OnLoad()
    {
        modData.ReadData(ref State);
        State ??= new();
        if (!State.Initialized) { State.RemainingBufferSeconds = DangerBufferSeconds; State.Initialized = true; }
        State.RemainingBufferSeconds = Mathf.Clamp(State.RemainingBufferSeconds, 0f, DangerBufferSeconds);
        lowDamageClock = highDamageClock = 0f;
        if (health != null) health.DeathStarted += OnDeath;
    }
    protected override void OnSave() => modData.WriteData(State);
    protected override void OnUnload()
    {
        if (health != null) health.DeathStarted -= OnDeath;
        modifiers.Clear();
        lowDamageClock = highDamageClock = 0f;
        health = null;
        Danger = 0;
        Changed = null;
    }
    private void OnDeath(Mod_DamageReceiver _) { lowDamageClock = highDamageClock = 0f; Danger = 0; }
    public void SetSafePressureRangeModifier(object source, float lowIncrease, float highIncrease)
    {
        if (source == null || !Valid(lowIncrease) || !Valid(highIncrease)) throw new ArgumentException("耐压修正必须是有限非负数。");
        modifiers[source] = new(lowIncrease, highIncrease);
        Changed?.Invoke();
    }
    public void RemoveSafePressureRangeModifier(object source)
    {
        if (source != null && modifiers.Remove(source)) Changed?.Invoke();
    }
    public void GetSafePressureRange(out float min, out float max)
    {
        double minimum = SafePressureMinKPa, maximum = SafePressureMaxKPa;
        foreach (Vector2 value in modifiers.Values) { minimum -= value.x; maximum += value.y; }
        min = (float)Math.Max(0d, minimum);
        max = (float)Math.Min(float.MaxValue, maximum);
    }
    public void ResetForNewLife()
    {
        State.RemainingBufferSeconds = DangerBufferSeconds;
        lowDamageClock = highDamageClock = 0f;
        Danger = 0;
    }
    #endregion

    #region 权威气压伤害
    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || item == null || health == null || health.Hp <= 0f || deltaTime <= 0f) return;
        if (ItemEnvironmentSources.TryGet(item, out ItemEnvironmentSample localEnvironment))
            EnvironmentPressureKPa = localEnvironment.PressureKPa;
        else if (AtmosphereService.TryGetForWorld(item.gameObject.scene.name, out AtmosphereState atmosphere))
            EnvironmentPressureKPa = AtmosphereService.PressureKPa(atmosphere);
        else return;
        GetSafePressureRange(out float min, out float max);
        int next = EnvironmentPressureKPa < min ? -1 : EnvironmentPressureKPa > max ? 1 : 0;
        if (next != Danger) { lowDamageClock = highDamageClock = 0f; Danger = next; }
        float before = State.RemainingBufferSeconds;
        State.RemainingBufferSeconds = Mathf.Clamp(before + (Danger == 0 ? deltaTime * BufferRecoveryPerSecond : -deltaTime), 0f, DangerBufferSeconds);
        if (Danger == 0) { lowDamageClock = highDamageClock = 0f; Changed?.Invoke(); return; }
        float exposure = Mathf.Max(0f, deltaTime - before);
        if (exposure <= 0f) return;
        if (Danger < 0) ApplyDamageClock(ref lowDamageClock, exposure, LowPressureDamage);
        else ApplyDamageClock(ref highDamageClock, exposure, HighPressureDamage);
        Changed?.Invoke();
    }
    private void ApplyDamageClock(ref float clock, float seconds, float damage)
    {
        clock += seconds;
        while (clock >= DamageIntervalSeconds && health != null && health.Hp > 0f)
        {
            clock -= DamageIntervalSeconds;
            health.ForceHurt(damage);
        }
    }
    public void ValidateJsonParameters(JObject parameters)
    {
        float min = parameters.Value<float?>(nameof(SafePressureMinKPa)) ?? SafePressureMinKPa;
        float max = parameters.Value<float?>(nameof(SafePressureMaxKPa)) ?? SafePressureMaxKPa;
        if (!Valid(min) || !Valid(max) || max < min) throw new InvalidOperationException("生物安全气压上下限无效。");
        foreach (string key in new[] { nameof(DangerBufferSeconds), nameof(BufferRecoveryPerSecond), nameof(LowPressureDamage), nameof(HighPressureDamage) })
            if (parameters.TryGetValue(key, out JToken value) && !Valid(value.Value<float>())) throw new InvalidOperationException("气压参数无效：" + key);
        float interval = parameters.Value<float?>(nameof(DamageIntervalSeconds)) ?? DamageIntervalSeconds;
        if (!float.IsFinite(interval) || interval <= 0f) throw new InvalidOperationException("气压伤害间隔必须为有限正数。");
    }
    private static bool Valid(float value) => float.IsFinite(value) && value >= 0f;
    #endregion
}

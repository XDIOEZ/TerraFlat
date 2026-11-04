using System;
using UnityEngine;

/// <summary>角色拥有并加载此模块即具备管理员权限，不依赖玩家类型、名字或输入设备。</summary>
public sealed class Mod_Administrator : Module
{
    #region 管理员能力与装配
    public const string ModuleId = "Module_Administrator";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Ex_ModData_MemoryPackable ModSaveData = new();
    public override ModuleData _Data { get => ModSaveData; set => ModSaveData = (Ex_ModData_MemoryPackable)value; }
    public bool IsInvincibilityEnabled { get; private set; }
    private IDisposable invulnerabilityLease;

    public static Mod_Administrator GetLoadedAdministratorModule(Item actor)
    {
        var module = actor?.itemMods?.GetMod_ByID<Mod_Administrator>(ModuleId);
        return module != null && module.isActiveAndEnabled && module.Enabled && module.IsRuntimeLoaded ? module : null;
    }

    /// <summary>运行时入口复用正式模块装配；角色定义也可直接组合此模块。</summary>
    public static Mod_Administrator EnsureAdministratorModule(Item actor)
    {
        var module = actor.itemMods.GetMod_ByID<Mod_Administrator>(ModuleId);
        if (module != null) module.SetEnabled(true);
        else module = (Mod_Administrator)ADDModTOItem(actor, ModuleId);
        return module;
    }
    #endregion

    #region 管理员生存规则
    protected override void OnLoad() => RestoreStamina();
    protected override void OnSave() { }
    protected override void OnUnload() => SetInvincibilityEnabled(false);

    private void OnEnable()
    {
        if (IsRuntimeLoaded && IsInvincibilityEnabled) SetInvincibilityEnabled(true);
    }

    private void OnDisable()
    {
        invulnerabilityLease?.Dispose();
        invulnerabilityLease = null;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!isActiveAndEnabled) return;
        RestoreStamina();
        if (IsInvincibilityEnabled) RestoreVitalStats();
    }

    public void SetInvincibilityEnabled(bool enabled)
    {
        invulnerabilityLease?.Dispose();
        invulnerabilityLease = null;
        IsInvincibilityEnabled = enabled;
        if (!enabled) return;
        RestoreVitalStats();
        var health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health == null) return;
        health.ResetBodyPartDurability();
        invulnerabilityLease = health.AcquireTemporaryInvulnerability();
    }

    private void RestoreStamina()
    {
        var stamina = item.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        if (stamina != null && stamina.MaxValue > 0f && !Mathf.Approximately(stamina.CurrentValue, stamina.MaxValue))
            stamina.CurrentValue = stamina.MaxValue;
    }

    private void RestoreVitalStats()
    {
        var health = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (health != null && health.MaxHp > 0f && health.Hp < health.MaxHp) health.Hp = health.MaxHp;
        var sanity = item.itemMods.GetMod_ByID<Mod_San>(Mod_San.ModuleId);
        if (sanity != null && sanity.MaxValue > 0f && sanity.CurrentValue < sanity.MaxValue)
            sanity.CurrentValue = sanity.MaxValue;
        var food = item.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
        if (food == null) return;
        food.Data.nutrition.Carbohydrates = food.Data.nutrition.Max_Carbohydrates;
        food.Data.nutrition.Fat = food.Data.nutrition.Max_Fat;
        food.Data.nutrition.Protein = food.Data.nutrition.Max_Protein;
        food.Data.nutrition.Water = food.Data.nutrition.Max_Water;
    }
    #endregion
}

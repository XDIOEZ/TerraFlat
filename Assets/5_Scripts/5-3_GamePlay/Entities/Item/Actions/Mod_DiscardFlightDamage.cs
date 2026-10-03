using FlatWorld.Combat;
using UnityEngine;

/// <summary>通用丢弃飞行伤害模块：玩家主动丢出物品时临时开启同物品的伤害模块。</summary>
public sealed class Mod_DiscardFlightDamage : Module, IItemModuleDependencyBinder, IDroppedItemSpawnContextReceiver
{
    #region 配置与依赖

    public const string PersistedModuleId = "Mod_DiscardFlightDamage";

    [SerializeField] private Ex_ModData_MemoryPackable moduleData = new();
    [SerializeField, Tooltip("负责飞行期间伤害结算的同物品伤害模块。")]
    private Mod_Damage damageModule;

    public override ModuleData _Data
    {
        get => moduleData;
        set => moduleData = (Ex_ModData_MemoryPackable)value;
    }

    public override string CanonicalModuleId => PersistedModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    public void BindModuleDependencies(ItemMods modules)
    {
        Mod_Damage resolved = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");
        if (damageModule != null && damageModule != resolved)
            throw new System.InvalidOperationException($"{name} 的伤害模块引用与 ItemMods 注册结果不一致。");

        damageModule = resolved;
    }

    #endregion

    #region 运行时状态

    private Item thrower;
    private bool armedByDiscard;
    private bool damageActive;
    private double damageEndTime;

    #endregion

    #region Module 生命周期

    public override void Load()
    {
        ResetRuntimeState();
    }

    public override void Save()
    {
    }

    public override void Unload()
    {
        StopDamage();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!armedByDiscard && !damageActive)
            return;

        bool stillFlying = item != null &&
                           Time.timeAsDouble < damageEndTime &&
                           Mod_Droping.IsDropInProgress(item);
        if (stillFlying)
        {
            TryStartDamage();
            return;
        }

        StopDamage();
    }

    #endregion

    #region 丢弃飞行伤害

    /// <summary>只有玩家主动丢弃才激活，普通掉落、战利品和自然生成不会产生伤害。</summary>
    public void OnDroppedItemSpawned(in DroppedItemSpawnContext context)
    {
        if (!Enabled || !isActiveAndEnabled ||
            context.Reason != DroppedItemSpawnReason.PlayerDiscard ||
            context.Duration <= 0f)
        {
            return;
        }

        thrower = context.SourceItem;
        damageEndTime = Time.timeAsDouble + context.Duration;
        armedByDiscard = true;
        TryStartDamage();
    }

    /// <summary>复用正式 Mod_Damage，使 GameObject 与数据后端目标继续走统一伤害链。</summary>
    private void TryStartDamage()
    {
        if (!armedByDiscard || damageActive || item == null || damageModule == null ||
            Time.timeAsDouble >= damageEndTime || !Mod_Droping.IsDropInProgress(item))
        {
            return;
        }

        item.Owner = thrower;
        damageModule.SetDeliveryCapabilities(
            CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneTargets);
        damageModule.StartAttack();
        damageActive = true;
    }

    /// <summary>飞行结束后关闭伤害并解除临时投掷者归属。</summary>
    private void StopDamage()
    {
        if (damageActive && damageModule != null)
        {
            damageModule.StopAttack();
            damageModule.SetDeliveryCapabilities(CombatDeliveryCapabilities.None);
        }

        if (item != null && item.Owner == thrower)
            item.Owner = null;

        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        thrower = null;
        armedByDiscard = false;
        damageActive = false;
        damageEndTime = 0d;
    }

    #endregion
}

using UnityEngine;

/// <summary>
/// 命中附加 Buff 模块：监听指定伤害模块的实体命中结果，并按概率给有效命中的目标添加 Buff。
/// 该模块不参与伤害计算，也不会作用于格子建筑，因此可被燃烧、中毒、冰冻等武器效果复用。
/// </summary>
public sealed class DamageOnHitBuffApplier : Module, IItemModuleDependencyBinder, ICombatDamageContextModifier,
    ICombustionStateReceiver
{
    [SerializeField, Tooltip("负责发布实体命中结果的伤害模块；Prefab 可显式绑定，JSON 组合由 ItemMods 绑定。")]
    private Mod_Damage damageModule;

    [SerializeField, Tooltip("命中成功后尝试添加的稳定 Buff ID。")]
    private string buffId = "燃烧";

    [SerializeField, Range(0f, 1f), Tooltip("每次有效实体命中附加 Buff 的概率。")]
    private float applicationChance = 0.25f;

    [SerializeField, Min(1), Tooltip("一次成功附加的 Buff 层数；一次提交完整层数以正确比较水火强度。")]
    private int applicationStacks = 1;

    [SerializeField, Tooltip("开启后，只有同一物品处于燃烧状态时才允许附加该 Buff。")]
    private bool requiresCombustion;

    [SerializeField] private Ex_ModData_MemoryPackable moduleData = new();
    private bool combustionActive = true;

    public override ModuleData _Data
    {
        get => moduleData;
        set => moduleData = (Ex_ModData_MemoryPackable)value;
    }

    public override string CanonicalModuleId => "Module_DamageOnHitBuff";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    private bool IsEffectActive => isActiveAndEnabled && (!requiresCombustion || combustionActive);

    /// <summary>新后端在统一结算中应用同一 Buff 配置；旧后端仍保留原命中回调且不会重复执行。</summary>
    public void ModifyDamageContext(ref FlatWorld.Combat.CombatDamageContext context)
    {
        if (!IsEffectActive || string.IsNullOrWhiteSpace(buffId) || applicationChance <= 0f) return;
        context.OnHitBuffs.Add(new FlatWorld.Combat.CombatOnHitBuff {
            Id = buffId.Trim(), Chance = Mathf.Clamp01(applicationChance), Stacks = Mathf.Max(1, applicationStacks) });
    }

    /// <summary>从 ItemMods 唯一稳定 ID 绑定伤害模块，并校验 Prefab 显式引用没有漂移。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        Mod_Damage resolved = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");
        if (damageModule != null && damageModule != resolved)
            throw new System.InvalidOperationException($"{name} 的伤害引用与 ItemMods 注册结果不一致。");

        damageModule = resolved;
    }

    /// <summary>燃烧状态只门控效果，不改写组件 enabled，避免破坏其它生命周期。</summary>
    public void SetCombustionActive(bool active) => combustionActive = active;

    /// <summary>模块加载时订阅伤害结算。</summary>
    public override void Load()
    {
        damageModule.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        damageModule.OnReceiverDamageResolved += HandleReceiverDamageResolved;
    }

    public override void Save()
    {
    }

    /// <summary>模块卸载时解除订阅，兼容对象池复用。</summary>
    public override void Unload()
    {
        if (damageModule != null)
            damageModule.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
    }

    /// <summary>对一次有效实体命中执行概率判定，并通过目标 BuffManager 添加状态。</summary>
    private void HandleReceiverDamageResolved(DamageReceiver receiver, float damageResult)
    {
        if (!IsEffectActive ||
            receiver == null ||
            damageResult < 0f ||
            string.IsNullOrWhiteSpace(buffId))
        {
            return;
        }

        float chance = Mathf.Clamp01(applicationChance);
        if (chance <= 0f || (chance < 1f && Random.value >= chance))
            return;

        Item targetItem = receiver.item;
        BuffManager buffManager = targetItem?.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
        if (buffManager != null)
            buffManager.AddBuff(buffId.Trim(), Mathf.Max(1, applicationStacks));
    }
}

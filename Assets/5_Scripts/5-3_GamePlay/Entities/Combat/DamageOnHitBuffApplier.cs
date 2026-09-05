using UnityEngine;

/// <summary>
/// 命中附加 Buff 模块：监听指定伤害模块的实体命中结果，并按概率给有效命中的目标添加 Buff。
/// 该模块不参与伤害计算，也不会作用于格子建筑，因此可被燃烧、中毒、冰冻等武器效果复用。
/// </summary>
public sealed class DamageOnHitBuffApplier : Module, IItemModuleDependencyBinder
{
    [SerializeField, Tooltip("负责发布实体命中结果的伤害模块；Prefab 可显式绑定，JSON 组合由 ItemMods 绑定。")]
    private Mod_Damage damageModule;

    [SerializeField, Tooltip("命中成功后尝试添加的稳定 Buff ID。")]
    private string buffId = "燃烧";

    [SerializeField, Range(0f, 1f), Tooltip("每次有效实体命中附加 Buff 的概率。")]
    private float applicationChance = 0.25f;

    [SerializeField] private Ex_ModData_MemoryPackable moduleData = new();

    public override ModuleData _Data
    {
        get => moduleData;
        set => moduleData = (Ex_ModData_MemoryPackable)value;
    }

    public override string CanonicalModuleId => "Module_DamageOnHitBuff";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    /// <summary>从 ItemMods 唯一稳定 ID 绑定伤害模块，并校验 Prefab 显式引用没有漂移。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        Mod_Damage resolved = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");
        if (damageModule != null && damageModule != resolved)
            throw new System.InvalidOperationException($"{name} 的伤害引用与 ItemMods 注册结果不一致。");

        damageModule = resolved;
    }

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
        if (receiver == null ||
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
            buffManager.AddBuff(buffId.Trim());
    }
}

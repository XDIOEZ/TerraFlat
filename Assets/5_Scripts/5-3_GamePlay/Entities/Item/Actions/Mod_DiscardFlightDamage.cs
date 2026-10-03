using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using UnityEngine;

/// <summary>通用丢弃飞行伤害模块：玩家主动丢出物品时临时开启同物品的伤害模块。</summary>
public sealed class Mod_DiscardFlightDamage : Module, IItemModuleDependencyBinder, IDroppedItemSpawnContextReceiver
{
    #region 配置与依赖

    public const string PersistedModuleId = "Mod_DiscardFlightDamage";

    [SerializeField] private Ex_ModData_MemoryPackable moduleData = new();
    [SerializeField, Tooltip("负责飞行期间伤害结算的同物品伤害模块。")]
    private Mod_Damage damageModule;

    [Tooltip("命中足够坚硬目标后转换得到的物品 ID；留空表示不启用命中变化。")]
    public string HardImpactTransformItemId = "";

    [Range(0f, 1f), Tooltip("满足硬度门槛后发生命中变化的概率。")]
    public float HardImpactTransformChance;

    [Min(0f), Tooltip("目标对应伤害类型防御达到该数值才视为足够坚硬。")]
    public float HardImpactMinimumDefense = 2f;

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
    private bool hardImpactTransformPending;
    private long lastHardImpactTargetKey = long.MinValue;
    private double lastHardImpactTime = double.NegativeInfinity;

    #endregion

    #region Module 生命周期

    public override void Load()
    {
        if (damageModule == null)
            throw new MissingComponentException($"{name} 缺少 Mod_Damage 依赖。");

        damageModule.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        damageModule.OnReceiverDamageResolved += HandleReceiverDamageResolved;
        damageModule.OnExternalDamageResolved -= HandleExternalDamageResolved;
        damageModule.OnExternalDamageResolved += HandleExternalDamageResolved;
        ResetRuntimeState();
    }

    public override void Save()
    {
    }

    public override void Unload()
    {
        if (damageModule != null)
        {
            damageModule.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
            damageModule.OnExternalDamageResolved -= HandleExternalDamageResolved;
        }

        StopDamage();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (hardImpactTransformPending)
        {
            CompleteHardImpactTransform();
            return;
        }

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
        hardImpactTransformPending = false;
        lastHardImpactTargetKey = long.MinValue;
        lastHardImpactTime = double.NegativeInfinity;
    }

    #endregion

    #region 丢弃命中变化

    /// <summary>只有当前正在由玩家丢出的物品才允许触发命中变化。</summary>
    private void HandleReceiverDamageResolved(Mod_DamageReceiver receiver, float resolvedDamage)
    {
        if (!damageActive || resolvedDamage < 0f || receiver == null)
            return;

        TryQueueHardImpactTransform(
            ResolveImpactDefense(receiver.Defense),
            BuildHardImpactTargetKey(receiver.GetInstanceID(), false));
    }

    /// <summary>自然实体走纯数据防御查询，避免把具体资源类型写进模块。</summary>
    private void HandleExternalDamageResolved(CombatDamageContext context, float resolvedDamage)
    {
        if (!damageActive || resolvedDamage < 0f)
            return;

        CombatDamageKind kind = damageModule.ResolveDamageValues().DominantKind;
        if (!NaturalEntityEcsService.TryGetCombatDefenseAtPoint(
                context.HitPoint, kind, out float defense, out int runtimeId))
        {
            return;
        }

        TryQueueHardImpactTransform(defense, BuildHardImpactTargetKey(runtimeId, true));
    }

    private float ResolveImpactDefense(CombatDefense defense)
    {
        if (defense == null)
            return 0f;

        return defense.Physical;
    }

    /// <summary>特殊变化必须由物品配置显式开启，材质标签本身不再代表该行为。</summary>
    private bool TryQueueHardImpactTransform(float defense, long targetKey)
    {
        if (hardImpactTransformPending ||
            string.IsNullOrWhiteSpace(HardImpactTransformItemId) ||
            HardImpactTransformChance <= 0f ||
            defense < Mathf.Max(0f, HardImpactMinimumDefense))
        {
            return false;
        }

        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(HardImpactTransformItemId, out _))
            return false;

        double now = Time.timeAsDouble;
        if (targetKey == lastHardImpactTargetKey && now - lastHardImpactTime < 0.12d)
            return false;

        lastHardImpactTargetKey = targetKey;
        lastHardImpactTime = now;
        if (Random.value >= Mathf.Clamp01(HardImpactTransformChance))
            return false;

        hardImpactTransformPending = true;
        SuspendDamageForTransform();
        return true;
    }

    /// <summary>先结束伤害窗口，下一帧再替换物品，避免在伤害回调栈里回收当前实体。</summary>
    private void SuspendDamageForTransform()
    {
        if (damageActive && damageModule != null)
        {
            damageModule.StopAttack();
            damageModule.SetDeliveryCapabilities(CombatDeliveryCapabilities.None);
        }

        damageActive = false;
        armedByDiscard = false;
        if (item != null && item.Owner == thrower)
            item.Owner = null;
    }

    private void CompleteHardImpactTransform()
    {
        GameRes resources = GameRes.ExistingInstance;
        ItemMgr itemManager = ItemMgr.Instance;
        if (item == null || resources == null || itemManager == null ||
            !resources.TryGetItemDefinition(HardImpactTransformItemId, out _))
        {
            ResetRuntimeState();
            return;
        }

        ItemData replacement = resources.CreateItemData(HardImpactTransformItemId);
        replacement.Stack.Amount = 1f;
        replacement.Stack.CanBePickedUp = true;
        DroppedItemService.Spawn(
            replacement,
            item.transform.position,
            rotation: item.transform.eulerAngles.z);
        ResetRuntimeState();
        itemManager.DespawnItem(item, saveData: false);
    }

    private static long BuildHardImpactTargetKey(int id, bool external)
        => ((long)(external ? 2 : 1) << 32) | (uint)id;

    #endregion
}

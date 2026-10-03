using FlatWorld.Combat;
using UnityEngine;

/// <summary>石头只在玩家主动丢出的飞行阶段开启自身伤害，普通生成和落地状态都无伤害。</summary>
public sealed class OreStoneDiscardDamage : MonoBehaviour, IDroppedItemSpawnContextReceiver
{
    #region 运行时状态

    private Item item;
    private Mod_Damage damage;
    private Item thrower;
    private bool armedByDiscard;
    private bool damageActive;
    private bool missingDamageReported;

    #endregion

    #region 生命周期

    private void Awake()
    {
        item = GetComponent<Item>();
    }

    private void Update()
    {
        if (!armedByDiscard && !damageActive)
            return;

        if (item == null)
            item = GetComponent<Item>();

        if (armedByDiscard && Mod_Droping.IsDropInProgress(item))
        {
            TryStartDamage();
            return;
        }

        StopDamage();
    }

    private void OnDisable()
    {
        StopDamage();
    }

    #endregion

    #region 丢弃伤害

    /// <summary>只响应玩家主动 F 丢弃，战利品散落等普通生成不会激活伤害。</summary>
    public void OnDroppedItemSpawned(in DroppedItemSpawnContext context)
    {
        if (context.Reason != DroppedItemSpawnReason.PlayerDiscard || context.Duration <= 0f)
            return;

        thrower = context.SourceItem;
        armedByDiscard = true;
        TryStartDamage();
    }

    /// <summary>复用石头现有 Mod_Damage，让 GO 与 ECS 目标继续走正式伤害结算。</summary>
    private void TryStartDamage()
    {
        if (!armedByDiscard || damageActive || item == null || !Mod_Droping.IsDropInProgress(item))
            return;

        damage ??= item.itemMods?.GetMod_ByID("Mod_Damage") as Mod_Damage;
        if (damage == null)
        {
            if (!missingDamageReported)
            {
                missingDamageReported = true;
                Debug.LogError("[OreStoneDiscardDamage] 石头缺少 Mod_Damage，无法启用丢弃伤害。", this);
            }
            return;
        }

        item.Owner = thrower;
        damage.SetDeliveryCapabilities(CombatDeliveryCapabilities.Projectile | CombatDeliveryCapabilities.AirborneTargets);
        damage.StartAttack();
        damageActive = true;
    }

    /// <summary>落地后立即关闭伤害并解除临时投掷者归属，恢复普通可拾取石头。</summary>
    private void StopDamage()
    {
        if (damageActive && damage != null)
        {
            damage.StopAttack();
            damage.SetDeliveryCapabilities(CombatDeliveryCapabilities.None);
        }

        if (item != null && item.Owner == thrower)
            item.Owner = null;

        damageActive = false;
        armedByDiscard = false;
        thrower = null;
    }

    #endregion
}

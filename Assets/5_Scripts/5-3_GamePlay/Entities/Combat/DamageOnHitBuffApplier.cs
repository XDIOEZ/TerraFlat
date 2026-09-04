using UnityEngine;

/// <summary>
/// 命中附加 Buff 组件：监听指定伤害模块的实体命中结果，并按概率给有效命中的目标添加 Buff。
/// 该组件不参与伤害计算，也不会作用于格子建筑，因此可被燃烧、中毒、冰冻等武器效果复用。
/// </summary>
public sealed class DamageOnHitBuffApplier : MonoBehaviour
{
    [SerializeField, Tooltip("负责发布实体命中结果的伤害模块，必须在 Prefab 中显式绑定。")]
    private Mod_Damage damageModule;

    [SerializeField, Tooltip("命中成功后尝试添加的稳定 Buff ID。")]
    private string buffId = "燃烧";

    [SerializeField, Range(0f, 1f), Tooltip("每次有效实体命中附加 Buff 的概率。")]
    private float applicationChance = 0.25f;

    /// <summary>启用时订阅伤害模块的实体命中结果。</summary>
    private void OnEnable()
    {
        if (damageModule == null)
        {
            Debug.LogError($"{name} 未绑定 Mod_Damage，无法附加命中 Buff。", this);
            return;
        }

        damageModule.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        damageModule.OnReceiverDamageResolved += HandleReceiverDamageResolved;
    }

    /// <summary>禁用时解除订阅，兼容对象池复用。</summary>
    private void OnDisable()
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

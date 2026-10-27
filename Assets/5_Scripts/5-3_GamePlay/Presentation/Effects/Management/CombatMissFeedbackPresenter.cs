using FlatWorld.Localization;
using UnityEngine;

/// <summary>在攻击未命中的位置播放白色小字，复用伤害文字的对象池与弹出淡出动画。</summary>
[DisallowMultipleComponent]
public sealed class CombatMissFeedbackPresenter : MonoBehaviour
{
    #region 配置

    [SerializeField]
    private DamageTextEffect damageTextPrefab;

    [SerializeField, Min(0.1f)]
    private float textScale = 0.65f;

    [SerializeField, Min(1f)]
    private float textWidthMultiplier = 4f;

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        CombatFeedbackEvents.LogicalMissed -= HandleLogicalMissed;
        CombatFeedbackEvents.LogicalMissed += HandleLogicalMissed;
        CombatFeedbackEvents.EquipmentBroken -= HandleEquipmentBroken;
        CombatFeedbackEvents.EquipmentBroken += HandleEquipmentBroken;
    }

    private void OnDisable()
    {
        CombatFeedbackEvents.LogicalMissed -= HandleLogicalMissed;
        CombatFeedbackEvents.EquipmentBroken -= HandleEquipmentBroken;
    }

    #endregion

    #region 未命中飘字

    private void HandleLogicalMissed(Vector2 attackPosition) => ShowFeedback(attackPosition, "未命中");

    // 损坏拒绝复用同一飘字池，不让装备规则依赖 UI。
    private void HandleEquipmentBroken(Vector2 position) => ShowFeedback(position, "耐久耗尽，无法使用");

    private void ShowFeedback(Vector2 attackPosition, string text)
    {
        if (damageTextPrefab == null)
            return;

        // 每次播放按当前语言取词，文字颜色和缩放通过独立播放数据覆盖。
        var data = new DamageTextEffectData(0f)
        {
            UseTextOverride = true,
            TextOverride = FlatWorldLocalizationService.GetUiText(text),
            UseColorOverride = true,
            ColorOverride = Color.white,
            ScaleMultiplier = textScale,
            TextWidthMultiplier = textWidthMultiplier
        };

        WorldRenderingManager effectManager = WorldRenderingManager.Instance;
        GameEffect effect = effectManager != null
            ? effectManager.GetGameEffectFromPool(damageTextPrefab)
            : Instantiate(damageTextPrefab);

        if (effect == null)
            return;

        effect.transform.position = attackPosition;
        effect.Effect(null, data);
    }

    #endregion
}

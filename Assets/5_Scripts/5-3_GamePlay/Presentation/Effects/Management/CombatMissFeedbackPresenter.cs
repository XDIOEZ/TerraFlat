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
    }

    private void OnDisable()
    {
        CombatFeedbackEvents.LogicalMissed -= HandleLogicalMissed;
    }

    #endregion

    #region 未命中飘字

    private void HandleLogicalMissed(Vector2 attackPosition)
    {
        if (damageTextPrefab == null)
            return;

        // 每次播放按当前语言取词，文字颜色和缩放通过独立播放数据覆盖。
        var data = new DamageTextEffectData(0f)
        {
            UseTextOverride = true,
            TextOverride = FlatWorldLocalizationService.GetUiText("未命中"),
            UseColorOverride = true,
            ColorOverride = Color.white,
            ScaleMultiplier = textScale,
            TextWidthMultiplier = textWidthMultiplier
        };

        VisualEffectManager effectManager = VisualEffectManager.Instance;
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

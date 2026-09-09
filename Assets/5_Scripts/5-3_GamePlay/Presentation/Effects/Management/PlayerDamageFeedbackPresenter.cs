using FlatWorld.Localization;
using UnityEngine;

/// <summary>
/// 玩家受伤的世界空间文字反馈：复用 DamageReceiver 的权威伤害快照和身体部位命中结果，
/// 在玩家附近弹出实际伤害数字以及“某部位受到攻击”的小字，不参与任何伤害结算。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageReceiver))]
public sealed class PlayerDamageFeedbackPresenter : MonoBehaviour
{
    #region 配置

    [Header("伤害文字")]
    [SerializeField]
    private DamageTextEffect damageTextPrefab;

    [SerializeField]
    private Vector2 damageTextOffset = new Vector2(0f, 0.2f);

    [Header("部位文字")]
    [SerializeField]
    private Vector2 bodyPartTextOffset = new Vector2(0f, -0.28f);

    [SerializeField, Min(0.05f)]
    private float bodyPartLineSpacing = 0.22f;

    [SerializeField, Min(0.1f)]
    private float bodyPartTextScale = 0.6f;

    [SerializeField, Min(1f)]
    private float bodyPartTextWidthMultiplier = 6f;

    [SerializeField]
    private Color bodyPartTextColor = new Color(1f, 0.82f, 0.45f, 1f);

    #endregion

    #region 运行时

    private DamageReceiver receiver;

    private void Awake()
    {
        receiver = GetComponent<DamageReceiver>();
    }

    private void OnEnable()
    {
        if (receiver == null)
            receiver = GetComponent<DamageReceiver>();

        if (receiver == null)
            return;

        receiver.OnDamageReceived -= HandleDamageReceived;
        receiver.OnDamageReceived += HandleDamageReceived;
    }

    private void OnDisable()
    {
        if (receiver != null)
            receiver.OnDamageReceived -= HandleDamageReceived;
    }

    #endregion

    #region 受伤反馈

    /// <summary>每次实际扣血后显示伤害数字，再按本次命中结果显示一个或两个身体部位提示。</summary>
    private void HandleDamageReceived(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo == null || damageInfo.DamageValue <= 0f || damageTextPrefab == null)
            return;

        Vector3 anchor = damageInfo.HitPosition;
        SpawnText(anchor + (Vector3)damageTextOffset, BuildDamageNumberData(damageInfo));
        SpawnBodyPartTexts(anchor, damageInfo);
    }

    private void SpawnBodyPartTexts(Vector3 anchor, DamageReceiverDamageInfo damageInfo)
    {
        // 环境/脚本直伤会按全身分摊部位生命，不代表一次具体攻击命中，避免一次弹出整套部位提示。
        if (damageInfo.DamageSender == null ||
            damageInfo.BodyPartHits == null ||
            damageInfo.BodyPartHits.Count == 0)
        {
            return;
        }

        string previousSourceText = null;
        int visibleLineIndex = 0;

        for (int i = 0; i < damageInfo.BodyPartHits.Count; i++)
        {
            BodyPartDamageInfo hit = damageInfo.BodyPartHits[i];
            if (hit == null || hit.DamageValue <= 0f)
                continue;

            string sourceText = GetBodyPartDamageText(hit.Part);
            if (string.IsNullOrEmpty(sourceText) || sourceText == previousSourceText)
                continue;

            previousSourceText = sourceText;
            string localizedText = FlatWorldLocalizationService.GetUiText(sourceText);
            DamageTextEffectData data = new DamageTextEffectData(0f)
            {
                UseTextOverride = true,
                TextOverride = localizedText,
                UseColorOverride = true,
                ColorOverride = bodyPartTextColor,
                ScaleMultiplier = bodyPartTextScale,
                TextWidthMultiplier = bodyPartTextWidthMultiplier
            };

            Vector3 lineOffset = (Vector3)bodyPartTextOffset +
                                 Vector3.down * (bodyPartLineSpacing * visibleLineIndex);
            SpawnText(anchor + lineOffset, data);
            visibleLineIndex++;
        }
    }

    private void SpawnText(Vector3 position, DamageTextEffectData data)
    {
        VisualEffectManager effectManager = VisualEffectManager.Instance;
        GameEffect effect = effectManager != null
            ? effectManager.GetGameEffectFromPool(damageTextPrefab)
            : Instantiate(damageTextPrefab);

        if (effect == null)
            return;

        effect.transform.position = position;
        effect.Effect(transform, data);
    }

    #endregion

    #region 文本与样式

    private static DamageTextEffectData BuildDamageNumberData(DamageReceiverDamageInfo damageInfo)
    {
        DamageTextStyle style = DamageTextStyle.Normal;
        CombatDamage senderDamage = damageInfo.SenderDamageValues;
        if (senderDamage != null)
        {
            style = senderDamage.DominantKind switch
            {
                CombatDamageKind.Cutting => DamageTextStyle.Cutting,
                CombatDamageKind.Chopping => DamageTextStyle.Cutting,
                CombatDamageKind.Piercing => DamageTextStyle.Piercing,
                CombatDamageKind.Blunt => DamageTextStyle.Blunt,
                _ => DamageTextStyle.Normal
            };
        }

        return new DamageTextEffectData(damageInfo.DamageValue, style);
    }

    private static string GetBodyPartDamageText(BodyPartType bodyPart)
    {
        return bodyPart switch
        {
            BodyPartType.Head => "头部受到攻击",
            BodyPartType.Chest => "胸部受到攻击",
            BodyPartType.Abdomen => "腹部受到攻击",
            BodyPartType.LeftHand => "手部受到攻击",
            BodyPartType.RightHand => "手部受到攻击",
            BodyPartType.Pelvis => "骨盆受到攻击",
            BodyPartType.LeftLeg => "腿部受到攻击",
            BodyPartType.RightLeg => "腿部受到攻击",
            _ => null
        };
    }

    #endregion
}

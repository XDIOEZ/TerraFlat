using FlatWorld.Localization;
using TMPro;
using Unity.Profiling;

public partial class Mod_PlayerDeathState
{
    #region 死亡描述展示
    private static readonly ProfilerMarker DeathMessageMarker = new("FlatWorld.Player.Death.Message");

    private void SubscribeDeathMessageLocalization()
    {
        FlatWorldLocalizationService.LanguageChanged -= OnDeathMessageLanguageChanged;
        FlatWorldLocalizationService.LanguageChanged += OnDeathMessageLanguageChanged;
    }

    private void UnsubscribeDeathMessageLocalization() =>
        FlatWorldLocalizationService.LanguageChanged -= OnDeathMessageLanguageChanged;

    private void OnDeathMessageLanguageChanged(string _) => RefreshDeathMessage();

    /// <summary>死亡结算和语言切换只刷新已有面板，描述来源是权威血量锁存的死亡记录。</summary>
    private void RefreshDeathMessage()
    {
        using var marker = DeathMessageMarker.Auto();
        if (_dyingPanel == null || !_isInDyingState)
            return;

        DeathCauseRecord record = _damageReceiver?.Data?.DeathCause;
        DeathMessageCatalog catalog = DeathMessageCatalog.Current;
        string locale = FlatWorldLocalizationService.CurrentLocaleCode;
        string attacker = ResolveDeathItemName(record?.AttackerItemId, record?.AttackerName);
        string weapon = ResolveDeathItemName(record?.WeaponItemId, record?.WeaponName);
        string effect = ResolveDeathEffectName(catalog, record, locale);

        TextMeshProUGUI title = _dyingPanel.GetText("FWUI_Death标题");
        TextMeshProUGUI copy = _dyingPanel.GetText("FWUI_DeathCopy");
        SetDeathDynamicText(title, catalog.Format(record, catalog.GetVictim(locale), attacker, weapon, effect, locale));
        SetDeathDynamicText(copy, catalog.GetSubtitle(locale));
    }

    private static string ResolveDeathItemName(string itemId, string snapshotName)
    {
        if (!string.IsNullOrWhiteSpace(itemId) && GameRes.Instance != null &&
            GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
        {
            string name = definition.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return string.IsNullOrWhiteSpace(snapshotName) || snapshotName == itemId
            ? string.Empty : snapshotName;
    }

    private static string ResolveDeathEffectName(DeathMessageCatalog catalog, DeathCauseRecord record, string locale)
    {
        if (record == null)
            return string.Empty;
        BuffDefinition definition = GameRes.Instance != null && !string.IsNullOrWhiteSpace(record.EffectId)
            ? GameRes.Instance.GetBuffDefinition(record.EffectId) : null;
        string sourceName = definition?.DisplayName ?? record.EffectName;
        if (string.IsNullOrWhiteSpace(sourceName))
            return string.Empty;
        if (definition == null && sourceName == record.EffectId)
            return catalog.GetEffectName(record.EffectId, string.Empty, locale);
        string fallback = catalog.GetEffectName(record.EffectId, sourceName, locale);
        string localized = FlatWorldLocalizationService.GetUiText(sourceName);
        return localized != sourceName ? localized : fallback;
    }

    private static void SetDeathDynamicText(TMP_Text text, string value)
    {
        if (text == null)
            return;
        // 静态扫描得到的旧标题绑定不能在切语言时覆盖动态死因。
        LocalizedTextBinder binder = text.GetComponent<LocalizedTextBinder>();
        if (binder != null)
            binder.enabled = false;
        text.richText = false;
        text.text = value;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>死亡描述只消费已锁存的死因，用数据目录组合文案，不参与伤害或死亡结算。</summary>
public sealed class DeathMessageCatalog
{
    #region 目录加载与校验

    public const string ResourcePath = "GameConfig/Combat/death-messages";
    private static DeathMessageCatalog current;
    private static readonly Regex PlaceholderPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private readonly CatalogData data;

    public static DeathMessageCatalog Current => current ??= LoadBuiltIn();

    private DeathMessageCatalog(CatalogData data)
    {
        this.data = data;
        Validate();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => current = null;

    private static DeathMessageCatalog LoadBuiltIn()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null)
            throw new InvalidDataException($"死亡描述目录缺失：{ResourcePath}.json");
        return Parse(asset.text);
    }

    /// <summary>显式解析入口方便静态诊断，非法模板在加载时报告。</summary>
    public static DeathMessageCatalog Parse(string json)
    {
        CatalogData loaded = JsonConvert.DeserializeObject<CatalogData>(json,
            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
        if (loaded == null)
            throw new InvalidDataException("死亡描述目录为空。");
        return new DeathMessageCatalog(loaded);
    }

    private void Validate()
    {
        ValidateText(data.Victim, "victim");
        ValidateText(data.Subtitle, "subtitle");
        if (data.Messages == null || data.Messages.Count == 0)
            throw new InvalidDataException("死亡描述目录没有文案。");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        bool hasUnknown = false;
        foreach (MessageEntry entry in data.Messages)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.CauseId) ||
                entry.Variants == null || entry.Variants.Count < 2)
                throw new InvalidDataException("每组死亡描述需要原因 ID 和至少两条中英文文案。");
            entry.RequirementMask = ResolveRequirements(entry.Requires);
            if (!identities.Add(entry.CauseId + ":" + entry.RequirementMask))
                throw new InvalidDataException($"死亡描述条件重复：{entry.CauseId}");
            if (entry.CauseId == "unknown" && entry.RequirementMask == 0)
                hasUnknown = true;
            foreach (LocalizedText variant in entry.Variants)
            {
                ValidateText(variant, entry.CauseId);
                ValidateTemplate(variant.ZhCN, entry);
                ValidateTemplate(variant.En, entry);
            }
        }
        if (!hasUnknown)
            throw new InvalidDataException("死亡描述目录缺少 unknown 兜底。");
        foreach (KeyValuePair<string, LocalizedText> effect in data.EffectNames)
            ValidateText(effect.Value, effect.Key);
    }

    private static void ValidateText(LocalizedText value, string identity)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ZhCN) || string.IsNullOrWhiteSpace(value.En))
            throw new InvalidDataException($"死亡描述缺少中英文：{identity}");
    }

    private static void ValidateTemplate(string template, MessageEntry entry)
    {
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            string token = match.Groups[1].Value;
            if (token == "victim")
                continue;
            int required = TokenMask(token);
            if (required == 0 || (entry.RequirementMask & required) == 0)
                throw new InvalidDataException($"死亡描述包含未知或未声明占位符：{entry.CauseId}/{token}");
        }
    }

    private static int ResolveRequirements(List<string> required)
    {
        int result = 0;
        foreach (string token in required ?? new List<string>())
        {
            int mask = TokenMask(token);
            if (mask == 0 || (result & mask) != 0)
                throw new InvalidDataException($"死亡描述条件无效：{token}");
            result |= mask;
        }
        return result;
    }

    private static int TokenMask(string token) => token switch
    {
        "attacker" => 1,
        "weapon" => 2,
        "effect" => 4,
        _ => 0
    };

    #endregion

    #region 文案组合

    public string GetVictim(string localeCode) => data.Victim.Get(localeCode);
    public string GetSubtitle(string localeCode) => data.Subtitle.Get(localeCode);

    /// <summary>常用 Buff 名有双语兜底，自定义 Buff 仍优先由正式本地化表提供名称。</summary>
    public string GetEffectName(string effectId, string fallback, string localeCode)
    {
        if (!string.IsNullOrEmpty(effectId) && data.EffectNames.TryGetValue(effectId, out LocalizedText name))
            return name.Get(localeCode);
        return fallback ?? string.Empty;
    }

    /// <summary>同一死亡记录始终使用同一 Variant，缺失来源时只选满足实际信息的模板。</summary>
    public string Format(DeathCauseRecord record, string victim, string attacker, string weapon,
        string effect, string localeCode)
    {
        int available = (string.IsNullOrWhiteSpace(attacker) ? 0 : 1) |
                        (string.IsNullOrWhiteSpace(weapon) ? 0 : 2) |
                        (string.IsNullOrWhiteSpace(effect) ? 0 : 4);
        string causeId = string.IsNullOrWhiteSpace(record?.CauseId) ? "unknown" : record.CauseId;
        // 状态伤害保留造成状态的来源链，环境死因则保留自己的具体原因。
        if ((available & 4) != 0 && (available & 3) != 0)
            causeId = "effect";

        MessageEntry selected = SelectEntry(causeId, available) ?? SelectEntry("unknown", 0);
        uint variant = unchecked((uint)(record?.Variant ?? 0));
        string template = selected.Variants[(int)(variant % (uint)selected.Variants.Count)].Get(localeCode);
        return PlaceholderPattern.Replace(template, match => match.Groups[1].Value switch
        {
            "victim" => string.IsNullOrWhiteSpace(victim) ? GetVictim(localeCode) : victim,
            "attacker" => attacker,
            "weapon" => weapon,
            "effect" => effect,
            _ => string.Empty
        });
    }

    private MessageEntry SelectEntry(string causeId, int available)
    {
        MessageEntry selected = null;
        int bestSpecificity = -1;
        foreach (MessageEntry entry in data.Messages)
        {
            if (!string.Equals(entry.CauseId, causeId, StringComparison.Ordinal) ||
                (entry.RequirementMask & available) != entry.RequirementMask)
                continue;
            int specificity = (entry.RequirementMask & 1) + ((entry.RequirementMask >> 1) & 1) +
                              ((entry.RequirementMask >> 2) & 1);
            if (specificity <= bestSpecificity)
                continue;
            selected = entry;
            bestSpecificity = specificity;
        }
        return selected;
    }

    #endregion

    #region JSON 数据

    private sealed class CatalogData
    {
        public LocalizedText Victim;
        public LocalizedText Subtitle;
        public List<MessageEntry> Messages;
        public Dictionary<string, LocalizedText> EffectNames = new(StringComparer.Ordinal);
    }

    private sealed class MessageEntry
    {
        public string CauseId;
        public List<string> Requires;
        public List<LocalizedText> Variants;
        [JsonIgnore] public int RequirementMask;
    }

    private sealed class LocalizedText
    {
        public string ZhCN;
        public string En;
        public string Get(string localeCode) =>
            localeCode != null && localeCode.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? En : ZhCN;
    }

    #endregion
}

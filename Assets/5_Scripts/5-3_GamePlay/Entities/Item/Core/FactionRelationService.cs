using System;
using System.Collections.Generic;

/// <summary>
/// 统一处理实体的阵营/队伍身份与关系。
/// 关系只有敌对、中立、友好三种：同一非空阵营默认友好，显式注册关系优先，
/// 未配置的不同阵营默认敌对以兼容旧有战斗行为；阵营 ID 存在 ItemData 中，
/// 因此会随存档、服务端物品快照和 Actor/MOD 定义一起传递。
/// </summary>
public enum FactionRelation : byte
{
    Hostile = 0,
    Neutral = 1,
    Friendly = 2
}

/// <summary>阵营关系服务：提供运行时查询、网络状态修改和 MOD 关系注册入口。</summary>
public static class FactionRelationService
{
    #region 内置阵营

    public const string WolfFactionId = "wolves";
    public const string PlayerFactionId = "players";
    public const string NeutralFactionId = "neutral";

    #endregion

    #region 外部关系注册

    private sealed class RegisteredRelation
    {
        public string OwnerId;
        public FactionRelation Relation;
    }

    private static readonly Dictionary<string, RegisteredRelation> ExternalRelations =
        new Dictionary<string, RegisteredRelation>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 查询

    /// <summary>按两个实体查询当前关系；空攻击者代表环境伤害，不通过此方法判定。</summary>
    public static FactionRelation GetRelation(Item source, Item target)
    {
        if (source == null || target == null)
            return FactionRelation.Hostile;

        if (ReferenceEquals(source, target))
            return FactionRelation.Friendly;

        return GetRelation(GetFactionId(source), GetFactionId(target));
    }

    /// <summary>判断攻击者是否可以对目标造成实体伤害。</summary>
    public static bool CanAttack(Item source, Item target)
    {
        if (source == null)
            return target != null;

        if (target == null || ReferenceEquals(source, target))
            return false;

        return GetRelation(source, target) == FactionRelation.Hostile;
    }

    /// <summary>读取实体的显式阵营，并为旧狼和玩家对象提供兼容回退。</summary>
    public static string GetFactionId(Item item)
    {
        if (item?.itemData == null)
            return string.Empty;

        string configuredFaction = item.itemData.FactionId?.Trim();
        if (!string.IsNullOrEmpty(configuredFaction))
            return configuredFaction;

        if (item is Player || HasTag(item, "Player"))
            return PlayerFactionId;

        // 旧存档和旧 Prefab 可能没有 FactionId，但正式狼仍带有 Wolf 标签。
        if (HasTag(item, "Wolf") ||
            string.Equals(item.itemData.IDName, "Wolf", StringComparison.OrdinalIgnoreCase))
        {
            return WolfFactionId;
        }

        return string.Empty;
    }

    /// <summary>按阵营 ID 查询关系；同阵营友好，neutral 与其他阵营默认中立，未知组合默认敌对。</summary>
    public static FactionRelation GetRelation(string leftFactionId, string rightFactionId)
    {
        string left = NormalizeFactionId(leftFactionId, allowEmpty: true);
        string right = NormalizeFactionId(rightFactionId, allowEmpty: true);

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return FactionRelation.Hostile;

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return FactionRelation.Friendly;

        string key = BuildRelationKey(left, right);
        if (ExternalRelations.TryGetValue(key, out RegisteredRelation registered))
            return registered.Relation;

        if (string.Equals(left, NeutralFactionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(right, NeutralFactionId, StringComparison.OrdinalIgnoreCase))
        {
            return FactionRelation.Neutral;
        }

        return FactionRelation.Hostile;
    }

    /// <summary>将关系枚举转换为 MOD API 使用的稳定英文值。</summary>
    public static string GetRelationName(FactionRelation relation)
    {
        return relation switch
        {
            FactionRelation.Neutral => "neutral",
            FactionRelation.Friendly => "friendly",
            _ => "hostile"
        };
    }

    /// <summary>解析 MOD 配置中的英文或中文关系名称。</summary>
    public static bool TryParseRelation(string value, out FactionRelation relation)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "hostile":
            case "enemy":
            case "敌对":
                relation = FactionRelation.Hostile;
                return true;
            case "neutral":
            case "中立":
                relation = FactionRelation.Neutral;
                return true;
            case "friendly":
            case "ally":
            case "友好":
                relation = FactionRelation.Friendly;
                return true;
            default:
                relation = FactionRelation.Hostile;
                return false;
        }
    }

    #endregion

    #region 运行时修改

    /// <summary>修改实体阵营并通知现有物品网络桥同步权威状态。</summary>
    public static bool TrySetFactionId(Item item, string factionId)
    {
        if (item?.itemData == null)
            return false;

        string normalizedFactionId = NormalizeFactionId(factionId, allowEmpty: true);
        if (string.Equals(item.itemData.FactionId, normalizedFactionId, StringComparison.Ordinal))
            return true;

        item.itemData.FactionId = normalizedFactionId;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        return true;
    }

    /// <summary>注册由指定 MOD 所属的对称阵营关系；同一阵营的友好关系无需注册。</summary>
    public static void RegisterExternalRelation(
        string ownerId,
        string leftFactionId,
        string rightFactionId,
        FactionRelation relation)
    {
        string owner = NormalizeFactionId(ownerId, allowEmpty: false);
        string left = NormalizeFactionId(leftFactionId, allowEmpty: false);
        string right = NormalizeFactionId(rightFactionId, allowEmpty: false);
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("不能为同一阵营注册跨阵营关系。", nameof(rightFactionId));

        string key = BuildRelationKey(left, right);
        if (ExternalRelations.TryGetValue(key, out RegisteredRelation existing) &&
            !string.Equals(existing.OwnerId, owner, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"阵营关系 {leftFactionId}/{rightFactionId} 已由 MOD {existing.OwnerId} 注册。");
        }

        ExternalRelations[key] = new RegisteredRelation
        {
            OwnerId = owner,
            Relation = relation
        };
    }

    /// <summary>卸载指定 MOD 的关系，避免重载 MOD 后残留旧规则。</summary>
    public static void ClearExternalRelations(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return;

        string owner = ownerId.Trim();
        List<string> keysToRemove = new List<string>();
        foreach (KeyValuePair<string, RegisteredRelation> pair in ExternalRelations)
        {
            if (string.Equals(pair.Value?.OwnerId, owner, StringComparison.OrdinalIgnoreCase))
                keysToRemove.Add(pair.Key);
        }

        for (int i = 0; i < keysToRemove.Count; i++)
            ExternalRelations.Remove(keysToRemove[i]);
    }

    /// <summary>清理全部外部关系，供 MOD 运行时整体卸载使用。</summary>
    public static void ClearExternalRelations()
    {
        ExternalRelations.Clear();
    }

    #endregion

    #region 私有方法

    /// <summary>读取兼容标签。</summary>
    private static bool HasTag(Item item, string tag)
    {
        List<string> tags = item?.itemData?.Tags;
        if (tags == null)
            return false;

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>标准化并校验阵营 ID。</summary>
    private static string NormalizeFactionId(string factionId, bool allowEmpty)
    {
        string normalized = factionId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            if (allowEmpty)
                return string.Empty;

            throw new ArgumentException("阵营 ID 不能为空。", nameof(factionId));
        }

        if (normalized.Length > 96)
            throw new ArgumentException("阵营 ID 不能超过 96 个字符。", nameof(factionId));

        for (int i = 0; i < normalized.Length; i++)
        {
            if (char.IsControl(normalized[i]))
                throw new ArgumentException("阵营 ID 不能包含控制字符。", nameof(factionId));
        }

        return normalized;
    }

    /// <summary>构造不区分左右顺序的关系键。</summary>
    private static string BuildRelationKey(string left, string right)
    {
        int comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return comparison <= 0 ? left + "\u001f" + right : right + "\u001f" + left;
    }

    #endregion
}

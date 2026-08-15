using System;
using System.Linq;
using UnityEngine;

#region MOD 公共 API

/// <summary>
/// Lua MOD 可访问的受限游戏接口。不直接暴露管理器、文件系统或 UnityEngine API。
/// </summary>
public sealed class ModApi
{
    private readonly ModRuntimeManager manager;

    internal ModApi(ModRuntimeManager manager, string modId)
    {
        this.manager = manager;
        ModId = modId;
    }

    public string ModId { get; }
    public string GameVersion => Application.version;

    public void Log(string message)
    {
        Debug.Log($"[MOD:{ModId}] {message}");
    }

    public void LogWarning(string message)
    {
        Debug.LogWarning($"[MOD:{ModId}] {message}");
    }

    public bool HasContent(string contentId)
    {
        return GameRes.Instance != null && GameRes.Instance.GetPrefab(contentId, false) != null;
    }

    public bool IsModLoaded(string modId)
    {
        return manager.IsModLoaded(modId);
    }

    public string GetModVersion(string modId)
    {
        return manager.GetModVersion(modId);
    }

    public string GetDefinitionInfoJson(string contentId)
    {
        return manager.GetDefinitionInfoJson(contentId);
    }

    public void EmitEvent(string eventName, string payloadJson = "{}")
    {
        manager.EmitModEvent(ModId, eventName, payloadJson);
    }

    public string Translate(string key, string fallback = "")
    {
        string normalized = string.IsNullOrWhiteSpace(key) || key.Contains(":", StringComparison.Ordinal)
            ? key
            : $"{ModId}:{key}";
        return ModLocalizationRegistry.Translate(normalized, fallback);
    }

    public string GetSettingJson(string settingId)
    {
        return ModSettingsRegistry.GetJson(ModId, settingId);
    }

    public bool GetBoolSetting(string settingId, bool fallback = false)
    {
        return ModSettingsRegistry.GetBool(ModId, settingId, fallback);
    }

    public double GetNumberSetting(string settingId, double fallback = 0d)
    {
        return ModSettingsRegistry.GetNumber(ModId, settingId, fallback);
    }

    public string GetStringSetting(string settingId, string fallback = "")
    {
        return ModSettingsRegistry.GetString(ModId, settingId, fallback);
    }

    /// <summary>为 MOD 注册对称的三态阵营关系，关系内容随 MOD 集合确定。</summary>
    public void RegisterFactionRelation(
        string leftFactionId,
        string rightFactionId,
        string relation)
    {
        if (!FactionRelationService.TryParseRelation(relation, out FactionRelation parsedRelation))
            throw new ArgumentException($"无效的阵营关系：{relation}", nameof(relation));

        FactionRelationService.RegisterExternalRelation(
            ModId,
            leftFactionId,
            rightFactionId,
            parsedRelation);
    }

    /// <summary>读取两个阵营的当前关系，返回 hostile、neutral 或 friendly。</summary>
    public string GetFactionRelation(string leftFactionId, string rightFactionId)
    {
        return FactionRelationService.GetRelationName(
            FactionRelationService.GetRelation(leftFactionId, rightFactionId));
    }

    public void SetClientSettingJson(string settingId, string jsonValue)
    {
        ModSettingsRegistry.SetClientValue(ModId, settingId, jsonValue);
    }

    public int SpawnItem(string itemId, float x, float y)
    {
        manager.EnsureWorldMutationAllowed("SpawnItem");
        if (ItemMgr.Instance == null)
            throw new InvalidOperationException("ItemMgr 尚未就绪");

        Item item = ItemMgr.Instance.InstantiateItem(itemId, new Vector3(x, y, 0f));
        return item?.itemData?.Guid ?? 0;
    }

    public string GetGlobalState()
    {
        return manager.GetGlobalState(ModId);
    }

    public void SetGlobalState(string json)
    {
        manager.SetGlobalState(ModId, json);
    }
}

/// <summary>
/// Lua 物品模块可访问的受限物品接口。
/// </summary>
public sealed class ModItemApi
{
    private readonly Item item;

    internal ModItemApi(Item item)
    {
        this.item = item;
    }

    public string Id => item?.itemData?.IDName ?? string.Empty;
    public int Guid => item?.itemData?.Guid ?? 0;
    public float Durability => item?.itemData?.Durability ?? 0f;
    public float MaxDurability => item?.itemData?.MaxDurability ?? 0f;
    public float X => item != null ? item.transform.position.x : 0f;
    public float Y => item != null ? item.transform.position.y : 0f;
    public bool IsActor => item != null && item.GetComponentsInChildren<MonoBehaviour>(true)
        .Any(component => component is IAIActor);
    public float Health => item?.GetComponentInChildren<DamageReceiver>(true)?.Hp ?? 0f;
    public float MaxHealth => item?.GetComponentInChildren<DamageReceiver>(true)?.MaxHp ?? 0f;
    public string FactionId => FactionRelationService.GetFactionId(item);

    /// <summary>在服务端权限允许时修改当前物品的阵营并触发联机状态同步。</summary>
    public bool SetFactionId(string factionId)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("SetFactionId");
        return FactionRelationService.TrySetFactionId(item, factionId);
    }

    public void AddDurability(float amount)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("AddDurability");
        item?.itemData?.AddDurability(amount);
    }

    public void Act()
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("Act");
        item?.OnAct?.Invoke();
    }

    /// <summary>让带 Mover_AI 的 Actor 前往世界坐标；基础状态机仍可在后续 Tick 覆盖目标。</summary>
    public bool MoveTo(float x, float y, bool forceRepath = false)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("ActorMoveTo");
        Mover_AI mover = item?.GetComponentInChildren<Mover_AI>(true);
        if (mover == null)
            return false;
        mover.SetDestination(new Vector2(x, y), forceRepath);
        return true;
    }

    public bool StopMoving()
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("ActorStopMoving");
        Mover_AI mover = item?.GetComponentInChildren<Mover_AI>(true);
        if (mover == null)
            return false;
        mover.StopMovement();
        return true;
    }
}

#endregion

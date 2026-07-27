using System;
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
}

#endregion

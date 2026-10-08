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

    /// <summary>判断本体或任意已加载 MOD 是否注册了指定液体；裸 ID 自动归属当前 MOD。</summary>
    public bool HasLiquidDefinition(string liquidId)
    {
        return GameRes.ExistingInstance?.GetLiquidDefinition(ResolveLiquidId(liquidId)) != null;
    }

    /// <summary>判断本体或任意已加载 MOD 是否注册了指定污染指标。</summary>
    public bool HasContaminationDefinition(string contaminationId)
    {
        string id = ResolveContaminationId(contaminationId);
        return GameRes.ExistingInstance?.GetContaminationDefinition(id) != null;
    }

    /// <summary>读取已加载世界地格的污染值；未知定义或未加载区块会抛出明确错误。</summary>
    public double GetContaminationValue(string contaminationId, float x, float y)
    {
        string id = ResolveContaminationId(contaminationId);
        if (!ContaminationSystem.TryGetValue(new Vector2(x, y), id, out float value))
            throw new InvalidOperationException($"无法读取污染值，定义不存在或地格未加载：{id} @ ({x},{y})");
        return value;
    }

    /// <summary>设置已加载世界地格的污染值；无命名空间 ID 自动归属当前 MOD。</summary>
    public bool SetContaminationValue(string contaminationId, float x, float y, float value)
    {
        manager.EnsureWorldMutationAllowed("SetContaminationValue");
        return ContaminationSystem.TrySetValue(
            new Vector2(x, y),
            ResolveContaminationId(contaminationId),
            value);
    }

    /// <summary>增减已加载世界地格的污染负荷。</summary>
    public bool AddContaminationValue(string contaminationId, float x, float y, float delta)
    {
        manager.EnsureWorldMutationAllowed("AddContaminationValue");
        return ContaminationSystem.TryAddValue(
            new Vector2(x, y),
            ResolveContaminationId(contaminationId),
            delta);
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

        // MOD 按物种路由生成生物，GUID 接口同时支持 GameObject 与 ECS。
        if (GameRes.Instance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) &&
            definition.IsActor)
        {
            if (!AiRuntimeBackendService.TrySpawnDirect(itemId, new Vector3(x, y, 0f), 0, out int actorGuid))
                throw new InvalidOperationException($"生物生成失败：{itemId}");
            return actorGuid;
        }
        Item item = ItemMgr.Instance.InstantiateItem(itemId, new Vector3(x, y, 0f));
        return item?.itemData?.Guid ?? 0;
    }

    /// <summary>MOD 用稳定 GUID 查询实际生物后端。</summary>
    public bool IsActorAlive(int actorGuid)
    {
        return AiRuntimeBackendService.TryGetActor(actorGuid, out _, out bool alive) && alive;
    }

    public bool AdvanceActorToItem(int actorGuid, int targetItemGuid,
        float arrivalDistance = 1.25f, bool attackActorsOnRoute = false)
    {
        manager.EnsureWorldMutationAllowed("AdvanceActorToItem");
        Item target = ItemMgr.Instance?.GetItemByGuid(targetItemGuid);
        if (target == null || target.DestructionHandled)
            return false;
        return AiRuntimeBackendService.TrySetAdvanceCommand(actorGuid,
            new AIAdvanceCommand(targetItemGuid, target.transform.position,
                arrivalDistance, attackActorsOnRoute));
    }

    public bool StopActorAdvance(int actorGuid)
    {
        manager.EnsureWorldMutationAllowed("StopActorAdvance");
        return AiRuntimeBackendService.TryClearAdvanceCommand(actorGuid);
    }

    public bool DespawnActor(int actorGuid)
    {
        manager.EnsureWorldMutationAllowed("DespawnActor");
        return AiRuntimeBackendService.TryDespawnActor(actorGuid);
    }

    public string GetGlobalState()
    {
        return manager.GetGlobalState(ModId);
    }

    public void SetGlobalState(string json)
    {
        manager.SetGlobalState(ModId, json);
    }

    /// <summary>MOD API 中裸污染 ID 默认使用当前 MOD 命名空间，同时允许显式引用 core 或依赖 MOD。</summary>
    private string ResolveContaminationId(string contaminationId)
    {
        if (string.IsNullOrWhiteSpace(contaminationId))
            throw new ArgumentException("污染 ID 不能为空", nameof(contaminationId));
        string trimmed = contaminationId.Trim();
        return trimmed.Contains(":", StringComparison.Ordinal) ? trimmed : $"{ModId}:{trimmed}";
    }

    /// <summary>MOD API 中裸液体 ID 默认使用当前 MOD 命名空间，同时允许显式引用 core 或依赖 MOD。</summary>
    private string ResolveLiquidId(string liquidId)
    {
        if (string.IsNullOrWhiteSpace(liquidId))
            throw new ArgumentException("液体 ID 不能为空", nameof(liquidId));
        string trimmed = liquidId.Trim();
        return trimmed.Contains(":", StringComparison.Ordinal) ? trimmed : $"{ModId}:{trimmed}";
    }
}

/// <summary>
/// Lua 物品模块可访问的受限物品接口。
/// </summary>
public sealed partial class ModItemApi
{
    private readonly Item item;
    private readonly uint runtimeGeneration;

    internal ModItemApi(Item item)
    {
        this.item = item;
        runtimeGeneration = item != null ? item.RuntimeGeneration : 0;
    }

    public string Id => item?.itemData?.IDName ?? string.Empty;
    public int Guid => item?.itemData?.Guid ?? 0;
    public float Durability => item?.itemData?.Durability ?? 0f;
    public float MaxDurability => item?.itemData?.MaxDurability ?? 0f;
    public float X => item != null ? item.transform.position.x : 0f;
    public float Y => item != null ? item.transform.position.y : 0f;
    public bool IsActor => AiRuntimeBackendService.TryGetGameObjectActor(item, out _);
    public float Health => item?.GetComponentInChildren<Mod_DamageReceiver>(true)?.Hp ?? 0f;
    public float MaxHealth => item?.GetComponentInChildren<Mod_DamageReceiver>(true)?.MaxHp ?? 0f;
    public string FactionId => FactionRelationService.GetFactionId(item);
    public bool IsLiquidContainer => GetLiquidContainer() != null;
    public string LiquidId => GetLiquidContainer()?.Data?.LiquidId ?? string.Empty;
    /// <summary>兼容旧 MOD 的完整份数视图；小数余量请读取 LiquidAmountExact。</summary>
    public int LiquidAmount => Mathf.FloorToInt((GetLiquidContainer()?.Data?.Amount ?? 0f) + Mod_WaterVessel.AmountEpsilon);
    public float LiquidAmountExact => GetLiquidContainer()?.Data?.Amount ?? 0f;
    public int LiquidCapacity => GetLiquidContainer()?.Capacity ?? 0;

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

    /// <summary>向当前物品的通用液体容器加入已注册液体，返回实际加入份数。</summary>
    public int AddLiquid(string liquidId, int amount)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("AddLiquid");
        if (string.IsNullOrWhiteSpace(liquidId))
            throw new ArgumentException("液体 ID 不能为空", nameof(liquidId));
        return GetLiquidContainer()?.AddLiquid(liquidId.Trim(), amount) ?? 0;
    }

    /// <summary>从当前物品的通用液体容器移除指定份数。</summary>
    public int RemoveLiquid(int amount)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("RemoveLiquid");
        return GetLiquidContainer()?.RemoveLiquid(amount) ?? 0;
    }

    /// <summary>清空当前物品的通用液体容器。</summary>
    public bool ClearLiquid()
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("ClearLiquid");
        return GetLiquidContainer()?.ClearContents() == true;
    }

    /// <summary>直接控制 GameObject Actor 的移动模块，状态机仍可在下一 Tick 选择新目标。</summary>
    public bool MoveTo(float x, float y, bool forceRepath = false)
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("ActorMoveTo");
        Mod_Mover_AI mover = item?.GetComponentInChildren<Mod_Mover_AI>(true);
        if (mover == null) return false;
        mover.SetDestination(new Vector2(x, y), forceRepath);
        return true;
    }

    public bool StopMoving()
    {
        ModRuntimeManager.Instance?.EnsureWorldMutationAllowed("ActorStopMoving");
        Mod_Mover_AI mover = item?.GetComponentInChildren<Mod_Mover_AI>(true);
        if (mover == null) return false;
        mover.StopMovement();
        return true;
    }

    private Mod_WaterVessel GetLiquidContainer() =>
        item?.itemMods?.GetMod_ByID<Mod_WaterVessel>(Mod_WaterVessel.ModuleId);
}

#endregion

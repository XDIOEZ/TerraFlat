using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>真实物品生命周期、MOD Lua 与网络副本共用正式加载链，全部只操作自有实例。</summary>
public static class GameplayTestItemPipelineRuntimeProbe
{
    #region 操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.RegisterAsync("item_runtime", "loaded_lifecycle", CaptureLifecycle);
        GameplayTestRuntimeProbe.Register("modding", "live_lua_module", CaptureLuaModule);
        GameplayTestRuntimeProbe.Register("networking", "live_replica", CaptureReplica);
    }
    #endregion

    #region 自有实例和模块状态
    private static Item Spawn(Player player) => LoadSpawned(ItemMgr.Instance.InstantiateItem("Knife_Flint",
        player.transform.position + new Vector3(1.5f, 0f, 0f)));

    private static Item SpawnHeld(Player player, ItemData data) =>
        LoadSpawned(ItemMgr.Instance.InstantiateHeldItem(data, player, player.transform));

    private static Item LoadSpawned(Item item)
    {
        try
        {
            // ItemMgr 负责装配和登记，模块运行态须由正式 Item.Load 建立。
            item.Load();
            return item;
        }
        catch
        {
            if (item != null && !item.DestructionHandled)
                ItemMgr.Instance.DespawnItem(item, saveData: false);
            throw;
        }
    }

    private static JObject State(Item item, Player player) => new()
    {
        ["initialized"] = item.IsInitialized, ["active"] = item.gameObject.activeInHierarchy,
        ["destructionHandled"] = item.DestructionHandled, ["inPool"] = item.IsInPool,
        ["inHand"] = item.InHand, ["ownedByPlayer"] = ReferenceEquals(item.Owner, player),
        ["guid"] = item.itemData.Guid, ["generation"] = item.RuntimeGeneration,
        ["registered"] = ItemMgr.Instance.IsRuntimeItemRegistered(item),
        ["indexed"] = ReferenceEquals(ItemMgr.Instance.GetItemByGuid(item.itemData.Guid), item),
        ["modules"] = item.Mods.Count,
        ["moduleDataCount"] = item.itemData.ModuleDataDic?.Count ?? 0,
        ["enabledModules"] = item.Mods.Values.Count(module => module != null && module.Enabled),
        ["loadedModules"] = item.Mods.Values.Count(module => module != null && module.IsRuntimeLoaded),
        ["stableNamesUnique"] = item.Mods.Values.Select(module => module._Data.StableName).Distinct().Count() == item.Mods.Count
    };

    private static void Despawn(ref Item item)
    {
        Item owned = item;
        item = null;
        if (owned != null) ItemMgr.Instance.DespawnItem(owned, saveData: false);
    }
    #endregion

    #region 正式加载卸载与重新生成
    private static IEnumerator CaptureLifecycle(Player player, JObject args, Action<JObject> completed)
    {
        Item first = null, second = null;
        int registered = 0, unregistered = 0;
        ItemData firstData = null;
        void OnRegistered(Item value) { if (ReferenceEquals(value.itemData, firstData)) registered++; }
        void OnUnregistered(Item value) { if (ReferenceEquals(value.itemData, firstData)) unregistered++; }
        ItemMgr.RuntimeItemRegistered += OnRegistered;
        ItemMgr.RuntimeItemUnregistered += OnUnregistered;
        var result = new JObject();
        try
        {
            firstData = GameRes.ExistingInstance.CreateItemData("Knife_Flint");
            // 生命周期跨帧采样使用正式手持入口，避免普通世界散落物按规则转为轻量掉落。
            first = SpawnHeld(player, firstData);
            int firstGuid = first.itemData.Guid;
            uint generation = first.RuntimeGeneration;
            result["spawned"] = State(first, player);
            yield return null;
            result["afterFrame"] = State(first, player);
            first.ModuleSave();
            result["saveKeptIdentity"] = ReferenceEquals(first.itemData, firstData) && first.RuntimeGeneration == generation;
            Despawn(ref first);
            result["firstRemoved"] = ItemMgr.Instance.GetItemByGuid(firstGuid) == null;
            result["registeredEvents"] = registered;
            result["unregisteredEvents"] = unregistered;
            second = SpawnHeld(player, GameRes.ExistingInstance.CreateItemData("Knife_Flint"));
            result["second"] = State(second, player);
            result["newGeneration"] = second.RuntimeGeneration != generation;
            result["independentData"] = !ReferenceEquals(second.itemData, firstData);
            Despawn(ref second);
            result["secondRemoved"] = ItemMgr.Instance.GetItemByGuid(result["second"].Value<int>("guid")) == null;
            completed(result);
        }
        finally
        {
            ItemMgr.RuntimeItemRegistered -= OnRegistered;
            ItemMgr.RuntimeItemUnregistered -= OnUnregistered;
            try { Despawn(ref second); }
            finally { Despawn(ref first); }
        }
    }
    #endregion

    #region MOD Lua 对真实物品的生命周期调用
    private static JObject CaptureLuaModule(Player player, JObject args)
    {
        string root = GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        string directory = Path.Combine(root, "LuaModuleProbes", Guid.NewGuid().ToString("N"));
        string parent = Path.GetDirectoryName(directory);
        if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("测试 Lua 包目录不允许目录联接。");
        Directory.CreateDirectory(directory);
        Item item = null;
        ModLuaRuntime runtime = null;
        var result = new JObject();
        try
        {
            File.WriteAllText(Path.Combine(directory, "main.lua"), @"
local value = 'new'
local callbacks = {}
callbacks.OnLoad = function(api) value = 'loaded' end
callbacks.OnLoadSave = function(api, state) value = state end
callbacks.OnEvent = function(api, name, payload)
  value = value .. ':' .. name
  callbacks.OnEvent = function(api, nextName, nextPayload)
    value = value .. ':replacement:' .. nextName
  end
end
callbacks.OnSave = function(api, previous) return value end
return callbacks");
            File.WriteAllText(Path.Combine(directory, "module.lua"), @"
local callbacks = {}
callbacks.OnLoad = function(item, state, dt, api) item:AddDurability(-item.MaxDurability * .2); return 'loaded:' .. item.Id end
callbacks.OnUpdate = function(item, state, dt, api)
  item:AddDurability(-item.MaxDurability * dt)
  callbacks.OnUpdate = function(nextItem, nextState, nextDt, nextApi)
    nextItem:AddDurability(-nextItem.MaxDurability * nextDt * .5)
    return nextState .. ':updated_again'
  end
  return state .. ':updated'
end
callbacks.OnUnload = function(item, state, dt, api) item:AddDurability(item.MaxDurability * .05); return state .. ':unloaded' end
return callbacks");
            item = Spawn(player);
            item.itemData.Durability = item.itemData.MaxDurability;
            result["maximum"] = item.itemData.MaxDurability;
            runtime = new ModLuaRuntime("lua-playmode-probe", directory, new ModApi(ModRuntimeManager.Instance, "lua-playmode-probe"));
            runtime.LoadMain("main.lua");
            result["mainLoaded"] = runtime.CaptureGlobalState("");
            runtime.RestoreGlobalState("restored");
            runtime.InvokeEvent("probe.event", "{}");
            result["mainState"] = runtime.CaptureGlobalState("");
            runtime.InvokeEvent("probe.second", "{}");
            result["mainAfterReplacement"] = runtime.CaptureGlobalState("");
            string state = runtime.InvokeModule("module.lua", "OnLoad", item, "");
            result["afterLoad"] = item.itemData.Durability;
            state = runtime.InvokeModule("module.lua", "OnUpdate", item, state, .1f);
            result["afterUpdate"] = item.itemData.Durability;
            state = runtime.InvokeModule("module.lua", "OnUpdate", item, state, .1f);
            result["afterReplacedUpdate"] = item.itemData.Durability;
            result["missingHandlerState"] = runtime.InvokeModule("module.lua", "OnUnused", item, "unchanged");
            state = runtime.InvokeModule("module.lua", "OnUnload", item, state);
            result["afterUnload"] = item.itemData.Durability;
            result["state"] = state;
            result["registeredAfterCallbacks"] = ItemMgr.Instance.IsRuntimeItemRegistered(item);
            return result;
        }
        finally
        {
            try { runtime?.Dispose(); }
            finally
            {
                try { Despawn(ref item); }
                finally
                {
                    // 此目录由本探针创建且已经校验属于本轮隔离存档。
                    Directory.Delete(directory, recursive: true);
                    result["packageRemoved"] = !Directory.Exists(directory);
                }
            }
        }
    }
    #endregion

    #region 真实网络负载与远程副本权威隔离
    private static JObject CaptureReplica(Player player, JObject args)
    {
        Item source = null, replica = null;
        var result = new JObject();
        try
        {
            source = Spawn(player);
            source.itemData.Durability = source.itemData.MaxDurability * .75f;
            source.itemData.ItemSpecialData = "{\"luaReplica\":42}";
            uint generation = source.RuntimeGeneration;
            byte[] payload = ItemNetworkStateSerialization.Capture(source, ignoreTransform: true);
            bool identity = ItemNetworkStateSerialization.TryReadIdentity(payload, out int guid, out string id);
            result["identityRead"] = identity; result["payloadGuid"] = guid; result["sourceGuid"] = source.itemData.Guid;
            result["itemId"] = id; result["bytes"] = payload.Length;
            result["sourcePreserved"] = source.RuntimeGeneration == generation && ItemMgr.Instance.IsRuntimeItemRegistered(source);
            // 在同进程中给副本独立 GUID，避免与仍在运行的权威物品冲突。
            replica = ItemMgr.Instance.InstantiateNetworkItem("Knife_Flint", ItemMgr.Instance.GenerateGuid(),
                player.transform.position + Vector3.left * 1.5f, Quaternion.identity, Vector3.one);
            int replicaGuid = replica.itemData.Guid;
            ItemMgr.Instance.MarkAsRemoteVisualOnly(replica);
            bool applied = ItemNetworkStateSerialization.ApplyRemoteReplica(replica, payload, null);
            result["applied"] = applied;
            result["replicaGuidPreserved"] = replica.itemData.Guid == replicaGuid;
            result["replicaDurability"] = replica.itemData.Durability;
            result["sourceDurability"] = source.itemData.Durability;
            result["specialData"] = replica.itemData.ItemSpecialData;
            result["replicaRegistered"] = ItemMgr.Instance.IsRuntimeItemRegistered(replica);
            result["replicaIndexed"] = ReferenceEquals(ItemMgr.Instance.GetItemByGuid(replicaGuid), replica);
            result["replicaActive"] = replica.gameObject.activeInHierarchy;
            var corrupted = (byte[])payload.Clone();
            corrupted[0] ^= 0xFF;
            result["corruptedApplied"] = ItemNetworkStateSerialization.ApplyRemoteReplica(replica, corrupted, null);
            result["afterRejectedDurability"] = replica.itemData.Durability;
            result["afterRejectedGuid"] = replica.itemData.Guid;
            result["expectedReplicaGuid"] = replicaGuid;
            return result;
        }
        finally
        {
            try { Despawn(ref replica); }
            finally { Despawn(ref source); }
        }
    }
    #endregion
}

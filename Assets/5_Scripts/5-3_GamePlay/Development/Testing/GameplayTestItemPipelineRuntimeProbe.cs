using System;
using System.Collections;
using System.Collections.Generic;
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
        GameplayTestRuntimeProbe.Register("item_runtime", "center_spatial_query", CaptureCenterSpatialQuery);
        GameplayTestRuntimeProbe.RegisterAsync("combat", "held_weapon_action", CaptureHeldWeaponAction);
        GameplayTestRuntimeProbe.Register("modding", "live_lua_module", CaptureLuaModule);
        GameplayTestRuntimeProbe.Register("networking", "live_replica", CaptureReplica);
    }
    #endregion

    #region 掉落物中心查询真实空间索引
    private static JObject CaptureCenterSpatialQuery(Player player, JObject args)
    {
        Item sample = null;
        ItemMgr manager = ItemMgr.Instance;
        var found = new List<Item>();
        var dedupe = new HashSet<Item>();
        var result = new JObject();
        try
        {
            Vector2 center = new(Mathf.Floor(player.transform.position.x) + .5f,
                Mathf.Floor(player.transform.position.y) + .5f);
            sample = LoadSpawned(manager.InstantiateItem("Knife_Flint", center));
            int Count(Vector2 origin, float radius, int mask = ~0, Item excluded = null)
            {
                manager.QueryItemCentersInCircleNonAlloc(origin, radius, mask, excluded, found, dedupe);
                return found.Count(candidate => ReferenceEquals(candidate, sample));
            }
            void Move(Vector2 position)
            {
                sample.transform.position = position;
                manager.NotifyRuntimeItemMoved(sample);
            }

            result["registered"] = manager.IsRuntimeItemRegistered(sample);
            result["zeroRadius"] = Count(center, 0f);
            Move(center + Vector2.right);
            result["boundary"] = Count(center, 1f);
            result["outside"] = Count(center, .99f);
            result["excluded"] = Count(center, 1f, excluded: sample);
            result["masked"] = Count(center, 1f, 0);
            result["negativeRadius"] = Count(center, -1f);
            result["invalidRadius"] = Count(center, float.NaN);
            sample.gameObject.SetActive(false);
            result["inactive"] = Count(center, 1f);
            sample.gameObject.SetActive(true);

            WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
            result["wrapped"] = topology.IsWrapped;
            if (!topology.IsWrapped) throw new InvalidOperationException("接缝查询回归需要正式循环地表世界。");
            Vector2 seamOrigin = new(topology.Min.x + .25f, topology.Min.y + 1.5f);
            Move(new Vector2(topology.Min.x + topology.Span.x - .25f, seamOrigin.y));
            result["seamBoundary"] = Count(seamOrigin, .5f);
            result["seamOutside"] = Count(seamOrigin, .49f);
            result["seamDeduplicated"] = Count(seamOrigin, .75f);

            // 同一正式大气查询预热后采样分配量，避免每个生物重复建立全量校验集合。
            string worldKey = player.gameObject.scene.name;
            if (!AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState atmosphere))
                throw new InvalidOperationException("当前隔离世界缺少大气状态。");
            for (int i = 0; i < 16; i++) AtmosphereService.TryGetForWorld(worldKey, out _);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            bool reused = true;
            for (int i = 0; i < 256; i++)
                reused &= AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState read) && ReferenceEquals(read, atmosphere);
            result["atmosphereReadBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            result["atmosphereIdentityPreserved"] = reused;
        }
        finally
        {
            int guid = sample != null ? sample.itemData.Guid : 0;
            Despawn(ref sample);
            result["fixtureRemoved"] = guid != 0 && manager.GetItemByGuid(guid) == null;
        }
        return result;
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

    #region 手持武器正式装配与动画伤害窗口
    private static IEnumerator CaptureHeldWeaponAction(Player player, JObject args, Action<JObject> completed)
    {
        Item weapon = null;
        string weaponId = args.Value<string>("weaponId") ?? "ChippedTool";
        Mod_Stamina stamina = player.itemMods?.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        float originalStamina = stamina != null ? stamina.CurrentValue : 0f;
        var result = new JObject();
        try
        {
            if (stamina != null) stamina.CurrentValue = stamina.MaxValue;
            // 使用正式手持工厂和公开动作接口，避免绕过定义装配或手动打开伤害盒。
            weapon = SpawnHeld(player, GameRes.ExistingInstance.CreateItemData(weaponId));
            Mod_Weapon_AnimationAction animation = weapon.itemMods.GetMod_ByID<Mod_Weapon_AnimationAction>("Module_Weapon_AnimationAction");
            Mod_Damage damage = weapon.itemMods.GetMod_ByID<Mod_Damage>("Mod_Damage");
            result["weaponId"] = weapon.itemData.IDName;
            // 只返回正式装配后的配置和即时精力，比例与期望值由 Lua 判断。
            result["physicalDamage"] = damage?.DamageValues?.Physical ?? 0f;
            result["impactKind"] = (int)(damage?.DamageValues?.ImpactKind ?? CombatDamageKind.None);
            result["staminaCostPerAttack"] = animation != null ? animation.StaminaCostPerAttack : 0f;
            result["attackSpeedMultiplier"] = animation != null ? animation.AttackSpeedMultiplier : 0f;
            if (GameRes.ExistingInstance.TryGetItemDefinition(weaponId, out RuntimeItemDefinition definition))
            {
                result["shellPrefabId"] = definition.ShellPrefabId;
                result["rendererPath"] = definition.RendererPath;
                result["hasKnifeTag"] = definition.HasTag("Knife");
            }
            result["held"] = State(weapon, player);
            result["animationRegistered"] = animation != null;
            result["animationLoaded"] = animation != null && animation.IsRuntimeLoaded;
            result["damageRegistered"] = damage != null;
            result["damageLoaded"] = damage != null && damage.IsRuntimeLoaded;
            result["initialWindowClosed"] = damage != null && !damage.IsDamageEnabled();

            result["staminaBeforeAction"] = stamina != null ? stamina.CurrentValue : 0f;
            bool requested = animation is IWeaponActionAnimation action && action.TryRequestAction();
            result["staminaAfterAction"] = stamina != null ? stamina.CurrentValue : 0f;
            bool sawAttackState = false, sawOpenWindow = false, returnedToIdle = false;
            int sampledFrames = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (requested && Time.realtimeSinceStartup < deadline)
            {
                Animator animator = animation.animator;
                if (animator != null)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    sawAttackState |= state.IsName("Attack_1");
                    returnedToIdle = sawAttackState && state.IsName("Idle_0");
                }
                sawOpenWindow |= damage != null && damage.IsDamageEnabled();
                sampledFrames++;
                if (returnedToIdle && damage != null && !damage.IsDamageEnabled()) break;
                yield return null;
            }
            result["actionRequested"] = requested;
            result["sawAttackState"] = sawAttackState;
            result["sawOpenWindow"] = sawOpenWindow;
            result["returnedToIdle"] = returnedToIdle;
            result["finalWindowClosed"] = damage != null && !damage.IsDamageEnabled();
            result["sampledFrames"] = sampledFrames;
            result["limitations"] = "正式手持装配、公开动作入口和真实动画伤害窗；未覆盖手机摇杆输入、命中结算或画面手感。";
        }
        finally
        {
            try { Despawn(ref weapon); }
            finally { if (stamina != null) stamina.CurrentValue = originalStamina; }
        }
        completed(result);
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

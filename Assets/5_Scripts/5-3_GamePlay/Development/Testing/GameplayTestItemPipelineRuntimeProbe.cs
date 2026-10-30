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
        GameplayTestRuntimeProbe.Register("item_runtime", "shell_final_configuration", CaptureShellFinalConfiguration);
        GameplayTestRuntimeProbe.Register("item_runtime", "center_spatial_query", CaptureCenterSpatialQuery);
        GameplayTestRuntimeProbe.RegisterAsync("combat", "held_weapon_action", CaptureHeldWeaponAction);
        GameplayTestRuntimeProbe.RegisterAsync("combat", "projectile_tick_lifecycle", CaptureProjectileTickLifecycle);
        GameplayTestRuntimeProbe.Register("modding", "live_lua_module", CaptureLuaModule);
        GameplayTestRuntimeProbe.Register("networking", "live_replica", CaptureReplica);
    }
    #endregion

    #region 冷外壳与复用装配
    // 自有停用外壳只走正式配置、对象池和准备布局，不登记世界身份或加载模块运行态。
    private static JObject CaptureShellFinalConfiguration(Player player, JObject args)
    {
        const string itemId = "Knife_Flint";
        GameRes resources = GameRes.ExistingInstance;
        if (!resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
            throw new InvalidOperationException("外壳装配回归缺少正式小刀定义。");
        var root = new GameObject("[LuaShellFinalConfiguration]");
        root.SetActive(false);
        var pool = new ItemObjectPool();
        Item item = null;
        int coldCreates = 0;
        var result = new JObject();
        try
        {
            // 采样正式目录，确认旧副本清理后仍能仅用 JSON 和通用壳创建物品。
            result["initialInventoryTemplates"] = resources.InventoryInitDict.Count;
            var jsonOnlyShells = new JObject();
            foreach (string definitionId in new[] { "Apple", "Egg", "Meat", "Ore_Copper", "WorkBench", "MineEntrance", "CaveExit", "Wall_Stone_Summoner" })
            {
                if (!resources.TryGetItemDefinition(definitionId, out RuntimeItemDefinition current))
                    throw new InvalidOperationException($"清理旧资源后缺少物品定义：{definitionId}");
                jsonOnlyShells[definitionId] = new JObject
                {
                    ["itemId"] = current.CreateItemData().IDName,
                    ["shell"] = current.ShellPrefab.name
                };
            }
            result["jsonOnlyShells"] = jsonOnlyShells;
            GameObject Create(string id)
            {
                coldCreates++;
                return resources.InstantiateItemShell(id, root.transform);
            }
            GameObject raw = pool.Acquire(itemId, Create);
            item = raw.GetComponent<Item>();
            Item prototype = definition.ShellPrefab.GetComponent<Item>();
            Module[] rawModules = raw.GetComponentsInChildren<Module>(true);
            Module[] templateModules = prototype.GetComponentsInChildren<Module>(true);
            result["rawModuleCount"] = rawModules.Length;
            result["templateModuleCount"] = templateModules.Length;
            result["rawKeptTemplateIdentity"] = item.itemData?.IDName == prototype.itemData?.IDName;
            item.PrepareForPoolReuse();
            item.IsPreparedCandidate = true;
            ItemData firstData = definition.CreateItemData();
            firstData.ItemSpecialData = "first";
            ItemDefinitionRuntime.ConfigureInstance(resources, definition, item, firstData);
            item.PoolMarker.CaptureBaseline();
            Module[] firstModules = raw.GetComponentsInChildren<Module>(true);
            result["firstDataBound"] = ReferenceEquals(item.itemData, firstData);
            result["configuredModuleCount"] = firstModules.Count(module => module != null &&
                definition.TryGetModuleAssembly(module.StableName, out var plan) && plan.MatchesBoundModule(module));
            result["expectedModuleCount"] = definition.ModuleAssemblies.Count(plan => !plan.IsDataOnly);
            int firstInstanceId = item.GetInstanceID();
            item.PrepareForDespawn(saveData: false);
            result["poolAccepted"] = pool.TryReturn(item, root.transform, 2, 2);
            if (!result.Value<bool>("poolAccepted"))
                throw new InvalidOperationException("自有未加载小刀外壳不能回池，无法验证同实例复用。");
            raw = pool.Acquire(itemId, Create);
            item = raw.GetComponent<Item>();
            raw.transform.SetParent(root.transform, false);
            item.PrepareForPoolReuse();
            item.IsPreparedCandidate = true;
            ItemData latestData = definition.CreateItemData();
            latestData.ItemSpecialData = "latest";
            ItemDefinitionRuntime.ConfigureInstance(resources, definition, item, latestData);
            result["samePooledInstance"] = item.GetInstanceID() == firstInstanceId;
            result["coldCreates"] = coldCreates;
            result["latestDataBound"] = ReferenceEquals(item.itemData, latestData) &&
                item.itemData.ItemSpecialData == "latest" && firstData.ItemSpecialData == "first";
            Module[] reusedModules = raw.GetComponentsInChildren<Module>(true);
            result["sameConfiguredComponents"] = firstModules.All(module => module != null &&
                reusedModules.Contains(module));

            RuntimeItemModuleAssemblyPlan firstPlan = definition.ModuleAssemblies.First(plan => !plan.IsDataOnly);
            var nested = new GameObject("NestedItem");
            nested.transform.SetParent(raw.transform, false);
            nested.AddComponent<GameItem>().IsPreparedCandidate = true;
            var nestedModule = nested.AddComponent<GameplayTestAnimationMotionSource>();
            nestedModule._Data = new Ex_ModData_MemoryPackable
                { StableName = firstPlan.StableName, ModuleId = firstPlan.ModuleId };
            item.PrepareRuntimeModuleLayout();
            result["nestedExcluded"] = item.Mods.Count == result.Value<int>("expectedModuleCount") &&
                !item.itemMods.HasMod(nestedModule) && nestedModule.Item_Data == null;
            result["allModulesBoundToLatestData"] = item.Mods.All(pair =>
                ReferenceEquals(pair.Value.Item_Data, latestData) &&
                ReferenceEquals(pair.Value._Data, latestData.ModuleDataDic[pair.Key]));

            Module original = item.Mods[firstPlan.StableName];
            Module duplicate = raw.AddComponent(original.GetType()) as Module;
            duplicate._Data = ItemInstanceDataFactory.CloneRuntime(latestData).ModuleDataDic[firstPlan.StableName];
            duplicate.BindRuntimeIdentity(firstPlan.StableName, firstPlan.ModuleId, firstPlan.PrefabId);
            try { item.PrepareRuntimeModuleLayout(); result["duplicateRejected"] = false; }
            catch (InvalidOperationException exception)
            {
                result["duplicateRejected"] = exception.Message.Contains("重复模块槽位");
            }

            GameObject direct = resources.InstantiatePrefab(itemId, parent: root.transform);
            Item directItem = direct.GetComponent<Item>();
            result["publicFactoryStillConfigured"] = directItem.itemData?.IDName == itemId &&
                direct.GetComponentsInChildren<Module>(true).Count(module => module != null &&
                    definition.TryGetModuleAssembly(module.StableName, out var plan) && plan.MatchesBoundModule(module)) ==
                result.Value<int>("expectedModuleCount");
            return result;
        }
        finally
        {
            try { if (item != null && !item.DestructionHandled) item.PrepareForDespawn(saveData: false); }
            finally { pool.Clear(); UnityEngine.Object.Destroy(root); }
        }
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

            if (!SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData activePlanet))
            {
                throw new InvalidOperationException("接缝查询回归缺少当前地表星球：" +
                    $"活动场景={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}，" +
                    $"执行场景={GameplayWorldRegistry.CurrentScene.name}，执行世界={GameplayWorldRegistry.ExecutionWorldKey}，" +
                    $"存在执行上下文={GameplayWorldRegistry.HasExecutionContext}，玩家场景={player.gameObject.scene.name}，" +
                    $"玩家存档世界={player.Data?.CurrentSceneName}。");
            }
            WorldTopologyMode originalTopology = activePlanet.TopologyMode;
            try
            {
                // 陈列厅默认无限地表，仅在本次同步查询内启用真实循环拓扑并立即归还。
                activePlanet.TopologyMode = WorldTopologyMode.Wrapped;
                WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
                result["wrapped"] = topology.IsWrapped;
                if (!topology.IsWrapped) throw new InvalidOperationException("接缝查询无法建立循环地表边界。");
                Vector2 seamOrigin = new(topology.Min.x + .25f, topology.Min.y + 1.5f);
                Move(new Vector2(topology.Min.x + topology.Span.x - .25f, seamOrigin.y));
                result["seamBoundary"] = Count(seamOrigin, .5f);
                result["seamOutside"] = Count(seamOrigin, .49f);
                result["seamDeduplicated"] = Count(seamOrigin, .75f);
            }
            finally { activePlanet.TopologyMode = originalTopology; }
            result["topologyRestored"] = activePlanet.TopologyMode == originalTopology &&
                WorldTopologyRuntime.GetActiveDomain().IsWrapped == (originalTopology == WorldTopologyMode.Wrapped);

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
    private sealed class HeldWeaponFixedStepObservation : IDisposable, IGameplayFixedStepSimulationSystem
    {
        private readonly float originalTimeScale = Time.timeScale;
        private readonly int originalTargetFrameRate = Application.targetFrameRate;
        private readonly int originalVSyncCount = QualitySettings.vSyncCount;
        private Mod_Damage damage;
        private Mod_DamageReceiver targetReceiver;
        private Item target;
        private bool registered, disposed;
        public readonly JArray Windows = new();
        public readonly JArray Contacts = new();
        public bool HostileTarget;
        public JObject OriginalSettings => new()
        {
            ["timeScale"] = originalTimeScale, ["targetFrameRate"] = originalTargetFrameRate,
            ["vSyncCount"] = originalVSyncCount
        };
        public static JObject CurrentSettings => new()
        {
            ["timeScale"] = Time.timeScale, ["targetFrameRate"] = Application.targetFrameRate,
            ["vSyncCount"] = QualitySettings.vSyncCount
        };

        // 目标、帧率与观察器均属于当前 Lua，断言失败和中断时恢复。
        public void Prepare(Player player, Mod_Damage source, float timeScale)
        {
            damage = source;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            Time.timeScale = timeScale;
            target = ItemMgr.Instance.InstantiateItem("Chicken", player.transform.position);
            target.Load();
            foreach (Module module in target.itemMods.Mods.Values)
                if (module is Mod_Food || module.ResolvedModuleId == ModText.AI ||
                    module.ResolvedModuleId == ModText.Mod_Mover_AI)
                    module.SetEnabled(false);
            targetReceiver = target.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (targetReceiver == null) throw new InvalidOperationException("固定步武器采样缺少目标生命模块。");
            targetReceiver.Data.UseBodyPartHealth = false;
            targetReceiver.MaxHp = 1000f;
            targetReceiver.Hp = 1000f;
            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body != null) { body.velocity = Vector2.zero; body.constraints = RigidbodyConstraints2D.FreezeAll; }
            BoxCollider2D receiverShape = target.GetComponentsInChildren<BoxCollider2D>(true)
                .FirstOrDefault(shape => shape.isActiveAndEnabled && CombatPhysicsChannels.IsDamageReceiverCollider(shape) &&
                    ReferenceEquals(GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(shape), targetReceiver));
            if (receiverShape == null) throw new InvalidOperationException("固定步武器采样缺少启用的受击盒。");
            receiverShape.size = Vector2.one * 4f;
            Physics2D.SyncTransforms();
            HostileTarget = FactionRelationService.CanAttack(damage.item, target);
            damage.OnReceiverDamageResolved += RecordContact;
            damage.OnReceiverLogicalMissed += RecordContact;
            GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.AfterPhysics);
            registered = true;
        }

        private void RecordContact(Mod_DamageReceiver receiver, float amount)
        {
            if (!ReferenceEquals(receiver, targetReceiver)) return;
            Contacts.Add(new JObject
            {
                ["tick"] = (long)GameplayFixedStepSimulation.TickIndex, ["frame"] = Time.frameCount,
                ["inFixedStep"] = Time.inFixedTimeStep, ["windowOpen"] = damage.IsDamageEnabled()
            });
        }

        public void SimulateFixedStep(float deltaTime)
        {
            if (damage == null || !damage.IsDamageEnabled()) return;
            Windows.Add(new JObject
            {
                ["tick"] = (long)GameplayFixedStepSimulation.TickIndex, ["frame"] = Time.frameCount,
                ["inFixedStep"] = Time.inFixedTimeStep, ["delta"] = deltaTime,
                ["damageTickEnabled"] = damage.TickMode == ModuleTickMode.EveryFrame
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (registered) GameplayFixedStepSimulation.Unregister(this);
                if (damage != null)
                {
                    damage.OnReceiverDamageResolved -= RecordContact;
                    damage.OnReceiverLogicalMissed -= RecordContact;
                }
                Despawn(ref target);
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                Application.targetFrameRate = originalTargetFrameRate;
                QualitySettings.vSyncCount = originalVSyncCount;
            }
        }
    }

    private static IEnumerator CaptureHeldWeaponAction(Player player, JObject args, Action<JObject> completed)
    {
        Item weapon = null;
        HeldWeaponFixedStepObservation fixedStep = null;
        string weaponId = args.Value<string>("weaponId") ?? "ChippedTool";
        // 按用例指定的真实攻击状态采样，覆盖不同武器的挥动动作。
        string attackStateName = args.Value<string>("attackStateName") ?? "Attack_1";
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
            // 直接采样实际实例与正式配方匹配，防止分类标签覆盖既有工具能力。
            result["heldTags"] = JArray.FromObject(weapon.itemData.Tags ?? new List<string>());
            result["heldPrimaryCategory"] = InventoryItemCategoryCatalog.GetPrimaryCategoryForItemTags(weapon.itemData.Tags).Id;
            result["heldMatchesKnifeIngredient"] = CraftingIngredientMatcher.MatchesIdentity(
                new RuntimeRecipeIngredient { matchMode = MatchMode.ByTag, Tag = "Knife", amount = 0 }, weapon.itemData);
            result["heldMatchesSpearIngredient"] = CraftingIngredientMatcher.MatchesIdentity(
                new RuntimeRecipeIngredient { matchMode = MatchMode.ByTag, Tag = "Spear", amount = 0 }, weapon.itemData);
            result["held"] = State(weapon, player);
            result["animationRegistered"] = animation != null;
            result["animationLoaded"] = animation != null && animation.IsRuntimeLoaded;
            result["damageRegistered"] = damage != null;
            result["damageLoaded"] = damage != null && damage.IsRuntimeLoaded;
            result["initialWindowClosed"] = damage != null && !damage.IsDamageEnabled();
            result["idleDamageTickDisabled"] = damage != null && damage.TickMode == ModuleTickMode.Disabled;
            result["animatorUpdateMode"] = animation?.animator != null ? animation.animator.updateMode.ToString() : "missing";

            if (args.Value<bool?>("verifyFixedStep") == true)
            {
                fixedStep = new HeldWeaponFixedStepObservation();
                GameplayTestRuntimeProbe.RegisterCleanup(fixedStep);
                fixedStep.Prepare(player, damage, args.Value<float?>("timeScale") ?? 1f);
            }

            result["staminaBeforeAction"] = stamina != null ? stamina.CurrentValue : 0f;
            bool requested = animation is IWeaponActionAnimation action && action.TryRequestAction();
            result["staminaAfterAction"] = stamina != null ? stamina.CurrentValue : 0f;
            bool sawAttackState = false, sawOpenWindow = false, sawOpenWindowWithFixedDamageTick = false, returnedToIdle = false;
            int sampledFrames = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (requested && Time.realtimeSinceStartup < deadline)
            {
                Animator animator = animation.animator;
                if (animator != null)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    sawAttackState |= state.IsName(attackStateName);
                    returnedToIdle = sawAttackState && state.IsName("Idle_0");
                }
                sawOpenWindow |= damage != null && damage.IsDamageEnabled();
                sawOpenWindowWithFixedDamageTick |= damage != null && damage.IsDamageEnabled() &&
                                                 damage.TickMode == ModuleTickMode.EveryFrame;
                sampledFrames++;
                if (returnedToIdle && damage != null && !damage.IsDamageEnabled()) break;
                yield return null;
            }
            result["actionRequested"] = requested;
            result["sawAttackState"] = sawAttackState;
            result["sawOpenWindow"] = sawOpenWindow;
            result["sawOpenWindowWithFixedDamageTick"] = sawOpenWindowWithFixedDamageTick;
            result["returnedToIdle"] = returnedToIdle;
            result["finalWindowClosed"] = damage != null && !damage.IsDamageEnabled();
            result["sampledFrames"] = sampledFrames;
            if (fixedStep != null)
                result["fixedStep"] = new JObject
                {
                    ["timeScale"] = Time.timeScale, ["targetFrameRate"] = Application.targetFrameRate,
                    ["vSyncCount"] = QualitySettings.vSyncCount, ["hostileTarget"] = fixedStep.HostileTarget,
                    ["windows"] = fixedStep.Windows, ["contacts"] = fixedStep.Contacts,
                    ["originalSettings"] = fixedStep.OriginalSettings
                };

            // 攻击中停用再启用真实武器，检查场景迁移同类的 Animator 失活路径。
            Animator weaponAnimator = animation?.animator;
            if (weaponAnimator != null)
            {
                bool keepStateOnDisable = weaponAnimator.keepAnimatorStateOnDisable;
                bool inactiveAnimatorWarning = false;
                Application.LogCallback onLog = (message, _, type) =>
                {
                    if (message.Contains("Game object with animator is inactive")) inactiveAnimatorWarning = true;
                };
                Application.logMessageReceived += onLog;
                try
                {
                    weaponAnimator.keepAnimatorStateOnDisable = true;
                    result["beforeDisableActionRequested"] = animation.TryRequestAction();
                    weapon.gameObject.SetActive(false);
                    result["inactiveActionRejected"] = !animation.TryRequestAction();
                    result["disabledWindowClosed"] = damage != null && !damage.IsDamageEnabled();
                    result["disabledHitStopCleared"] = !animation.IsHitStopped &&
                                                       Mathf.Approximately(weaponAnimator.speed, animation.AttackSpeedMultiplier);
                    weapon.gameObject.SetActive(true);
                    yield return null;
                    yield return null;
                    result["reactivatedIdle"] = weaponAnimator.isActiveAndEnabled &&
                                                weaponAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle_0");
                    result["reactivatedWindowClosed"] = damage != null && !damage.IsDamageEnabled();
                    result["reactivatedActionRequested"] = animation.TryRequestAction();
                    result["reactivatedAttackState"] = weaponAnimator.GetCurrentAnimatorStateInfo(0).IsName(attackStateName);
                }
                finally
                {
                    Application.logMessageReceived -= onLog;
                    if (weaponAnimator != null) weaponAnimator.keepAnimatorStateOnDisable = keepStateOnDisable;
                    result["inactiveAnimatorWarning"] = inactiveAnimatorWarning;
                }
            }
            result["limitations"] = "正式手持装配、公开动作入口和真实动画伤害窗；未覆盖手机摇杆输入、命中结算或画面手感。";
        }
        finally
        {
            try { fixedStep?.Dispose(); }
            finally
            {
                if (result["fixedStep"] is JObject sample)
                    sample["restoredSettings"] = HeldWeaponFixedStepObservation.CurrentSettings;
                try { Despawn(ref weapon); }
                finally { if (stamina != null) stamina.CurrentValue = originalStamina; }
            }
        }
        completed(result);
    }
    #endregion

    #region 投射物调度与来源生命周期
    private static IEnumerator CaptureProjectileTickLifecycle(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var scope = new ProjectileTickProbeScope();
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        var result = new JObject();
        try
        {
            // 石头同时拥有投射和丢弃伤害，隔离宿主阻止转换，并远离玩家自动拾取。
            Vector3 stonePosition = player.transform.position + Vector3.right * 4f;
            scope.Item = ItemMgr.Instance.InstantiateItem("Ore_Stone", stonePosition);
            scope.Item.PersistenceOwner = scope;
            scope.Item.Owner = player;
            scope.Item.Load();
            Item stone = scope.Item;
            var projectile = stone.itemMods.GetMod_ByID<Mod_Projectile>(Mod_Projectile.PersistedModuleId);
            var damage = stone.itemMods.GetMod_ByID<Mod_Damage>("Mod_Damage");
            var discard = stone.itemMods.GetMod_ByID<Mod_DiscardFlightDamage>(Mod_DiscardFlightDamage.PersistedModuleId);
            if (projectile == null || damage == null || discard == null)
                throw new InvalidOperationException("石头缺少投射、伤害或丢弃伤害模块。");

            result["idleProjectile"] = projectile.TickMode == ModuleTickMode.Disabled;
            result["idleDamage"] = damage.TickMode == ModuleTickMode.Disabled;
            damage.SetIdleTickSuppressed(true, discard);
            discard.SetEnabled(false);
            result["projectileSourceRetained"] = damage.TickMode == ModuleTickMode.Disabled;
            discard.SetEnabled(true);
            projectile.SetEnabled(false);
            result["discardSourceRetained"] = damage.TickMode == ModuleTickMode.Disabled;
            damage.SetEnabled(false);
            damage.SetEnabled(true);
            result["damageReloadRetainedSource"] = damage.TickMode == ModuleTickMode.Disabled;
            discard.SetEnabled(false);
            result["lastSourceReleased"] = damage.TickMode == ModuleTickMode.EveryFrame;
            projectile.SetEnabled(true);
            discard.SetEnabled(true);

            stone.SetInHand(true);
            result["heldDamageActive"] = damage.TickMode == ModuleTickMode.EveryFrame;
            stone.SetInHand(false);
            bool originalHandOnlyDamage = damage.OnlyDealDamageWhenInHand;
            CombatDamage originalDamage = damage.ResolveDamageValues().Scaled(1f);
            scope.Restore = () =>
            {
                if (damage == null) return;
                damage.OnlyDealDamageWhenInHand = originalHandOnlyDamage;
                damage.SetDamageValues(originalDamage);
            };
            damage.OnlyDealDamageWhenInHand = true;
            damage.SetDamageEnabled(true);
            result["openDamageActive"] = damage.TickMode == ModuleTickMode.EveryFrame;
            damage.SetDamageEnabled(false);
            result["closedDamageIdle"] = damage.TickMode == ModuleTickMode.Disabled;

            // 用零速度和伤害门禁保留正式飞行时钟，同时避免影响附近目标。
            projectile.Launch(player, Vector2.right, 0.2f, 1f, 0f);
            result["flightStarted"] = projectile.HasActiveWorldAttachment &&
                                      projectile.TickMode == ModuleTickMode.EveryFrame && damage.IsDamageEnabled();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (stone != null && !stone.DestructionHandled && projectile.HasActiveWorldAttachment &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            result["landedAndIdle"] = stone != null && !stone.DestructionHandled &&
                                      !projectile.HasActiveWorldAttachment && projectile.TickMode == ModuleTickMode.Disabled &&
                                      !damage.IsDamageEnabled() && damage.TickMode == ModuleTickMode.Disabled;

            int shellId = stone.GetInstanceID();
            uint generation = stone.RuntimeGeneration;
            scope.Dispose();
            scope.Item = ItemMgr.Instance.InstantiateItem("Ore_Stone", stonePosition);
            scope.Item.PersistenceOwner = scope;
            scope.Item.Load();
            var reloadedProjectile = scope.Item.itemMods.GetMod_ByID<Mod_Projectile>(Mod_Projectile.PersistedModuleId);
            var reloadedDamage = scope.Item.itemMods.GetMod_ByID<Mod_Damage>("Mod_Damage");
            bool sameShellReused = scope.Item.GetInstanceID() == shellId;
            result["sameShellReused"] = sameShellReused;
            result["reloadedIdle"] = scope.Item.IsInitialized && (!sameShellReused || scope.Item.RuntimeGeneration != generation) &&
                                     reloadedProjectile != null &&
                                     reloadedProjectile.TickMode == ModuleTickMode.Disabled && reloadedDamage != null &&
                                     reloadedDamage.TickMode == ModuleTickMode.Disabled;
        }
        finally
        {
            scope.Dispose();
        }
        completed(result);
    }

    private sealed class ProjectileTickProbeScope : IDisposable
    {
        public Item Item;
        public Action Restore;

        public void Dispose()
        {
            Item owned = Item;
            Item = null;
            if (owned == null || owned.DestructionHandled) return;
            Restore?.Invoke();
            Restore = null;
            owned.PersistenceOwner = null;
            ItemMgr.GetExistingInstance()?.DespawnItem(owned, saveData: false);
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

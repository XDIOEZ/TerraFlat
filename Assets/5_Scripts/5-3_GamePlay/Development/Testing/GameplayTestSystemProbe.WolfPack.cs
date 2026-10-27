using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 狼群规则探针注册

    private static void RegisterWolfPackOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/wolf_pack", EntityAiWolfPack);
        operations.Add("ai/wolf_formation_topology", EntityWolfFormationTopology);
        operations.Add("ai/wolf_target_memory", _ => EntityAiWolfPack(
            new JObject { ["count"] = 3, ["probeMemory"] = true }));
        operations.Add("ai/wolf_combat_position", _ => EntityAiWolfPack(
            new JObject { ["count"] = 3, ["probeCombat"] = true }));
    }

    // 正式狼群站位算法在环绕、无限世界和半周期平局时保持同一距离及分翼规则。
    private static JObject EntityWolfFormationTopology(JObject args)
    {
        var wrapped = new WorldTopologyDomain(new int2(-16, -16), new int2(32, 32), true);
        var infinite = default(WorldTopologyDomain);
        Vector2[] origins =
        {
            new(15.5f, .5f), new(-15.5f, .5f), new(.5f, 15.5f),
            new(.5f, -15.5f), Vector2.zero, Vector2.zero, new(3f, 4f)
        };
        Vector2[] destinations =
        {
            new(-15.5f, .5f), new(15.5f, .5f), new(.5f, -15.5f),
            new(.5f, 15.5f), new(16f, 0f), new(-16f, 0f), new(3f, 4f)
        };
        Vector2[] wrappedExpected =
        {
            new(1f, 0f), new(-1f, 0f), new(0f, 1f),
            new(0f, -1f), new(-16f, 0f), new(-16f, 0f), Vector2.zero
        };

        bool wrappedDeltas = true, infiniteDeltas = true;
        for (int i = 0; i < origins.Length; i++)
        {
            wrappedDeltas &= AI_Wolf.GetChaseDelta(origins[i], destinations[i], wrapped) == wrappedExpected[i];
            infiniteDeltas &= AI_Wolf.GetChaseDelta(origins[i], destinations[i], infinite) ==
                destinations[i] - origins[i];
        }

        Vector3 position = new(17.25f, -17.75f, 3.5f);
        bool normalized = AI_Wolf.NormalizeChasePosition(position, wrapped) ==
            new Vector3(-14.75f, 14.25f, 3.5f);
        bool infiniteUnchanged = AI_Wolf.NormalizeChasePosition(position, infinite) == position;
        bool laneSeamRight = AI_Wolf.ResolveRightChaseLane(origins[0], destinations[0], 0, wrapped);
        bool laneSeamLeft = !AI_Wolf.ResolveRightChaseLane(origins[1], destinations[1], 1, wrapped);
        bool unwrappedLaneLeft = !AI_Wolf.ResolveRightChaseLane(origins[0], destinations[0], 1, infinite);
        bool laneTieParity = !AI_Wolf.ResolveRightChaseLane(Vector2.zero, Vector2.zero, 0, wrapped) &&
                             AI_Wolf.ResolveRightChaseLane(Vector2.zero, Vector2.zero, 1, wrapped);
        bool laneNearTie = AI_Wolf.ResolveRightChaseLane(Vector2.zero, new Vector2(.04f, 0f), 1, wrapped) &&
                           !AI_Wolf.ResolveRightChaseLane(Vector2.zero, new Vector2(-.04f, 0f), 0, wrapped);

        return new JObject
        {
            ["wrappedDeltas"] = wrappedDeltas, ["infiniteDeltas"] = infiniteDeltas,
            ["normalized"] = normalized, ["infiniteUnchanged"] = infiniteUnchanged,
            ["laneSeamRight"] = laneSeamRight, ["laneSeamLeft"] = laneSeamLeft,
            ["unwrappedLaneLeft"] = unwrappedLaneLeft, ["laneTieParity"] = laneTieParity,
            ["laneNearTie"] = laneNearTie, ["cases"] = origins.Length
        };
    }

    #endregion

    #region 狼群身份与正式决策采样

    // 独立未激活实体按正式定义注册模块，子类仅开放原有评估钩子，不复制狼群规则。
    private static JObject EntityAiWolfPack(JObject args)
    {
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        ItemDefinitionDto definition = definitions.Find(value => value.Id == "Wolf");
        ItemModuleDefinitionDto configuredAi = definition.Modules["ai"];
#if UNITY_EDITOR
        GameObject shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
#else
        GameObject shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
#endif
        var root = new GameObject("[LuaWolfPack]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var wolves = new List<GameplayTestWolfPackProbeAI>();
            var detectors = new List<Mod_ItemDetector>();
            var candidates = new List<Item>();
            int count = Mathf.Clamp((int)Number(args, "count", 4), 1, 4);
            string exclusion = Text(args, "exclude", string.Empty);
            bool probeCombat = Flag(args, "probeCombat", false);
            for (int index = 0; index < count; index++)
            {
                var host = new GameObject("Wolf" + index);
                host.transform.SetParent(root.transform, false);
                GameItem item = host.AddComponent<GameItem>();
                Data_GeneralItem data = EntityProbeData("test:wolf_pack:" + index, 1f);
                data.Guid = index + 101;
                data.FactionId = definition.ItemData.Value<string>("factionId");
                data.Tags = new List<string> { "Wolf", "Animal" };
                item.BindData(data);
                GameplayWorldRegistry.AssignItemToWorld(item, "probe:wolf-pack");
                item.itemMods.BindOwner(item);
                var ai = host.AddComponent<GameplayTestWolfPackProbeAI>();
                var detector = host.AddComponent<Mod_ItemDetector>();
                var health = host.AddComponent<Mod_DamageReceiver>();
                Register("ai", ai);
                Register("detector", detector);
                Register("health", health);
                detector.wallsBlockPerception = false;
                health.Data.Hp = health.Data.MaxHp * (float)Number(args, "hpRate", 1d);
                ai.PrepareProbe(detector, health);
                if (probeCombat)
                {
                    var mover = host.AddComponent<Mod_Mover_AI>();
                    Register("mover", mover);
                    ai.PrepareCombatProbe(mover);
                }
                host.transform.position = new Vector3(index, 0f);
                if (index == count - 1)
                {
                    if (exclusion == "dead") health.Data.Hp = 0f;
                    if (exclusion == "disabled") ai._Data.Enabled = false;
                    if (exclusion == "unready") ai.SetProbeReady(false);
                    if (exclusion == "foreign") data.FactionId = "test:hostile_wolves";
                }
                wolves.Add(ai);
                detectors.Add(detector);
                candidates.Add(item);

                void Register(string slot, Module instance)
                {
                    ItemModuleDefinitionDto configured = definition.Modules[slot];
                    Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, null, configured.Id) ??
                        ItemDefinitionCatalogLoader.FindModulePrototype(shell, null, configured.Prefab);
                    ModuleData moduleData = FastCloner.FastCloner.DeepClone<object>(prototype?._Data) as ModuleData;
                    if (moduleData == null) throw new InvalidOperationException("狼群采样缺少正式模块原型：" + slot);
                    if (configured.Enabled.HasValue) moduleData.Enabled = configured.Enabled.Value;
                    instance._Data = moduleData;
                    instance.BindRuntimeIdentity(slot, configured.Id, configured.Prefab);
                    ModuleJsonConfigurator.Apply(instance, definition.Id, slot, configured.Id, configured.Parameters?.ToString());
                    item.itemData.ModuleDataDic[slot] = moduleData;
                    instance.ModuleInit(item, moduleData);
                    item.itemMods.AddMod(instance);
                }
            }

            var playerHost = new GameObject("PlayerThreat");
            playerHost.transform.SetParent(root.transform, false);
            GameItem player = playerHost.AddComponent<GameItem>();
            Data_GeneralItem playerData = EntityProbeData("test:wolf_player", 1f);
            playerData.Guid = 200;
            playerData.FactionId = FactionRelationService.PlayerFactionId;
            playerData.Tags = new List<string> { "Player" };
            player.BindData(playerData);
            GameplayWorldRegistry.AssignItemToWorld(player, "probe:wolf-pack");
            playerHost.transform.position = new Vector3((float)Number(args, "playerX", 15d), 0f);
            if (probeCombat) root.SetActive(true);
            for (int index = 0; index < count; index++)
            {
                var visible = new List<Item>(candidates) { candidates[0] };
                if (index == 0 || !Flag(args, "callOnly", false)) visible.Add(player);
                detectors[index].ApplyDetectorResults(0, visible);
                wolves[index].RefreshProbe();
            }
            wolves[0].TriggerPackAttack(player);
            if (Flag(args, "probeMemory", false))
                return SampleWolfTargetMemory(wolves, detectors, player);
            if (probeCombat)
                return SampleWolfCombatPosition(wolves, detectors[0], player);
            var packCounts = new JArray();
            var validatedAllyCounts = new JArray();
            var leaders = new JArray();
            var states = new JArray();
            var threats = new JArray();
            foreach (GameplayTestWolfPackProbeAI ai in wolves)
            {
                ai.RefreshProbe();
                packCounts.Add(ai.PackMemberCount);
                validatedAllyCounts.Add(ai.ValidatedPackAllyCount);
                leaders.Add(ai.PackLeader?.item?.itemData?.Guid ?? 0);
                states.Add(ai.EvaluateProbe().ToString());
                threats.Add(ai.CurrentThreat?.itemData?.Guid ?? 0);
            }
            AI_Wolf first = wolves[0];
            bool identityBound = ReferenceEquals(first.item.itemMods.GetMod_ByID<AI_Wolf>(ModText.AI), first);
            first.item.itemMods.RemoveMod(first);
            first.BindRuntimeIdentity("ai", "test:wrong_wolf_ai", configuredAi.Prefab);
            first.item.itemMods.AddMod(first);
            bool wrongIdRejected = first.item.itemMods.GetMod_ByID<AI_Wolf>(ModText.AI) == null;
            return new JObject
            {
                ["declaredId"] = configuredAi.Id, ["prefabId"] = configuredAi.Prefab,
                ["identityBound"] = identityBound, ["wrongIdRejected"] = wrongIdRejected,
                ["packCounts"] = packCounts, ["validatedAllyCounts"] = validatedAllyCounts,
                ["leaders"] = leaders,
                ["states"] = states, ["threats"] = threats,
                ["detectorRadius"] = detectors[0].DetectionRadius
            };
        }
        finally
        {
            foreach (Item item in root.GetComponentsInChildren<Item>(true)) GameplayWorldRegistry.AssignItemToWorld(item, null);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // 直接推进正式攻击节点，采样偏离站位、刹车漂移、恢复追击和冷却连续性。
    private static JObject SampleWolfCombatPosition(List<GameplayTestWolfPackProbeAI> wolves,
        Mod_ItemDetector detector, Item player)
    {
        if (Application.isPlaying || ChunkMgr.ExistingInstance != null)
            throw new InvalidOperationException("狼攻击站位规则只允许在未加载世界的编辑态执行。");
        GameplayTestWolfPackProbeAI wolf = wolves[0];
        Mod_Mover_AI mover = wolf.GetComponent<Mod_Mover_AI>();
        player.transform.position = Vector3.zero;
        wolf.transform.position = new Vector3(-1.8f, 0f);
        wolves[1].transform.position = new Vector3(1.05f, 0f);
        wolves[2].transform.position = new Vector3(1.05f, -.42f);
        var result = new JObject { ["offSlotState"] = wolf.TickCombatProbe(0f).ToString() };
        Vector3? formation = wolf.ChaseFormationTarget;
        result["formationActive"] = formation.HasValue;
        result["slotError"] = formation.HasValue
            ? WorldTopologyRuntime.Distance(wolf.transform.position, formation.Value) : -1f;
        result["firstAttackStarted"] = wolf.IsAttackLocked && !mover.CanMove;

        wolf.TickTimersProbe(wolf.attackDamageStartDelay + wolf.attackDamageWindow + .01f);
        wolf.TickTimersProbe(wolf.attackRecoveryDuration + .01f);
        float cooldown = wolf.AttackCooldownRemaining;
        result["cooldownBefore"] = cooldown;
        wolf.transform.position = new Vector3(-1.05f, 0f);
        wolf.TickCombatProbe(0f);
        result["arrivedStopped"] = !mover.CanMove && !mover.HasActiveDestination;
        wolf.transform.position = new Vector3(-1.45f, 0f);
        wolf.TickCombatProbe(0f);
        result["driftStopped"] = !mover.CanMove && !mover.HasActiveDestination;
        player.transform.position = new Vector3(.1f, 0f);
        wolf.TickCombatProbe(0f);
        result["smallTargetMotionStopped"] = !mover.CanMove && !mover.HasActiveDestination;

        wolf.transform.position = new Vector3(-1.75f, 0f);
        result["repositionState"] = wolf.TickCombatProbe(0f).ToString();
        result["repositionResumed"] = mover.CanMove && mover.HasActiveDestination;
        player.transform.position = new Vector3(1.5f, 0f);
        result["outOfRangeState"] = wolf.TickCombatProbe(0f).ToString();
        result["outOfRangeResumed"] = mover.CanMove && mover.HasActiveDestination && !wolf.IsAttackLocked;
        result["cooldownAfterChase"] = wolf.AttackCooldownRemaining;
        player.transform.position = Vector3.zero;
        result["returnedState"] = wolf.TickCombatProbe(0f).ToString();
        result["cooldownAfterReturn"] = wolf.AttackCooldownRemaining;
        result["secondAttackState"] = wolf.TickCombatProbe(cooldown + .01f).ToString();
        result["secondAttackStarted"] = wolf.IsAttackLocked && !mover.CanMove;

        detector.wallsBlockPerception = true;
        result["hiddenState"] = wolf.TickCombatProbe(0f).ToString();
        result["hiddenAttackStopped"] = !wolf.IsAttackLocked;
        return result;
    }

    // 无世界规则环境使用正式检测器的不可见结果，状态转换仍调用正式评估与切换钩子。
    private static JObject SampleWolfTargetMemory(List<GameplayTestWolfPackProbeAI> wolves,
        List<Mod_ItemDetector> detectors, Item player)
    {
        if (Application.isPlaying || ChunkMgr.ExistingInstance != null)
            throw new InvalidOperationException("狼记忆规则只允许在未加载世界的编辑态执行。");
        GameplayTestWolfPackProbeAI wolf = wolves[0];
        Mod_ItemDetector detector = detectors[0];
        wolf.enableWander = false;
        var result = new JObject { ["visibleState"] = wolf.DecideProbe().ToString() };
        Vector3 firstPosition = wolf.LastKnownThreatPosition;
        result["initialMemoryX"] = firstPosition.x;

        // 保留旧候选列表，同时移动隐藏目标，核验旧列表不会偷更新最后已知坐标。
        detector.wallsBlockPerception = true;
        player.transform.position += new Vector3(0f, 8f);
        result["hiddenState"] = wolf.DecideProbe().ToString();
        result["memoryFrozen"] = wolf.LastKnownThreatPosition == firstPosition && !wolf.ThreatVisible;
        wolf.TickTimersProbe(2f);
        result["travellingMemoryRetained"] = wolf.HasThreatMemory && !wolf.WaitingAtLastThreatPosition;
        player.transform.position = wolf.transform.position + Vector3.right * .1f;
        result["hiddenNearbyState"] = wolf.DecideProbe().ToString();
        result["hiddenNearbyMemoryFrozen"] = wolf.LastKnownThreatPosition == firstPosition;

        wolf.transform.position = firstPosition;
        wolf.DecideProbe();
        result["arrivalWaitStarted"] = wolf.WaitingAtLastThreatPosition;
        wolf.TickTimersProbe(wolf.lostTargetSearchDuration - .1f);
        result["waitRetained"] = wolf.HasThreatMemory;
        wolf.TickTimersProbe(.2f);
        result["waitExpired"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;
        result["afterWaitState"] = wolf.DecideProbe().ToString();

        wolf.transform.position = Vector3.zero;
        player.transform.position = new Vector3(8f, 0f);
        detector.wallsBlockPerception = false;
        result["reacquiredState"] = wolf.DecideProbe().ToString();
        detector.wallsBlockPerception = true;
        player.transform.position = new Vector3(20f, 3f);
        wolf.DecideProbe();
        wolf.TickTimersProbe(1f);
        detector.wallsBlockPerception = false;
        result["reacquiredDuringTravelState"] = wolf.DecideProbe().ToString();
        result["reacquiredMemoryUpdated"] = wolf.ThreatVisible &&
            wolf.LastKnownThreatPosition == player.transform.position;

        detector.wallsBlockPerception = true;
        wolf.DecideProbe();
        wolf.transform.position = wolf.LastKnownThreatPosition;
        wolf.DecideProbe();
        player.transform.position = new Vector3(25f, 3f);
        detector.wallsBlockPerception = false;
        result["reacquiredDuringWaitState"] = wolf.DecideProbe().ToString();
        result["reacquiredWaitCleared"] = !wolf.WaitingAtLastThreatPosition && wolf.ThreatVisible;

        detector.wallsBlockPerception = true;
        wolf.DecideProbe();
        wolf.TickTimersProbe(wolf.lostTargetTravelTimeout + .1f);
        result["travelExpired"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;

        wolf.transform.position = Vector3.zero;
        player.transform.position = new Vector3(8f, 0f);
        detector.wallsBlockPerception = false;
        wolf.DecideProbe();
        detector.wallsBlockPerception = true;
        player.itemData.Guid++;
        wolf.DecideProbe();
        result["reusedIdentityCleared"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;

        detector.wallsBlockPerception = false;
        wolf.DecideProbe();
        wolf.CancelAdvance();
        result["commandCancelCleared"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;
        wolf.DecideProbe();
        wolf.ResetProbe();
        result["runtimeResetCleared"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;

        GameplayTestWolfPackProbeAI caller = wolves[1];
        detectors[1].wallsBlockPerception = false;
        caller.DecideProbe();
        Vector3 reportedPosition = caller.LastKnownThreatPosition;
        detector.wallsBlockPerception = true;
        wolf.ReceivePackCall(player, caller);
        player.transform.position += new Vector3(7f, 6f);
        result["reportedState"] = wolf.DecideProbe().ToString();
        result["reportedMemoryFrozen"] = wolf.LastKnownThreatPosition == reportedPosition && !wolf.ThreatVisible;
        detectors[1].wallsBlockPerception = true;
        caller.DecideProbe();
        wolf.ResetProbe();
        wolf.ReceivePackCall(player, caller);
        result["unseenCallerIgnored"] = !wolf.HasThreatMemory && wolf.CurrentThreat == null;

        // 集火及支援入口在尚未首次切换状态时也必须锁住已确认的追踪意图。
        wolf.ResetIdleProbe();
        wolf.transform.position = Vector3.zero;
        player.transform.position = new Vector3(8f, 0f);
        detector.wallsBlockPerception = false;
        wolf.TriggerPackAttack(player);
        Vector3 triggeredPosition = wolf.LastKnownThreatPosition;
        detector.wallsBlockPerception = true;
        player.transform.position += new Vector3(0f, 6f);
        result["triggerBeforeDecisionState"] = wolf.DecideProbe().ToString();
        result["triggerBeforeDecisionMemoryFrozen"] = wolf.HasThreatMemory && !wolf.ThreatVisible &&
            wolf.LastKnownThreatPosition == triggeredPosition;

        wolf.ResetIdleProbe();
        detector.wallsBlockPerception = false;
        detectors[1].wallsBlockPerception = false;
        caller.DecideProbe();
        wolf.ReceivePackCall(player, caller);
        Vector3 calledPosition = wolf.LastKnownThreatPosition;
        detector.wallsBlockPerception = true;
        player.transform.position += new Vector3(0f, 6f);
        result["callBeforeDecisionState"] = wolf.DecideProbe().ToString();
        result["callBeforeDecisionMemoryFrozen"] = wolf.HasThreatMemory && !wolf.ThreatVisible &&
            wolf.LastKnownThreatPosition == calledPosition;
        return result;
    }

    #endregion
}

// 仅为规则采样开放受保护的绑定和决策入口，不加载或推进真实世界。
internal sealed class GameplayTestWolfPackProbeAI : AI_Wolf
{
    #region 正式狼群规则入口

    public void PrepareProbe(Mod_ItemDetector detector, Mod_DamageReceiver health)
    {
        _detector = detector;
        _hp = health;
        _isReady = true;
        _currentState = WolfState.Avoid;
        OnResetRuntimeState();
        OnBindExtraModules();
    }

    public void SetProbeReady(bool ready) => _isReady = ready;
    public void PrepareCombatProbe(Mod_Mover_AI mover)
    {
        _mover = mover;
        _currentState = WolfState.Chase;
        _stateMachine = new AIStateMachine<WolfState>();
        ConfigureStateNodes(_stateMachine);
        _stateMachine.Initialize(_currentState);
    }

    public WolfState TickCombatProbe(float deltaTime)
    {
        UpdateExtraTimers(deltaTime);
        OnPreEvaluate();
        WolfState next = EvaluateNextState();
        if (next != _currentState) SwitchState(next);
        _stateMachine.Tick(deltaTime);
        return _currentState;
    }

    public void RefreshProbe() => OnPreEvaluate();
    public WolfState EvaluateProbe() => EvaluateNextState();
    public WolfState DecideProbe()
    {
        OnPreEvaluate();
        WolfState next = EvaluateNextState();
        if (next != _currentState)
        {
            OnBeforeSwitchState(_currentState, next);
            _currentState = next;
        }
        return next;
    }
    public void TickTimersProbe(float deltaTime) => UpdateExtraTimers(deltaTime);
    public void ResetProbe() => OnResetRuntimeState();
    public void ResetIdleProbe()
    {
        _currentState = WolfState.Idle;
        OnResetRuntimeState();
    }

    #endregion
}

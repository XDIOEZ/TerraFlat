using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 狼群规则探针注册

    private static void RegisterWolfPackOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/wolf_pack", EntityAiWolfPack);
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
            playerHost.transform.position = new Vector3((float)Number(args, "playerX", 15d), 0f);
            for (int index = 0; index < count; index++)
            {
                var visible = new List<Item>(candidates) { candidates[0] };
                if (index == 0 || !Flag(args, "callOnly", false)) visible.Add(player);
                detectors[index].ApplyDetectorResults(0, visible);
                wolves[index].RefreshProbe();
            }
            wolves[0].TriggerPackAttack(player);
            var packCounts = new JArray();
            var leaders = new JArray();
            var states = new JArray();
            var threats = new JArray();
            foreach (GameplayTestWolfPackProbeAI ai in wolves)
            {
                ai.RefreshProbe();
                packCounts.Add(ai.PackMemberCount);
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
                ["packCounts"] = packCounts, ["leaders"] = leaders,
                ["states"] = states, ["threats"] = threats,
                ["detectorRadius"] = detectors[0].DetectionRadius
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
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
    public void RefreshProbe() => OnPreEvaluate();
    public WolfState EvaluateProbe() => EvaluateNextState();

    #endregion
}

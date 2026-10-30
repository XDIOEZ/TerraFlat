using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 熊的连续驻留与独立仇恨采样

    private static void RegisterBearProvocationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/bear_provocation_sequence", EntityBearProvocationSequence);
        operations.Add("ai/bear_definition_and_hit_credit", EntityBearDefinitionAndHitCredit);
    }

    // 夹具只编排生产账本的输入和采样，激怒门槛、衰减与加仇恨都由正式代码执行。
    private static JObject EntityBearProvocationSequence(JObject args)
    {
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 256)
            throw new ArgumentException("熊仇恨采样需要 1 至 256 个连续步骤。");

        var ledger = new AIProximityHatredLedger<long>();
        ledger.Configure(60f, 120f, 1f, 30f);
        var identities = new HashSet<long>();
        var samples = new JArray();
        foreach (JToken value in steps)
        {
            if (value is not JObject step) throw new ArgumentException("熊仇恨步骤必须是对象。");
            if (Flag(step, "clear", false)) ledger.Clear();
            float delta = (float)Number(step, "deltaTime", 0);
            if (Flag(step, "invalidDelta", false)) delta = float.NaN;
            bool deltaRejected = false;
            try { ledger.BeginUpdate(delta); }
            catch (ArgumentOutOfRangeException) { deltaRejected = true; }
            if (!deltaRejected)
            {
                foreach (long identity in ReadBearProbeIdentities(step, "nearby"))
                {
                    identities.Add(identity);
                    ledger.ObserveNearbyPlayer(identity);
                }
                ledger.EndUpdate();
                foreach (long identity in ReadBearProbeIdentities(step, "hits"))
                {
                    identities.Add(identity);
                    ledger.RecordHit(identity);
                }
                foreach (long identity in ReadBearProbeIdentities(step, "remove")) ledger.Remove(identity);
            }

            var hatred = new JObject();
            var presence = new JObject();
            foreach (long identity in identities)
            {
                string key = identity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                hatred[key] = ledger.GetHatred(identity);
                presence[key] = ledger.GetContinuousPresenceSeconds(identity);
            }
            var ineligible = new HashSet<long>(ReadBearProbeIdentities(step, "ineligible"));
            bool hasTarget = ledger.TryGetHighestHatredTarget(out long highestTarget);
            bool hasEligible = ledger.TryGetHighestHatredTarget(identity => !ineligible.Contains(identity), out long eligibleTarget);
            samples.Add(new JObject
            {
                ["hatred"] = hatred, ["presence"] = presence, ["deltaRejected"] = deltaRejected,
                ["highestTarget"] = hasTarget ? highestTarget : 0, ["eligibleTarget"] = hasEligible ? eligibleTarget : 0
            });
        }
        return new JObject { ["samples"] = samples };
    }

    private static IEnumerable<long> ReadBearProbeIdentities(JObject step, string field)
    {
        if (step[field] == null) yield break;
        if (step[field] is not JArray identities || identities.Count > 32)
            throw new ArgumentException("熊仇恨身份列表最多包含 32 项：" + field);
        foreach (JToken identity in identities)
        {
            if (identity.Type != JTokenType.Integer) throw new ArgumentException("熊仇恨身份必须是整数。");
            yield return identity.Value<long>();
        }
    }

    #endregion

    #region 正式熊配置装配与受击归属

    // 正式目录、模块身份与受击入口共用生产实现，独立夹具只提供玩家、武器和时间。
    private static JObject EntityBearDefinitionAndHitCredit(JObject args)
    {
        if (Application.isPlaying || ChunkMgr.ExistingInstance != null)
            throw new InvalidOperationException("熊受击规则只允许在未加载世界的编辑态执行。");
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        ItemDefinitionDto definition = definitions.Find(value => value.Id == "Bear") ??
            throw new InvalidOperationException("正式 Actor Manifest 缺少熊。");
#if UNITY_EDITOR
        GameObject shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
        GameObject aiPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/Gameplay/Modules/AI/Module_AnimalBehaviorTree.prefab");
#else
        GameObject shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
        GameObject aiPrefab = GameRes.ExistingInstance?.GetPrefab(definition.Modules["ai"].Prefab, false);
#endif
        var root = new GameObject("[LuaBearHitCredit]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var host = new GameObject("Bear");
            host.transform.SetParent(root.transform, false);
            GameItem bear = host.AddComponent<GameItem>();
            Data_GeneralItem bearData = EntityProbeData("test:bear_hit_credit", 250f);
            bearData.Guid = 1900000101;
            bearData.FactionId = definition.ItemData.Value<string>("factionId");
            bearData.Tags = new List<string> { "Bear", "Animal", "Omnivore" };
            bear.BindData(bearData);
            GameplayWorldRegistry.AssignItemToWorld(bear, "probe:bear-perception");
            bear.itemMods.BindOwner(bear);
            var ai = host.AddComponent<Mod_AnimalBehaviorTree>();
            RegisterModule("ai", ai);
            var detector = (Mod_ItemDetector)RegisterModule("detector");
            var health = (Mod_DamageReceiver)RegisterModule("health");
            var mover = (Mod_Mover_AI)RegisterModule("mover");
            Module damage = RegisterModule("damage");
            RegisterModule("animator");
            detector.wallsBlockPerception = false;

            GameItem first = CreatePlayer(1900000201, new Vector3(4f, 0f));
            GameItem second = CreatePlayer(1900000202, new Vector3(1f, 0f));
            GameItem weapon = CreateItem("Weapon", 1900000301);
            weapon.Owner = first;
            root.SetActive(true);
            ai.Load();
            AnimalTerritorialHatredCapability territory = RequireTerritory();
            JObject territoryConfig = ai.BehaviorTree.Capabilities["territory"] as JObject ??
                throw new InvalidOperationException("熊行为树缺少领地仇恨参数。");
            var result = new JObject
            {
                ["moduleId"] = ai._Data.ModuleId, ["prefabId"] = ai.PrefabId,
                ["identityBound"] = ReferenceEquals(bear.itemMods.GetMod_ByID<Mod_AnimalBehaviorTree>(ModText.AI), ai),
                ["hasDamage"] = damage is Mod_Damage,
                ["hasPack"] = ai.Context.TryGetCapability("pack", out AnimalPackCapability _) ||
                    ai.BehaviorTree.Capabilities["pack"] != null,
                ["hasWolfBite"] = definition.Modules.ContainsKey("biteAttackSkill"),
                ["hasWolfAudio"] = definition.Modules.ContainsKey("animalAudio"),
                ["hasFood"] = definition.Modules.ContainsKey("food"),
                ["spriteAddress"] = definition.Visual?.SpriteAddress ?? string.Empty,
                ["flipX"] = definition.Visual?.FlipX ?? false,
                ["radius"] = territoryConfig.Value<float>("presenceRadius"),
                ["threshold"] = territoryConfig.Value<float>("presenceThresholdSeconds"),
                ["initialHatred"] = territoryConfig.Value<float>("initialHatred"),
                ["decay"] = territoryConfig.Value<float>("decayPerSecond"),
                ["perHit"] = territoryConfig.Value<float>("hatredPerHit"), ["peacefulState"] = EvaluateAction()
            };
            CombatIdentity firstIdentity = GameplayCombatBridge.Identity(first);
            CombatIdentity weaponIdentity = GameplayCombatBridge.Identity(weapon);
            DamageReceiverDamageInfo blockedHit = Hit(weapon, first, 0f);
            result["resolvedOwner"] = AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(blockedHit)?.itemData.Guid ?? 0;
            territory.HandleConfirmedHit(blockedHit);
            result["zeroDamageHatred"] = territory.Ledger.GetHatred(firstIdentity);
            result["weaponHatred"] = territory.Ledger.GetHatred(weaponIdentity);
            result["zeroDamageTarget"] = territory.CurrentTarget?.itemData.Guid ?? 0;
            result["zeroDamageState"] = EvaluateAction();
            territory.HandleConfirmedHit(blockedHit);
            result["secondHitHatred"] = territory.Ledger.GetHatred(firstIdentity);

            territory.HandleConfirmedHit(Hit(second, second, 1f));
            territory.HandleConfirmedHit(Hit(second, second, 0f));
            territory.HandleConfirmedHit(Hit(second, second, 0f));
            result["highestHatredTarget"] = territory.CurrentTarget?.itemData.Guid ?? 0;
            result["highestHatredState"] = EvaluateAction();

            weapon.Owner = second;
            result["changedOwnerNotBlamed"] = AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(blockedHit) != second;
            DamageReceiverDamageInfo environmentHit = Hit(first, first, 0f);
            environmentHit.Context.Attack.Source.Backend = CombatBackend.Environment;
            territory.HandleConfirmedHit(environmentHit);
            result["environmentHatred"] = territory.Ledger.GetHatred(firstIdentity);
            Data_GeneralItem rebound = EntityProbeData("test:bear_rebound_player", 1f);
            rebound.Guid = 1900000203;
            rebound.FactionId = FactionRelationService.PlayerFactionId;
            rebound.Tags = new List<string> { "Player" };
            first.BindData(rebound);
            territory.Tick(0f, null);
            result["staleIdentityCleared"] = territory.Ledger.GetHatred(firstIdentity) == 0f;
            result["staleCreditRejected"] = AnimalTerritorialHatredCapability.ResolveRetaliationCreditActor(blockedHit) == null;
            territory.Tick(90f, null);
            result["expiredTarget"] = territory.CurrentTarget?.itemData.Guid ?? 0;
            result["expiredState"] = EvaluateAction();
            ai.Unload();
            ai.Load();
            territory = RequireTerritory();
            detector.wallsBlockPerception = true;
            result["hiddenFixture"] = !detector.HasLineOfSight(second);
            territory.Tick(61f, new Item[] { second });
            CombatIdentity secondIdentity = GameplayCombatBridge.Identity(second);
            result["hiddenPresence"] = territory.Ledger.GetContinuousPresenceSeconds(secondIdentity);
            result["hiddenHatred"] = territory.Ledger.GetHatred(secondIdentity);
            result["hiddenTarget"] = territory.CurrentTarget?.itemData.Guid ?? 0;
            detector.wallsBlockPerception = false;
            territory.Tick(0f, new Item[] { second });
            result["reappearedTarget"] = territory.CurrentTarget?.itemData.Guid ?? 0;
            result["reappearedState"] = EvaluateAction();
            return result;

            AnimalTerritorialHatredCapability RequireTerritory()
            {
                if (!ai.Context.TryGetCapability("territory", out AnimalTerritorialHatredCapability ability))
                    throw new InvalidOperationException("熊正式行为树未绑定领地仇恨能力。");
                return ability;
            }

            string EvaluateAction()
            {
                ai.ModUpdate(0f);
                return ai.CurrentAction ?? string.Empty;
            }

            Module RegisterModule(string slot, Module instance = null)
            {
                ItemModuleDefinitionDto configured = definition.Modules[slot];
                Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, slot == "ai" ? aiPrefab : null, configured.Prefab)
                    ?? throw new InvalidOperationException("熊规则缺少正式模块原型：" + slot);
                instance ??= UnityEngine.Object.Instantiate(prototype, host.transform, false);
                ModuleData data = FastCloner.FastCloner.DeepClone<object>(prototype._Data) as ModuleData
                    ?? throw new InvalidOperationException("熊规则缺少正式模块数据：" + slot);
                if (configured.Enabled.HasValue) data.Enabled = configured.Enabled.Value;
                instance._Data = data;
                instance.BindRuntimeIdentity(slot, configured.Id, configured.Prefab);
                ModuleJsonConfigurator.Apply(instance, definition.Id, slot, configured.Id, configured.Parameters?.ToString());
                bear.itemData.ModuleDataDic[slot] = data;
                instance.ModuleInit(bear, data);
                bear.itemMods.AddMod(instance);
                return instance;
            }

            GameItem CreateItem(string name, int guid)
            {
                var itemHost = new GameObject(name);
                itemHost.transform.SetParent(root.transform, false);
                GameItem instance = itemHost.AddComponent<GameItem>();
                Data_GeneralItem data = EntityProbeData("test:bear_" + name, 1f);
                data.Guid = guid;
                instance.BindData(data);
                GameplayWorldRegistry.AssignItemToWorld(instance, "probe:bear-perception");
                instance.itemMods.BindOwner(instance);
                return instance;
            }

            GameItem CreatePlayer(int guid, Vector3 position)
            {
                GameItem player = CreateItem("Player" + guid, guid);
                player.itemData.FactionId = FactionRelationService.PlayerFactionId;
                player.itemData.Tags = new List<string> { "Player" };
                player.transform.position = position;
                var hp = player.gameObject.AddComponent<Mod_DamageReceiver>();
                hp.BindRuntimeIdentity("health", ModText.Hp, "Mod_DamageReceiver");
                hp.ModuleInit(player, new Ex_ModData { ID = ModText.Hp, Enabled = true });
                hp.Data = new Mod_DamageReceiver.DamageReceiver_SaveData { Hp = 100, MaxHp = 100 };
                player.itemMods.AddMod(hp);
                return player;
            }

            DamageReceiverDamageInfo Hit(Item source, Item credit, float value) => new()
            {
                Attacker = source, DamageValue = value, ReceiverItem = bear,
                Context = new CombatDamageContext
                {
                    Attack = new CombatAttackKey { Source = GameplayCombatBridge.Identity(source) },
                    Credit = GameplayCombatBridge.Identity(credit)
                }
            };
        }
        finally
        {
            foreach (Mod_AnimalBehaviorTree ai in root.GetComponentsInChildren<Mod_AnimalBehaviorTree>(true)) ai.Unload();
            foreach (Item item in root.GetComponentsInChildren<Item>(true)) GameplayWorldRegistry.AssignItemToWorld(item, null);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion
}

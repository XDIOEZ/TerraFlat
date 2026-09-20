using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    /// <summary>当前 Actor/MOD 定义到纯值模板的冷路径编译器；不实例化旧 AI，不读取 P0 审计快照。</summary>
    public static class AiecsDefinitionCompiler
    {
        #region Actor 编译
        /// <summary>从当前已合并目录读取共同 AI 语义；默认使用主动战斗规则。</summary>
        public static AiecsActorTemplate Compile(string actorId, string faction, int definitionIndex, int factionIndex,
            float senseOverride, out AiecsDefinition definition)
        {
            return Compile(actorId, faction, definitionIndex, factionIndex, senseOverride, false, out definition);
        }

        /// <summary>从当前已合并目录读取共同 AI 语义；被动生态单位发现敌对目标后只逃离，不反向启用旧 AI。</summary>
        public static AiecsActorTemplate Compile(string actorId, string faction, int definitionIndex, int factionIndex,
            float senseOverride, bool fleeFromHostiles, out AiecsDefinition definition)
        {
            if (!GameRes.Instance.TryGetItemDefinition(actorId, out RuntimeItemDefinition source) || !source.IsActor)
                throw new InvalidOperationException("AIECS 找不到当前 Actor 定义：" + actorId);
            ItemData item = source.CreateItemData();
            JObject ai = Module<IAIActor>(source, item, out _);
            JObject detector = Module<Mod_ItemDetector>(source, item, out _);
            JObject mover = Module<Mover_AI>(source, item, out _);
            JObject health = Module<DamageReceiver>(source, item, out var healthModule);
            JObject attack = Module<Mod_Damage>(source, item, out _);
            if (ai == null || health == null || mover == null || detector == null ||
                (!fleeFromHostiles && attack == null))
                throw new InvalidOperationException(actorId + " 缺少本轮基础战斗切片所需模块；不得通过旧 AI 静默回退。");
            var life = health["Data"].ToObject<DamageReceiver.DamageReceiver_SaveData>();
            var body = Body(source, health, healthModule);
            float start = Number(ai, "attackTriggerDistance", 1.2f);
            float sense = senseOverride > 0f ? senseOverride : Number(ai, "chaseTriggerDistance", Number(detector, "detectionRadius", 10f));
            definition = new AiecsDefinition { Id = actorId, Faction = faction, LootTable = source.LootTableId ?? string.Empty,
                SenseRange = sense, ChaseRange = math.max(sense, Number(ai, "chaseLossDistance", sense * 1.5f)),
                ChaseRetryDelay = math.max(0.1f, Number(ai, "chasePathRetryDelay", 3f)),
                PerceptionPeriod = math.max(0.1f, Number(ai, "detectorRefreshInterval", 0.6f)), DecisionPeriod = 0.15f,
                FleeHealthRatio = Number(ai, "fleeTriggerHpRate", 0.22f), FleeSeconds = 3f, MemorySeconds = 4f,
                WanderRadius = Number(ai, "wanderRadius", 4f), WanderSeconds = 2.5f,
                IdleSeconds = (Number(ai, "idleMinDuration", 0.8f) + Number(ai, "idleMaxDuration", 2.2f)) * 0.5f,
                MoveSpeed = Speed(mover["Data"]?["Speed"]), AttackStartRange = start,
                HitRange = start * AI_AttackController.DefaultDamageRangeMultiplier, AttackArcCos = 0f,
                Windup = Number(ai, "attackDamageStartDelay", 0.5f), Active = Number(ai, "attackDamageWindow", 0.2f),
                Recovery = Number(ai, "attackRecoveryDuration", 0.35f), Cooldown = Number(ai, "attackCooldown", 2f),
                // 被动生态单位不会进入 Attack 规则，因此无需为了纯逃跑行为伪造旧攻击模块。
                Damage = GameplayCombatBridge.Values(attack?["DamageValues"]?.ToObject<CombatDamage>()),
                SlowMultiplier = Number(attack, "HitSlowMultiplier", 0.5f),
                SlowDuration = attack == null || (bool?)attack["EnableHitSlowdown"] == false
                    ? 0f
                    : Number(attack, "HitSlowDuration", 0.35f),
                CandidateBudget = 64, RequireLos = (byte)((bool?)detector["wallsBlockPerception"] == false ? 0 : 1) };
            AddRules(ref definition, fleeFromHostiles);
            JObject onHit = Module<DamageOnHitBuffApplier>(source, item, out _);
            if (onHit != null && !string.IsNullOrWhiteSpace((string)onHit["buffId"]))
                definition.OnHitBuffs.Add(new CombatOnHitBuff { Id = (string)onHit["buffId"],
                    Chance = Number(onHit, "applicationChance", 0.25f), Stacks = Mathf.Max(1, (int)Number(onHit, "applicationStacks", 1f)) });
            var anatomy = new AiecsAnatomy { TwoPartChance = life.TwoPartHitChance };
            // Actor 的版本 0 数据沿用旧后端升级为身体部位生命的规则。
            if (life.UseBodyPartHealth || life.BodyPartDataVersion == 0)
            {
                var parts = life.BodyParts != null && life.BodyParts.Count > 0 ? life.BodyParts : DamageReceiver.CreateDefaultBodyParts(life.Hp, life.MaxHp);
                foreach (BodyPartHealth part in parts) anatomy.Parts.Add(new AiecsBodyPart { Id = (int)part.Part, Hp = part.Hp,
                    MaxHp = part.MaxHp, Weight = part.AreaRatio * part.InjuryProbability });
            }
            float hp = life.Hp, maxHp = life.MaxHp;
            if (anatomy.Parts.Length > 0)
            {
                hp = 0f; maxHp = 0f;
                foreach (var part in anatomy.Parts) { hp += part.Hp; maxHp += part.MaxHp; }
            }
            return new AiecsActorTemplate { Definition = definitionIndex, Faction = factionIndex, Body = body,
                Vital = new AiecsVital { Hp = hp, MaxHp = maxHp, DamageInterval = life.DamageInterval,
                    ReceivedMultiplier = 1f, LastDamageTime = double.NegativeInfinity },
                Anatomy = anatomy, Defense = new AiecsDefense { Values = GameplayCombatBridge.Values(life.DefenseValues) },
                HasBlood = (byte)(item.Tags != null && item.Tags.Contains("Blood") ? 1 : 0) };
        }

        /// <summary>验证 Actor 是否满足当前正式 AIECS 基础切片，不创建旧 AI 实例。</summary>
        public static bool TryValidate(string actorId, out string reason)
        {
            return TryValidate(actorId, false, out reason);
        }

        /// <summary>按生态实际策略验证 Actor；纯逃跑单位不强制要求永远不会使用的攻击模块。</summary>
        public static bool TryValidate(string actorId, bool fleeFromHostiles, out string reason)
        {
            try
            {
                Compile(actorId, "aiecs.validation", 0, 0, 0f, fleeFromHostiles, out _);
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        /// <summary>无物种分支的共同优先级；生态配置只选择“主动战斗”或“受威胁逃离”策略。</summary>
        private static void AddRules(ref AiecsDefinition definition, bool fleeFromHostiles)
        {
            if (fleeFromHostiles)
            {
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.LowHealth | AiecsDecisionFacts.HasThreat, Behavior = (int)AiecsBehavior.Flee, Priority = 90 });
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Flee, Priority = 80 });
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.RestFinished, Exclude = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Wander, Priority = 10 });
                definition.Rules.Add(new AiecsDecisionRule { Behavior = (int)AiecsBehavior.Idle, Priority = 0 });
                return;
            }

            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.AttackLocked, Behavior = (int)AiecsBehavior.Attack, Priority = 100 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.LowHealth | AiecsDecisionFacts.HasThreat, Behavior = (int)AiecsBehavior.Flee, Priority = 90 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget | AiecsDecisionFacts.InAttackRange, Behavior = (int)AiecsBehavior.Attack, Priority = 70 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Chase, Priority = 50 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.RestFinished, Exclude = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Wander, Priority = 10 });
            definition.Rules.Add(new AiecsDecisionRule { Behavior = (int)AiecsBehavior.Idle, Priority = 0 });
        }

        /// <summary>复用正式模块定位规则并叠加当前 JSON 参数，不修改 Prefab 或创建运行时 Module。</summary>
        private static JObject Module<T>(RuntimeItemDefinition source, ItemData item, out T prototype) where T : class
        {
            foreach (var pair in item.ModuleDataDic)
            {
                string prefabId = source.GetModulePrefabId(pair.Key, pair.Value.ID);
                Module module = ItemDefinitionCatalogLoader.FindModulePrototype(source.ShellPrefab, GameRes.Instance.GetPrefab(prefabId, false), prefabId);
                if (!(module is T match)) continue;
                prototype = match;
                JObject parameters = JObject.Parse(JsonUtility.ToJson(match));
                if (source.TryGetModuleParameters(pair.Key, out string json) && !string.IsNullOrEmpty(json))
                    parameters.Merge(JObject.Parse(json), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                return parameters;
            }
            prototype = null; return null;
        }

        /// <summary>根感知形状和生命模块受击形状分别编译，禁止把感知脚底框当作完整受击框。</summary>
        private static AiecsBody Body(RuntimeItemDefinition source, JObject health, DamageReceiver prototype)
        {
            if (source.PerceptionShapes == null || source.PerceptionShapes.Count != 1)
                throw new InvalidOperationException(source.Id + " 需要单个圆/AABB 感知体型，复杂组合尚未迁移。");
            var perception = source.PerceptionShapes[0];
            BoxCollider2D box = prototype.GetComponent<BoxCollider2D>();
            JObject collider = health["$collider2D"] as JObject;
            if (box == null || (collider != null && (string)collider["type"] != "BoxCollider2D"))
                throw new InvalidOperationException(source.Id + " 的受击形状不是已支持的 BoxCollider2D。");
            float2 size = Pair(collider?["size"], box.size), offset = Pair(collider?["offset"], box.offset);
            JToken transform = health["$transform"];
            float2 scale = Pair(transform?["localScale"], prototype.transform.localScale);
            float2 translation = Pair(transform?["localPosition"], prototype.transform.localPosition);
            float angle = math.radians((float?)transform?["localEulerAngles"]?["z"] ?? prototype.transform.localEulerAngles.z);
            float2 x = new float2(math.cos(angle), math.sin(angle)) * scale.x;
            float2 y = new float2(-math.sin(angle), math.cos(angle)) * scale.y;
            var hit = PerceptionShape2D.Aabb(offset, size * 0.5f).Transform(translation, x, y);
            float radius = perception.IsCircle != 0 ? perception.Radius : math.cmax(perception.Extents);
            radius = math.max(0.05f, radius);
            if (radius >= 0.5f) throw new InvalidOperationException(source.Id + " 的体型需要额外通行配置，不能使用当前共享点格配置。");
            return new AiecsBody { Perception = perception, Hit = hit, Radius = radius, Facing = new float2(1, 0) };
        }

        /// <summary>读取当前参数中的数值，缺失时使用对应通用能力默认值。</summary>
        private static float Number(JToken value, string name, float fallback) => (float?)value?[name] ?? fallback;

        /// <summary>读取二维作者数据，保留未配置值。</summary>
        private static float2 Pair(JToken value, Vector2 fallback) => new float2((float?)value?["x"] ?? fallback.x, (float?)value?["y"] ?? fallback.y);

        /// <summary>编译 GameValue 的加法与乘法层级，禁止只读取基础速度而丢弃当前定义的修饰。</summary>
        private static float Speed(JToken value) => math.max(0.01f, value?.ToObject<GameValue_float>().Value ?? 3f);
        #endregion

        #region Buff 编译
        /// <summary>只注册已完整支持效果的当前 Buff，未支持定义由诊断列表显式报告。</summary>
        public static AiecsBuffDefinition[] CompileBuffs(List<string> unsupported)
        {
            var result = new List<AiecsBuffDefinition>();
            foreach (var pair in GameRes.Instance.BuffDefinitions)
            {
                var source = pair.Value;
                var definition = new AiecsBuffDefinition { Id = source.Id, Duration = source.DurationSeconds ?? float.PositiveInfinity,
                    Interval = source.TickIntervalSeconds, StackMode = (byte)source.StackMode, MaxStacks = source.MaxStacks };
                bool supported = true;
                foreach (var effect in source.Effects)
                {
                    if (effect.Phase != BuffEffectPhase.Tick || !string.IsNullOrEmpty(effect.RequiredTag)) { supported = false; break; }
                    if (effect.TypeId == "core:true_damage")
                    {
                        if (effect.ScaleWithStacks) definition.TrueDamagePerStack += effect.Value;
                        else definition.TrueDamage += effect.Value;
                    }
                    else if (effect.TypeId == "core:nutrition_change" && effect.TargetId == "water") definition.WaterDelta += effect.Value;
                    else { supported = false; break; }
                }
                if (!supported) { unsupported.Add(source.Id); continue; }
                if (source.Category == BuffCategory.BloodLoss && BloodLossBuffIds.TryGetTier(source.Id, out int tier)) definition.BleedingTier = tier;
                result.Add(definition);
            }
            for (int tier = 1; tier <= 3; tier++) if (!result.Exists(value => value.BleedingTier == tier))
                throw new InvalidOperationException("当前出血 " + tier + " 的效果尚未完整编译，不能静默跳过。");
            return result.ToArray();
        }
        #endregion
    }
}

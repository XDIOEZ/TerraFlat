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
    /// <summary>行为树与独立运动模块编译出的 ECS 参数快照。</summary>
    public struct AiecsBehaviorParameterSnapshot
    {
        public float SenseRange, ChaseRange, ChaseRetryDelay, PerceptionPeriod, DecisionPeriod, FleeHealthRatio, MemorySeconds;
        public float WanderRadius, IdleSeconds, MoveSpeed, AttackRange, Windup, Active, Recovery, Cooldown;
        public float NutritionMaximum, NutritionDrain, FeedBelow, FeedGain, SleepDayStart, SleepDayEnd, SleepHealth;
        public float FlightHeight, FlightDrain, FlightSpeed, FlightStamina, FlightRecovery;
        public float ReproductionDays, DarknessThreshold, DarknessRetreatRadius;
        public string EggItemId;
    }

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
            JObject ecs = source.ActorEcs;
            AiecsCapability capabilities = ParseCapabilities(actorId, ecs);
            ItemData item = source.CreateItemData();
            JObject ai = Module<IAIActor>(source, item, out _);
            JObject flight = Module<Mod_AnimalFlightMovement>(source, item, out _);
            JObject aquatic = Module<Mod_AquaticMovement>(source, item, out _);
            JObject colony = Module<Mod_ColonyResident>(source, item, out _);
            JObject light = Module<Mod_LightSensitiveNavigation>(source, item, out _);
            JObject detector = Module<Mod_ItemDetector>(source, item, out _);
            JObject mover = Module<Mod_Mover_AI>(source, item, out _);
            JObject health = Module<Mod_DamageReceiver>(source, item, out var healthModule);
            JObject attack = Module<Mod_Damage>(source, item, out _);
            if (health == null && ecs?["health"] == null)
                throw new InvalidOperationException(actorId + " 缺少 ecs.health 生命配置。");
            if ((capabilities & AiecsCapability.Combat) != 0 &&
                attack == null && ecs?["combat"]?["damage"] == null)
                throw new InvalidOperationException(actorId + " 缺少 ecs.combat.damage 攻击配置。");
            var life = health != null
                ? health["Data"].ToObject<Mod_DamageReceiver.DamageReceiver_SaveData>()
                : EcsLife(ecs["health"]);
            var body = Body(source, health, healthModule);
            AiecsBehaviorParameterSnapshot behavior = CompileBehaviorParameters(ai, ecs, detector, mover, flight, aquatic, colony, light, senseOverride);
            definition = new AiecsDefinition { Id = actorId, Faction = faction, LootTable = source.LootTableId ?? string.Empty,
                Capabilities = capabilities,
                SenseRange = behavior.SenseRange, ChaseRange = behavior.ChaseRange,
                ChaseRetryDelay = behavior.ChaseRetryDelay, PerceptionPeriod = behavior.PerceptionPeriod,
                DecisionPeriod = behavior.DecisionPeriod, FleeHealthRatio = behavior.FleeHealthRatio,
                FleeSeconds = 3f, MemorySeconds = behavior.MemorySeconds, WanderRadius = behavior.WanderRadius,
                WanderSeconds = 2.5f, IdleSeconds = behavior.IdleSeconds,
                MoveSpeed = behavior.MoveSpeed, AttackStartRange = behavior.AttackRange,
                HitRange = behavior.AttackRange * AI_AttackController.DefaultDamageRangeMultiplier, AttackArcCos = 0f,
                Windup = behavior.Windup, Active = behavior.Active, Recovery = behavior.Recovery, Cooldown = behavior.Cooldown,
                // 被动生态单位不会进入 Attack 规则，因此无需为了纯逃跑行为伪造旧攻击模块。
                Damage = attack != null ? GameplayCombatBridge.Values(attack["DamageValues"]?.ToObject<CombatDamage>()) :
                    GameplayCombatBridge.Values(ecs?["combat"]?["damage"]?.ToObject<CombatDamage>()),
                SlowMultiplier = Number(attack, "HitSlowMultiplier", 0.5f),
                SlowDuration = attack == null || (bool?)attack["EnableHitSlowdown"] == false
                    ? 0f
                    : Number(attack, "HitSlowDuration", 0.35f),
                CandidateBudget = 64, RequireLos = (byte)((bool?)detector?["wallsBlockPerception"] == false ? 0 : 1) };
            AddRules(ref definition, capabilities);
            JObject onHit = Module<Mod_DamageOnHitBuffApplier>(source, item, out _);
            if (onHit != null && !string.IsNullOrWhiteSpace((string)onHit["buffId"]))
                definition.OnHitBuffs.Add(new CombatOnHitBuff { Id = (string)onHit["buffId"],
                    Chance = Number(onHit, "applicationChance", 0.25f), Stacks = Mathf.Max(1, (int)Number(onHit, "applicationStacks", 1f)) });
            var anatomy = new AiecsAnatomy { TwoPartChance = life.TwoPartHitChance };
            // Actor 的版本 0 数据沿用旧后端升级为身体部位生命的规则。
            if (life.UseBodyPartHealth || life.BodyPartDataVersion == 0)
            {
                var parts = life.BodyParts != null && life.BodyParts.Count > 0 ? life.BodyParts : Mod_DamageReceiver.CreateDefaultBodyParts(life.Hp, life.MaxHp);
                foreach (BodyPartHealth part in parts) anatomy.Parts.Add(new AiecsBodyPart { Id = (int)part.Part, Hp = part.Hp,
                    MaxHp = part.MaxHp, Weight = part.AreaRatio * part.InjuryProbability });
            }
            float hp = life.Hp, maxHp = life.MaxHp;
            bool permanentFlight = behavior.FlightDrain <= 0f;
            return new AiecsActorTemplate { Definition = definitionIndex, Faction = factionIndex, Body = body,
                Capabilities = capabilities,
                Nutrition = new AiecsNutrition
                {
                    Maximum = behavior.NutritionMaximum, Current = behavior.NutritionMaximum,
                    DrainPerSecond = behavior.NutritionDrain, FeedBelow = behavior.FeedBelow, FeedGain = behavior.FeedGain,
                    Resource = string.Equals((string)ecs?["nutrition"]?["resource"], "nectar",
                        StringComparison.OrdinalIgnoreCase) ? AiecsFoodResource.Nectar : AiecsFoodResource.Grass,
                    CanFeed = (byte)((capabilities & AiecsCapability.Feeding) != 0 ? 1 : 0)
                },
                Sleep = new AiecsSleep
                {
                    DayStartRatio = behavior.SleepDayStart, DayEndRatio = behavior.SleepDayEnd, HealthBelow = behavior.SleepHealth
                },
                Flight = new AiecsFlight
                {
                    Height = permanentFlight ? behavior.FlightHeight : 0f,
                    TargetHeight = permanentFlight ? behavior.FlightHeight : 0f,
                    CruiseHeight = behavior.FlightHeight,
                    Speed = behavior.FlightSpeed,
                    GroundSpeed = definition.MoveSpeed,
                    Stamina = behavior.FlightStamina, StaminaMaximum = behavior.FlightStamina,
                    DrainPerSecond = behavior.FlightDrain, RecoveryPerSecond = behavior.FlightRecovery,
                    TakeoffRecoveryRatio = Number(ecs?["flight"], "takeoffRecoveryRatio", 0.5f),
                    TakeoffChancePerSecond = Number(ecs?["flight"], "takeoffChancePerSecond", 0.25f),
                    BaseWaterPushSpeed = WaterCurrentPushConfigService.ResolvePushSpeed(actorId, item.Stack.Weight),
                    Airborne = (byte)(permanentFlight ? 1 : 0),
                    Recovering = 0
                },
                Reproduction = new AiecsReproduction
                {
                    CooldownDays = behavior.ReproductionDays,
                    EggItemId = new FixedString64Bytes(behavior.EggItemId),
                    EggLaying = (byte)((capabilities & AiecsCapability.EggLaying) != 0 ? 1 : 0)
                },
                Darkness = new AiecsDarkness
                {
                    DamageThreshold = behavior.DarknessThreshold,
                    DamageFractionPerSecond = Number(ecs?["darkness"], "damageFractionPerSecond", 0.05f),
                    RetreatRadius = behavior.DarknessRetreatRadius
                },
                Tactics = new AiecsTactics
                {
                    Flags = capabilities,
                    BaseSpeed = definition.MoveSpeed,
                    ChargeMultiplier = Number(ecs?["charge"], "speedMultiplier", 1.8f),
                    ChargeSeconds = Number(ecs?["charge"], "durationSeconds", 1.2f),
                    ChargeCooldown = Number(ecs?["charge"], "cooldownSeconds", 5f),
                    PredatorMultiplier = Number(ecs?["predator"], "chaseSpeedMultiplier", 1.15f)
                },
                Pack = new AiecsPack
                {
                    AssistRadius = Number(ecs?["pack"], "assistRadius", 8f),
                    CohesionRadius = Number(ecs?["pack"], "cohesionRadius", 3f),
                    CandidateBudget = Mathf.Max(1, (int)Number(ecs?["pack"], "candidateBudget", 32f))
                },
                WaterCurrentPushSpeed = WaterCurrentPushConfigService.ResolvePushSpeed(actorId, item.Stack.Weight),
                MassKg = item.Stack.CurrentWeight,
                Vital = new AiecsVital { Hp = hp, MaxHp = maxHp, DamageInterval = life.DamageInterval,
                    ReceivedMultiplier = 1f, LastDamageTime = double.NegativeInfinity },
                Anatomy = anatomy, Defense = new AiecsDefense { Values = GameplayCombatBridge.Values(life.DefenseValues) },
                HasBlood = (byte)(item.Tags != null && item.Tags.Contains("Blood") ? 1 : 0) };
        }

        /// <summary>从最终合并配置提取通用行为参数，供运行编译与规则测试共用。</summary>
        public static AiecsBehaviorParameterSnapshot CompileBehaviorParameters(JObject ai, JObject ecs,
            JObject detector, JObject mover, JObject flight, JObject aquatic, JObject colony, JObject light,
            float senseOverride = 0f)
        {
            JToken tree = ai?["behaviorTree"], behavior = tree?["capabilities"];
            JToken combat = behavior?["combat"], pack = behavior?["pack"], hunting = behavior?["hunting"];
            JToken territory = behavior?["territory"], threat = behavior?["threat"], wander = behavior?["wander"];
            JToken chase = behavior?["chase"], forage = behavior?["forage"], sleep = behavior?["sleep"];
            JToken production = behavior?["production"];
            float defaultSense = Number(territory, "chaseDistance", Number(hunting, "hungryRange",
                Number(pack, "chaseDistance", Number(threat, "radius", Number(aquatic, "fleeTriggerDistance",
                    Number(light, "perceptionRadius", Number(detector, "detectionRadius", 10f)))))));
            float sense = senseOverride > 0f ? senseOverride : Number(ecs?["perception"], "radius", defaultSense);
            float chaseRange = Number(territory, "chaseDistance", Number(hunting, "lossRange",
                Number(pack, "lossDistance", sense * 1.5f)));
            float idle = (Number(wander, "idleMin", .8f) + Number(wander, "idleMax", 2.2f)) * .5f;
            if (wander == null && flight != null) idle = Number(flight, "groundDuration", idle);
            float maximum = Number(ecs?["nutrition"], "maximum", Number(colony, "SatietyMaximum", 100f));
            bool permanentFlight = (bool?)flight?["permanentFlight"] == true;
            return new AiecsBehaviorParameterSnapshot
            {
                SenseRange = sense, ChaseRange = math.max(sense, chaseRange),
                ChaseRetryDelay = math.max(.1f, Number(chase, "retryDelay", 3f)),
                PerceptionPeriod = math.max(.1f, Number(ai, "detectorRefreshInterval", .6f)),
                DecisionPeriod = Number(pack, "decisionInterval", .15f),
                FleeHealthRatio = FindTreeConditionNumber(tree, "land.healthBelow", "enter", .22f),
                MemorySeconds = Number(behavior?["memory"], "searchDuration", 4f),
                WanderRadius = Number(wander, "radius", Number(aquatic, "wanderRadius",
                    Number(flight, "groundWanderRadius", Number(light, "wanderRadius", 4f)))),
                IdleSeconds = idle, MoveSpeed = Number(ecs?["movement"], "speed",
                    Number(aquatic, "swimSpeed", Speed(mover?["Data"]?["Speed"]))),
                AttackRange = Number(combat, "range", Number(colony, "StingDistance", 1.2f)),
                Windup = Number(combat, "windup", .5f), Active = Number(combat, "damageWindow", .2f),
                Recovery = Number(combat, "recovery", .35f), Cooldown = Number(combat, "cooldown", Number(colony, "StingInterval", 2f)),
                NutritionMaximum = maximum,
                NutritionDrain = Number(ecs?["nutrition"], "drainPerSecond", Number(colony, "SatietyDrainPerSecond", .02f)),
                FeedBelow = Number(ecs?["nutrition"], "feedBelow", Number(colony, "ForageBelow", Number(forage, "enterHunger", .35f) * maximum)),
                FeedGain = Number(ecs?["nutrition"], "feedGain", Number(colony, "HoneyMealGain", 25f)),
                SleepDayStart = Number(ecs?["sleep"], "dayStartRatio", Number(sleep, "dayStart", .25f)),
                SleepDayEnd = Number(ecs?["sleep"], "dayEndRatio", Number(sleep, "dayEnd", .75f)),
                SleepHealth = Number(ecs?["sleep"], "healthBelow", Number(sleep, "enterHp", .5f)),
                FlightHeight = Number(ecs?["flight"], "height", Number(flight, "flightHeight", .8f)),
                FlightDrain = Number(ecs?["flight"], "staminaDrainPerSecond", permanentFlight ? 0f : Number(flight, "flightStaminaDrainRate", 1f)),
                FlightSpeed = Number(ecs?["flight"], "speed", Number(flight, "flightSpeed", 3.2f)),
                FlightStamina = Number(ecs?["flight"], "staminaMaximum", Number(flight, "flightStaminaMax", 100f)),
                FlightRecovery = Number(ecs?["flight"], "staminaRecoveryPerSecond", Number(flight, "flightStaminaRecoveryRate", 10f)),
                ReproductionDays = Number(ecs?["reproduction"], "cooldownDays", Number(production, "intervalDays", Number(flight, "layEggMinimumIntervalDays", 2f))),
                EggItemId = (string)ecs?["reproduction"]?["eggItemId"] ?? (string)production?["itemId"] ?? (string)flight?["eggItemId"] ?? "Egg",
                DarknessThreshold = Number(ecs?["darkness"], "damageThreshold", Number(light, "lightDamageThreshold", .5f)),
                DarknessRetreatRadius = Number(ecs?["darkness"], "retreatRadius", Number(light, "retreatSearchRadius", 5f)),
            };
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
        private static void AddRules(ref AiecsDefinition definition, AiecsCapability capabilities)
        {
            bool chase = (capabilities & AiecsCapability.Chase) != 0;
            bool flee = (capabilities & AiecsCapability.Flee) != 0;
            bool combat = (capabilities & AiecsCapability.Combat) != 0;
            if (!chase)
            {
                if (flee)
                {
                    definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.LowHealth | AiecsDecisionFacts.HasThreat, Behavior = (int)AiecsBehavior.Flee, Priority = 90 });
                    definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Flee, Priority = 80 });
                }
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.RestFinished, Exclude = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Wander, Priority = 10 });
                definition.Rules.Add(new AiecsDecisionRule { Behavior = (int)AiecsBehavior.Idle, Priority = 0 });
                return;
            }

            if (combat)
            {
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.AttackLocked, Behavior = (int)AiecsBehavior.Attack, Priority = 100 });
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget | AiecsDecisionFacts.InAttackRange, Behavior = (int)AiecsBehavior.Attack, Priority = 70 });
            }
            if (flee)
                definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.LowHealth | AiecsDecisionFacts.HasThreat, Behavior = (int)AiecsBehavior.Flee, Priority = 90 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Chase, Priority = 50 });
            definition.Rules.Add(new AiecsDecisionRule { Require = AiecsDecisionFacts.RestFinished, Exclude = AiecsDecisionFacts.HasTarget, Behavior = (int)AiecsBehavior.Wander, Priority = 10 });
            definition.Rules.Add(new AiecsDecisionRule { Behavior = (int)AiecsBehavior.Idle, Priority = 0 });
        }

        /// <summary>能力名称集中校验，内容作者只需组合已有模块并填写参数。</summary>
        private static AiecsCapability ParseCapabilities(string actorId, JObject ecs)
        {
            if (ecs?["capabilities"] is not JArray list || list.Count == 0)
                throw new InvalidOperationException(actorId + " 缺少 ecs.capabilities 能力列表。");
            AiecsCapability result = AiecsCapability.None;
            foreach (JToken token in list)
            {
                string name = (string)token;
                if (string.IsNullOrWhiteSpace(name) ||
                    !Enum.TryParse(name, true, out AiecsCapability capability) ||
                    capability == AiecsCapability.None ||
                    (result & capability) != 0)
                    throw new InvalidOperationException(actorId + " 包含无效或重复的 ECS 能力：" + name);
                result |= capability;
            }
            if ((result & (AiecsCapability.Movement | AiecsCapability.Perception)) !=
                (AiecsCapability.Movement | AiecsCapability.Perception))
                throw new InvalidOperationException(actorId + " 必须组合 movement 与 perception。");
            if ((result & AiecsCapability.Feeding) != 0 && (result & AiecsCapability.Nutrition) == 0)
                throw new InvalidOperationException(actorId + " feeding 依赖 nutrition。");
            if ((result & AiecsCapability.EggLaying) != 0 && (result & AiecsCapability.Reproduction) == 0)
                throw new InvalidOperationException(actorId + " eggLaying 依赖 reproduction。");
            return result;
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
                    MergeConfiguredModuleParameters(parameters, JObject.Parse(json));
                return parameters;
            }
            prototype = null; return null;
        }

        /// <summary>合并模块参数时保留攻击数值的显式赋值顺序。</summary>
        public static JObject MergeConfiguredModuleParameters(JObject parameters, JObject overrides)
        {
            parameters.Merge(overrides, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            if (parameters["DamageValues"] is not JObject damage || overrides["DamageValues"] is not JObject authored) return parameters;
            // 物理总量和四槽会互相覆写，必须先保留原型，再按配置顺序应用作者字段。
            foreach (JProperty property in authored.Properties())
            {
                JToken value = damage[property.Name]?.DeepClone();
                damage.Remove(property.Name);
                damage.Add(property.Name, value);
            }
            return parameters;
        }

        /// <summary>新物种可直接从 ECS 配置获得生命值，不要求挂旧 Mod_DamageReceiver 模块。</summary>
        private static Mod_DamageReceiver.DamageReceiver_SaveData EcsLife(JToken health)
        {
            float maximum = math.max(0.001f, Number(health, "maxHp", 50f));
            JToken defense = health?["defense"];
            return new Mod_DamageReceiver.DamageReceiver_SaveData
            {
                Hp = math.clamp(Number(health, "hp", maximum), 0.001f, maximum),
                MaxHp = maximum,
                DamageInterval = math.max(0f, Number(health, "damageInterval", 0.1f)),
                BodyPartDataVersion = 1,
                TwoPartHitChance = 0f,
                DefenseValues = defense?.ToObject<CombatDefense>() ?? new CombatDefense()
            };
        }

        /// <summary>旧作者受击盒与纯 ECS 感知体型统一编译，后者默认同时用作受击盒。</summary>
        private static AiecsBody Body(RuntimeItemDefinition source, JObject health, Mod_DamageReceiver prototype)
        {
            if (source.PerceptionShapes == null || source.PerceptionShapes.Count != 1)
                throw new InvalidOperationException(source.Id + " 需要单个圆/AABB 感知体型，复杂组合尚未迁移。");
            var perception = source.PerceptionShapes[0];
            var hit = perception;
            if (health != null && prototype != null)
            {
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
                hit = PerceptionShape2D.Aabb(offset, size * 0.5f).Transform(translation, x, y);
            }
            float radius = perception.IsCircle != 0 ? perception.Radius : math.cmax(perception.Extents);
            radius = math.max(0.05f, radius);
            if (radius >= 0.5f) throw new InvalidOperationException(source.Id + " 的体型需要额外通行配置，不能使用当前共享点格配置。");
            return new AiecsBody { Perception = perception, Hit = hit, Radius = radius, Facing = new float2(1, 0) };
        }

        /// <summary>读取当前参数中的数值，缺失时使用对应通用能力默认值。</summary>
        private static float Number(JToken value, string name, float fallback) => (float?)value?[name] ?? fallback;

        /// <summary>从行为树守卫读取低血等决策阈值，不依赖物种名称。</summary>
        private static float FindTreeConditionNumber(JToken tree, string condition, string parameter, float fallback)
        {
            AnimalBehaviorTreeDefinition definition = tree?.ToObject<AnimalBehaviorTreeDefinition>();
            return TryFindTreeConditionNumber(definition?.Root, definition?.Subtrees, new JObject(),
                new HashSet<string>(StringComparer.Ordinal), condition, parameter, out float value) ? value : fallback;
        }

        private static bool TryFindTreeConditionNumber(AnimalBehaviorTreeNodeDefinition node,
            Dictionary<string, AnimalBehaviorSubtreeDefinition> localSubtrees, JObject arguments, HashSet<string> ancestors,
            string condition, string parameter, out float value)
        {
            value = default;
            if (node == null) return false;
            if (node.Type == "condition" && node.Name == condition)
            {
                JToken number = ResolveTreeArgument(node.Parameters?[parameter], arguments);
                if (number?.Type == JTokenType.Float || number?.Type == JTokenType.Integer)
                { value = number.Value<float>(); return true; }
            }
            if (node.Type == "subtree" && !string.IsNullOrEmpty(node.Name) && ancestors.Add(node.Name))
            {
                AnimalBehaviorSubtreeDefinition subtree = null;
                if (localSubtrees != null) localSubtrees.TryGetValue(node.Name, out subtree);
                subtree ??= AnimalBehaviorTreeNodeCatalog.GetSharedSubtree(node.Name);
                if (subtree != null)
                {
                    JObject supplied = (JObject)(subtree.Parameters?.DeepClone() ?? new JObject());
                    if (node.Parameters != null)
                        foreach (JProperty property in node.Parameters.Properties())
                            supplied[property.Name] = ResolveTreeArgument(property.Value, arguments)?.DeepClone();
                    bool found = TryFindTreeConditionNumber(subtree.Root, localSubtrees, supplied, ancestors,
                        condition, parameter, out value);
                    ancestors.Remove(node.Name);
                    if (found) return true;
                }
                else ancestors.Remove(node.Name);
            }
            if (node.Children != null)
                foreach (AnimalBehaviorTreeNodeDefinition child in node.Children)
                    if (TryFindTreeConditionNumber(child, localSubtrees, arguments, ancestors, condition, parameter, out value)) return true;
            return false;
        }

        private static JToken ResolveTreeArgument(JToken value, JObject arguments)
        {
            string text = value?.Type == JTokenType.String ? value.Value<string>() : null;
            return text != null && text.StartsWith("$", StringComparison.Ordinal) ? arguments[text.Substring(1)] : value;
        }

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

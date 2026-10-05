using FlatWorld.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace FlatWorld.AIECS
{
    #region 进食请求

    public enum AiecsFeedOperation : byte { Search, Consume }

    public struct AiecsFeedRequest
    {
        public Entity Entity;
        public CombatIdentity Identity;
        public float2 Position;
        public int2 FoodCell;
        public int FoodGuid;
        public AiecsFoodResource Resource;
        public AiecsFoodTarget TargetKind;
        public AiecsFeedOperation Operation;
    }

    /// <summary>营养与觅食意图统一按带能力的查询更新，资源扣减交给 Gameplay 边界。</summary>
    [BurstCompile]
    internal partial struct AiecsNutritionJob : IJobEntity
    {
        public CombatClock Clock;
        public WorldTopologyDomain Domain;
        public NativeQueue<AiecsFeedRequest>.ParallelWriter Requests;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsSimulationPulse pulse, in AiecsFlowAgent actor,
            ref AiecsNutrition nutrition, ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            nutrition.Current = math.max(0f, nutrition.Current - nutrition.DrainPerSecond * pulse.DeltaTime);
            if (nutrition.CanFeed == 0) return;
            if (nutrition.Current >= nutrition.FeedBelow)
            {
                nutrition.HasFoodTarget = 0;
                return;
            }
            if (nutrition.HasFoodTarget != 0)
            {
                if (math.lengthsq(Domain.ShortestDelta(actor.Position, nutrition.FoodPosition)) <= 0.36f)
                {
                    Requests.Enqueue(new AiecsFeedRequest { Entity = entity, Identity = identity.Key,
                        Position = actor.Position, FoodCell = nutrition.FoodCell,
                        FoodGuid = nutrition.FoodGuid, Resource = nutrition.Resource,
                        TargetKind = nutrition.TargetKind,
                        Operation = AiecsFeedOperation.Consume });
                    nutrition.HasFoodTarget = 0;
                    nutrition.NextSearchTime = Clock.Time + 0.5d;
                    proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Idle, Priority = 60 };
                }
                else
                {
                    proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                        Priority = 60, HasDestination = 1, Destination = nutrition.FoodPosition };
                }
                return;
            }
            if (Clock.Time < nutrition.NextSearchTime) return;
            Requests.Enqueue(new AiecsFeedRequest { Entity = entity, Identity = identity.Key,
                Position = actor.Position, Resource = nutrition.Resource,
                Operation = AiecsFeedOperation.Search });
            nutrition.NextSearchTime = Clock.Time + 1d;
        }
    }

    #endregion

    #region 群体与战术能力

    /// <summary>同物种同阵营共享已发现的目标；无敌情时保持松散聚群。</summary>
    [BurstCompile]
    internal partial struct AiecsPackJob : IJobEntity
    {
        [ReadOnly] public AiecsSpatialView Spatial;
        [ReadOnly] public ComponentLookup<AiecsBrain> Brains;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsBrain brain, in AiecsFlowAgent actor, in AiecsPack pack,
            ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0 || brain.Target != Entity.Null)
                return;
            float radius = math.max(1f, pack.AssistRadius);
            Spatial.BucketRange(actor.Position, new float2(radius), out int2 first, out int2 size);
            int budget = math.max(1, pack.CandidateBudget);
            float nearestAlly = float.PositiveInfinity;
            float2 allyPosition = default;
            float nearestTarget = float.PositiveInfinity;
            float2 targetPosition = default;
            for (int cell = 0; cell < size.x * size.y && budget > 0; cell++)
            {
                int2 bucket = Spatial.NormalizeBucket(first + new int2(cell % size.x, cell / size.x));
                if (!Spatial.Buckets.TryGetFirstValue(new int3(bucket, identity.Faction),
                        out int index, out var iterator))
                    continue;
                do
                {
                    if (--budget < 0) break;
                    AiecsTargetSample ally = Spatial.Samples[index];
                    if (ally.Entity == entity || ally.Dead != 0 ||
                        ally.Identity.Definition != identity.Definition || !Brains.HasComponent(ally.Entity))
                        continue;
                    float distance = math.lengthsq(Spatial.Domain.ShortestDelta(actor.Position, ally.Position));
                    if (distance > radius * radius) continue;
                    if (distance < nearestAlly)
                    {
                        nearestAlly = distance;
                        allyPosition = ally.Position;
                    }
                    AiecsBrain mate = Brains[ally.Entity];
                    if (mate.Target == Entity.Null ||
                        !Spatial.TryTarget(mate.Target, mate.TargetKey, out AiecsTargetSample threat) ||
                        distance >= nearestTarget)
                        continue;
                    nearestTarget = distance;
                    targetPosition = threat.Position;
                } while (budget > 0 && Spatial.Buckets.TryGetNextValue(out index, ref iterator));
            }
            if (nearestTarget < float.PositiveInfinity && proposal.Priority < 55)
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                    Priority = 55, HasDestination = 1, Destination = targetPosition };
            else if (nearestAlly < float.PositiveInfinity &&
                nearestAlly > pack.CohesionRadius * pack.CohesionRadius && proposal.Priority < 15)
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                    Priority = 15, HasDestination = 1, Destination = allyPosition };
        }
    }

    /// <summary>冲锋和猎食追击共享移动修正，冷却与倍率由 Actor 模块配置。</summary>
    [BurstCompile]
    internal partial struct AiecsTacticsJob : IJobEntity
    {
        public double Time;

        private void Execute(in AiecsIdentity identity, in AiecsVital vital,
            in AiecsSimulationPulse pulse, in AiecsBehaviorIntent intent,
            ref AiecsTactics tactics, ref AiecsFlowAgent actor)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            bool chasing = intent.Behavior == (int)AiecsBehavior.Chase;
            tactics.ChargeRemaining = math.max(0f, tactics.ChargeRemaining - pulse.DeltaTime);
            if ((tactics.Flags & AiecsCapability.Charge) != 0 && chasing &&
                tactics.ChargeRemaining <= 0f && Time >= tactics.NextChargeTime)
            {
                tactics.ChargeRemaining = math.max(0.1f, tactics.ChargeSeconds);
                tactics.NextChargeTime = Time + tactics.ChargeRemaining +
                    math.max(0f, tactics.ChargeCooldown);
            }
            float multiplier = tactics.ChargeRemaining > 0f ? tactics.ChargeMultiplier :
                (tactics.Flags & AiecsCapability.Predator) != 0 && chasing
                    ? tactics.PredatorMultiplier : 1f;
            actor.Speed = math.max(0.01f, tactics.BaseSpeed * math.max(0.01f, multiplier));
        }
    }

    #endregion

    #region 避光能力

    /// <summary>受光判定和避光目标来自环境桥；伤害仍走统一 ECS 命中结算。</summary>
    [BurstCompile]
    internal partial struct AiecsDarknessJob : IJobEntity
    {
        public CombatClock Clock;
        public NativeQueue<AiecsHitEvent>.ParallelWriter Hits;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsFlowAgent actor, in AiecsBrain brain,
            ref AiecsDarkness darkness, ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            if (brain.Target == Entity.Null && darkness.LightLevel > 0.0001f && proposal.Priority < 85)
                proposal = darkness.HasDarkDestination != 0
                    ? new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                        Priority = 85, HasDestination = 1, Destination = darkness.DarkDestination }
                    : new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Idle, Priority = 85 };
            if (darkness.LightLevel <= darkness.DamageThreshold)
            {
                darkness.NextDamageTime = 0d;
                return;
            }
            if (darkness.NextDamageTime <= 0d)
            {
                darkness.NextDamageTime = Clock.Time + 1d;
                return;
            }
            if (Clock.Time < darkness.NextDamageTime) return;
            darkness.NextDamageTime = Clock.Time + 1d;
            CombatIdentity source = identity.Key;
            source.Backend = CombatBackend.Environment;
            Hits.Enqueue(new AiecsHitEvent { Target = entity, TargetKey = identity.Key,
                Context = new CombatDamageContext
                {
                    Attack = new CombatAttackKey { Source = source, Sequence = (uint)Clock.Tick,
                        Window = (uint)(Clock.Tick >> 32), Pulse = 0xDA11u },
                    Clock = Clock,
                    Damage = new float4(0f, 0f, 0f,
                        vital.MaxHp * math.max(0f, darkness.DamageFractionPerSecond)),
                    IsTrueDamage = 1,
                    Origin = actor.Position,
                    HitPoint = actor.Position,
                    BuildingMultiplier = 1f
                } });
        }
    }

    #endregion

    #region 生产能力

    public struct AiecsProductionRequest
    {
        public Entity Entity;
        public CombatIdentity Identity;
        public float2 Position;
        public FixedString64Bytes ItemId;
    }

    /// <summary>产物时钟按世界天数统一推进；具体掉落只在 Gameplay 边界创建。</summary>
    [BurstCompile]
    internal partial struct AiecsReproductionJob : IJobEntity
    {
        public double GameDays;
        public NativeQueue<AiecsProductionRequest>.ParallelWriter Requests;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsFlowAgent actor, ref AiecsReproduction reproduction)
        {
            if (identity.External != 0 || vital.Dead != 0 || reproduction.EggLaying == 0)
                return;
            if (reproduction.NextBirthTime <= 0d)
            {
                reproduction.NextBirthTime = GameDays + math.max(0.1f, reproduction.CooldownDays);
                return;
            }
            if (GameDays < reproduction.NextBirthTime) return;
            reproduction.NextBirthTime = GameDays + math.max(0.1f, reproduction.CooldownDays);
            Requests.Enqueue(new AiecsProductionRequest { Entity = entity, Identity = identity.Key,
                Position = actor.Position, ItemId = reproduction.EggItemId });
        }
    }

    #endregion

    #region 蜂巢成员

    /// <summary>归巢、防守与领地巡逻以同一能力驱动，蜂巢只更新共享的状态值。</summary>
    [BurstCompile]
    internal partial struct AiecsHiveJob : IJobEntity
    {
        public WorldTopologyDomain Domain;
        public double Time;

        private void Execute(Entity entity, in AiecsIdentity identity, in AiecsVital vital,
            in AiecsSimulationPulse pulse, in AiecsBrain brain, in AiecsFlowAgent actor,
            in AiecsNutrition nutrition,
            ref AiecsHiveMember hive, ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            if (hive.HasDefenseTarget != 0)
            {
                hive.DefenseRemaining = math.max(0f, hive.DefenseRemaining - pulse.DeltaTime);
                if (hive.DefenseRemaining <= 0f) hive.HasDefenseTarget = 0;
                else if (brain.Target == Entity.Null)
                {
                    proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                        Priority = 90, HasDestination = 1, Destination = hive.DefensePosition };
                    return;
                }
            }
            if (hive.HomeGuid == 0) return;
            if (hive.ReturnHome != 0 || hive.CarryingHoney != 0)
            {
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                    Priority = 97, HasDestination = 1, Destination = hive.Home };
                return;
            }
            if (nutrition.Current < nutrition.FeedBelow && nutrition.HasFoodTarget == 0 &&
                proposal.Priority < 65)
            {
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                    Priority = 65, HasDestination = 1, Destination = hive.Home };
                return;
            }
            if (brain.Target != Entity.Null || hive.Alert >= 1f) return;
            float phase = (float)(Time * 0.35d) + entity.Index * 1.618f;
            float2 center = hive.Home;
            float radius = math.max(0.5f, hive.PatrolRadius * 0.7f);
            float2 target = Domain.Normalize(center + new float2(math.cos(phase), math.sin(phase)) * radius);
            if (proposal.Priority < 12 &&
                math.lengthsq(Domain.ShortestDelta(actor.Position, target)) > 0.16f)
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Custom,
                    Priority = 12, HasDestination = 1, Destination = target };
        }
    }

    #endregion

    #region 睡眠与飞行

    /// <summary>睡眠只给共享决策系统提交高优先级待机意图，受伤或敌人出现即唤醒。</summary>
    [BurstCompile]
    internal partial struct AiecsSleepJob : IJobEntity
    {
        public float DayRatio;
        public double Time;

        private void Execute(in AiecsIdentity identity, in AiecsVital vital, in AiecsBrain brain,
            ref AiecsSleep sleep, ref AiecsBehaviorProposal proposal)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            bool day = DayRatio >= sleep.DayStartRatio && DayRatio <= sleep.DayEndRatio;
            bool threatened = brain.Target != Entity.Null || Time - vital.LastDamageTime < 3d;
            sleep.Sleeping = (byte)((!day || vital.Hp < vital.MaxHp * sleep.HealthBelow) && !threatened ? 1 : 0);
            if (sleep.Sleeping != 0)
                proposal = new AiecsBehaviorProposal { Behavior = (int)AiecsBehavior.Idle, Priority = 95 };
        }
    }

    /// <summary>同一飞行模块处理鸟与蜂的高度、耐力和水流暴露。</summary>
    [BurstCompile]
    internal partial struct AiecsFlightJob : IJobEntity
    {
        private void Execute(in AiecsIdentity identity, in AiecsVital vital,
            in AiecsSimulationPulse pulse, in AiecsBehaviorIntent intent,
            ref AiecsBrain brain, ref AiecsFlight flight, ref AiecsFlowAgent actor)
        {
            if (identity.External != 0 || vital.Dead != 0) return;
            bool moving = actor.Mode != AiecsMoveMode.Hold && intent.Behavior != (int)AiecsBehavior.Idle;
            bool permanent = flight.DrainPerSecond <= 0f;
            if (!permanent && flight.Airborne == 0 && flight.Stamina <= 0f)
                flight.Recovering = 1;
            if (permanent)
            {
                flight.Recovering = 0;
                if (moving) flight.Airborne = 1;
            }
            else if (flight.Recovering != 0)
            {
                flight.Airborne = 0;
                flight.Stamina = math.min(flight.StaminaMaximum,
                    flight.Stamina + pulse.DeltaTime * flight.RecoveryPerSecond);
                if (CanChooseTakeoff(moving, pulse.DeltaTime, ref brain, flight))
                {
                    flight.Airborne = 1;
                    flight.Recovering = 0;
                }
            }
            else
            {
                if (flight.Airborne == 0 && CanChooseTakeoff(moving, pulse.DeltaTime, ref brain, flight))
                    flight.Airborne = 1;
                if (flight.Airborne != 0)
                {
                    flight.Stamina = math.max(0f,
                        flight.Stamina - flight.DrainPerSecond * pulse.DeltaTime);
                    if (flight.Stamina <= 0f)
                    {
                        flight.Stamina = 0f;
                        flight.Airborne = 0;
                        flight.Recovering = 1;
                    }
                }
                else
                {
                    flight.Stamina = math.min(flight.StaminaMaximum,
                        flight.Stamina + pulse.DeltaTime * flight.RecoveryPerSecond);
                }
            }
            flight.TargetHeight = flight.Airborne != 0 ? math.max(0f, flight.CruiseHeight) : 0f;
            flight.Height = math.lerp(flight.Height, flight.TargetHeight,
                math.saturate(pulse.DeltaTime * 3f));
            if (flight.Airborne != 0)
            {
                actor.Speed = math.max(0.01f, flight.Speed);
                actor.WaterCurrentPushSpeed = 0f;
            }
            else
            {
                actor.Speed = math.max(0.01f, flight.GroundSpeed);
                actor.WaterCurrentPushSpeed = flight.BaseWaterPushSpeed;
            }
        }

        /// <summary>恢复到阈值后按每秒概率自主选择起飞；满耐力也不会被强制起飞。</summary>
        private static bool CanChooseTakeoff(bool moving, float deltaTime, ref AiecsBrain brain, AiecsFlight flight)
        {
            if (!moving || flight.StaminaMaximum <= 0f ||
                flight.Stamina < flight.StaminaMaximum * math.saturate(flight.TakeoffRecoveryRatio))
                return false;
            float perSecond = math.saturate(flight.TakeoffChancePerSecond);
            if (perSecond <= 0f) return false;
            float chance = 1f - math.pow(1f - perSecond, math.max(0f, deltaTime));
            var random = new Random(brain.RandomState == 0 ? 1u : brain.RandomState);
            bool takeoff = random.NextFloat() < chance;
            brain.RandomState = random.state;
            return takeoff;
        }
    }

    #endregion
}

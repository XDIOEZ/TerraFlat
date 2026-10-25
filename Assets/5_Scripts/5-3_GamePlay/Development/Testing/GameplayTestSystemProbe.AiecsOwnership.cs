using System.Collections.Generic;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.Geometry;
using FlatWorld.Navigation;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public static partial class GameplayTestSystemProbe
{
    #region ECS 居民区块与休眠采样
    private sealed class AiecsOwnershipGrid : IFlowGridSource
    {
        public WorldTopologyDomain Domain { get; set; }
        public readonly HashSet<int2> Loaded = new();
        public bool TryGetCell(int2 cell, out FlowNavigationCellData data)
        {
            data = new FlowNavigationCellData { Penalty = 100 };
            return Loaded.Contains(FlowNavigationMath.ChunkOf(cell, Domain));
        }
        public void CollectChunks(List<int2> chunks) => chunks.AddRange(Loaded);
    }

    // 直接运行正式脉冲与实体批次，Lua 判断迁块、卸载保留及暂停期限。
    private static JObject AiEcsResidentOwnership(JObject args)
    {
        bool wrapped = Flag(args, "wrapped", false);
        var grid = new AiecsOwnershipGrid
        {
            Domain = wrapped ? new WorldTopologyDomain(new int2(-16), new int2(32), true) : default
        };
        int2 first = wrapped ? new int2(1, 1) : int2.zero;
        int2 second = wrapped ? new int2(0, 1) : new int2(1, 0);
        grid.Loaded.Add(first); grid.Loaded.Add(second);
        using var navigation = new FlowNavigationCache(grid);
        var definition = new AiecsDefinition { Id = "ownership-probe", IdleSeconds = 1000f,
            PerceptionPeriod = 1000f, DecisionPeriod = 1000f };
        using var simulation = new AiecsSimulation(new[] { definition },
            new[] { new AiecsBuffDefinition { Id = "probe-buff", Interval = 1f, Duration = 1000f } },
            new[] { new FixedString128Bytes("probe") }, new byte[1], 1, .25f, 10d);
        var template = new AiecsActorTemplate
        {
            Definition = 0,
            Body = new AiecsBody { Radius = .2f, Facing = new float2(1f, 0f),
                Perception = PerceptionShape2D.Circle(float2.zero, .2f), Hit = PerceptionShape2D.Circle(float2.zero, .2f) },
            Vital = new AiecsVital { Hp = 100f, MaxHp = 100f, ReceivedMultiplier = 1f },
            Capabilities = AiecsCapability.Nutrition | AiecsCapability.Darkness | AiecsCapability.Charge | AiecsCapability.Reproduction,
            Nutrition = new AiecsNutrition { Current = 100f, Maximum = 100f },
            Darkness = new AiecsDarkness { LightLevel = 1f, DamageThreshold = .5f },
            Reproduction = new AiecsReproduction { NextBirthTime = 100d }
        };
        var key = new CombatIdentity { Backend = CombatBackend.Entity, Value = 7, Generation = 1, World = 13, Dimension = 2 };
        Entity entity = simulation.Spawn(template, key, new float2(1f, 1f), 0);
        EntityManager entities = simulation.Entities;
        void ProtectTimers()
        {
            var brain = entities.GetComponentData<AiecsBrain>(entity);
            brain.NextDecision = brain.NextPerception = brain.BehaviorUntil = 10000d;
            entities.SetComponentData(entity, brain);
            var attack = entities.GetComponentData<AiecsAttackState>(entity); attack.NextAttack = 10000d;
            entities.SetComponentData(entity, attack);
            var nutrition = entities.GetComponentData<AiecsNutrition>(entity); nutrition.NextSearchTime = 10000d;
            entities.SetComponentData(entity, nutrition);
            var darkness = entities.GetComponentData<AiecsDarkness>(entity);
            darkness.NextDamageTime = darkness.NextSampleTime = 10000d;
            entities.SetComponentData(entity, darkness);
            var tactics = entities.GetComponentData<AiecsTactics>(entity); tactics.NextChargeTime = 10000d;
            entities.SetComponentData(entity, tactics);
            entities.GetBuffer<AiecsBuff>(entity).Clear();
            entities.GetBuffer<AiecsBuff>(entity).Add(new AiecsBuff { Definition = 0, Expires = 10000d, NextTick = 10000d });
        }
        void Step(double time, double days)
        {
            simulation.GameDays = days;
            simulation.Step(navigation, default, 1f / 60f, time);
            simulation.Complete();
        }
        ProtectTimers();
        Step(10.02d, 1d);
        bool initiallyActive = entities.IsComponentEnabled<AiecsSimulationPulse>(entity);
        var agent = entities.GetComponentData<AiecsFlowAgent>(entity); agent.Position = new float2(17f, 1f);
        entities.SetComponentData(entity, agent);
        grid.Loaded.Remove(first); navigation.MarkCellDirty(new int2(1, 1));
        Step(10.04d, 1d);
        bool movedChunkActive = entities.IsComponentEnabled<AiecsSimulationPulse>(entity);
        grid.Loaded.Remove(second); navigation.MarkCellDirty(new int2(17, 1));
        Step(11d, 1d);
        bool unloadedSleeping = !entities.IsComponentEnabled<AiecsSimulationPulse>(entity);
        bool residentRetained = entities.Exists(entity) && simulation.Display.Length == 1 && simulation.Display[0].Key == key;
        double nextAttack = entities.GetComponentData<AiecsAttackState>(entity).NextAttack;
        grid.Loaded.Add(second); navigation.MarkCellDirty(new int2(17, 1));
        Step(61d, 2d);
        bool reloadedActive = entities.IsComponentEnabled<AiecsSimulationPulse>(entity);
        double chunkTimerShift = entities.GetComponentData<AiecsAttackState>(entity).NextAttack - nextAttack;
        double birthBeforeSuspend = entities.GetComponentData<AiecsReproduction>(entity).NextBirthTime;
        simulation.Suspend(62d); simulation.Suspend(72d);
        double pausedAt = entities.GetComponentData<AiecsSimulationPulse>(entity).PausedAt;
        Step(82d, 4d);
        var result = new JObject
        {
            ["initiallyActive"] = initiallyActive, ["movedChunkActive"] = movedChunkActive,
            ["unloadedSleeping"] = unloadedSleeping, ["residentRetained"] = residentRetained,
            ["reloadedActive"] = reloadedActive, ["sameIdentity"] = entities.GetComponentData<AiecsIdentity>(entity).Key == key,
            ["chunkTimerShift"] = chunkTimerShift, ["pausedAt"] = pausedAt,
            ["attackDeadline"] = entities.GetComponentData<AiecsAttackState>(entity).NextAttack,
            ["nutritionDeadline"] = entities.GetComponentData<AiecsNutrition>(entity).NextSearchTime,
            ["darknessDeadline"] = entities.GetComponentData<AiecsDarkness>(entity).NextDamageTime,
            ["darknessSampleDeadline"] = entities.GetComponentData<AiecsDarkness>(entity).NextSampleTime,
            ["chargeDeadline"] = entities.GetComponentData<AiecsTactics>(entity).NextChargeTime,
            ["buffDeadline"] = entities.GetBuffer<AiecsBuff>(entity)[0].Expires,
            ["birthShift"] = entities.GetComponentData<AiecsReproduction>(entity).NextBirthTime - birthBeforeSuspend
        };
        return result;
    }
    #endregion
}

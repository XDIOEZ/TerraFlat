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
        // 两个空地区块提供有效的透明视线容器，未知格继续按正式规则阻挡。
        using var losChunks = new NativeParallelHashMap<int2, int>(2, Allocator.Persistent);
        losChunks.Add(first, 0); losChunks.Add(second, 1);
        using var losCells = new NativeArray<byte>(2 * 256, Allocator.Persistent);
        var los = new AiecsLosView { Domain = grid.Domain, Chunks = losChunks, Cells = losCells };
        var definition = new AiecsDefinition { Id = "ownership-probe", IdleSeconds = 1000f,
            PerceptionPeriod = 1000f, DecisionPeriod = 1000f };
        var secondDefinition = new AiecsDefinition { Id = "ownership-probe-second", IdleSeconds = 1000f,
            PerceptionPeriod = 1000f, DecisionPeriod = 1000f };
        using var simulation = new AiecsSimulation(new[] { definition, secondDefinition },
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
        var secondKey = new CombatIdentity { Backend = CombatBackend.Entity, Value = 19, Generation = 2, World = 13, Dimension = 2 };
        var externalKey = new CombatIdentity { Backend = CombatBackend.External, Value = 31, Generation = 3, World = 13, Dimension = 2 };
        var secondTemplate = template; secondTemplate.Definition = 1;
        Entity secondEntity = simulation.Spawn(secondTemplate, secondKey, new float2(3f, 1f), 0);
        Entity externalEntity = simulation.Spawn(template, externalKey, new float2(5f, 1f), 0, external: true);
        EntityManager entities = simulation.Entities;
        void ProtectTimers(Entity protectedEntity)
        {
            var brain = entities.GetComponentData<AiecsBrain>(protectedEntity);
            brain.NextDecision = brain.NextPerception = brain.BehaviorUntil = 10000d;
            entities.SetComponentData(protectedEntity, brain);
            var attack = entities.GetComponentData<AiecsAttackState>(protectedEntity); attack.NextAttack = 10000d;
            entities.SetComponentData(protectedEntity, attack);
            var nutrition = entities.GetComponentData<AiecsNutrition>(protectedEntity); nutrition.NextSearchTime = 10000d;
            entities.SetComponentData(protectedEntity, nutrition);
            var darkness = entities.GetComponentData<AiecsDarkness>(protectedEntity);
            darkness.NextDamageTime = darkness.NextSampleTime = 10000d;
            entities.SetComponentData(protectedEntity, darkness);
            var tactics = entities.GetComponentData<AiecsTactics>(protectedEntity); tactics.NextChargeTime = 10000d;
            entities.SetComponentData(protectedEntity, tactics);
            entities.GetBuffer<AiecsBuff>(protectedEntity).Clear();
            entities.GetBuffer<AiecsBuff>(protectedEntity).Add(new AiecsBuff { Definition = 0, Expires = 10000d, NextTick = 10000d });
        }
        void Step(double time, double days)
        {
            simulation.GameDays = days;
            simulation.Step(navigation, los, 1f / 60f, time);
            simulation.Complete();
        }
        ProtectTimers(entity);
        ProtectTimers(secondEntity);
        ProtectTimers(externalEntity);
        Step(10.02d, 1d);
        // 分别采集实体权威状态和实际表现快照，由 Lua 比较完整身份与内容。
        JObject CaptureRecord(CombatIdentity identity, int actorDefinition, byte external, float2 position) => new()
        {
            ["backend"] = (int)identity.Backend, ["value"] = (long)identity.Value,
            ["generation"] = identity.Generation, ["world"] = identity.World, ["dimension"] = identity.Dimension,
            ["definition"] = actorDefinition, ["external"] = (int)external,
            ["x"] = position.x, ["y"] = position.y
        };
        var sourceRecords = new JArray();
        foreach (Entity current in new[] { entity, secondEntity, externalEntity })
        {
            AiecsIdentity identity = entities.GetComponentData<AiecsIdentity>(current);
            AiecsFlowAgent actor = entities.GetComponentData<AiecsFlowAgent>(current);
            sourceRecords.Add(CaptureRecord(identity.Key, identity.Definition, identity.External, actor.Position));
        }
        var displayRecords = new JArray();
        foreach (AiecsDisplayRecord record in simulation.Display)
            displayRecords.Add(CaptureRecord(record.Key, record.Definition, record.External, record.Position));
        simulation.Despawn(secondEntity);
        simulation.Despawn(externalEntity);
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
            ["sourceRecords"] = sourceRecords, ["displayRecords"] = displayRecords,
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

using System;
using System.IO;
using FlatWorld.AIECS;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

/// <summary>只使用隔离 World 的能力回归，不进入世界、不移动玩家、不读写真实存档。</summary>
public static class WorldEntityCapabilityDiagnostics
{
    #region 隔离回归

    [MenuItem("FlatWorld/诊断/验证统一实体能力")]
    public static void Run()
    {
        var result = new DiagnosticResult { runId = Guid.NewGuid().ToString("N") };
        try
        {
            RunChecks();
            result.passed = true;
            result.message = "All isolated entity capability checks passed.";
        }
        catch (Exception exception)
        {
            result.message = exception.ToString();
            throw;
        }
        finally
        {
            result.finishedUtc = DateTime.UtcNow.ToString("O");
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "FlatWorldDiagnostics");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "world-entity-capabilities.json"), JsonUtility.ToJson(result, true));
        }
    }

    [Serializable]
    private sealed class DiagnosticResult
    {
        public string runId, finishedUtc, message;
        public bool passed;
    }

    private static void RunChecks()
    {
        World original = WorldEntityRuntime.Current;
        ulong generation = WorldEntityRuntime.Generation;
        using var world = new World("统一实体能力隔离验证");
        EntityManager manager = world.EntityManager;
        var system = world.GetOrCreateSystemManaged<EntityCapabilitySystem>();
        system.StepSeconds = 1f;
        system.GameTime = 1d;
        system.DayLength = 24f;
        system.DifficultyGrowthMultiplier = 1f;
        system.WeatherMultiplier = 1f;
        var season = new EntitySeasonPeriod
        { EndDay = double.PositiveInfinity, Days = new float4(6f), Temperatures = new float4(0, 12, 0, -30) };
        system.SetSeasons(new[] { season });

        Entity tree = manager.CreateEntity(typeof(AiecsVital));
        manager.SetComponentData(tree, new AiecsVital { Hp = 50f, MaxHp = 100f, ReceivedMultiplier = 1f });
        AttachGrowth(manager, tree);
        Entity stone = manager.CreateEntity(typeof(AiecsVital));
        manager.SetComponentData(stone, new AiecsVital { Hp = 180f, MaxHp = 180f, ReceivedMultiplier = 1f });

        using var ai = new AiecsSimulation(new AiecsDefinition[1], Array.Empty<AiecsBuffDefinition>(),
            new[] { new FixedString128Bytes("neutral") }, new byte[1], 1, 0.25f, 0d, world);
        Entity animal = ai.Spawn(new AiecsActorTemplate
        {
            Body = new AiecsBody { Radius = 0.2f },
            Vital = new AiecsVital { Hp = 50f, MaxHp = 100f, ReceivedMultiplier = 1f }
        }, new CombatIdentity { Backend = CombatBackend.Entity, Value = 1, World = 1, Generation = 1 }, float2.zero, 0);
        AttachGrowth(manager, animal);
        system.Update(); system.Complete();
        Check(Near(manager.GetComponentData<EntityGrowth>(tree).Progress, 21f), "树木未执行通用成长");
        Check(Near(manager.GetComponentData<EntityGrowth>(animal).Progress, 21f), "带 AI 的实体不能复用同一成长模块");
        Check(!manager.HasComponent<AiecsBrain>(tree) && manager.HasComponent<AiecsBrain>(animal), "静态实体被强加 AI 模块");
        Check(Near(manager.GetComponentData<AiecsVital>(stone).Hp, 180f), "无成长模块的矿物被修改");
        Check(Near(manager.GetComponentData<AiecsVital>(tree).Hp / manager.GetComponentData<AiecsVital>(tree).MaxHp, 0.5f),
            "阶段变化没有保持生命比例");

        manager.SetComponentEnabled<EntityModuleActive>(tree, false);
        system.GameTime = 2d;
        system.Update(); system.Complete();
        Check(Near(manager.GetComponentData<EntityGrowth>(tree).Progress, 21f), "暂停的能力仍在计算");
        manager.SetComponentEnabled<EntityModuleActive>(tree, true);
        manager.AddComponentData(tree, new EntityClimate
        {
            BaselineCelsius = -100f, AmbientCelsius = 20f, EnvironmentReady = 1,
            MinimumGrowthTemperature = 0f, MaximumGrowthTemperature = 35f,
            MinimumSurvivalTemperature = -15f, MaximumSurvivalTemperature = 45f,
            FatalExposureHours = 1f, RecoveryRate = 0.5f, ClockInitialized = 1
        });
        system.Update(); system.Complete();
        Check(manager.GetComponentData<AiecsVital>(tree).Dead != 0, "历史耐候未通过共享生命组件死亡");
        Check(Near(manager.GetComponentData<AiecsVital>(tree).Hp, 0f), "死亡后生命没有归零");

        ai.Dispose();
        Check(world.IsCreated && manager.Exists(tree) && manager.Exists(stone) && !manager.Exists(animal),
            "关闭 AI 运行器删除了其它能力实体或共享 World");
        Check(Near(season.Sample(0d, out _, out _, out _), 0f) &&
              Near(season.Sample(9d, out _, out _, out _), 12f) &&
              Near(season.Sample(21d, out _, out _, out _), -30f) &&
              Near(season.Sample(24d, out _, out _, out _), -15f), "季节数值核发生漂移");

        var savedGrowth = new GrowData { MaxGrowProgress = 100f };
        Mod_Grow.InitializeNaturalGrowthData(savedGrowth, 1234, 1f);
        Check(Near(savedGrowth.GrowProgress, 12.34f) && Near(savedGrowth.environmentGrowthMultiplier, 1.2f), "初始树龄或降水倍率错误");
        savedGrowth.GrowProgress = 70f;
        Mod_Grow.InitializeNaturalGrowthData(savedGrowth, 9999, 0f);
        Check(Near(savedGrowth.GrowProgress, 70f), "再次绑定重置了已有树龄");
        Check(new NaturalEntityHandle(7, 1) != new NaturalEntityHandle(7, 2), "旧世界句柄可命中新世界");
        Check(ReferenceEquals(original, WorldEntityRuntime.Current) && generation == WorldEntityRuntime.Generation,
            "隔离验证修改了正式实体世界");
        Debug.Log("[WorldEntityCapabilityDiagnostics] PASS：共享 World、动物/静物复用成长、模块门禁、生命比例、耐候历史、AI 独立释放、季节、树龄和句柄代际。");
    }

    #endregion

    #region 测试值构造

    private static void AttachGrowth(EntityManager manager, Entity entity)
    {
        var growth = new EntityGrowth
        { Progress = 19f, MaxProgress = 100f, Speed = 2f, EnvironmentMultiplier = 1f, MatureMaxHealth = 200f };
        growth.Thresholds.Add(0f); growth.Thresholds.Add(20f); growth.Thresholds.Add(100f);
        growth.Scales.Add(0.2f); growth.Scales.Add(0.5f); growth.Scales.Add(1f);
        growth.HealthRatios.Add(0.25f); growth.HealthRatios.Add(0.5f); growth.HealthRatios.Add(1f);
        manager.AddComponentData(entity, growth);
        manager.AddComponentData(entity, new EntityModuleAppearance { Scale = new float2(0.2f), Revision = 1 });
        manager.AddComponent<EntityModuleActive>(entity);
    }

    private static bool Near(float actual, float expected) => math.abs(actual - expected) < 0.001f;
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("[WorldEntityCapabilityDiagnostics] " + reason);
    }

    #endregion
}

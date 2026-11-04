using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 首次生物创建的代码预热规则

    private static int startupCodePreparationInvocations;

    private static JObject StartupActorCodePrewarm(JObject args)
    {
        startupCodePreparationInvocations = 0;
        var roots = new HashSet<Type>(GameRes.GetActorRuntimePreparationRoots());
        var methods = new List<MethodBase>(ResourceRuntimeMethodPrewarmer.EnumerateClosedMethods(
            new[] { typeof(StartupCodePrewarmFixture), typeof(StartupCodePrewarmFixture), typeof(Action), typeof(List<>) }));
        var unique = new HashSet<MethodBase>(methods);
        var capabilityMethods = new List<MethodBase>(
            GameRes.EnumerateActorCapabilityRuntimePreparationMethods(typeof(AnimalCombatCapability)));
        bool constructor = false, closure = false, concreteVirtual = false, staticConstructor = false;
        foreach (MethodBase method in methods)
        {
            constructor |= method is ConstructorInfo && !method.IsStatic;
            closure |= method.DeclaringType != typeof(StartupCodePrewarmFixture) && method.Name.Contains("b__");
            concreteVirtual |= method.Name == nameof(StartupCodePrewarmFixture.AbstractFixture.Concrete);
            staticConstructor |= method is ConstructorInfo && method.IsStatic;
        }
        var result = new JObject
        {
            ["productionRoots"] = roots.Contains(typeof(Mod_AnimalBehaviorTree)) && roots.Contains(typeof(MonsterSpawnerManager)) &&
                roots.Contains(typeof(RuntimeItemModuleAssemblyPlan)) && roots.Contains(typeof(AnimalSpecialMovementBehaviorNodes)),
            ["playerRoots"] = roots.Contains(typeof(Player)) && roots.Contains(typeof(ItemMgr)) &&
                roots.Contains(typeof(ItemTickScheduler)) && roots.Contains(typeof(WorldPresentationProjection)) &&
                roots.Contains(typeof(GameManager)) && roots.Contains(typeof(SaveDataMgr)),
            ["capabilitiesAndNodes"] = roots.Contains(typeof(AnimalCombatCapability)) && roots.Contains(typeof(AnimalWanderAction)),
            ["capabilityFactoryHelpers"] = roots.Contains(typeof(AnimalLandActionSupport)) && roots.Contains(typeof(AnimalLandTargetRules)),
            ["closedCapabilityFactory"] = capabilityMethods.Exists(method =>
                method.DeclaringType == typeof(AnimalLandActionSupport) && method.Name == nameof(AnimalLandActionSupport.Capability) &&
                !method.ContainsGenericParameters && method.GetGenericArguments()[0] == typeof(AnimalCombatCapability)),
            ["closedCapabilityClosure"] = capabilityMethods.Exists(method =>
                method.DeclaringType.DeclaringType == typeof(AnimalLandActionSupport) &&
                method.DeclaringType.IsGenericType && !method.ContainsGenericParameters && method.Name.Contains("b__") &&
                method.DeclaringType.GetGenericArguments()[0] == typeof(AnimalCombatCapability)),
            ["invalidCapabilityRejected"] =
                new List<MethodBase>(GameRes.EnumerateActorCapabilityRuntimePreparationMethods(null)).Count == 0 &&
                new List<MethodBase>(GameRes.EnumerateActorCapabilityRuntimePreparationMethods(typeof(AnimalBehaviorTreeCapability))).Count == 0 &&
                new List<MethodBase>(GameRes.EnumerateActorCapabilityRuntimePreparationMethods(typeof(AnimalWanderAction))).Count == 0,
            ["unique"] = unique.Count == methods.Count,
            ["constructorsAndClosures"] = constructor && closure && concreteVirtual,
            ["noStaticConstructors"] = !staticConstructor,
            ["openGenericRejected"] = !ResourceRuntimeMethodPrewarmer.CanPrepareMethod(
                typeof(StartupCodePrewarmFixture).GetMethod(nameof(StartupCodePrewarmFixture.OpenGeneric))),
            ["abstractRejected"] = !ResourceRuntimeMethodPrewarmer.CanPrepareMethod(
                typeof(StartupCodePrewarmFixture.AbstractFixture).GetMethod(nameof(StartupCodePrewarmFixture.AbstractFixture.Execute))),
            ["discoveryDoesNotExecute"] = startupCodePreparationInvocations == 0
        };
#if UNITY_EDITOR || ENABLE_MONO
        bool prepared = true, reused = true;
        foreach (MethodBase method in methods)
        {
            prepared &= ResourceRuntimeMethodPrewarmer.PrepareMethod(method) != ResourceRuntimeMethodPrewarmer.PreparationResult.Unsupported;
            reused &= ResourceRuntimeMethodPrewarmer.PrepareMethod(method) == ResourceRuntimeMethodPrewarmer.PreparationResult.AlreadyPrepared;
        }
        result["available"] = true;
        result["prepared"] = prepared;
        result["reused"] = reused;
#else
        result["available"] = false;
#endif
        result["preparationDoesNotExecute"] = startupCodePreparationInvocations == 0;
        return result;
    }

    // 夹具的构造、静态初始化和普通方法都留下计数，用于拒绝把执行玩法当成预编译。
    private sealed class StartupCodePrewarmFixture
    {
        static StartupCodePrewarmFixture() { startupCodePreparationInvocations++; }
        public StartupCodePrewarmFixture() { startupCodePreparationInvocations++; }
        public static int Execute() { startupCodePreparationInvocations++; return 1; }
        public static Func<int> Capture(int value) => () => value + 1;
        public static void OpenGeneric<T>() { startupCodePreparationInvocations++; }
        public abstract class AbstractFixture
        {
            public abstract void Execute();
            public virtual int Concrete() => 3;
        }
    }

    #endregion
}

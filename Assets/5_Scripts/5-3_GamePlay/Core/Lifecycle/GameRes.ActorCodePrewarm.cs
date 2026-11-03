using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

public partial class GameRes
{
    #region 生物首次创建代码预备

    private IEnumerator PrewarmActorRuntimeCode()
    {
        // 原位更新不预编译整个目录，避免把加载卡顿重新带入正在运行的世界。
        if (preparingInPlaceReload) yield break;
#if UNITY_EDITOR || ENABLE_MONO
        var types = new HashSet<Type>();
        foreach (Type type in GetActorRuntimePreparationRoots())
        {
            types.Add(type);
            if (ShouldYieldResourceWork()) yield return null;
        }
        foreach (RuntimeItemDefinition definition in ItemDefinitions.Values)
        {
            if (!definition.IsActor) continue;
            foreach (RuntimeItemModuleAssemblyPlan assembly in definition.ModuleAssemblies)
            {
                // 按实际装配类型包含继承实现，不维护逐物种或逐技能的预热名单。
                for (Type type = assembly.ModuleType; type != null && typeof(Module).IsAssignableFrom(type); type = type.BaseType)
                    types.Add(type);
            }
            if (ShouldYieldResourceWork()) yield return null;
        }

        // 玩家不是普通 Actor 定义，直接从正式玩家 Prefab 收集实际运行的脚本。
        MonoBehaviour[] playerComponents = GetPrefab("Player", logError: false)?
            .GetComponentsInChildren<MonoBehaviour>(true) ?? Array.Empty<MonoBehaviour>();
        foreach (MonoBehaviour component in playerComponents)
        {
            if (component == null) continue;
            for (Type type = component.GetType(); type != null && typeof(MonoBehaviour).IsAssignableFrom(type);
                 type = type.BaseType)
            {
                string assemblyName = type.Assembly.GetName().Name;
                if (assemblyName == "GamePlay" || assemblyName == "UI" ||
                    assemblyName.StartsWith("FlatWorld.", StringComparison.Ordinal))
                    types.Add(type);
            }
            if (ShouldYieldResourceWork()) yield return null;
        }

        int prepared = 0, unsupported = 0;
        foreach (MethodBase method in ResourceRuntimeMethodPrewarmer.EnumerateClosedMethods(types))
        {
            Count(ResourceRuntimeMethodPrewarmer.PrepareMethod(method));
            if (ShouldYieldResourceWork()) yield return null;
        }

        // 能力工厂的泛型入口必须按实际能力闭合，开放泛型不能提前编译。
        foreach (Type type in types)
        {
            foreach (MethodBase method in EnumerateActorCapabilityRuntimePreparationMethods(type))
            {
                Count(ResourceRuntimeMethodPrewarmer.PrepareMethod(method));
                if (ShouldYieldResourceWork()) yield return null;
            }
        }

        // 预先加载玩家 Prefab 声明的纯内容，避免首帧创建角色时解析配置。
        foreach (MonoBehaviour component in playerComponents)
        {
            if (component is not IStartupRuntimeContentPrewarm prewarmer) continue;
            IEnumerator steps = prewarmer.PrewarmStartupContent(ShouldYieldResourceWork);
            if (steps == null) continue;
            while (steps.MoveNext())
                yield return steps.Current;
        }

        // 仅初始化空的私有集合，提前完成首只动物才会触发的泛型容器初始化。
        var nodes = new List<AnimalBehaviorTreeNode>();
        nodes.Add(null);
        nodes.Clear();
        if (ShouldYieldResourceWork()) yield return null;
        var capabilities = new List<AnimalBehaviorTreeCapability>();
        capabilities.Add(null);
        capabilities.Clear();
        if (ShouldYieldResourceWork()) yield return null;
        Debug.Log($"[GameRes] 生物首次创建代码预热完成：{prepared} 个方法，跳过 {unsupported} 个不支持的方法。");

        void Count(ResourceRuntimeMethodPrewarmer.PreparationResult result)
        {
            if (result == ResourceRuntimeMethodPrewarmer.PreparationResult.Prepared) prepared++;
            else if (result == ResourceRuntimeMethodPrewarmer.PreparationResult.Unsupported) unsupported++;
        }
#endif
    }

    internal static IEnumerable<Type> GetActorRuntimePreparationRoots()
    {
        yield return typeof(Mod_AnimalBehaviorTree);
        yield return typeof(AnimalBehaviorTreeContext);
        yield return typeof(AnimalBehaviorTreeBlackboard);
        yield return typeof(AnimalBehaviorTreePerception);
        yield return typeof(AnimalBehaviorTreeRegistry);
        yield return typeof(AnimalBehaviorTreeCompiler);
        yield return typeof(AnimalBehaviorTreeNodeCatalog);
        yield return typeof(AnimalBehaviorTreeTemplate);
        yield return typeof(AnimalBehaviorTreeRuntime);
        yield return typeof(AnimalLandBehaviorNodes);
        yield return typeof(AnimalLandActionSupport);
        yield return typeof(AnimalLandTargetRules);
        yield return typeof(AnimalLandTargetSnapshot);
        yield return typeof(AnimalSpecialMovementBehaviorNodes);
        yield return typeof(AnimalTemperatureSafetyBehaviorNodes);
        yield return typeof(MonsterSpawnerManager);
        yield return typeof(RuntimeItemDefinition);
        yield return typeof(RuntimeItemModuleAssemblyPlan);
        yield return typeof(Item);
        yield return typeof(ItemMods);
        yield return typeof(Module);
        yield return typeof(Player);
        yield return typeof(ItemMgr);
        yield return typeof(ItemTickScheduler);
        yield return typeof(WorldPresentationProjection);
        foreach (Type type in typeof(AnimalBehaviorTreeNode).Assembly.GetTypes())
            if (typeof(AnimalBehaviorTreeNode).IsAssignableFrom(type) ||
                typeof(AnimalBehaviorTreeCapability).IsAssignableFrom(type)) yield return type;
    }

    internal static IEnumerable<MethodBase> EnumerateActorCapabilityRuntimePreparationMethods(Type capabilityType)
    {
        if (capabilityType == null || capabilityType.IsAbstract || capabilityType.ContainsGenericParameters ||
            !typeof(AnimalBehaviorTreeCapability).IsAssignableFrom(capabilityType)) yield break;

        foreach (MethodInfo method in typeof(AnimalBehaviorTreeContext).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                (method.Name == nameof(AnimalBehaviorTreeContext.GetOrCreateCapability) ||
                 method.Name == nameof(AnimalBehaviorTreeContext.TryGetCapability)))
                yield return method.MakeGenericMethod(capabilityType);

        yield return typeof(AnimalLandActionSupport).GetMethod(nameof(AnimalLandActionSupport.Capability))
            .MakeGenericMethod(capabilityType);

        // 能力工厂捕获泛型参数，必须闭合编译器生成的闭包，不能只预备外层方法。
        foreach (Type nested in typeof(AnimalLandActionSupport).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!nested.IsGenericTypeDefinition || nested.GetGenericArguments().Length != 1 ||
                !nested.IsDefined(typeof(CompilerGeneratedAttribute), false)) continue;
            foreach (MethodBase method in ResourceRuntimeMethodPrewarmer.EnumerateClosedMethods(
                new[] { nested.MakeGenericType(capabilityType) }))
                yield return method;
        }
    }

    #endregion
}

/// <summary>Prefab 声明需要在世界 Ready 前完成的纯资源预热，不执行玩法生命周期。</summary>
public interface IStartupRuntimeContentPrewarm
{
    IEnumerator PrewarmStartupContent(Func<bool> shouldYield);
}

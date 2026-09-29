using System.Collections.Generic;
using UnityEngine;

/// <summary>无物理目标的作者视觉命中范围；用于准线、按键和鼠标共用同一目标。</summary>
public interface ISpatialInteractionShape
{
    bool ContainsInteractionPoint(Vector2 point);
}

/// <summary>无 Collider 世界目标注册表；按世界距离查询，沿用普通交互与描边入口。</summary>
public static class SpatialInteractionRegistry
{
    #region 空间查询
    private readonly struct Registration
    {
        public readonly float Radius;
        public readonly IInteractable Interaction;
        public Registration(float radius, IInteractable interaction)
        { Radius = radius; Interaction = interaction; }
    }

    private static readonly Dictionary<Component, Registration> Targets = new(); // 视觉归属与权威交互目标分离。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Targets.Clear();
    public static void Register(Component target, float radius) => Register(target, radius, target as IInteractable);
    /// <summary>视觉组件只提供命中范围和生命周期，交互仍返回同一权威数据目标。</summary>
    public static void Register(Component target, float radius, IInteractable interaction)
    {
        if (target != null && interaction != null) Targets[target] = new Registration(radius, interaction);
    }
    public static void Unregister(Component target) => Targets.Remove(target);

    /// <summary>执行正式交互查询；pointer 为 null 表示邻近查询。</summary>
    public static void Query(Item actor, float radius, Vector2? pointer, List<IInteractable> results)
    {
        QueryRegisteredTargets(actor, radius, pointer, results);
        MachineWorld.QueryInteractionTargets(actor, radius, pointer, results);
        FlatWorld.NaturalEntities.NaturalEntityEcsService.QueryInteractions(actor, radius, pointer, results);
    }

    /// <summary>只查询已注册的视觉目标与资源实体，不为机器描边逐帧查询机械图。</summary>
    public static void QueryPreview(Item actor, float radius, Vector2? pointer, List<IInteractable> results)
    {
        QueryRegisteredTargets(actor, radius, pointer, results);
        FlatWorld.NaturalEntities.NaturalEntityEcsService.QueryInteractions(actor, radius, pointer, results);
    }

    /// <summary>查询同场景注册组件，供正式交互与视觉预览共用。</summary>
    private static void QueryRegisteredTargets(Item actor, float radius, Vector2? pointer, List<IInteractable> results)
    {
        foreach (KeyValuePair<Component, Registration> entry in Targets)
        {
            Component target = entry.Key;
            IInteractable interaction = entry.Value.Interaction;
            if (target == null || !target.gameObject.activeInHierarchy ||
                target.gameObject.scene != actor.gameObject.scene || interaction == null ||
                !interaction.CanInteract(actor) || WorldTopologyRuntime.Distance(actor.transform.position, target.transform.position) > radius)
                continue;
            if (pointer.HasValue && (target is ISpatialInteractionShape shape
                ? !shape.ContainsInteractionPoint(pointer.Value)
                : WorldTopologyRuntime.Distance(pointer.Value, target.transform.position) > entry.Value.Radius)) continue;
            if (!results.Contains(interaction)) results.Add(interaction);
        }
    }
    #endregion
}

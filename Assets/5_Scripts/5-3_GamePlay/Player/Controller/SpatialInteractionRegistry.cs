using System.Collections.Generic;
using Unity.Mathematics;
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
        => Query(actor, radius, pointer, results, WorldTopologyRuntime.GetActiveDomain());

    internal static void Query(Item actor, float radius, Vector2? pointer,
        List<IInteractable> results, WorldTopologyDomain topology)
    {
        QueryRegisteredTargets(actor, radius, pointer, results, topology);
        MachineWorld.QueryInteractionTargets(actor, radius, pointer, results);
        FlatWorld.NaturalEntities.NaturalEntityEcsService.QueryInteractions(actor, radius, pointer, results, topology);
    }

    /// <summary>只查询已注册的视觉目标与资源实体，不为机器描边逐帧查询机械图。</summary>
    public static void QueryPreview(Item actor, float radius, Vector2? pointer, List<IInteractable> results)
        => QueryPreview(actor, radius, pointer, results, WorldTopologyRuntime.GetActiveDomain());

    internal static void QueryPreview(Item actor, float radius, Vector2? pointer,
        List<IInteractable> results, WorldTopologyDomain topology)
    {
        QueryRegisteredTargets(actor, radius, pointer, results, topology);
        FlatWorld.NaturalEntities.NaturalEntityEcsService.QueryInteractions(actor, radius, pointer, results, topology);
    }

    /// <summary>查询同场景注册组件，供正式交互与视觉预览共用。</summary>
    internal static void QueryRegisteredTargets(Item actor, float radius, Vector2? pointer,
        List<IInteractable> results, WorldTopologyDomain topology)
    {
        // 同轮冻结玩家位置和场景，先排除远处/失活目标，再执行能力准入判断。
        Vector2 actorPosition = actor.transform.position;
        var actorScene = actor.gameObject.scene;
        foreach (KeyValuePair<Component, Registration> entry in Targets)
        {
            Component target = entry.Key;
            IInteractable interaction = entry.Value.Interaction;
            if (target == null || interaction == null || !target.gameObject.activeInHierarchy ||
                target.gameObject.scene != actorScene) continue;
            Vector3 position = target is IWorldInteractionTarget worldTarget ? worldTarget.WorldPosition
                : target.transform.position;
            if (!IsWithinInteractionRadius(actorPosition, position, radius, topology) ||
                !interaction.CanInteract(actor))
                continue;
            if (pointer.HasValue && (interaction is IGridInteractionTarget gridTarget
                ? !gridTarget.TryGetInteractionGridHit(pointer.Value, topology, out _)
                : target is ISpatialInteractionShape shape
                    ? !shape.ContainsInteractionPoint(pointer.Value)
                    : !IsWithinInteractionRadius(pointer.Value, position, entry.Value.Radius, topology))) continue;
            if (!results.Contains(interaction)) results.Add(interaction);
        }
    }

    /// <summary>只用最短位移的平方比较范围，半径为负时拒绝所有正常命中。</summary>
    internal static bool IsWithinInteractionRadius(Vector2 origin, Vector2 target, float radius,
        in WorldTopologyDomain topology)
    {
        if (radius < 0f) return false;
        return !(topology.SqrDistance(new float2(origin.x, origin.y),
            new float2(target.x, target.y)) > radius * radius);
    }
    #endregion
}

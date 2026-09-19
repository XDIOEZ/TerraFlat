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
    private static readonly Dictionary<Component, float> Targets = new(); // 组件及点选半径。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Targets.Clear();
    public static void Register(Component target, float radius) => Targets[target] = radius;
    public static void Unregister(Component target) => Targets.Remove(target);

    /// <summary>查询同场景有效目标；pointer 为 null 表示邻近查询。</summary>
    public static void Query(Item actor, float radius, Vector2? pointer, List<IInteractable> results)
    {
        foreach (KeyValuePair<Component, float> entry in Targets)
        {
            Component target = entry.Key;
            if (target == null || !target.gameObject.activeInHierarchy ||
                target.gameObject.scene != actor.gameObject.scene || target is not IInteractable interaction ||
                !interaction.CanInteract(actor) || WorldTopologyRuntime.Distance(actor.transform.position, target.transform.position) > radius)
                continue;
            if (pointer.HasValue && (target is ISpatialInteractionShape shape
                ? !shape.ContainsInteractionPoint(pointer.Value)
                : WorldTopologyRuntime.Distance(pointer.Value, target.transform.position) > entry.Value)) continue;
            if (!results.Contains(interaction)) results.Add(interaction);
        }
    }
    #endregion
}

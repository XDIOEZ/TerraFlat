using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 树木栖息地索引：仅首次绑定枚举 ItemMgr，之后通过正式注册/注销事件增量维护。
/// 生成与数量检查共用两秒快照，只统计当前场景、玩家加载范围内的活动树；不扫描 Physics2D。
/// </summary>
public sealed class RuntimeTreeHabitatIndex : IDisposable
{
    #region 生命周期索引
    private readonly HashSet<Item> trees = new();
    private readonly List<Vector2> activePositions = new();
    private float nextRefresh;
    private string snapshotScene;

    public void Bind(IEnumerable<Item> items)
    {
        Dispose();
        ItemMgr.RuntimeItemRegistered += Register;
        ItemMgr.RuntimeItemUnregistered += Unregister;
        foreach (Item candidate in items) Register(candidate);
    }

    private void Register(Item candidate)
    {
        if (candidate != null && candidate.itemData?.Tags != null && candidate.itemData.Tags.ContainsTag(Tag.Tree))
        {
            trees.Add(candidate);
            nextRefresh = 0f;
        }
    }

    private void Unregister(Item candidate)
    {
        if (trees.Remove(candidate)) nextRefresh = 0f;
    }

    public void Dispose()
    {
        ItemMgr.RuntimeItemRegistered -= Register;
        ItemMgr.RuntimeItemUnregistered -= Unregister;
        trees.Clear();
        activePositions.Clear();
        nextRefresh = 0f;
        snapshotScene = null;
    }
    #endregion

    #region 低频生态快照
    public IReadOnlyList<Vector2> GetActivePositions(string sceneName)
    {
        if (snapshotScene == sceneName && Time.unscaledTime < nextRefresh)
            return activePositions;
        snapshotScene = sceneName;
        nextRefresh = Time.unscaledTime + 2f;
        activePositions.Clear();
        foreach (Item tree in trees)
        {
            if (tree == null || tree.DestructionHandled || !tree.gameObject.activeInHierarchy ||
                tree.gameObject.scene.name != sceneName)
                continue;
            activePositions.Add(tree.transform.position);
        }
        return activePositions;
    }

    /// <summary>树木容量纯函数；零树零鸟，每四棵树默认提供一个名额。</summary>
    public static int ResolveCapacity(int treeCount, int treesPerActor) =>
        treeCount <= 0 ? 0 : 1 + (treeCount - 1) / Math.Max(1, treesPerActor);
    #endregion
}

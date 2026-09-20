using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>供只读诊断与自主游玩观察使用的 ECS 掉落物快照，不暴露可修改的 ECS/ItemData 引用。</summary>
public readonly struct DroppedItemObservation
{
    public DroppedItemObservation(
        int id,
        string itemId,
        string gameName,
        Vector2 position,
        float amount,
        bool pickable,
        string[] tags)
    {
        Id = id;
        ItemId = itemId ?? string.Empty;
        GameName = gameName ?? string.Empty;
        Position = position;
        Amount = amount;
        Pickable = pickable;
        Tags = tags ?? System.Array.Empty<string>();
    }

    public int Id { get; }
    public string ItemId { get; }
    public string GameName { get; }
    public Vector2 Position { get; }
    public float Amount { get; }
    public bool Pickable { get; }
    public IReadOnlyList<string> Tags { get; }
}

public static partial class DroppedItemService
{
    #region 世界实物查询与原子消耗

    private static readonly List<Item> forageLegacyCandidates = new();
    private static readonly HashSet<Item> forageLegacyDedupe = new();

    /// <summary>
    /// 读取玩家附近的离线 ECS 掉落物；包含飞行中与已落地实体的当前世界坐标。
    /// 只复制观察字段，不返回 ECS Entity、ItemData 或其它可修改权威状态。
    /// </summary>
    public static int QueryNearbyEntityDrops(
        Vector2 origin,
        float radius,
        List<DroppedItemObservation> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();
        if (!UsesEntities || runtime == null || radius <= 0f ||
            float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return 0;
        }

        runtime.QueryNearbyObservations(origin, radius, results);
        return results.Count;
    }

    /// <summary>按统一内容标签查询附近已落地实物；离线走 ECS 空间桶，联机走现有权威 Item 索引。</summary>
    public static bool TryFindNearestTagged(Vector2 origin, float radius, string tag, out DroppedItemHandle handle)
    {
        handle = default;
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius) || string.IsNullOrWhiteSpace(tag)) return false;
        if (UsesEntities)
        {
            if (runtime == null || !runtime.TryFindNearestTagged(origin, radius, tag, out int id)) return false;
            handle = new DroppedItemHandle(id, Epoch);
            return true;
        }
        if (!GameNetwork.HasStateAuthority || ItemMgr.Instance == null) return false;
        ItemMgr.Instance.QueryItemsInCircleNonAlloc(origin, radius, ~0, null, forageLegacyCandidates, forageLegacyDedupe);
        float nearest = radius * radius;
        foreach (Item candidate in forageLegacyCandidates)
        {
            if (!IsLoosePickable(candidate) || candidate.itemData.Tags?.Contains(tag) != true) continue;
            float distance = WorldTopologyRuntime.ShortestDelta(origin, candidate.transform.position).sqrMagnitude;
            if (distance > nearest) continue;
            nearest = distance;
            handle = new DroppedItemHandle(candidate.itemData.Guid, 0, candidate);
        }
        forageLegacyCandidates.Clear(); forageLegacyDedupe.Clear();
        return handle.IsValid;
    }

    /// <summary>重查句柄和落地资格，目标被拾取、沉没或换世界后立即失效。</summary>
    public static bool TryGetPickablePosition(DroppedItemHandle handle, out Vector2 position)
    {
        position = default;
        if (handle.Legacy != null)
        {
            if (!IsLoosePickable(handle.Legacy)) return false;
            position = handle.Legacy.transform.position;
            return true;
        }
        return handle.Epoch == Epoch && runtime != null && runtime.TryGetPickablePosition(handle.Id, out position);
    }

    /// <summary>实际使用前取得独立冷载荷；调用者不能通过此快照修改世界中的库存数量。</summary>
    public static bool TryGetSnapshot(DroppedItemHandle handle, out ItemData snapshot)
    {
        snapshot = null;
        if (!TryGetPickablePosition(handle, out _)) return false;
        snapshot = handle.Legacy != null ? FastCloner.FastCloner.DeepClone(handle.Legacy.itemData) : runtime.Snapshot(handle.Id);
        return true;
    }

    /// <summary>正式消耗时再次校验 Tag、位置、数量和拾取预约；一份种子不可能被两只鸟重复吃掉。</summary>
    public static bool TryConsumeTagged(DroppedItemHandle handle, Vector2 origin, float reach, string tag, int amount)
    {
        if (!GameNetwork.HasStateAuthority || amount <= 0 || reach < 0f || float.IsNaN(reach) || float.IsInfinity(reach) ||
            !TryGetPickablePosition(handle, out Vector2 position) ||
            WorldTopologyRuntime.ShortestDelta(origin, position).sqrMagnitude > reach * reach) return false;
        if (handle.Legacy != null)
        {
            Item source = handle.Legacy;
            if (source.itemData.Tags?.Contains(tag) != true || source.itemData.Stack.Amount < amount) return false;
            source.itemData.Stack.Amount -= amount;
            if (source.itemData.Stack.Amount <= 0f) source.DestroySelf();
            else source.OnUIRefresh?.Invoke();
            return true;
        }
        return handle.Epoch == Epoch && runtime != null && runtime.TryConsumeTagged(handle.Id, tag, amount);
    }

    private static bool IsLoosePickable(Item item) => item != null && !item.DestructionHandled && !item.InHand &&
        item.Owner == null && item.itemData?.Stack != null && item.itemData.Stack.CanBePickedUp && item.itemData.Stack.Amount >= 1f;

    #endregion
}

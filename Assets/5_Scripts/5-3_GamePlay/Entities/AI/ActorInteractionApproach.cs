using UnityEngine;

/// <summary>交互目标占据不可走格时，选择真实作用范围内的站位，避免把路径结束误当作已经够近。</summary>
public static class ActorInteractionApproach
{
    #region 范围内站位
    public static bool TrySubmitDestination(Item actor, Mod_Mover_AI mover, Vector2 target, float reach)
    {
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        if (navigation == null || !navigation.TryGetCell(target, out _, out bool walkable)) return false;
        if (walkable)
        {
            mover.SetInteractionDestination(target, reach, 4000, true);
            return true;
        }

        Vector2Int center = WorldNavigationGrid.WorldToCell(target);
        int radius = Mathf.CeilToInt(reach);
        Vector2 selected = default;
        float nearest = float.PositiveInfinity, selectedMargin = 0f;
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            Vector2 candidate = WorldTopologyRuntime.NormalizePosition(
                (Vector2)(center + new Vector2Int(x, y)) + Vector2.one * .5f);
            float margin = reach - WorldTopologyRuntime.Distance(candidate, target);
            if (margin < .05f || !navigation.TryGetCell(candidate, out _, out bool canStand) || !canStand) continue;
            float distance = WorldTopologyRuntime.SqrDistance(actor.transform.position, candidate);
            if (distance >= nearest) continue;
            nearest = distance; selected = candidate; selectedMargin = margin;
        }
        if (float.IsPositiveInfinity(nearest)) return false;
        // 停止距离最多为剩余交互余量的一半，不能停在合法范围之外。
        mover.SetInteractionDestination(selected, selectedMargin, 4000, true);
        return true;
    }
    #endregion
}

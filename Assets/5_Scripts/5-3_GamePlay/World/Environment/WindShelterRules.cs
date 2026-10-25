using UnityEngine;

/// <summary>查询真实挡风格；未加载的格子返回 false，不生成新地形。</summary>
public interface IWindShelterQuery
{
    #region 挡风格查询
    bool TryGetWindBlocking(Vector2Int cell, out bool blocking);
    #endregion
}

/// <summary>沿实际来风方向查找完整障碍，在其背风面留下有限距离的安全区。</summary>
public static class WindShelterRules
{
    #region 背风区规则

    public const float ShelterDistance = 12f;

    public static bool IsSheltered<T>(Vector2 position, Vector2 windVelocity, ref T query)
        where T : struct, IWindShelterQuery
    {
        float speed = windVelocity.magnitude;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) ||
            !float.IsFinite(speed) || speed <= 0f)
            return false;

        Vector2 direction = -windVelocity / speed;
        var cell = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        int stepX = direction.x > 0f ? 1 : direction.x < 0f ? -1 : 0;
        int stepY = direction.y > 0f ? 1 : direction.y < 0f ? -1 : 0;
        float deltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.x);
        float deltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1f / direction.y);
        float nextX = stepX == 0 ? float.PositiveInfinity :
            (stepX > 0 ? cell.x + 1f - position.x : position.x - cell.x) * deltaX;
        float nextY = stepY == 0 ? float.PositiveInfinity :
            (stepY > 0 ? cell.y + 1f - position.y : position.y - cell.y) * deltaY;

        if (!query.TryGetWindBlocking(cell, out bool blocking)) return false;
        if (blocking) return true;
        while (Mathf.Min(nextX, nextY) <= ShelterDistance)
        {
            // 同时跨越格角时检查两侧，拼成斜墙的完整方块不能在接缝漏风。
            if (Mathf.Abs(nextX - nextY) <= 0.00001f)
            {
                bool knownX = query.TryGetWindBlocking(new Vector2Int(cell.x + stepX, cell.y), out bool sideX);
                bool knownY = query.TryGetWindBlocking(new Vector2Int(cell.x, cell.y + stepY), out bool sideY);
                if ((knownX && sideX) || (knownY && sideY)) return true;
                if (!knownX || !knownY) return false;
                cell.x += stepX;
                cell.y += stepY;
                nextX += deltaX;
                nextY += deltaY;
            }
            else if (nextX < nextY)
            {
                cell.x += stepX;
                nextX += deltaX;
            }
            else
            {
                cell.y += stepY;
                nextY += deltaY;
            }
            if (!query.TryGetWindBlocking(cell, out blocking)) return false;
            if (blocking) return true;
        }
        return false;
    }

    #endregion
}

using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
[RequireComponent(typeof(Tilemap), typeof(TilemapCollider2D))]
public sealed class TilemapDamageReceiver : MonoBehaviour
{
    private Map map;
    private Tilemap tilemap;
    private TilemapCollider2D tilemapCollider;

    public Map BoundMap => map;

    public void Bind(Map owner, Tilemap targetTilemap, TilemapCollider2D targetCollider)
    {
        map = owner;
        tilemap = targetTilemap;
        tilemapCollider = targetCollider;
    }

    public bool TryResolveHit(
        Collider2D attackCollider,
        Vector2 attackOrigin,
        Vector2 attackDirection,
        out TileBuildingHitCandidate candidate)
    {
        candidate = default;
        if (map?.Data == null || tilemap == null || tilemapCollider == null || attackCollider == null)
            return false;

        Bounds attackBounds = attackCollider.bounds;
        float epsilon = Mathf.Max(0.0001f, Mathf.Min(tilemap.cellSize.x, tilemap.cellSize.y) * 0.01f);
        Vector3Int minCell = tilemap.WorldToCell(attackBounds.min - Vector3.one * epsilon);
        Vector3Int maxCell = tilemap.WorldToCell(attackBounds.max + Vector3.one * epsilon);

        bool found = false;
        TileBuildingHitCandidate best = default;
        Vector2 attackCenter = attackBounds.center;
        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                Vector3Int tileCell = new Vector3Int(x, y, 0);
                if (!tilemap.HasTile(tileCell))
                    continue;

                Vector2Int worldCell = new Vector2Int(x, y);
                TileData topTile = map.Data.GetTopTile(worldCell);
                if (!BlockingTilemapLayer.IsBlockingTile(topTile))
                    continue;

                Vector2 center = tilemap.GetCellCenterWorld(tileCell);
                if (!TryGetCellHitPoint(tileCell, attackCollider, center, out Vector2 hitPoint))
                    continue;

                Vector2 fromOrigin = center - attackOrigin;
                float forward = Vector2.Dot(fromOrigin, attackDirection);
                if (forward < -epsilon)
                    continue;

                float lateral = Mathf.Abs(
                    attackDirection.x * fromOrigin.y -
                    attackDirection.y * fromOrigin.x);
                float distance = (center - attackCenter).sqrMagnitude;
                TileBuildingHitCandidate current = new TileBuildingHitCandidate(
                    this,
                    worldCell,
                    hitPoint,
                    Mathf.Max(0f, forward),
                    lateral,
                    distance);

                if (!found || IsBetterCandidate(current, best))
                {
                    found = true;
                    best = current;
                }
            }
        }

        candidate = best;
        return found;
    }

    private bool TryGetCellHitPoint(
        Vector3Int cell,
        Collider2D attackCollider,
        Vector2 cellCenter,
        out Vector2 hitPoint)
    {
        Vector3 a = tilemap.CellToWorld(cell);
        Vector3 b = tilemap.CellToWorld(new Vector3Int(cell.x + 1, cell.y + 1, cell.z));
        float minX = Mathf.Min(a.x, b.x);
        float maxX = Mathf.Max(a.x, b.x);
        float minY = Mathf.Min(a.y, b.y);
        float maxY = Mathf.Max(a.y, b.y);
        hitPoint = attackCollider.ClosestPoint(cellCenter);
        if (maxX - minX <= 0.0001f || maxY - minY <= 0.0001f)
            return false;

        const float epsilon = 0.0001f;
        return hitPoint.x >= minX - epsilon &&
               hitPoint.x <= maxX + epsilon &&
               hitPoint.y >= minY - epsilon &&
               hitPoint.y <= maxY + epsilon;
    }

    private static bool IsBetterCandidate(
        TileBuildingHitCandidate candidate,
        TileBuildingHitCandidate current)
    {
        const float epsilon = 0.0001f;
        if (candidate.ForwardDistance < current.ForwardDistance - epsilon)
            return true;
        if (candidate.ForwardDistance > current.ForwardDistance + epsilon)
            return false;
        if (candidate.LateralDistance < current.LateralDistance - epsilon)
            return true;
        if (candidate.LateralDistance > current.LateralDistance + epsilon)
            return false;
        if (candidate.AttackDistance < current.AttackDistance - epsilon)
            return true;
        if (candidate.AttackDistance > current.AttackDistance + epsilon)
            return false;
        if (candidate.Cell.x != current.Cell.x)
            return candidate.Cell.x < current.Cell.x;
        return candidate.Cell.y < current.Cell.y;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct TileBuildingCell
{
    public TileBuildingCell(Map map, Vector2Int position)
    {
        Map = map;
        Position = position;
    }

    public Map Map { get; }
    public Vector2Int Position { get; }
}

public readonly struct TileBuildingHitCandidate
{
    public TileBuildingHitCandidate(
        TilemapDamageReceiver receiver,
        Vector2Int cell,
        Vector2 hitPoint,
        float forwardDistance,
        float lateralDistance,
        float attackDistance)
    {
        Receiver = receiver;
        Cell = cell;
        HitPoint = hitPoint;
        ForwardDistance = forwardDistance;
        LateralDistance = lateralDistance;
        AttackDistance = attackDistance;
    }

    public TilemapDamageReceiver Receiver { get; }
    public Vector2Int Cell { get; }
    public Vector2 HitPoint { get; }
    public float ForwardDistance { get; }
    public float LateralDistance { get; }
    public float AttackDistance { get; }
}

public readonly struct TileBuildingDamageResult
{
    public TileBuildingDamageResult(
        Map map,
        Vector2Int cell,
        Vector2 hitPoint,
        float appliedDamage,
        float remainingHealth,
        bool destroyed,
        CombatImpactMaterial impactMaterial)
    {
        Map = map;
        Cell = cell;
        HitPoint = hitPoint;
        AppliedDamage = appliedDamage;
        RemainingHealth = remainingHealth;
        Destroyed = destroyed;
        ImpactMaterial = impactMaterial;
    }

    public Map Map { get; }
    public Vector2Int Cell { get; }
    public Vector2 HitPoint { get; }
    public float AppliedDamage { get; }
    public float RemainingHealth { get; }
    public bool Destroyed { get; }
    public CombatImpactMaterial ImpactMaterial { get; }
}

/// <summary>
/// 墙壁等非工作方块的权威运行时入口。实例状态保存在 Map.Data 的顶层 TileData，
/// 不创建 Item、Module 或每格 GameObject。
/// </summary>
public static class TileBuildingSystem
{
    private static readonly List<Collider2D> OverlapBuffer = new List<Collider2D>(16);
    private static readonly HashSet<int> VisitedReceivers = new HashSet<int>();

    public static event Action<TileBuildingCell> CellPlaced;
    public static event Action<TileBuildingDamageResult> CellDamaged;
    public static event Action<TileBuildingDamageResult> CellDestroyed;

    public static bool TryPlace(
        Vector3 worldPosition,
        string tileBlockId,
        out TileBuildingCell placedCell,
        out string reason)
    {
        placedCell = default;
        reason = null;
        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y));

        if (!TryGetLoadedMap(cell, out Map map, out reason))
            return false;

        Tile_Block definition = GameRes.Instance?.GetTileBlock(tileBlockId);
        if (definition?.tileDataTemplate == null)
        {
            reason = $"找不到格子建筑定义：{tileBlockId}";
            return false;
        }

        if (!BlockingTilemapLayer.IsBlockingTile(definition.tileDataTemplate))
        {
            reason = $"{tileBlockId} 不是有效的阻挡 Tile";
            return false;
        }

        List<TileData> tiles = map.Data.GetTileListAt(cell);
        if (tiles == null || tiles.Count == 0)
        {
            reason = $"地块 {cell} 不可建造";
            return false;
        }

        if (BlockingTilemapLayer.IsBlockingTile(tiles[^1]))
        {
            reason = $"地块 {cell} 已有阻挡方块";
            return false;
        }

        TileData tile = definition.tileDataTemplate.Clone();
        TileBuildingDamageProfile profile = definition.damageProfile;
        if (profile?.Damageable == true)
            tile = TileData_CellBuilding.FromTile(tile, Mathf.Max(1f, profile.MaxHealth));

        map.ADDTile(cell, tile);
        RefreshBlockingState(map, cell);
        placedCell = new TileBuildingCell(map, cell);
        CellPlaced?.Invoke(placedCell);
        return true;
    }

    public static bool TryRemove(
        Map map,
        Vector2Int cell,
        bool spawnDrop,
        out string reason)
    {
        reason = null;
        if (!TryGetTopBlockingTile(map, cell, out List<TileData> tiles, out TileData topTile))
        {
            reason = $"地块 {cell} 没有可移除的阻挡方块";
            return false;
        }

        TileBuildingDamageProfile profile = ResolveProfile(topTile);
        int topIndex = tiles.Count - 1;
        map.DELTile(cell, topIndex);
        RefreshBlockingState(map, cell);
        if (spawnDrop)
            SpawnDrop(profile, cell);
        return true;
    }

    public static bool TryDamageNearest(
        Mod_Damage sender,
        Collider2D attackCollider,
        out TileBuildingDamageResult result)
    {
        result = default;
        if (sender == null || attackCollider == null || !attackCollider.enabled)
            return false;

        // 远程客户端不能自行修改世界格；服务器/单机才是权威端。
        if (ItemNetworkStateSerialization.DeferLocalDestruction())
            return false;

        Vector2 attackCenter = attackCollider.bounds.center;
        Vector2 origin = ResolveAttackOrigin(sender, attackCenter);
        Vector2 direction = attackCenter - origin;
        if (direction.sqrMagnitude < 0.000001f)
            direction = attackCollider.transform.right;
        direction.Normalize();

        // 项目关闭了 Physics2D 自动同步；动画当帧移动/旋转武器后，查询前必须刷新物理姿态。
        Physics2D.SyncTransforms();
        OverlapBuffer.Clear();
        VisitedReceivers.Clear();
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
            useNormalAngle = false
        };
        attackCollider.OverlapCollider(filter, OverlapBuffer);

        bool found = false;
        TileBuildingHitCandidate best = default;
        for (int i = 0; i < OverlapBuffer.Count; i++)
        {
            Collider2D overlap = OverlapBuffer[i];
            if (overlap == null ||
                !overlap.TryGetComponent(out TilemapDamageReceiver receiver) ||
                !VisitedReceivers.Add(receiver.GetInstanceID()) ||
                !receiver.TryResolveHit(attackCollider, origin, direction, out TileBuildingHitCandidate candidate))
            {
                continue;
            }

            if (!found || IsBetterCandidate(candidate, best))
            {
                found = true;
                best = candidate;
            }
        }

        return found && TryDamage(best, sender, out result);
    }

    public static float CalculateDamage(
        TileBuildingDamageProfile profile,
        IDamageSender sender,
        out bool weaknessMatched)
    {
        weaknessMatched = false;
        if (profile?.Damageable != true || sender?.Damage == null)
            return 0f;

        if (profile.RequiredTool != TileDamageToolKind.None &&
            (sender is not Mod_Damage damageModule ||
             damageModule.TileDamageToolKind != profile.RequiredTool))
        {
            return 0f;
        }

        float defenseReductionRatio = 0f;
        if (profile.Weakness != null && sender.Weakness != null)
        {
            for (int i = 0; i < profile.Weakness.Count; i++)
            {
                for (int j = 0; j < sender.Weakness.Count; j++)
                {
                    DamageType receiverType = profile.Weakness[i];
                    DamageType attackerType = sender.Weakness[j];
                    if (receiverType.Tag != attackerType.Tag)
                        continue;

                    weaknessMatched = true;
                    int receiverLevel = Mathf.Max(1, receiverType.Level);
                    int attackerLevel = Mathf.Max(1, attackerType.Level);
                    float reduction = 1f - Mathf.Clamp01((receiverLevel - attackerLevel) * 0.5f);
                    defenseReductionRatio = Mathf.Max(defenseReductionRatio, reduction);
                }
            }
        }

        if (profile.RequireWeaknessMatch && !weaknessMatched)
            return 0f;

        float multiplier = GameDifficultyService.ResolveDirectDamageMultiplier(sender.attacker, null);
        float rawDamage = sender.Damage.Value * multiplier;
        if (rawDamage <= 0f)
            return 0f;

        float effectiveDefense = profile.Defense * (1f - defenseReductionRatio);
        return Mathf.Max(1f, rawDamage - effectiveDefense);
    }

    private static bool TryDamage(
        TileBuildingHitCandidate hit,
        Mod_Damage sender,
        out TileBuildingDamageResult result)
    {
        result = default;
        Map map = hit.Receiver != null ? hit.Receiver.BoundMap : null;
        if (!TryGetTopBlockingTile(map, hit.Cell, out List<TileData> tiles, out TileData topTile))
            return false;

        TileBuildingDamageProfile profile = ResolveProfile(topTile);
        float calculatedDamage = CalculateDamage(profile, sender, out _);
        if (calculatedDamage <= 0f)
            return false;

        int topIndex = tiles.Count - 1;
        TileData_CellBuilding state = topTile as TileData_CellBuilding;
        if (state == null)
        {
            state = TileData_CellBuilding.FromTile(topTile, Mathf.Max(1f, profile.MaxHealth));
            if (state == null || !map.Data.UpdateTileData(hit.Cell, topIndex, state))
                return false;
        }

        float maxHealth = Mathf.Max(1f, profile.MaxHealth);
        if (state.Version <= 0)
        {
            state.Version = TileData_CellBuilding.CurrentVersion;
            state.CurrentHp = maxHealth;
        }
        state.CurrentHp = Mathf.Clamp(state.CurrentHp, 0f, maxHealth);

        float appliedDamage = Mathf.Min(calculatedDamage, state.CurrentHp);
        if (appliedDamage <= 0f)
            return false;

        state.CurrentHp -= appliedDamage;
        bool destroyed = state.CurrentHp <= 0f;
        if (destroyed)
        {
            map.DELTile(hit.Cell, topIndex);
            RefreshBlockingState(map, hit.Cell);
            SpawnDrop(profile, hit.Cell);
        }

        result = new TileBuildingDamageResult(
            map,
            hit.Cell,
            hit.HitPoint,
            appliedDamage,
            state.CurrentHp,
            destroyed,
            profile.ImpactMaterial);
        CellDamaged?.Invoke(result);
        if (destroyed)
            CellDestroyed?.Invoke(result);
        return true;
    }

    private static bool TryGetLoadedMap(Vector2Int cell, out Map map, out string reason)
    {
        map = null;
        reason = null;
        if (ChunkMgr.Instance == null)
        {
            reason = "区块管理器尚未就绪";
            return false;
        }

        Vector2 center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
        ChunkMgr.Instance.GetChunkBy_ItemPosition(center, out Chunk chunk);
        map = chunk?.Map;
        if (map?.Data != null)
            return true;

        reason = $"地块 {cell} 所在区块尚未加载";
        return false;
    }

    private static bool TryGetTopBlockingTile(
        Map map,
        Vector2Int cell,
        out List<TileData> tiles,
        out TileData topTile)
    {
        tiles = map?.Data?.GetTileListAt(cell);
        topTile = tiles != null && tiles.Count > 0 ? tiles[^1] : null;
        return BlockingTilemapLayer.IsBlockingTile(topTile);
    }

    private static TileBuildingDamageProfile ResolveProfile(TileData tile)
    {
        if (tile == null || GameRes.Instance == null)
            return null;

        Tile_Block definition = GameRes.Instance.GetTileBlock(tile.ID);
        if (definition == null && !string.Equals(tile.ID, tile.Name, StringComparison.Ordinal))
            definition = GameRes.Instance.GetTileBlock(tile.Name);
        return definition?.damageProfile;
    }

    private static void RefreshBlockingState(Map map, Vector2Int cell)
    {
        if (map == null)
            return;

        map.MarkPenaltyDirty(cell);
        map.BackTilePenalty_Async();
        map.GetComponent<BlockingTilemapLayer>()?.ProcessColliderChanges();
    }

    private static void SpawnDrop(TileBuildingDamageProfile profile, Vector2Int cell)
    {
        if (profile == null ||
            string.IsNullOrWhiteSpace(profile.DropItemId) ||
            profile.DropAmount <= 0 ||
            ItemMgr.Instance == null)
        {
            return;
        }

        Vector3 position = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        for (int i = 0; i < profile.DropAmount; i++)
        {
            try
            {
                Item drop = ItemMgr.Instance.InstantiateItem(profile.DropItemId, position);
                if (drop == null)
                    continue;

                drop.Load();
                if (drop.itemData?.Stack != null)
                    drop.itemData.Stack.CanBePickedUp = true;
                drop.DropInRange();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[格子建筑] 掉落 {profile.DropItemId} 失败：{exception.Message}");
                break;
            }
        }
    }

    private static Vector2 ResolveAttackOrigin(Mod_Damage sender, Vector2 fallback)
    {
        Item attacker = sender.item;
        if (attacker?.Owner != null)
            return attacker.Owner.transform.position;
        if (attacker != null)
            return attacker.transform.position;
        return fallback - (Vector2)sender.transform.right;
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

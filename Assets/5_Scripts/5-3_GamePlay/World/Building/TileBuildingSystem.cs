using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

using RuntimeChunk = FlatWorld.WorldModel.ChunkRuntime;

public readonly struct TileBuildingCell
{
    public TileBuildingCell(Map map, Vector2Int position)
    {
        Map = map;
        Position = position;
        RuntimeChunk = null;
        LocalPosition = position;
        RuntimeTileId = 0;
        TileBlockId = null;
    }

    public TileBuildingCell(RuntimeChunk runtimeChunk, Vector2Int position,
        Vector2Int localPosition, int runtimeTileId, string tileBlockId)
    {
        Map = null;
        Position = position;
        RuntimeChunk = runtimeChunk;
        LocalPosition = localPosition;
        RuntimeTileId = runtimeTileId;
        TileBlockId = tileBlockId;
    }

    public Map Map { get; }
    public RuntimeChunk RuntimeChunk { get; }
    public Vector2Int Position { get; }
    public Vector2Int LocalPosition { get; }
    public int RuntimeTileId { get; }
    public string TileBlockId { get; }
    public bool UsesRuntimeTerrain => RuntimeChunk != null;
}

public readonly struct TileBuildingHitCandidate
{
    public TileBuildingHitCandidate(
        TileBuildingCell target,
        Vector2 hitPoint,
        float forwardDistance,
        float lateralDistance,
        float attackDistance)
    {
        Target = target;
        HitPoint = hitPoint;
        ForwardDistance = forwardDistance;
        LateralDistance = lateralDistance;
        AttackDistance = attackDistance;
    }

    public TileBuildingCell Target { get; }
    public Vector2Int Cell => Target.Position;
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
/// 墙壁等非工作方块的权威运行时入口。新区块把状态保存在 ChunkTerrainData 的
/// BlockingTileId，旧 Map.Data 仅作为兼容回退，不创建每格 GameObject。
/// </summary>
public static class TileBuildingSystem
{
    /// <summary>运行时区块中每格建筑累计损伤使用的权威环境层。</summary>
    public const string RuntimeDamageLayerId = "flatworld.tileBuilding.damage";

    private static readonly List<Collider2D> OverlapBuffer = new List<Collider2D>(16);
    private static readonly HashSet<int> VisitedReceivers = new HashSet<int>();

    public static event Action<TileBuildingCell> CellPlaced;
    public static event Action<TileBuildingDamageResult> CellDamaged;
    public static event Action<TileBuildingDamageResult> CellDestroyed;

    /// <summary>只做放置资格检查，不改动地图；建筑虚影和右键放置共用这条规则。</summary>
    public static bool CanPlace(Vector3 worldPosition, string tileBlockId, out string reason)
    {
        reason = null;
        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y));

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

        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager?.RuntimeChunks != null)
        {
            Vector2 center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            if (!chunkManager.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample runtimeTile))
            {
                reason = $"地块 {cell} 所在新区块尚未加载";
                return false;
            }

            if (!TryResolveRuntimeTileId(chunkManager, tileBlockId, out int runtimeTileId))
            {
                reason = $"新区块配置未注册建筑地块：{tileBlockId}";
                return false;
            }

            ChunkTerrainData terrain = runtimeTile.Terrain;
            if (runtimeTile.TopTileId == 0 || !terrain.IsWalkable(
                    runtimeTile.LocalCell.x, runtimeTile.LocalCell.y))
            {
                reason = $"地块 {cell} 不可建造或已有阻挡方块";
                return false;
            }

            if (!terrain.CanSetBlockingTile(runtimeTile.LocalCell.x, runtimeTile.LocalCell.y,
                    runtimeTileId))
            {
                reason = $"地块 {cell} 使用了不支持运行时建筑的扩展地块堆栈";
                return false;
            }

            return true;
        }

        if (!TryGetLoadedMap(cell, out Map map, out reason))
            return false;

        int layerCount = map.Data.GetLayerCount(cell);
        if (layerCount == 0)
        {
            reason = $"地块 {cell} 不可建造";
            return false;
        }

        if (BlockingTilemapLayer.IsBlockingTile(map.Data.GetTopTile(cell)))
        {
            reason = $"地块 {cell} 已有阻挡方块";
            return false;
        }

        return true;
    }

    /// <summary>在旧 Map 或新版 ChunkTerrainData 的阻挡层中放置一格建筑。</summary>
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

        // 新区块已经成为权威地形时，建筑直接写入 ChunkTerrainData，不能再要求旧 Map 表现对象存在。
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager?.RuntimeChunks != null)
        {
            Vector2 center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            if (!chunkManager.TryGetRuntimeTerrainTile(center, out RuntimeTerrainTileSample runtimeTile))
            {
                reason = $"地块 {cell} 所在新区块尚未加载";
                return false;
            }

            if (!TryResolveRuntimeTileId(chunkManager, tileBlockId, out int runtimeTileId))
            {
                reason = $"新区块配置未注册建筑地块：{tileBlockId}";
                return false;
            }

            ChunkTerrainData terrain = runtimeTile.Terrain;
            if (runtimeTile.TopTileId == 0 || !terrain.IsWalkable(
                    runtimeTile.LocalCell.x, runtimeTile.LocalCell.y))
            {
                reason = $"地块 {cell} 不可建造或已有阻挡方块";
                return false;
            }

            if (!terrain.TrySetBlockingTile(runtimeTile.LocalCell.x, runtimeTile.LocalCell.y,
                    runtimeTileId))
            {
                reason = $"地块 {cell} 使用了不支持运行时建筑的扩展地块堆栈";
                return false;
            }

            if (!chunkManager.TryGetChunkRuntime(runtimeTile.Address, out RuntimeChunk runtimeChunk))
            {
                terrain.TryRemoveBlockingTile(runtimeTile.LocalCell.x, runtimeTile.LocalCell.y,
                    runtimeTileId);
                reason = $"地块 {cell} 所在新区块已失效";
                return false;
            }

            placedCell = new TileBuildingCell(runtimeChunk, runtimeTile.WorldCell,
                runtimeTile.LocalCell, runtimeTileId, tileBlockId);
            ResetRuntimeDamage(terrain, runtimeTile.LocalCell);
            SaveDataMgr.Instance?.RecordRuntimeTerrainChange(placedCell);
            CellPlaced?.Invoke(placedCell);
            return true;
        }

        if (!TryGetLoadedMap(cell, out Map map, out reason))
            return false;

        int layerCount = map.Data.GetLayerCount(cell);
        if (layerCount == 0)
        {
            reason = $"地块 {cell} 不可建造";
            return false;
        }

        if (BlockingTilemapLayer.IsBlockingTile(map.Data.GetTopTile(cell)))
        {
            reason = $"地块 {cell} 已有阻挡方块";
            return false;
        }

        TileData tile = definition.tileDataTemplate.Clone();
        TileBuildingDamageProfile profile = definition.damageProfile;
        if (profile?.Damageable == true)
            tile = TileData_CellBuilding.FromTile(tile, Mathf.Max(1f, profile.MaxHealth));

        map.PushTile(cell, tile);
        RefreshBlockingState(map, cell);
        placedCell = new TileBuildingCell(map, cell);
        CellPlaced?.Invoke(placedCell);
        return true;
    }

    /// <summary>按放置时保存的权威区块位置移除建筑，兼容旧 Map 单元格调用。</summary>
    public static bool TryRemove(
        TileBuildingCell placedCell,
        bool spawnDrop,
        out string reason)
    {
        if (!placedCell.UsesRuntimeTerrain)
            return TryRemove(placedCell.Map, placedCell.Position, spawnDrop, out reason);

        reason = null;
        RuntimeChunk runtimeChunk = placedCell.RuntimeChunk;
        ChunkTerrainData terrain = runtimeChunk?.Terrain;
        if (runtimeChunk == null || runtimeChunk.DataStatus != ChunkDataStatus.Ready ||
            terrain == null || terrain.IsDisposed)
        {
            reason = $"地块 {placedCell.Position} 所在新区块已卸载";
            return false;
        }

        int localX = placedCell.LocalPosition.x;
        int localY = placedCell.LocalPosition.y;
        TerrainCell current = terrain.GetCell(localX, localY);
        if (current.BlockingTileId == 0 ||
            (placedCell.RuntimeTileId != 0 && current.BlockingTileId != placedCell.RuntimeTileId))
        {
            reason = $"地块 {placedCell.Position} 没有可移除的运行时建筑";
            return false;
        }

        if (!terrain.TryRemoveBlockingTile(localX, localY, placedCell.RuntimeTileId))
        {
            reason = $"地块 {placedCell.Position} 的运行时建筑状态已改变";
            return false;
        }

        ResetRuntimeDamage(terrain, placedCell.LocalPosition);
        SaveDataMgr.Instance?.RecordRuntimeTerrainChange(placedCell);

        if (spawnDrop)
        {
            TileBuildingDamageProfile profile = ResolveRuntimeProfile(placedCell);
            SpawnDrop(profile, placedCell.Position);
        }

        return true;
    }

    /// <summary>从旧 Map.Data 中移除一格阻挡建筑并按需生成掉落。</summary>
    public static bool TryRemove(
        Map map,
        Vector2Int cell,
        bool spawnDrop,
        out string reason)
    {
        reason = null;
        if (!TryGetTopBlockingTile(map, cell, out int topIndex, out TileData topTile))
        {
            reason = $"地块 {cell} 没有可移除的阻挡方块";
            return false;
        }

        TileBuildingDamageProfile profile = ResolveProfile(topTile);
        map.RemoveTile(cell, topIndex);
        RefreshBlockingState(map, cell);
        if (spawnDrop)
            SpawnDrop(profile, cell);
        return true;
    }

    /// <summary>从权威区块与旧 Tilemap 中选择最近命中格，并结算一次建筑伤害。</summary>
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

        // 项目关闭了 Physics2D 自动同步；动画当帧移动/旋转武器后，读取 Bounds 前先刷新物理姿态。
        Physics2D.SyncTransforms();
        Vector2 attackCenter = attackCollider.bounds.center;
        Vector2 origin = ResolveAttackOrigin(sender, attackCenter);
        Vector2 direction = attackCenter - origin;
        if (direction.sqrMagnitude < 0.000001f)
            direction = attackCollider.transform.right;
        direction.Normalize();

        // 新版区块直接读取 ChunkTerrainData 权威阻挡层，不依赖 Tilemap 表现是否带 Receiver 或处于何种物理层。
        bool found = TryFindRuntimeHitCandidate(
            attackCollider,
            origin,
            direction,
            out TileBuildingHitCandidate best);

        OverlapBuffer.Clear();
        VisitedReceivers.Clear();
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = false,
            useDepth = false,
            useNormalAngle = false
        };

        // Tile/建筑伤害是显式查询通道，不能依赖 DamageSender 与 Tilemap Layer 是否允许物理接触。
        // 这里只用攻击 Collider 的世界 AABB 做广相；TilemapDamageReceiver.TryResolveHit 会继续用
        // 原始 attackCollider.ClosestPoint 做逐格精确验证，因此旋转武器不会因为 AABB 扩大而误伤。
        Bounds attackBounds = attackCollider.bounds;
        Physics2D.OverlapBox(
            attackBounds.center,
            attackBounds.size,
            0f,
            filter,
            OverlapBuffer);

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

    /// <summary>按工具门槛、难度倍率、防御和建筑最低武器伤害计算最终数值。</summary>
    public static float CalculateDamage(
        TileBuildingDamageProfile profile,
        IDamageSender sender,
        out bool weaknessMatched)
    {
        // 旧 weaknessMatched 输出仅为 API 兼容；等级弱点系统已经移除。
        weaknessMatched = true;
        if (profile?.Damageable != true || sender?.DamageValues == null)
            return 0f;

        if (profile.RequiredTool != TileDamageToolKind.None &&
            (sender is not Mod_Damage damageModule ||
             damageModule.TileDamageToolKind != profile.RequiredTool))
        {
            return 0f;
        }

        float multiplier = GameDifficultyService.ResolveDirectDamageMultiplier(sender.attacker, null);
        CombatDamage scaledDamage = sender.DamageValues.Scaled(multiplier);
        float calculatedDamage = scaledDamage.CalculateAgainst(profile.ResolveDefense());
        if (scaledDamage.TotalCombatPower > 0f && profile.MinimumWeaponDamage > 0f)
            calculatedDamage = Mathf.Max(calculatedDamage, profile.MinimumWeaponDamage);
        return calculatedDamage;
    }

    /// <summary>按命中目标所属的权威地形分派到新版区块或旧 Map 伤害链。</summary>
    private static bool TryDamage(
        TileBuildingHitCandidate hit,
        Mod_Damage sender,
        out TileBuildingDamageResult result)
    {
        return hit.Target.UsesRuntimeTerrain
            ? TryDamageRuntime(hit, sender, out result)
            : TryDamageLegacy(hit, sender, out result);
    }

    /// <summary>结算旧 Map.Data 中带 TileData_CellBuilding 状态的格子建筑伤害。</summary>
    private static bool TryDamageLegacy(
        TileBuildingHitCandidate hit,
        Mod_Damage sender,
        out TileBuildingDamageResult result)
    {
        result = default;
        Map map = hit.Target.Map;
        if (!TryGetTopBlockingTile(map, hit.Cell, out int topIndex, out TileData topTile))
            return false;

        TileBuildingDamageProfile profile = ResolveProfile(topTile);
        if (profile?.Damageable != true)
            return false;

        float maxHealth = Mathf.Max(1f, profile.MaxHealth);
        TileData_CellBuilding state = topTile as TileData_CellBuilding;
        float remainingHealth = state != null
            ? Mathf.Clamp(state.CurrentHp, 0f, maxHealth)
            : maxHealth;
        float calculatedDamage = CalculateDamage(profile, sender, out _);
        if (calculatedDamage <= 0f)
        {
            result = new TileBuildingDamageResult(
                map,
                hit.Cell,
                hit.HitPoint,
                0f,
                remainingHealth,
                false,
                profile.ImpactMaterial);
            return true;
        }

        if (state == null)
        {
            state = TileData_CellBuilding.FromTile(topTile, maxHealth);
            if (state == null || !map.Data.UpdateTileAt(hit.Cell, topIndex, state))
                return false;
        }

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
            map.RemoveTile(hit.Cell, topIndex);
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

    /// <summary>结算 ChunkTerrainData 阻挡层建筑伤害，并把累计损伤同步到运行时差量。</summary>
    private static bool TryDamageRuntime(
        TileBuildingHitCandidate hit,
        Mod_Damage sender,
        out TileBuildingDamageResult result)
    {
        result = default;
        TileBuildingCell target = hit.Target;
        RuntimeChunk runtimeChunk = target.RuntimeChunk;
        ChunkTerrainData terrain = runtimeChunk?.Terrain;
        if (runtimeChunk == null || runtimeChunk.DataStatus != ChunkDataStatus.Ready ||
            terrain == null || terrain.IsDisposed)
        {
            return false;
        }

        int localX = target.LocalPosition.x;
        int localY = target.LocalPosition.y;
        if ((uint)localX >= (uint)terrain.Width || (uint)localY >= (uint)terrain.Height)
            return false;

        TerrainCell current = terrain.GetCell(localX, localY);
        if (current.BlockingTileId == 0 ||
            (target.RuntimeTileId != 0 && current.BlockingTileId != target.RuntimeTileId))
        {
            return false;
        }

        TileBuildingDamageProfile profile = ResolveRuntimeProfile(target);
        if (profile?.Damageable != true)
            return false;

        float maxHealth = Mathf.Max(1f, profile.MaxHealth);
        float accumulatedDamage = ResolveRuntimeDamage(terrain, target.LocalPosition, maxHealth);
        float remainingHealth = Mathf.Max(0f, maxHealth - accumulatedDamage);
        float calculatedDamage = CalculateDamage(profile, sender, out _);
        if (calculatedDamage <= 0f)
        {
            result = new TileBuildingDamageResult(
                null,
                target.Position,
                hit.HitPoint,
                0f,
                remainingHealth,
                false,
                profile.ImpactMaterial);
            return true;
        }

        float appliedDamage = Mathf.Min(calculatedDamage, remainingHealth);
        if (appliedDamage <= 0f)
            return false;

        accumulatedDamage = Mathf.Min(maxHealth, accumulatedDamage + appliedDamage);
        remainingHealth = Mathf.Max(0f, maxHealth - accumulatedDamage);
        bool destroyed = remainingHealth <= 0f;
        if (destroyed)
        {
            if (!terrain.TryRemoveBlockingTile(localX, localY, current.BlockingTileId))
                return false;
            ResetRuntimeDamage(terrain, target.LocalPosition);
            SpawnDrop(profile, target.Position);
        }
        else
        {
            terrain.SetEnvironmentValue(
                RuntimeDamageLayerId,
                localX,
                localY,
                accumulatedDamage);
        }

        SaveDataMgr.Instance?.RecordRuntimeTerrainChange(target);
        result = new TileBuildingDamageResult(
            null,
            target.Position,
            hit.HitPoint,
            appliedDamage,
            remainingHealth,
            destroyed,
            profile.ImpactMaterial);
        CellDamaged?.Invoke(result);
        if (destroyed)
            CellDestroyed?.Invoke(result);
        return true;
    }

    /// <summary>从新版区块权威阻挡层中选出与攻击形状真正相交且最靠前的格子。</summary>
    private static bool TryFindRuntimeHitCandidate(
        Collider2D attackCollider,
        Vector2 attackOrigin,
        Vector2 attackDirection,
        out TileBuildingHitCandidate candidate)
    {
        candidate = default;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager?.RuntimeChunks == null)
            return false;

        const float epsilon = 0.0001f;
        Bounds attackBounds = attackCollider.bounds;
        int minX = Mathf.FloorToInt(attackBounds.min.x - epsilon);
        int maxX = Mathf.FloorToInt(attackBounds.max.x + epsilon);
        int minY = Mathf.FloorToInt(attackBounds.min.y - epsilon);
        int maxY = Mathf.FloorToInt(attackBounds.max.y + epsilon);
        Vector2 attackCenter = attackBounds.center;
        bool found = false;
        TileBuildingHitCandidate best = default;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2 queryCenter = new Vector2(x + 0.5f, y + 0.5f);
                if (!chunkManager.TryGetRuntimeTerrainTile(
                        queryCenter,
                        out RuntimeTerrainTileSample sample) ||
                    sample.Cell.BlockingTileId == 0 ||
                    !chunkManager.TryGetChunkRuntime(sample.Address, out RuntimeChunk runtimeChunk) ||
                    !TryResolveRuntimeTileBlockId(
                        chunkManager,
                        sample.Cell.BlockingTileId,
                        out string tileBlockId) ||
                    !TryGetRuntimeCellHitPoint(
                        attackCollider,
                        new Vector2Int(x, y),
                        queryCenter,
                        out Vector2 hitPoint))
                {
                    continue;
                }

                Vector2 fromOrigin = queryCenter - attackOrigin;
                float forward = Vector2.Dot(fromOrigin, attackDirection);
                if (forward < -epsilon)
                    continue;

                float lateral = Mathf.Abs(
                    attackDirection.x * fromOrigin.y -
                    attackDirection.y * fromOrigin.x);
                float distance = (queryCenter - attackCenter).sqrMagnitude;
                TileBuildingCell target = new TileBuildingCell(
                    runtimeChunk,
                    sample.WorldCell,
                    sample.LocalCell,
                    sample.Cell.BlockingTileId,
                    tileBlockId);
                TileBuildingHitCandidate current = new TileBuildingHitCandidate(
                    target,
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

    /// <summary>用攻击 Collider 的最近点验证它是否真正进入指定的一格世界方块。</summary>
    private static bool TryGetRuntimeCellHitPoint(
        Collider2D attackCollider,
        Vector2Int queryCell,
        Vector2 cellCenter,
        out Vector2 hitPoint)
    {
        hitPoint = attackCollider.ClosestPoint(cellCenter);
        const float epsilon = 0.0001f;
        return hitPoint.x >= queryCell.x - epsilon &&
               hitPoint.x <= queryCell.x + 1f + epsilon &&
               hitPoint.y >= queryCell.y - epsilon &&
               hitPoint.y <= queryCell.y + 1f + epsilon;
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

    private static bool TryResolveRuntimeTileId(
        ChunkMgr chunkManager,
        string tileBlockId,
        out int runtimeTileId)
    {
        runtimeTileId = 0;
        if (chunkManager == null || string.IsNullOrWhiteSpace(tileBlockId))
            return false;

        const string prefix = "tile.block.";
        IReadOnlyDictionary<string, string> textParameters =
            chunkManager.ActiveGenerationProfile?.TextParameters;
        if (textParameters == null)
            return false;

        foreach (KeyValuePair<string, string> parameter in textParameters)
        {
            if (!parameter.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                !string.Equals(parameter.Value, tileBlockId, StringComparison.Ordinal))
            {
                continue;
            }

            string numericPart = parameter.Key.Substring(prefix.Length);
            if (int.TryParse(numericPart, out runtimeTileId) && runtimeTileId > 0)
                return true;
        }

        return false;
    }

    /// <summary>把新版地形中的数字阻挡 TileId 解析回稳定的 Tile_Block ID。</summary>
    private static bool TryResolveRuntimeTileBlockId(
        ChunkMgr chunkManager,
        int runtimeTileId,
        out string tileBlockId)
    {
        tileBlockId = null;
        if (chunkManager == null || runtimeTileId <= 0)
            return false;

        IReadOnlyDictionary<string, string> textParameters =
            chunkManager.ActiveGenerationProfile?.TextParameters;
        return textParameters != null &&
               textParameters.TryGetValue($"tile.block.{runtimeTileId}", out tileBlockId) &&
               !string.IsNullOrWhiteSpace(tileBlockId);
    }

    private static bool TryGetTopBlockingTile(
        Map map,
        Vector2Int cell,
        out int topIndex,
        out TileData topTile)
    {
        int layerCount = map?.Data?.GetLayerCount(cell) ?? 0;
        topIndex = layerCount - 1;
        topTile = layerCount > 0 ? map.Data.GetTopTile(cell) : null;
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

    /// <summary>从运行时数字 TileId 或已知稳定 ID 解析对应的格子建筑伤害配置。</summary>
    private static TileBuildingDamageProfile ResolveRuntimeProfile(TileBuildingCell placedCell)
    {
        if (GameRes.Instance == null)
            return null;

        string tileBlockId = placedCell.TileBlockId;
        if (string.IsNullOrWhiteSpace(tileBlockId) &&
            !TryResolveRuntimeTileBlockId(
                ChunkMgr.Instance,
                placedCell.RuntimeTileId,
                out tileBlockId))
        {
            return null;
        }

        Tile_Block definition = GameRes.Instance.GetTileBlock(tileBlockId);
        return definition?.damageProfile;
    }

    /// <summary>读取并校正一格运行时建筑已经累计的损伤值。</summary>
    private static float ResolveRuntimeDamage(
        ChunkTerrainData terrain,
        Vector2Int localPosition,
        float maxHealth)
    {
        if (terrain == null ||
            !terrain.TryGetEnvironmentValue(
                RuntimeDamageLayerId,
                localPosition.x,
                localPosition.y,
                out float accumulatedDamage))
        {
            return 0f;
        }

        return Mathf.Clamp(accumulatedDamage, 0f, Mathf.Max(1f, maxHealth));
    }

    /// <summary>清除已拆除或新放置建筑格中可能遗留的运行时损伤。</summary>
    private static void ResetRuntimeDamage(
        ChunkTerrainData terrain,
        Vector2Int localPosition)
    {
        if (terrain == null ||
            !terrain.TryGetEnvironmentValue(
                RuntimeDamageLayerId,
                localPosition.x,
                localPosition.y,
                out float accumulatedDamage) ||
            Mathf.Approximately(accumulatedDamage, 0f))
        {
            return;
        }

        terrain.SetEnvironmentValue(
            RuntimeDamageLayerId,
            localPosition.x,
            localPosition.y,
            0f);
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

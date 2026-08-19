using FlatWorld.WorldModel;
using UltEvents;
using UnityEngine;

/// <summary>
/// 地块效果接收器。优先读取 ChunkRuntime/ChunkTerrainData 权威地形，进入、离开或地块来源变化时
/// 调用 Tile_Block 行为；旧 Map 仅作为尚未迁移场景的兼容回退。
/// </summary>
public class TileEffectReceiver : Module
{
    private const float WaterEdgeTolerance = 0.2f;
    private static readonly Vector2Int[] WaterEdgeDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up
    };

    #region Inspector

    [Header("位置信息")]
    public Vector2Int lastGridPos;

    [Header("旧 Map 兼容缓存")]
    public Map Cache_map;

    [Header("Tile事件")]
    public UltEvent<TileData> OnTileEnterEvent = new();
    public UltEvent<TileData> OnTileExitEvent = new();

    [Tooltip("当前踩着的 TileData 缓存，供其他模块引用")]
    public TileData currentTileData;

    #endregion

    #region 运行时状态

    private Tile_Block activeTileBlock;
    private TileData activeTileData;
    private Map activeTileMap;
    private ChunkTerrainData activeRuntimeTerrain;
    private int activeRuntimeTileId;
    private Vector2Int activeGridPos;
    private bool hasActiveTileEffects;
    private bool isPreparedForWorldTransition;
    private EnvironmentInteractionRunner environmentInteractions;

    public bool HasActiveTileEffects => hasActiveTileEffects;
    public EnvironmentInteractionRunner EnvironmentInteractions => EnsureEnvironmentInteractions();
    public override string CanonicalModuleId => ModText.TileEffectReceiver;

    #endregion

    #region 模块数据

    public Ex_ModData_MemoryPackable ModSaveData;

    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = (Ex_ModData_MemoryPackable)value;
    }

    #endregion

    #region 生命周期

    private void Start()
    {
        enabled = true;
        EnsureEnvironmentInteractions();
        RefreshCurrentTileEffects();
    }

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.TileEffectReceiver;
    }

    public override void ModUpdate(float deltaTime)
    {
        UpdateLegacyMapReference();
        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos || !IsActiveSourceCurrent(currentGridPos))
        {
            ExitCurrentTileEffects();
            lastGridPos = currentGridPos;
            EnterTile(currentGridPos);
        }

        UpdateCurrentTile(deltaTime);
    }

    #endregion

    #region 模块接口

    public override void Load()
    {
        ModSaveData?.ReadData(ref lastGridPos);
        UpdateLegacyMapReference();
    }

    public override void Save()
    {
        // 自动保存不代表离开当前地块，保存过程不能撤销环境效果。
        ModSaveData?.WriteData(lastGridPos);
    }

    public override void Act()
    {
        base.Act();
    }

    #endregion

    #region 地块事件

    private bool EnterTile(Vector2Int gridPos)
    {
        if (item == null || !TryResolveTileEffect(gridPos, out TileEffectResolution resolution))
        {
            currentTileData = null;
            return false;
        }

        CacheActiveTile(gridPos, resolution);
        resolution.TileBlock.OnEnter(item, resolution.TileData, resolution.Map, this);
        OnTileEnterEvent.Invoke(resolution.TileData);
        return true;
    }

    /// <summary>使用进入时缓存退出，避免换维度后拿新地图错误查询旧坐标。</summary>
    private bool ExitCurrentTileEffects()
    {
        if (!hasActiveTileEffects)
        {
            currentTileData = null;
            return false;
        }

        Tile_Block tileBlock = activeTileBlock;
        TileData tileData = activeTileData;
        Map tileMap = activeTileMap;
        ClearActiveTile();

        if (item == null || tileBlock == null || tileData == null)
            return false;

        tileBlock.OnExit(item, tileData, tileMap, this);
        OnTileExitEvent.Invoke(tileData);
        return true;
    }

    private void UpdateCurrentTile(float deltaTime)
    {
        if (!hasActiveTileEffects || item == null || activeTileBlock == null || activeTileData == null)
            return;

        currentTileData = activeTileData;
        activeTileBlock.OnUpdate(item, activeTileData, activeTileMap, this, deltaTime);
    }

    /// <summary>世界切换保存前撤销脚下地块效果，防止环境 Buff 带到下一维度。</summary>
    public void PrepareForWorldTransition()
    {
        if (isPreparedForWorldTransition)
            return;

        isPreparedForWorldTransition = true;
        if (!hasActiveTileEffects && item != null)
        {
            Vector2Int gridPos = GetCurrentGridPos();
            if (TryResolveTileEffect(gridPos, out TileEffectResolution resolution))
            {
                CacheActiveTile(gridPos, resolution);
                isPreparedForWorldTransition = true;
                lastGridPos = gridPos;
            }
        }

        ExitCurrentTileEffects();
    }

    /// <summary>地图加载完成或切换失败恢复后，立即重新绑定脚下地块效果。</summary>
    public bool RefreshCurrentTileEffects()
    {
        UpdateLegacyMapReference();
        if (item == null)
            return false;

        Vector2Int gridPos = GetCurrentGridPos();
        if (IsActiveSourceCurrent(gridPos))
        {
            lastGridPos = gridPos;
            return true;
        }

        ExitCurrentTileEffects();
        lastGridPos = gridPos;
        return EnterTile(gridPos);
    }

    #endregion

    #region 查询与缓存

    private bool TryResolveTileEffect(Vector2Int gridPos, out TileEffectResolution resolution)
    {
        if (TryResolveTileEffectAtPosition(transform.position, gridPos, out TileEffectResolution exactResolution))
        {
            if (exactResolution.TileData is TileData_Water ||
                !IsNearCellEdge(transform.position, gridPos) ||
                !TryResolveNearbyWater(gridPos, out resolution))
            {
                resolution = exactResolution;
            }

            return true;
        }

        if (TryResolveNearbyWater(gridPos, out resolution))
            return true;

        resolution = default;
        return false;
    }

    /// <summary>按指定世界采样点查询新版运行时地形或旧 Map 地块行为。</summary>
    private bool TryResolveTileEffectAtPosition(Vector2 samplePosition, Vector2Int gridPos,
        out TileEffectResolution resolution)
    {
        ChunkMgr manager = ChunkMgr.Instance;
        if (manager != null && manager.TryGetRuntimeTileEffect(samplePosition,
                out RuntimeTerrainTileSample sample, out TileData runtimeData,
                out Tile_Block runtimeBlock))
        {
            resolution = new TileEffectResolution(runtimeBlock, runtimeData, null,
                sample.Terrain, sample.TopTileId);
            return true;
        }

        TileData legacyData = Cache_map?.GetTile(gridPos);
        if (legacyData != null && GameRes.Instance != null)
        {
            Tile_Block legacyBlock = GameRes.Instance.GetTileBlock(legacyData.Name);
            if (legacyBlock != null)
            {
                resolution = new TileEffectResolution(legacyBlock, legacyData, Cache_map, null, 0);
                return true;
            }
        }

        resolution = default;
        return false;
    }

    /// <summary>在角色贴近边界时查找相邻水格，给水池交互保留小范围容错。</summary>
    private bool TryResolveNearbyWater(Vector2Int gridPos, out TileEffectResolution resolution)
    {
        resolution = default;
        if (!IsNearCellEdge(transform.position, gridPos))
            return false;

        for (int i = 0; i < WaterEdgeDirections.Length; i++)
        {
            Vector2Int neighborGridPos = gridPos + WaterEdgeDirections[i];
            Vector2 samplePosition = new Vector2(neighborGridPos.x + 0.5f, neighborGridPos.y + 0.5f);
            if (!TryResolveTileEffectAtPosition(samplePosition, neighborGridPos,
                    out TileEffectResolution candidate) ||
                !(candidate.TileData is TileData_Water))
            {
                continue;
            }

            resolution = candidate;
            return true;
        }

        return false;
    }

    /// <summary>判断角色与当前格子边界的最近距离是否进入水边容错范围。</summary>
    private static bool IsNearCellEdge(Vector2 position, Vector2Int gridPos)
    {
        float distanceToLeft = position.x - gridPos.x;
        float distanceToRight = gridPos.x + 1f - position.x;
        float distanceToBottom = position.y - gridPos.y;
        float distanceToTop = gridPos.y + 1f - position.y;
        float nearestDistance = Mathf.Min(
            distanceToLeft,
            distanceToRight,
            distanceToBottom,
            distanceToTop);
        return nearestDistance >= 0f && nearestDistance <= WaterEdgeTolerance;
    }

    private bool IsActiveSourceCurrent(Vector2Int gridPos)
    {
        if (!hasActiveTileEffects || activeGridPos != gridPos)
            return false;

        if (!TryResolveTileEffect(gridPos, out TileEffectResolution currentResolution))
            return false;

        if (activeRuntimeTerrain != null)
            return ReferenceEquals(currentResolution.RuntimeTerrain, activeRuntimeTerrain) &&
                   currentResolution.RuntimeTileId == activeRuntimeTileId;

        return activeTileMap != null && activeTileMap == currentResolution.Map &&
               ReferenceEquals(activeTileData, currentResolution.TileData);
    }

    private void CacheActiveTile(Vector2Int gridPos, TileEffectResolution resolution)
    {
        activeTileBlock = resolution.TileBlock;
        activeTileData = resolution.TileData;
        activeTileMap = resolution.Map;
        activeRuntimeTerrain = resolution.RuntimeTerrain;
        activeRuntimeTileId = resolution.RuntimeTileId;
        activeGridPos = gridPos;
        hasActiveTileEffects = true;
        isPreparedForWorldTransition = false;
        currentTileData = resolution.TileData;
    }

    private void ClearActiveTile()
    {
        activeTileBlock = null;
        activeTileData = null;
        activeTileMap = null;
        activeRuntimeTerrain = null;
        activeRuntimeTileId = 0;
        hasActiveTileEffects = false;
        currentTileData = null;
    }

    /// <summary>运行时补装通用环境动作运行器；它只保存角色自己的动作实例，不写入存档。</summary>
    private EnvironmentInteractionRunner EnsureEnvironmentInteractions()
    {
        if (environmentInteractions == null)
            environmentInteractions = GetComponent<EnvironmentInteractionRunner>();
        if (environmentInteractions == null)
            environmentInteractions = gameObject.AddComponent<EnvironmentInteractionRunner>();

        environmentInteractions.Bind(item ?? GetComponentInParent<Item>());
        return environmentInteractions;
    }

    private void UpdateLegacyMapReference()
    {
        ChunkMgr manager = ChunkMgr.Instance;
        if (manager == null ||
            manager.TryGetRuntimeTerrainTile(transform.position, out _))
            return;

        manager.GetChunkBy_ItemPosition(transform.position, out Chunk chunk);
        Map currentMap = chunk?.Map;
        if (currentMap != null || Cache_map == null || !Cache_map.gameObject.activeInHierarchy)
            Cache_map = currentMap;
    }

    private Vector2Int GetCurrentGridPos()
    {
        if (Cache_map != null && Cache_map.tileMap != null)
        {
            Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
            return WorldTopologyRuntime.NormalizeCell(new Vector2Int(cell.x, cell.y));
        }

        Vector2 normalized = WorldTopologyRuntime.NormalizePosition(transform.position);
        return new Vector2Int(Mathf.FloorToInt(normalized.x), Mathf.FloorToInt(normalized.y));
    }

    public TileData GetCurrentTileData()
    {
        if (currentTileData != null)
            return currentTileData;

        return TryResolveTileEffect(GetCurrentGridPos(), out TileEffectResolution resolution)
            ? resolution.TileData
            : null;
    }

    private readonly struct TileEffectResolution
    {
        public TileEffectResolution(Tile_Block tileBlock, TileData tileData, Map map,
            ChunkTerrainData runtimeTerrain, int runtimeTileId)
        {
            TileBlock = tileBlock;
            TileData = tileData;
            Map = map;
            RuntimeTerrain = runtimeTerrain;
            RuntimeTileId = runtimeTileId;
        }

        public Tile_Block TileBlock { get; }
        public TileData TileData { get; }
        public Map Map { get; }
        public ChunkTerrainData RuntimeTerrain { get; }
        public int RuntimeTileId { get; }
    }

    #endregion
}

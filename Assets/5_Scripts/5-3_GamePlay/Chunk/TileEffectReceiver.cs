using System.Collections.Generic;
using System.Linq;
using UltEvents;
using UnityEngine;

/// <summary>
/// Tile效果接收器模块，用于处理物品与地图Tile的交互
/// 负责检测物品所在的Tile变化，并触发相应的进入、退出和更新事件
/// </summary>
public class TileEffectReceiver : Module
{
    #region 公共变量
    [Header("位置信息")]
    public Vector2Int lastGridPos;
    [Header("当前所处地图缓存")]
    public Map Cache_map;

    [Header("Tile事件")]
    public UltEvent<TileData> OnTileEnterEvent = new UltEvent<TileData>();
    public UltEvent<TileData> OnTileExitEvent = new UltEvent<TileData>();

    [Tooltip("当前踩着的TileData缓存，供其他模块引用")]
    public TileData currentTileData;
    #endregion

    #region 静态缓存相关

    // 预留：如需对 Tile_Block 做本地缓存，可以在此添加字典
    // 目前直接通过 GameRes.GetTileBlock 按需获取，避免与旧的 Prefab/IBlockTile 缓存混用
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
        lastGridPos = GetCurrentGridPos();
        // 初始化当前TileData缓存
        currentTileData = Cache_map?.GetTile(lastGridPos);
        enabled = true;

        OnTileEnter(lastGridPos);
    }

    private void OnValidate()
    {
        // 设置模块ID
        _Data.ID = ModText.TileEffectReceiver;
    }

    public override void ModUpdate(float deltaTime)
    {
        UpdateMapReference();

        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos)
        {
            // 先退出旧的Tile
            OnTileExit(lastGridPos);

            // 获取新位置所在的Chunk
            Chunk chunk;
            ChunkMgr.Instance.GetChunkBy_ItemPosition(currentGridPos, out chunk);

            if (chunk == null)
            {                // 踏上空白地图，不触发事件
                return;
            }

            Cache_map = chunk.Map;
            lastGridPos = currentGridPos;

            // 进入新的Tile
            OnTileEnter(currentGridPos);
        }

        // 每帧更新当前Tile状态
        OnTileUpdate(currentGridPos);
    }
    #endregion

    #region 模块接口实现
    public override void Load()
    {
        ModSaveData.ReadData(ref lastGridPos);
        InitializeMap();
    }

    public override void Save()
    {
        // 确保在销毁时退出当前Tile
        OnTileExit(lastGridPos);
        ModSaveData.WriteData(lastGridPos);
    }


    public override void Act()
    {
        base.Act();
    }
    #endregion

    #region 初始化方法
    /// <summary>
    /// 初始化地图引用
    /// </summary>
    private void InitializeMap()
    {
        if (Cache_map != null) return;

        // 从当前位置获取Chunk和Map引用
        Chunk chunk;
        ChunkMgr.Instance.GetChunkBy_ItemPosition(transform.position, out chunk);

        if (chunk == null)
        {
            Debug.LogWarning($"TileEffectReceiver: 未找到有效的 Chunk 组件！{(item != null ? $"对象: {item.itemData.IDName}" : "")}");
            return;
        }

        Cache_map = chunk.Map;
        // 如果仍未找到Map，尝试在场景中查找
        if (Cache_map == null)
        {
            Cache_map = FindFirstObjectByType<Map>();
        }

        if (Cache_map == null)
        {
            Debug.LogError("TileEffectReceiver: 未找到有效的 Map 组件！");
            enabled = false;
        }
    }

    /// <summary>
    /// 更新地图引用
    /// 当当前Map为空或未激活时重新获取Map引用
    /// </summary>
    private void UpdateMapReference()
    {
        if (ChunkMgr.Instance == null) return;

        if (Cache_map == null || !Cache_map.gameObject.activeInHierarchy)
        {
            Chunk chunk;
            ChunkMgr.Instance.GetChunkBy_ItemPosition(transform.position, out chunk);
            Cache_map = chunk?.Map;
        }
    }
    #endregion

    #region Tile事件处理
    /// <summary>
    /// 处理进入Tile的逻辑
    /// </summary>
    private void OnTileEnter(Vector2Int gridPos)
    {
        if (item == null) return;

        if (TryGetTileBlock(gridPos, out TileData tileData, out Tile_Block tileBlock))
        {
            // 更新当前TileData缓存
            currentTileData = tileData;
            tileBlock.OnEnter(item, tileData, Cache_map, this);
            OnTileEnterEvent.Invoke(tileData);
        }
    }

    /// <summary>
    /// 处理离开Tile的逻辑
    /// </summary>
    private void OnTileExit(Vector2Int gridPos)
    {
        if (item == null) return;

        if (TryGetTileBlock(gridPos, out TileData tileData, out Tile_Block tileBlock))
        {
            tileBlock.OnExit(item, tileData, Cache_map, this);
            OnTileExitEvent.Invoke(tileData);
        }
    }

    /// <summary>
    /// 处理Tile更新的逻辑
    /// </summary>
    private void OnTileUpdate(Vector2Int gridPos)
    {
        if (Cache_map == null || item == null) return;

        if (TryGetTileBlock(gridPos, out TileData tileData, out Tile_Block tileBlock))
        {
            // 更新当前TileData缓存
            currentTileData = tileData;
            tileBlock.OnUpdate(item, tileData, Cache_map, this, Time.deltaTime);
        }
    }
    #endregion

    #region 缓存管理方法
    // 旧的 Prefab/IBlockTile 缓存逻辑已移除，如需缓存 Tile_Block 可在此根据需要重新实现
    #endregion

    #region 辅助方法
    /// <summary>
    /// 获取当前网格坐标
    /// </summary>
    private Vector2Int GetCurrentGridPos()
    {
        // 若地图为空，则返回上一次的坐标
        if (Cache_map == null) return lastGridPos;

        Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
        return new Vector2Int(cell.x, cell.y);
    }

    /// <summary>
    /// 尝试获取指定位置的 Tile_Block SO 和 TileData
    /// </summary>
    private bool TryGetTileBlock(Vector2Int pos, out TileData tileData, out Tile_Block tileBlock)
    {
        tileData = null;
        tileBlock = null;

        // 获取 TileData
        tileData = Cache_map?.GetTile(pos);
        if (tileData == null)
            return false;

        // 通过 TileData.Name 作为 key 获取对应的 Tile_Block SO
        // 注意：要求 Tile_Block.tileItemName 与 TileData.Name 对应，例如 "TileItem_Water" 等
        if (GameRes.Instance == null)
        {
            // 退出 Play Mode 时 GameRes 会先销毁，此时 TileEffectReceiver 的 OnExit 只需静默结束。
            if (Application.isPlaying)
                Debug.LogError("TileEffectReceiver: GameRes.Instance 为空，无法获取 Tile_Block");
            return false;
        }

        tileBlock = GameRes.Instance.GetTileBlock(tileData.Name);
        if (tileBlock == null)
        {
            Debug.LogWarning($"TileEffectReceiver: 找不到对应的 Tile_Block SO，Key = {tileData.Name};");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 获取当前Tile数据
    /// </summary>
    public TileData GetCurrentTileData()
    {
        var pos = GetCurrentGridPos();
        return Cache_map?.GetTile(pos);
    }
    #endregion
}

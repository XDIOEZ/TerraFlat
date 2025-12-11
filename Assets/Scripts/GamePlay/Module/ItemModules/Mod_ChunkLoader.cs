using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 区块加载器模块
/// 负责根据物体位置加载、卸载和管理区块
/// </summary>
public class Mod_ChunkLoader : Module
{
    #region 序列化字段与数据
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    // 三个距离配置 (Inspector 可调)
    [Header("区块加载距离设置")]
    [Tooltip("区块失活距离")]
    public int UnActiveDistance = 2;
    [Tooltip("区块销毁距离")]
    public int DestroyChunkDistance = 3;
    [Tooltip("区块加载距离")]
    public int LoadChunkDistance = 1;

    [ShowInInspector]
    (int UnActiveDistance, int DestroyChunkDistance, int LoadChunkDistance) Data
        = (UnActiveDistance: 2, DestroyChunkDistance: 3, LoadChunkDistance: 1);
    #endregion

    #region 运行时字段
    [Header("区块加载器运行时字段")]
    public Vector2 lastChunkPos;
    #endregion

    #region 生命周期

    private void OnValidate()
    {
        _Data.ID = ModText.ChunkLoader;
    }

    public override void Load()
    {
        Data.UnActiveDistance = UnActiveDistance;
        Data.DestroyChunkDistance = DestroyChunkDistance;
        Data.LoadChunkDistance = LoadChunkDistance;

        ModData.ReadData(ref Data);

        // 把存档的数据写回字段，保证 Inspector 一致
        UnActiveDistance = Data.UnActiveDistance;
        DestroyChunkDistance = Data.DestroyChunkDistance;
        LoadChunkDistance = Data.LoadChunkDistance;

        // 初始化区块加载
        lastChunkPos = Chunk.GetChunkPosition(transform.position);
    }

    public override void Save()
    {
        // 确保保存的是最新的值
        Data.UnActiveDistance = UnActiveDistance;
        Data.DestroyChunkDistance = DestroyChunkDistance;
        Data.LoadChunkDistance = LoadChunkDistance;

        ModData.WriteData(Data);
    }

    public override void ModUpdate(float deltaTime)
    {
        // 检测位置是否跨区块
        CheckPositionChange();
    }
    
    #endregion

    #region 初始化方法

    /// <summary>
    /// 初始化区块加载逻辑
    /// </summary>
    private void InitializeChunkLoading()
    {
        //销毁过远的失活的区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(item.gameObject, Distance: Data.DestroyChunkDistance);
        //将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(item.gameObject, Distance: Data.UnActiveDistance);
        //获取所在区块坐标
        Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);
        //同步绘制寻路网格
        AstarGameManager.Instance.UpdateMeshSync(currentChunkPos, Data.LoadChunkDistance);
        
        if (SaveDataMgr.Instance.SaveData.CurrentPlanetData.AutoGenerateMap == false)
        {
            ChunkMgr.Instance.LoadChunkCloseToPlayer(item.gameObject, Distance: 1);
        }
        else
        {
            ChunkMgr.Instance.LoadChunkCloseToPlayer(item.gameObject, Distance: Data.LoadChunkDistance);
        }
    }
    #endregion

    #region 位置检测方法
    /// <summary>
    /// 检测位置是否发生变化
    /// </summary>
    private void CheckPositionChange()
    {
        // 获取当前区块位置
        Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);
        
        // 检查是否跨区块
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            UpdateChunks(currentChunkPos);
        }
    }
    #endregion

    #region 区块更新逻辑
    /// <summary>
    /// 封装区块更新逻辑
    /// </summary>
    private void UpdateChunks(Vector2 chunkPos)
    {
        // 检查组件是否有效
        if (this == null || gameObject == null) return;
        
        //销毁过远的失活的区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: Data.DestroyChunkDistance);
        //将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: Data.UnActiveDistance);
        //异步绘制寻路网格
        AstarGameManager.Instance.UpdateMeshAsync(chunkPos, Data.LoadChunkDistance, () =>
        {
            // 在回调中再次检查组件和游戏对象是否仍然存在
            if (this == null || gameObject == null) return;
            
            // 检查是否仍在运行
            if (_Data != null && _Data.isRunning == false) return;
        
            // 完整的null检查
            bool autoGenerateMap = true; // 默认值
            if (SaveDataMgr.Instance != null && SaveDataMgr.Instance.SaveData != null)
            {
                PlanetData currentPlanetData = SaveDataMgr.Instance.SaveData.CurrentPlanetData;
                if (currentPlanetData != null)
                {
                    autoGenerateMap = currentPlanetData.AutoGenerateMap;
                }
            }
        
            if (autoGenerateMap == false)
            {
                ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: 1);
            }
            else
            {
                //异步加载较近的区块 同时 赋值权重
                ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: Data.LoadChunkDistance);
            }
        });
    }
    #endregion
}
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ChunkLoader : Module
{
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    // 三个距离配置 (Inspector 可调)
    public int UnActiveDistance = 2;
    public int DestroyChunkDistance = 3;
    public int LoadChunkDistance = 1;

    [ShowInInspector]
    (int UnActiveDistance, int DestroyChunkDistance, int LoadChunkDistance) Data
        = (UnActiveDistance: 2, DestroyChunkDistance: 3, LoadChunkDistance: 1);

    private Vector2 lastChunkPos;
    private TileEffectReceiver tileEffectReceiver;

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

        // 初始化 lastChunkPos
        lastChunkPos = Chunk.GetChunkPosition(transform.position);

        // 获取 TileEffectReceiver 组件
        tileEffectReceiver = item.itemMods.GetMod_ByID<TileEffectReceiver>(ModText.TileEffect);
        if (tileEffectReceiver != null)
        {
            // 订阅 Tile 离开事件（更可靠，因为离开的Tile肯定存在）
            tileEffectReceiver.OnTileExitEvent +=(OnTileExited);
        }
        else
        {
            Debug.LogWarning("未找到 TileEffectReceiver 组件，将使用定时检测方式");
        }

            //销毁过远的失活的区块
            ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: Data.DestroyChunkDistance);
            //将较远的区块设置为非激活状态
            ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: Data.UnActiveDistance);
            //同步绘制寻路网格
            AstarGameManager.Instance.UpdateMeshSync(transform.position, Data.LoadChunkDistance);
        if (SaveDataMgr.Instance.SaveData.CurrentPlanetData.AutoGenerateMap == false)
        {
            ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: 1);
        }
        else
        {
            ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: Data.LoadChunkDistance);
        }
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

    }

    /// <summary>
    /// Tile离开事件回调
    /// </summary>
    private void OnTileExited(TileData tileData)
    {
        // 检查是否跨区块
        Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            UpdateChunks(currentChunkPos);
        }
    }

/// <summary>
/// 封装区块更新逻辑，避免重复代码
/// </summary>
private void UpdateChunks(Vector2 chunkPos)
{
        //销毁过远的失活的区块
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: Data.DestroyChunkDistance);
        //将较远的区块设置为非激活状态
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: Data.UnActiveDistance);
        //异步绘制寻路网格
        AstarGameManager.Instance.UpdateMeshAsync(chunkPos, Data.LoadChunkDistance, () =>
        {
            // 在回调中再次检查组件和游戏对象是否仍然存在
            if (this == null || gameObject == null)
            {
                Debug.Log("Shit:Mod_ChunkLoader 或 GameObject 已被销毁，取消后续操作");
                return;
            }
            
            // 检查是否仍在运行
            if (_Data != null && _Data.isRunning == false)
            {
                return;
            }

            if (SaveDataMgr.Instance.SaveData.CurrentPlanetData.AutoGenerateMap == false)
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

    /// <summary>
    /// 清理事件监听
    /// </summary>
    public void OnDestroy()
    {
        if (tileEffectReceiver != null)
        {
            tileEffectReceiver.OnTileExitEvent -=(OnTileExited);
        }
    }
}
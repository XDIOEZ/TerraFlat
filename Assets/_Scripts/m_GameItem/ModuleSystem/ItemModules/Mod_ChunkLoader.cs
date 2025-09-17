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
    private float timer = 0f;
    private float updateInterval = 0.5f; // 每 0.5 秒更新一次

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

        if (ItemMgr.Instance.User_Player == null)
            return;

        // 初次加载时直接更新区块
        UpdateChunks(lastChunkPos);
    }

    public override void Save()
    {
        // 确保保存的是最新的值
        Data.UnActiveDistance = UnActiveDistance;
        Data.DestroyChunkDistance = DestroyChunkDistance;
        Data.LoadChunkDistance = LoadChunkDistance;

        ModData.WriteData(Data);
    }

    public override void Action(float deltaTime)
    {
        if (_Data.isRunning == false)
            return;

        timer += deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;

            // ✅ 检测是否跨区块
            Vector2 currentChunkPos = Chunk.GetChunkPosition(transform.position);
            if (currentChunkPos != lastChunkPos)
            {
                lastChunkPos = currentChunkPos;
                UpdateChunks(lastChunkPos);
            }
        }
    }

    /// <summary>
    /// 封装区块更新逻辑，避免重复代码
    /// </summary>
    private void UpdateChunks(Vector2 chunkPos)
    {
        ChunkMgr.Instance.DestroyChunk_In_Distance(gameObject, Distance: Data.DestroyChunkDistance);
        ChunkMgr.Instance.LoadChunkCloseToPlayer(gameObject, Distance: Data.LoadChunkDistance);
        ChunkMgr.Instance.SwitchActiveChunks_TO_UnActive(gameObject, Distance: Data.UnActiveDistance);

        AstarGameManager.Instance.UpdateMeshAsync(chunkPos, Data.LoadChunkDistance);
    }

    [Button("更新 Mesh")]
    public void UpdateMesh(Vector2 currentChunkPos)
    {
        AstarGameManager.Instance.UpdateMeshAsync(
            center: currentChunkPos,
            radius: Data.LoadChunkDistance
        );
    }
}

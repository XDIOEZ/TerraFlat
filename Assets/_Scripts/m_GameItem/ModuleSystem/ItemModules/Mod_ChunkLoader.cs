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
    (int UnActiveDistance, int DestroyChunkDistance, int LoadChunkDistance) Distance
        = (UnActiveDistance: 2, DestroyChunkDistance: 3, LoadChunkDistance: 1);

    private Vector2Int lastChunkPos; // ✅ 记录上一次的区块坐标
    private float timer = 0f;
    private float updateInterval = 0.5f; // 每 0.5 秒更新一次

    public override void Load()
    {
        Distance.UnActiveDistance = UnActiveDistance;
        Distance.DestroyChunkDistance = DestroyChunkDistance;
        Distance.LoadChunkDistance = LoadChunkDistance;

        ModData.ReadData(ref Distance);

        // 把存档的数据写回字段，保证 Inspector 一致
        UnActiveDistance = Distance.UnActiveDistance;
        DestroyChunkDistance = Distance.DestroyChunkDistance;
        LoadChunkDistance = Distance.LoadChunkDistance;

        // 初始化 lastChunkPos
        lastChunkPos = Chunk.GetChunkPosition(transform.position);


        // 跨区块后才更新
        GameChunkManager.Instance.DestroyChunk_In_Distance(item.gameObject, Distance: Distance.DestroyChunkDistance);
        GameChunkManager.Instance.LoadChunkCloseToPlayer(item.gameObject, Distance: Distance.LoadChunkDistance);
        GameChunkManager.Instance.SwitchActiveChunks_TO_UnActive(item.gameObject, Distance: Distance.UnActiveDistance);
    }

    public override void Save()
    {
        // 确保保存的是最新的值
        Distance.UnActiveDistance = UnActiveDistance;
        Distance.DestroyChunkDistance = DestroyChunkDistance;
        Distance.LoadChunkDistance = LoadChunkDistance;

        ModData.WriteData(Distance);
    }

    public override void Action(float deltaTime)
    {
        if (_Data.isRunning == false)
            return;

        timer += deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f; // 重置

            // ✅ 检测是否跨区块
            Vector2Int currentChunkPos = Chunk.GetChunkPosition(transform.position);
            if (currentChunkPos != lastChunkPos)
            {
                lastChunkPos = currentChunkPos;

                // 跨区块后才更新
                GameChunkManager.Instance.DestroyChunk_In_Distance(item.gameObject, Distance: Distance.DestroyChunkDistance);
                GameChunkManager.Instance.LoadChunkCloseToPlayer(item.gameObject, Distance: Distance.LoadChunkDistance);
                GameChunkManager.Instance.SwitchActiveChunks_TO_UnActive(item.gameObject, Distance: Distance.UnActiveDistance);
            }
        }
    }
}

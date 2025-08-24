using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ChunkLoader : Module
{
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public int UnActiveDistance = 2;
    public int DestroyChunkDistance = 3;

    [ShowInInspector]
    (int UnActiveDistance, int DestroyChunkDistance) Distance = (UnActiveDistance: 2, DestroyChunkDistance: 3);

    public override void Load()
    {
        Distance.UnActiveDistance = UnActiveDistance; 
        Distance.DestroyChunkDistance = DestroyChunkDistance;

        ModData.ReadData(ref Distance);
    }

    public override void Save()
    {
        ModData.WriteData(Distance);
    }
    private float timer = 0f;
    private float updateInterval = 0.5f; // 每 0.5 秒更新一次

    public override void Action(float deltaTime)
    {
        if(_Data.isRunning == false)
        {
            return;
        }
        timer += deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f; // 重置
            GameChunkManager.Instance.DestroyChunk_In_Distance(item.gameObject, Distance: Distance.DestroyChunkDistance);
            GameChunkManager.Instance.SwitchActiveChunks_TO_UnActive(item.gameObject, Distance: Distance.UnActiveDistance);
            GameChunkManager.Instance.LoadChunkCloseToPlayer(item.gameObject);

        }
    }

}

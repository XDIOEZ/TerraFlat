using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_ItemChunkAssigner : Module
{
    public Ex_ModData_MemoryPackable ModData;
    private Vector2Int lastChunkPos;

    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    public override void Load()
    {
        ModData.ReadData(ref lastChunkPos);
    }

    public override void Save()
    {
        ModData.WriteData(lastChunkPos);
    }
    public override void Action(float deltaTime)
    {
        if (_Data.isRunning == false)
            return;

        Vector2Int currentChunkPos = Chunk.GetChunkPosition(transform.position);
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            GameChunkManager.Instance.UpdateItem_ChunkOwner(item);
        }
    }
}

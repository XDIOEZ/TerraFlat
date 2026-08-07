using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Mod_ItemChunkAssigner : Module
{
    public Ex_ModData_MemoryPackable ModData;
    [ShowInInspector]
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
    
    public override void ModUpdate(float deltaTime)
    {
        if (_Data.isRunning == false)
            return;

        Vector2Int currentChunkPos = ChunkMgr.NormalizeChunkPosition(
            Chunk.GetChunkPosition(transform.position));
        if (currentChunkPos != lastChunkPos)
        {
            // ChunkMgr performs the old-owner removal and new-owner insertion atomically.
            lastChunkPos = currentChunkPos;
            ChunkMgr.Instance.UpdateItem_ChunkOwner(item);
        }
    }
}

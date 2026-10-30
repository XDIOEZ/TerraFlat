using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>兼容旧 Prefab 模块 ID；运行时只刷新新版 WorldAddress 实体索引。</summary>
public class Mod_ItemChunkAssigner : Module
{
    public Ex_ModData_MemoryPackable ModData;
    [ShowInInspector]
    private Vector2Int lastChunkPos;

    public override ModuleData _Data { get { return ModData; } set { ModData = (Ex_ModData_MemoryPackable)value; } }

    protected override void OnLoad()
    {
        ModuleGridPositionSaveSnapshot saved = ModData.GetData<ModuleGridPositionSaveSnapshot>();
        if (saved != null) lastChunkPos = new Vector2Int(saved.X, saved.Y);
    }

    protected override void OnSave()
    {
        ModData.WriteData(new ModuleGridPositionSaveSnapshot { X = lastChunkPos.x, Y = lastChunkPos.y });
    }
    
    public override void ModUpdate(float deltaTime)
    {
        if (_Data.isRunning == false || !ChunkMgr.IsSurfaceStreamingScene)
            return;

        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
            return;

        Vector2Int currentChunkPos = chunkManager.ResolveRuntimeChunkOrigin(transform.position);
        if (currentChunkPos != lastChunkPos)
        {
            lastChunkPos = currentChunkPos;
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        }
    }
}

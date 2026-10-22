using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 隐藏线路表现
    private bool engineeringWireVision;

    private void RefreshEngineeringWireVisibility()
    {
        bool visible = EngineeringVision.LocalCanSee;
        if (engineeringWireVision == visible || !batchPresentationComplete) return;
        engineeringWireVision = visible;
        var origin = boundChunk.Address.ChunkOrigin;
        MachineWorld.CollectInBounds(new BoundsInt(origin.X, origin.Y, 0,
            boundChunk.Terrain.Width, boundChunk.Terrain.Height, 1), mechanicalVisualNodes);
        // 资格变化时只重提隐藏线路，普通设备与电网保持原有状态。
        foreach (MachineWorld.MachineRenderCell entry in mechanicalVisualNodes)
            if (entry.Node.Definition.Electrical?.Hidden == true)
                SubmitMechanicalNode(entry.Node, entry.DisplayCell.x - origin.X, entry.DisplayCell.y - origin.Y);
    }
    #endregion
}

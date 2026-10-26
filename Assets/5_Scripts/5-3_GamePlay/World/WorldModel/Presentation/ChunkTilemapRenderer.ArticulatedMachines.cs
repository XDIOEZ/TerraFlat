using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 关节机器共享行批绘
    private void SubmitArticulatedMachineParts(MachineEntity node, int x, int y, RuntimeItemDefinition item,
        Material material, Vector3 origin, Quaternion rotation, MachineArticulatedVisual rig)
    {
        MachineArticulatedMotionClock clock = MachineArticulatedVisual.SynchronizeClock(node, rig);
        float size = node.Definition.Fluid.ResolveBodySizeCells(node.Definition.Layer);
        float fit = Fit(item.Sprite, size, size).x;
        Vector3Int key = new(x, y, node.Definition.Layer);
        MechanicalDepthVisual visual = mechanicalDepthVisuals[key];
        for (int i = 0; i < rig.Parts.Length; i++)
        {
            // 保留六、七号部件给机械轴端口，零件顺序由装配声明单独决定。
            int partId = i < 6 ? i : i + 2;
            MachineArticulatedVisual.PartPose pose = rig.GetPartPose(item, i, fit, clock);
            Quaternion turn = rotation * pose.Rotation;
            Vector3 offset = Quaternion.Inverse(pose.Rotation) * pose.Offset;
            MechanicalShadowRegistry.SetPart(this, key, partId, pose.Sprite, origin, turn, offset,
                pose.Scale, pose.Mode, pose.Animation, pose.Parameters, registerStatic: true);
            visual.SetPart(partId, pose.Sprite, material, turn, offset, pose.Scale, pose.Mode,
                pose.Animation, conveyor: pose.Parameters, partOrder: pose.Order);
        }
    }
    #endregion
}

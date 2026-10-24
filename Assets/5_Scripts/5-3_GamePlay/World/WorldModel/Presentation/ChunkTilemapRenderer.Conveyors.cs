using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 输送带图层
    private void SubmitConveyor(MachineEntity node, int x, int y, RuntimeItemDefinition def,
        Material material, Vector3 origin)
    {
        ConveyorPath route = node.ConveyorRoute;
        MachineTransportVisualDefinition visual = node.Definition.Transport.Visual;
        Sprite port = ConveyorPresentation.AxisPort();
        Sprite shaft = null;
        // 直带显示供能侧轴，拐角隐藏侧轴图片。
        for (int direction = 0; direction < 4; direction++)
        {
            if (!ConveyorPresentation.HasVisibleSidePort(route, direction)) continue;
            bool connected = (node.ConveyorConnectedDriveMask & (1 << direction)) != 0;
            Quaternion rotation = Quaternion.Euler(0f, 0f, direction * 90f);
            if (connected)
            {
                shaft ??= ConveyorPresentation.SideShaft();
                ConveyorPresentation.SideShaftGeometry(shaft, out Vector3 offset, out Vector3 shaftScale);
                Part(node, x, y, direction * 2, shaft, material, origin, rotation,
                    offset, shaftScale, 2, 1f, drawBelowMechanical: true);
            }
            Part(node, x, y, direction * 2 + 1, port, material, origin, rotation,
                ConveyorPresentation.SidePortPosition(route, visual, port, connected),
                Vector3.one, 0, 0f, drawBelowMechanical: true);
        }
        Vector3 scale = ConveyorPresentation.Scale(def.Sprite, route, visual);
        // 传送带贴地绘制，与侧轴一起固定在玩家和机械主体下层。
        Part(node, x, y, 8, def.Sprite, material, origin, Quaternion.Euler(0f, 0f, route.Rotation * 90f),
            ConveyorPresentation.Offset(def.Sprite, scale), scale, route.Curved ? 7 : 6, 1f,
            track: 3, stroke: visual.CanvasHeight, drawBelowMechanical: true,
            conveyorSurface: ConveyorPresentation.Surface(def.Sprite, visual));
    }
    #endregion
}

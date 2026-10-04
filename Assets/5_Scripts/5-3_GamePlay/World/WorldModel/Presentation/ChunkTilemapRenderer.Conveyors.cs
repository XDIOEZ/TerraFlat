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
        int part = 0;
        // 两个未用于输送的格边是轴口，带端只连接下一段带。
        for (int direction = 0; direction < 4; direction++)
        {
            if (!route.HasDrivePort(direction)) continue;
            Part(node, x, y, part++, port, material, origin, Quaternion.Euler(0f, 0f, direction * 90f),
                ConveyorPresentation.SidePortPosition(route, visual), Vector3.one, 0, 0f, drawBelowMechanical: true);
        }
        Vector3 scale = ConveyorPresentation.Scale(def.Sprite, route, visual);
        // 传送带贴地绘制，与侧轴一起固定在玩家和机械主体下层。
        Part(node, x, y, part, def.Sprite, material, origin, Quaternion.Euler(0f, 0f, route.Rotation * 90f),
            ConveyorPresentation.Offset(def.Sprite, scale), scale, route.Curved ? 7 : 6, 1f,
            track: 3, stroke: visual.CanvasHeight, drawBelowMechanical: true,
            conveyorSurface: ConveyorPresentation.Surface(def.Sprite, visual));
    }
    #endregion
}

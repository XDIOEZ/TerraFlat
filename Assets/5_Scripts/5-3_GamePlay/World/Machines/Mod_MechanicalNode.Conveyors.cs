using UnityEngine;

public sealed partial class Mod_MechanicalNode
{
    #region 输送带放置预览
    private MaterialPropertyBlock conveyorPreviewBlock;
    private static readonly int ConveyorAnimationId = Shader.PropertyToID("_ConveyorAnimation");
    private static readonly int ConveyorSurfaceId = Shader.PropertyToID("_ConveyorSurface");
    private static readonly int ConveyorRegionId = Shader.PropertyToID("_ConveyorRegion");
    private static readonly string[] ConveyorPortNames = { "ConveyorPortE", "ConveyorPortN", "ConveyorPortW", "ConveyorPortS" };

    private void ApplyConveyorPreview(BuildingShadow shadow)
    {
        if (Definition?.Transport == null) return;
        SpriteRenderer body = shadow.ShadowRenderer;
        ConveyorPath route = MachineWorld.GetConveyorPreviewPath(MachineWorld.CellOf(shadow.transform.position),
            PlacementQuarterTurns, Definition.Transport.AutoConnect);
        MachineTransportVisualDefinition visual = Definition.Transport.Visual;
        Quaternion rotation = Quaternion.Euler(0f, 0f, route.Rotation * 90f);
        body.transform.localRotation = rotation;
        body.transform.localScale = ConveyorPresentation.Scale(body.sprite, route, visual);
        body.transform.localPosition = rotation * ConveyorPresentation.Offset(body.sprite, body.transform.localScale);
        body.flipX = false; body.flipY = false;
        // 首次更新预览时再创建属性块，避免组件构造期间调用 Unity 原生接口。
        conveyorPreviewBlock ??= new MaterialPropertyBlock();
        body.GetPropertyBlock(conveyorPreviewBlock);
        conveyorPreviewBlock.SetVector(ConveyorAnimationId, new Vector4(route.Curved ? 7 : 6, 0f, 0f, visual.CanvasHeight));
        conveyorPreviewBlock.SetVector(ConveyorSurfaceId, ConveyorPresentation.Surface(body.sprite, visual));
        conveyorPreviewBlock.SetVector(ConveyorRegionId, ConveyorPresentation.Region(body.sprite));
        body.SetPropertyBlock(conveyorPreviewBlock);
        Sprite port = ConveyorPresentation.AxisPort();
        for (int direction = 0; direction < 4; direction++)
        {
            SpriteRenderer overlay = shadow.EnsureOverlay(ConveyorPortNames[direction], port,
                (Vector3)ConveyorPath.Rotate(ConveyorPresentation.SidePortPosition(route, visual), direction),
                body.sharedMaterial);
            if (overlay == null) continue;
            overlay.gameObject.SetActive(ConveyorPresentation.HasVisibleSidePort(route, direction));
            overlay.transform.localRotation = Quaternion.Euler(0f, 0f, direction * 90f);
            overlay.transform.localScale = Vector3.one;
            overlay.sortingOrder = body.sortingOrder - 1;
        }
    }
    #endregion
}

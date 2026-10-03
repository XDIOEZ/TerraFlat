using System;
using UnityEngine;

/// <summary>带面和边框共用原图，带面 UV 范围可以由机械目录覆盖。</summary>
public sealed class MachineTransportVisualDefinition
{
    #region 输送带图层配置
    public float SurfaceUvMin = .26f, SurfaceUvMax = .74f;
    public float TextureInsetX = .023f, CanvasHeight = .84f, SidePortOffset = .25f;
    public Vector4 Surface => new(SurfaceUvMin, SurfaceUvMax, TextureInsetX, CanvasHeight);
    public void Validate(string id)
    {
        if (!MachineDefinition.NonNegative(SurfaceUvMin) || !MachineDefinition.Positive(SurfaceUvMax) ||
            SurfaceUvMax > 1f || SurfaceUvMin >= SurfaceUvMax ||
            !MachineDefinition.NonNegative(TextureInsetX) || TextureInsetX >= .5f ||
            !MachineDefinition.Positive(CanvasHeight) || !MachineDefinition.NonNegative(SidePortOffset))
            throw new ArgumentException("输送带图层参数无效：" + id);
    }
    #endregion
}

public static class ConveyorPresentation
{
    #region 共用输送带表现
    public static Sprite AxisPort()
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition("Shaft_Wood", out RuntimeItemDefinition def) ||
            !def.TryGetVisualStateSprite("axisPort", out Sprite port))
            throw new InvalidOperationException("输送带侧轴接口贴图缺失。");
        return port;
    }
    public static Vector3 Scale(Sprite sprite, ConveyorPath route, MachineTransportVisualDefinition visual)
        => new(1f / sprite.bounds.size.x, (route.Curved ? 1f : visual.CanvasHeight) / sprite.bounds.size.y, 1f);
    public static Vector4 Region(Sprite sprite)
    {
        Rect rect = sprite.textureRect;
        return new Vector4(rect.xMin / sprite.texture.width, rect.yMin / sprite.texture.height,
            rect.xMax / sprite.texture.width, rect.yMax / sprite.texture.height);
    }
    #endregion
}

using System;
using UnityEngine;

/// <summary>带面和边框共用原图，带面 UV 范围可以由机械目录覆盖。</summary>
[MemoryPack.MemoryPackable]
public sealed partial class MachineTransportVisualDefinition
{
    #region 输送带图层配置
    public float SurfaceUvMin = .25f, SurfaceUvMax = .75f;
    public float TextureInsetX = .023f, CanvasHeight = .84f, SidePortOffset = .1f;
    public float CurvedSidePortOffset = -.07f;
    [MemoryPack.MemoryPackIgnore]
    public Vector4 Surface => new(SurfaceUvMin, SurfaceUvMax, TextureInsetX, CanvasHeight);
    public void Validate(string id)
    {
        if (!MachineDefinition.NonNegative(SurfaceUvMin) || !MachineDefinition.Positive(SurfaceUvMax) ||
            SurfaceUvMax > 1f || SurfaceUvMin >= SurfaceUvMax ||
            !MachineDefinition.NonNegative(TextureInsetX) || TextureInsetX >= .5f ||
            !MachineDefinition.Positive(CanvasHeight) || !MachineDefinition.NonNegative(SidePortOffset) ||
            !MachineDefinition.Finite(CurvedSidePortOffset))
            throw new ArgumentException("输送带图层参数无效：" + id);
    }
    #endregion
}

public static class ConveyorPresentation
{
    #region 共用输送带表现
    public static Sprite AxisPort()
        => ShaftSprite("axisPort");
    public static Sprite SideShaft()
        => ShaftSprite("shaftRollingCore");
    private static Sprite ShaftSprite(string state)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition("Shaft_Wood", out RuntimeItemDefinition def) ||
            !def.TryGetVisualStateSprite(state, out Sprite port) || port == null)
            throw new InvalidOperationException("输送带侧轴贴图缺失：" + state);
        return port;
    }
    // 拐角只隐藏侧轴图片，供能端口仍由权威路径管理。
    public static bool HasVisibleSidePort(ConveyorPath route, int direction)
        => !route.Curved && route.HasDrivePort(direction);
    // 铁环图片自身偏向外端，让内侧压在带框下面。
    public static Vector3 SidePortPosition(ConveyorPath route, MachineTransportVisualDefinition visual)
        => Vector3.right * (route.Curved ? visual.CurvedSidePortOffset : visual.SidePortOffset);
    // 连通时只移动铁环，不拉伸像素；外缘按真实切片与 Pivot 对齐格边。
    public static Vector3 SidePortPosition(ConveyorPath route, MachineTransportVisualDefinition visual,
        Sprite port, bool connected)
        => connected ? Vector3.right * (.5f - (port.rect.width - port.pivot.x) / port.pixelsPerUnit)
            : SidePortPosition(route, visual);
    // 半格木芯藏入带框，外端与邻格传动轴接合，厚度沿用标准木轴。
    public static void SideShaftGeometry(Sprite shaft, out Vector3 offset, out Vector3 scale)
    {
        scale = new Vector3(.5f * shaft.pixelsPerUnit / shaft.rect.width,
            (28f / 128f) * shaft.pixelsPerUnit / shaft.rect.height, 1f);
        offset = Vector3.right * .25f + Offset(shaft, scale);
    }
    public static Vector3 Scale(Sprite sprite, ConveyorPath route, MachineTransportVisualDefinition visual)
        => new(sprite.pixelsPerUnit / sprite.rect.width,
            (route.Curved ? 1f : visual.CanvasHeight) * sprite.pixelsPerUnit / sprite.rect.height, 1f);
    // 完整画布对齐格心，不让透明裁边和非中心 Pivot 改变圆弧端点。
    public static Vector3 Offset(Sprite sprite, Vector3 scale)
        => -Vector3.Scale((Vector3)(sprite.rect.size * .5f - sprite.pivot) / sprite.pixelsPerUnit, scale);
    public static Vector4 Surface(Sprite sprite, MachineTransportVisualDefinition visual)
    {
        Vector4 surface = visual.Surface;
        // 动态带面向外对齐完整像素行，带条两端不会残留静止细边。
        surface.x = Mathf.Floor(surface.x * sprite.rect.height) / sprite.rect.height;
        surface.y = Mathf.Ceil(surface.y * sprite.rect.height) / sprite.rect.height;
        surface.z = Mathf.Min(.499f, (Mathf.Ceil(visual.TextureInsetX * sprite.rect.width) + .5f) / sprite.rect.width);
        return surface;
    }
    public static Vector4 Region(Sprite sprite)
    {
        Rect rect = sprite.textureRect;
        return new Vector4(rect.xMin / sprite.texture.width, rect.yMin / sprite.texture.height,
            rect.xMax / sprite.texture.width, rect.yMax / sprite.texture.height);
    }
    #endregion
}

using System;
using UnityEngine;

public sealed class BatteryChargeIndicatorDefinition
{
    #region 三格指示灯配置
    public Vector4[] Cells = Array.Empty<Vector4>(); // 原图左上为原点的像素矩形，依次为左、中、右。
    public Color Color = new(.12f, .85f, .28f, 1f);
    #endregion
}

public static class BatteryChargePresentation
{
    #region 电量档位与共享色块
    public const int CellCount = 3;
    private static Sprite indicatorSprite;
    static BatteryChargePresentation() => SharedSpriteMeshCache.Clearing += Reset;

    public static int GetLitCount(MachineEntity node)
    {
        ElectricalDefinition electrical = node?.Definition.Electrical;
        if (electrical?.IsBattery != true || electrical.CapacityJoules <= 0f) return 0;
        double chargePercent = Mathf.Clamp(node.ElectricalStoredJoules, 0f, electrical.CapacityJoules) *
            100d / electrical.CapacityJoules;
        return Math.Min(CellCount, (int)(chargePercent / 33d));
    }

    // 色块按原 Sprite 的 Pivot 和 PPU 对齐，地面和船上共用同一套坐标与门槛。
    public static bool TryGetCell(MachineEntity node, Sprite body, int index,
        out Sprite sprite, out Vector3 offset, out Vector3 scale)
    {
        sprite = null; offset = Vector3.zero; scale = Vector3.one;
        Vector4[] cells = node?.Definition.BatteryIndicator?.Cells;
        if (body == null || cells == null || (uint)index >= CellCount || index >= cells.Length) return false;
        Vector4 rect = cells[index];
        if (rect.z <= 0f || rect.w <= 0f) return false;
        if (indicatorSprite == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            indicatorSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), texture.width, 0, SpriteMeshType.FullRect);
            indicatorSprite.hideFlags = HideFlags.DontSave;
        }
        sprite = indicatorSprite;
        offset = new Vector3((rect.x + rect.z * .5f - body.pivot.x) / body.pixelsPerUnit,
            (body.rect.height - rect.y - rect.w * .5f - body.pivot.y) / body.pixelsPerUnit, 0f);
        scale = new Vector3(rect.z / body.pixelsPerUnit, rect.w / body.pixelsPerUnit, 1f);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        if (indicatorSprite != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(indicatorSprite);
            else UnityEngine.Object.DestroyImmediate(indicatorSprite);
        }
        indicatorSprite = null;
    }
    #endregion
}

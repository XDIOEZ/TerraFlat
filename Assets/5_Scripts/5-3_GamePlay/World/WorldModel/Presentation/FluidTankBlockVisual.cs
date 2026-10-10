using System.Collections.Generic;
using UnityEngine;

/// <summary>气罐方块共用九宫格切片，按权威连接去掉内部边框。</summary>
public static class FluidTankBlockVisual
{
    #region 单格罐体切片
    private const float BorderFraction = .18f;
    private static readonly Dictionary<Sprite, Sprite[]> slices = new();
    static FluidTankBlockVisual() => SharedSpriteMeshCache.Clearing += Reset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Sprite[] sprites in slices.Values)
            foreach (Sprite sprite in sprites)
                if (sprite != null)
                {
                    if (Application.isPlaying) Object.Destroy(sprite);
                    else Object.DestroyImmediate(sprite);
                }
        slices.Clear();
    }

    public static void GetPart(Sprite source, int index, int connectionMask,
        out Sprite sprite, out Vector3 offset, out Vector3 scale)
    {
        int x = index % 3, y = index / 3;
        int sourceX = x, sourceY = y;
        // 接通的一侧取中央平板纹理，外侧仍保留原图的完整边框。
        if (x == 0 && (connectionMask & 8) != 0 || x == 2 && (connectionMask & 2) != 0) sourceX = 1;
        if (y == 0 && (connectionMask & 4) != 0 || y == 2 && (connectionMask & 1) != 0) sourceY = 1;
        sprite = GetSlices(source)[sourceX + sourceY * 3];
        float width = x == 1 ? 1f - BorderFraction * 2f : BorderFraction;
        float height = y == 1 ? 1f - BorderFraction * 2f : BorderFraction;
        offset = new Vector3((x - 1) * (.5f - BorderFraction * .5f),
            (y - 1) * (.5f - BorderFraction * .5f));
        scale = new Vector3(width / sprite.bounds.size.x, height / sprite.bounds.size.y, 1f);
    }

    private static Sprite[] GetSlices(Sprite source)
    {
        if (slices.TryGetValue(source, out Sprite[] result)) return result;
        result = new Sprite[9];
        Rect rect = source.rect;
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            float startX = x == 0 ? 0f : x == 1 ? BorderFraction : 1f - BorderFraction;
            float startY = y == 0 ? 0f : y == 1 ? BorderFraction : 1f - BorderFraction;
            Rect slice = new(rect.x + rect.width * startX, rect.y + rect.height * startY,
                rect.width * (x == 1 ? 1f - BorderFraction * 2f : BorderFraction),
                rect.height * (y == 1 ? 1f - BorderFraction * 2f : BorderFraction));
            result[x + y * 3] = Sprite.Create(source.texture, slice, new Vector2(.5f, .5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            result[x + y * 3].name = source.name + "_TankPart_" + (x + y * 3);
            result[x + y * 3].hideFlags = HideFlags.DontSave;
        }
        slices.Add(source, result);
        return result;
    }
    #endregion
}

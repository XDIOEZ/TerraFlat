using System.Collections.Generic;
using UnityEngine;

/// <summary>所有管道方向共用同一根管臂，连接格边不再受各张拼图的像素偏差影响。</summary>
public static class FluidPipeConnectionVisual
{
    #region 标准管臂切片与几何
    private static readonly Dictionary<Sprite, Sprite> arms = new();
    static FluidPipeConnectionVisual() => SharedSpriteMeshCache.Clearing += Clear;
    private static void Clear()
    {
        foreach (Sprite sprite in arms.Values)
            if (sprite != null)
            {
                if (Application.isPlaying) Object.Destroy(sprite);
                else Object.DestroyImmediate(sprite);
            }
        arms.Clear();
    }
    public static Sprite GetArm(Sprite north)
    {
        if (arms.TryGetValue(north, out Sprite arm) && arm != null) return arm;
        Rect bounds = north.rect;
        float width = Mathf.Round(bounds.width * .27f);
        float height = Mathf.Round(bounds.height * .29f);
        Rect cut = new(Mathf.Round(bounds.center.x - width * .5f), Mathf.Round(bounds.yMax - height), width, height);
        arm = Sprite.Create(north.texture, cut, new Vector2(.5f, .5f), north.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        arm.name = north.name + "_StandardArm"; arm.hideFlags = HideFlags.DontSave;
        arms[north] = arm;
        return arm;
    }
    public static void GetArmPose(Sprite arm, int direction, out Quaternion rotation, out Vector3 offset, out Vector3 scale)
    {
        rotation = Quaternion.Euler(0f, 0f, -direction * 90f);
        offset = rotation * new Vector3(0f, .25f);
        scale = new Vector3(1f, .5f / arm.bounds.size.y, 1f);
    }
    public static void Apply(SpriteRenderer source, Sprite north, ref SpriteRenderer[] parts, int mask)
    {
        Sprite arm = GetArm(north);
        parts ??= new SpriteRenderer[4];
        for (int direction = 0; direction < 4; direction++)
        {
            SpriteRenderer part = parts[direction];
            if (part == null)
            {
                GameObject child = new("Pipe Arm " + direction);
                child.transform.SetParent(source.transform, false);
                part = parts[direction] = child.AddComponent<SpriteRenderer>();
            }
            part.enabled = (mask & (1 << direction)) != 0;
            if (!part.enabled) continue;
            part.sprite = arm; part.sharedMaterial = source.sharedMaterial; part.color = source.color;
            part.sortingLayerID = source.sortingLayerID; part.sortingOrder = source.sortingOrder - 1;
            GetArmPose(arm, direction, out Quaternion rotation, out Vector3 offset, out Vector3 scale);
            part.transform.localPosition = offset; part.transform.localRotation = rotation; part.transform.localScale = scale;
        }
    }
    #endregion
}

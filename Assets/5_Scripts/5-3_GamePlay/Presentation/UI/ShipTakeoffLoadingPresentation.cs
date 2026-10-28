using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShipTakeoffLoadingPresentation : IDisposable
{
    #region 跨场景的船体外观快照
    private readonly List<SpritePose> sprites = new();
    private readonly List<RectTransform> streaks = new();
    private GameObject root;
    private RectTransform frame;
    private Vector2 originalFrameSize;
    private double elapsedSeconds;
    private float scrollSpeed;

    private struct SpritePose
    {
        public Sprite Sprite;
        public Color Color;
        public Vector2 Center, Size;
        public float Angle;
        public WorldVisualSortingKey SortingKey;
        public bool FlipX, FlipY;
    }

    internal static ShipTakeoffLoadingPresentation Capture(List<SpriteRenderer> renderers,
        List<WorldVisualSnapshot> snapshots, float speed)
    {
        var result = new ShipTakeoffLoadingPresentation { scrollSpeed = speed };
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null) continue;
            Vector3 center = renderer.sprite.bounds.center;
            if (renderer.flipX) center.x = -center.x;
            if (renderer.flipY) center.y = -center.y;
            Vector3 scale = renderer.transform.lossyScale;
            result.sprites.Add(new SpritePose
            {
                Sprite = renderer.sprite, Color = renderer.color,
                Center = renderer.transform.TransformPoint(center),
                Size = new Vector2(renderer.sprite.bounds.size.x * Mathf.Abs(scale.x), renderer.sprite.bounds.size.y * Mathf.Abs(scale.y)),
                Angle = renderer.transform.eulerAngles.z,
                SortingKey = WorldVisualSortingKey.FromRenderer(renderer),
                FlipX = renderer.flipX ^ scale.x < 0f, FlipY = renderer.flipY ^ scale.y < 0f
            });
        }
        foreach (WorldVisualSnapshot snapshot in snapshots)
        {
            if (snapshot.Sprite == null || snapshot.Color.a <= 0f) continue;
            Matrix4x4 matrix = snapshot.Matrix;
            Vector3 axisX = matrix.MultiplyVector(Vector3.right), axisY = matrix.MultiplyVector(Vector3.up);
            float determinant = matrix.m00 * matrix.m11 - matrix.m01 * matrix.m10;
            result.sprites.Add(new SpritePose
            {
                Sprite = snapshot.Sprite, Color = snapshot.Color,
                Center = matrix.MultiplyPoint3x4(snapshot.Sprite.bounds.center),
                Size = new Vector2(snapshot.Sprite.bounds.size.x * axisX.magnitude, snapshot.Sprite.bounds.size.y * axisY.magnitude),
                Angle = Mathf.Atan2(axisX.y, axisX.x) * Mathf.Rad2Deg,
                SortingKey = snapshot.SortingKey, FlipY = determinant < 0f
            });
        }
        result.sprites.Sort((left, right) => left.SortingKey.CompareTo(right.SortingKey));
        return result.sprites.Count > 0 ? result : null;
    }
    #endregion

    #region 只按速度和时间播放的加载过渡
    public void Show(Image icon)
    {
        icon.enabled = false;
        if (root != null) return;
        frame = icon.transform.parent as RectTransform;
        originalFrameSize = frame.sizeDelta;
        frame.sizeDelta = new Vector2(620f, originalFrameSize.y);
        root = new GameObject("持续起飞过渡", typeof(RectTransform), typeof(RectMask2D));
        RectTransform area = root.GetComponent<RectTransform>();
        area.SetParent(icon.transform, false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = area.offsetMax = Vector2.zero;
        for (int i = 0; i < 24; i++)
        {
            Image streak = CreateImage("气流", area, null, new Color(.72f, .8f, .88f, .16f));
            streak.rectTransform.sizeDelta = new Vector2(2f, 12f + i % 4 * 6f);
            streaks.Add(streak.rectTransform);
        }
        Bounds bounds = new(sprites[0].Center, Vector3.zero);
        foreach (SpritePose pose in sprites)
        {
            float radians = pose.Angle * Mathf.Deg2Rad;
            Vector2 extent = new(Mathf.Abs(Mathf.Cos(radians)) * pose.Size.x + Mathf.Abs(Mathf.Sin(radians)) * pose.Size.y,
                Mathf.Abs(Mathf.Sin(radians)) * pose.Size.x + Mathf.Abs(Mathf.Cos(radians)) * pose.Size.y);
            bounds.Encapsulate(new Bounds(pose.Center, extent));
        }
        float pixelsPerUnit = Mathf.Min(420f / Mathf.Max(.01f, bounds.size.x), 86f / Mathf.Max(.01f, bounds.size.y));
        foreach (SpritePose pose in sprites)
        {
            Image image = CreateImage("船体快照", area, pose.Sprite, pose.Color);
            image.rectTransform.anchoredPosition = (pose.Center - (Vector2)bounds.center) * pixelsPerUnit;
            image.rectTransform.sizeDelta = pose.Size * pixelsPerUnit;
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, pose.Angle);
            image.rectTransform.localScale = new Vector3(pose.FlipX ? -1f : 1f, pose.FlipY ? -1f : 1f, 1f);
        }
        Tick(0f);
    }

    public void Tick(float seconds)
    {
        if (root == null) return;
        elapsedSeconds += seconds;
        RectTransform area = root.GetComponent<RectTransform>();
        float width = area.rect.width, height = Mathf.Max(1f, area.rect.height);
        // 镜头跟着船，气流无限向下滚动，加载多久就播放多久。
        float travel = (float)(elapsedSeconds * scrollSpeed % (height + 40f));
        for (int i = 0; i < streaks.Count; i++)
            streaks[i].anchoredPosition = new Vector2(((i * .618034f) % 1f - .5f) * width,
                height * .5f + 20f - Mathf.Repeat(travel + i * 17.3f, height + 40f));
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        node.transform.SetParent(parent, false);
        Image image = node.GetComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    public void Dispose()
    {
        if (frame != null) frame.sizeDelta = originalFrameSize;
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null; sprites.Clear(); streaks.Clear();
    }
    #endregion
}

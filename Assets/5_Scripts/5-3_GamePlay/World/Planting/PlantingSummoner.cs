using UnityEngine;

/// <summary>
/// 统一种植召唤器：只负责把可种植物品的作物精灵显示在指针位置，并反馈当前地块是否合法。
/// 它是通用运行时预览，不承载作物数据、耕地校验或扣除种子，避免每种作物复制一套召唤器。
/// </summary>
public sealed class PlantingSummoner
{
    private const string PreviewSortingLayer = "Shadow";
    private const int PreviewSortingOrder = 1000;

    private readonly GameObject previewObject;
    private readonly SpriteRenderer previewRenderer;

    public PlantingSummoner(Sprite sprite)
    {
        if (sprite == null)
            throw new MissingReferenceException("[PlantingSummoner] 作物预览缺少 Sprite。");

        previewObject = new GameObject("PlantingSummoner");
        previewObject.hideFlags = HideFlags.DontSave;
        previewRenderer = previewObject.AddComponent<SpriteRenderer>();
        previewRenderer.sprite = sprite;
        previewRenderer.sortingLayerName = PreviewSortingLayer;
        previewRenderer.sortingOrder = PreviewSortingOrder;
        previewRenderer.enabled = true;
    }

    /// <summary>更新统一预览的位置、合法颜色和透明度。</summary>
    public void SetPreview(Vector3 position, bool valid, float alpha)
    {
        if (previewObject == null)
            return;

        previewObject.transform.position = position;
        Color color = valid ? new Color(0.45f, 1f, 0.45f) : new Color(1f, 0.25f, 0.25f);
        color.a = Mathf.Clamp01(alpha);
        previewRenderer.color = color;
    }

    /// <summary>销毁临时预览，避免切换快捷栏后残留在世界中。</summary>
    public void Dispose()
    {
        if (previewObject != null)
            Object.Destroy(previewObject);
    }
}

// AI-Context: 建筑放置预览的纯表现组件；放置占用由 BuildingOccupancyRegistry 的离散格层判定，禁止在这里恢复 Physics2D 碰撞检测。
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 建筑放置虚影只负责复制最终建筑的 Sprite 表现和合法/非法颜色。
/// 动态建筑的放置冲突由 BuildingOccupancyRegistry 维护的世界格记录负责，虚影不再创建 Collider/Rigidbody。
/// </summary>
public class BuildingShadow : MonoBehaviour
{
    private const string PreviewSortingLayer = "Shadow";
    // 预览必须压过 Default 世界精灵，但保持在 Player 等角色层之下。
    private const int PreviewSortingOrder = 1000;

    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    private bool isBlocked;
    private float visibility = 1f;

    private void OnDestroy()
    {
        transform.DOKill();
        DOTween.Kill(ShadowRenderer);
    }

    public void UpdateColor(bool hasOverlap)
    {
        if (ShadowRenderer == null || isBlocked == hasOverlap)
            return;

        isBlocked = hasOverlap;
        ApplyVisualState();
    }

    public void InitShadow(
        SpriteRenderer sourceRenderer,
        Transform sourceRoot,
        bool copySourceOffset = true)
    {
        if (sourceRenderer == null || sourceRoot == null || ShadowRenderer == null)
            throw new MissingComponentException("BuildingShadow 缺少 SpriteRenderer 引用");

        ShadowRenderer.sprite = sourceRenderer.sprite;
        // 虚影必须继承建筑实际材质；预制体材质丢失时仍可稳定显示。
        if (sourceRenderer.sharedMaterial != null)
            ShadowRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        if (ShadowRenderer.sharedMaterial == null)
            throw new MissingComponentException("BuildingShadow 缺少可用 Sprite 材质");

        int previewLayerId = SortingLayer.NameToID(PreviewSortingLayer);
        ShadowRenderer.sortingLayerID = previewLayerId != 0
            ? previewLayerId
            : sourceRenderer.sortingLayerID;
        ShadowRenderer.sortingOrder = PreviewSortingOrder;
        ShadowRenderer.flipX = sourceRenderer.flipX;
        ShadowRenderer.flipY = sourceRenderer.flipY;
        ShadowRenderer.drawMode = sourceRenderer.drawMode;
        ShadowRenderer.size = sourceRenderer.size;
        ShadowRenderer.maskInteraction = sourceRenderer.maskInteraction;
        ShadowRenderer.enabled = true;

        Transform shadowTransform = ShadowRenderer.transform;
        shadowTransform.localPosition = copySourceOffset
            ? sourceRoot.InverseTransformPoint(sourceRenderer.transform.position)
            : Vector3.zero;
        shadowTransform.localRotation = copySourceOffset
            ? Quaternion.Inverse(sourceRoot.rotation) * sourceRenderer.transform.rotation
            : Quaternion.identity;
        shadowTransform.localScale = DivideScale(sourceRenderer.transform.lossyScale, sourceRoot.lossyScale);
        ApplyVisualState();
    }

    public void UpdateAlpha(float alpha)
    {
        if (ShadowRenderer == null)
            return;

        float nextVisibility = Mathf.Clamp01(alpha);
        if (Mathf.Approximately(visibility, nextVisibility))
            return;

        visibility = nextVisibility;
        ApplyVisualState();
    }

    public void SmoothMove(Vector3 targetPosition)
    {
        transform.DOKill();
        transform.DOMove(targetPosition, 0.1f).SetEase(Ease.OutQuad);
    }

    private static Vector3 DivideScale(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            Mathf.Abs(divisor.x) > Mathf.Epsilon ? value.x / divisor.x : value.x,
            Mathf.Abs(divisor.y) > Mathf.Epsilon ? value.y / divisor.y : value.y,
            Mathf.Abs(divisor.z) > Mathf.Epsilon ? value.z / divisor.z : value.z);
    }

    private void ApplyVisualState()
    {
        if (ShadowRenderer == null)
            return;

        Color color = isBlocked ? WarringColor : ShadowColor;
        color.a *= visibility;
        ShadowRenderer.color = color;
    }
}

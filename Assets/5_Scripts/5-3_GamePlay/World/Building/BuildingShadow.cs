// AI-Context: 建筑放置预览的纯表现与碰撞缓存；这里只报告障碍物，不生成建筑、不扣材料、也不直接发送网络消息。
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class BuildingShadow : MonoBehaviour
{
    private const string PreviewSortingLayer = "Shadow";
    // 预览必须压过 Default 世界精灵，但保持在 Player 等角色层之下。
    private const int PreviewSortingOrder = 1000;

    public List<GameObject> AroundObjects = new();
    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    [Header("碰撞体设置")]
    public Vector2 BoxColliderScale = Vector2.one;
    public Vector2 BoxColliderOffset;
    [SerializeField] private BoxCollider2D previewCollider;

    [Header("调试信息")]
    public Collider2D obstacleCollider;
    public GameObject firstObstacle;

    private readonly HashSet<Collider2D> obstacles = new();
    private bool isBlocked;
    private float visibility = 1f;

    private void OnValidate()
    {
        if (previewCollider == null)
            previewCollider = GetComponentInChildren<BoxCollider2D>(true);
    }

    public bool AroundHaveGameObject
    {
        get
        {
            PruneDestroyedObstacles();
            return obstacles.Count > 0;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsObstacle(collision) || !obstacles.Add(collision))
            return;

        RebuildDebugView();
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision == null || !obstacles.Remove(collision))
            return;

        RebuildDebugView();
    }

    private void OnDisable()
    {
        obstacles.Clear();
        AroundObjects.Clear();
        obstacleCollider = null;
        firstObstacle = null;
    }

    private void OnDestroy()
    {
        transform.DOKill();
        DOTween.Kill(ShadowRenderer);
    }

    public void UpdateColor(bool hasOverlap)
    {
        if (ShadowRenderer == null)
            return;

        if (isBlocked == hasOverlap)
            return;

        isBlocked = hasOverlap;
        ApplyVisualState();
    }

    public void InitShadow(SpriteRenderer sourceRenderer, Transform sourceRoot, Bounds footprint)
    {
        if (sourceRenderer == null || sourceRoot == null || ShadowRenderer == null)
            throw new MissingComponentException("BuildingShadow 缺少 SpriteRenderer 引用");

        ShadowRenderer.sprite = sourceRenderer.sprite;
        // 虚影必须继承建筑实际材质；预制体材质丢失时仍可稳定显示。
        // 建筑旧资源的材质引用可能已经丢失；此时保留虚影 Prefab 的默认 Sprite 材质。
        if (sourceRenderer.sharedMaterial != null)
            ShadowRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        if (ShadowRenderer.sharedMaterial == null)
            throw new MissingComponentException("BuildingShadow 缺少可用 Sprite 材质");
        int previewLayerId = SortingLayer.NameToID(PreviewSortingLayer);
        ShadowRenderer.sortingLayerID = previewLayerId != 0
            ? previewLayerId
            : sourceRenderer.sortingLayerID;
        // 不沿用建筑本体的排序序号，避免同层的地表装饰把虚影盖住。
        ShadowRenderer.sortingOrder = PreviewSortingOrder;
        ShadowRenderer.flipX = sourceRenderer.flipX;
        ShadowRenderer.flipY = sourceRenderer.flipY;
        ShadowRenderer.drawMode = sourceRenderer.drawMode;
        ShadowRenderer.size = sourceRenderer.size;
        ShadowRenderer.maskInteraction = sourceRenderer.maskInteraction;
        ShadowRenderer.enabled = true;

        Transform shadowTransform = ShadowRenderer.transform;
        shadowTransform.localPosition = sourceRoot.InverseTransformPoint(sourceRenderer.transform.position);
        shadowTransform.localRotation = Quaternion.Inverse(sourceRoot.rotation) * sourceRenderer.transform.rotation;
        shadowTransform.localScale = DivideScale(sourceRenderer.transform.lossyScale, sourceRoot.lossyScale);

        BoxColliderScale = new Vector2(
            Mathf.Max(0.05f, footprint.size.x),
            Mathf.Max(0.05f, footprint.size.y));
        BoxColliderOffset = footprint.center;
        RefreshColliderSize();
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

    public string GetObstacleInfo()
    {
        PruneDestroyedObstacles();
        return firstObstacle != null ? firstObstacle.name : "未知障碍物";
    }

    public void RefreshColliderSize()
    {
        // 渲染节点需要复刻建筑自身的局部偏移，因此预览碰撞体必须独立放在根节点，
        // 否则图片偏移会再次影响占地检测。
        BoxCollider2D[] childColliders = GetComponentsInChildren<BoxCollider2D>(true);
        for (int i = 0; i < childColliders.Length; i++)
        {
            if (childColliders[i].transform != transform)
                childColliders[i].enabled = false;
        }

        previewCollider = GetComponent<BoxCollider2D>();
        if (previewCollider == null)
        {
            previewCollider = gameObject.AddComponent<BoxCollider2D>();
            Debug.LogWarning("[建筑预览] BuildingShadow 缺少 BoxCollider2D，已在运行时自动补全。", this);
        }

        Rigidbody2D body = GetComponentInParent<Rigidbody2D>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
        }

        previewCollider.size = new Vector2(Mathf.Max(0.05f, BoxColliderScale.x), Mathf.Max(0.05f, BoxColliderScale.y));
        previewCollider.offset = BoxColliderOffset;
        previewCollider.enabled = true;
        previewCollider.isTrigger = true;
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

    private bool IsObstacle(Collider2D collision)
    {
        Collider2D source = WorldTopologyColliderProxy.Resolve(collision);
        return source != null && !source.isTrigger && source.gameObject != gameObject &&
               source.GetComponentInParent<Player>() == null && source.gameObject.tag != "IgnoreShadow" &&
               !source.transform.IsChildOf(transform);
    }

    private void PruneDestroyedObstacles()
    {
        obstacles.RemoveWhere(collider => collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy);
        RebuildDebugView();
    }

    private void RebuildDebugView()
    {
        AroundObjects.Clear();
        obstacleCollider = null;
        firstObstacle = null;

        foreach (Collider2D collider in obstacles)
        {
            if (collider == null)
                continue;

            AroundObjects.Add(collider.gameObject);
            if (obstacleCollider == null)
            {
                obstacleCollider = collider;
                firstObstacle = collider.gameObject;
            }
        }
    }
}

// AI-Context: 建筑放置预览的纯表现与碰撞缓存；这里只报告障碍物，不生成建筑、不扣材料、也不直接发送网络消息。
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class BuildingShadow : MonoBehaviour
{
    public List<GameObject> AroundObjects = new();
    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    [Header("碰撞体设置")]
    public Vector2 BoxColliderScale = Vector2.one;
    [SerializeField] private BoxCollider2D previewCollider;

    [Header("位置偏移")]
    public Vector2 ShadowPositionOffset = new(0f, 0.5f);

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

    public Vector3 ApplyPlacementOffset(Vector3 worldPosition)
    {
        worldPosition += (Vector3)ShadowPositionOffset;
        worldPosition.z = 0f;
        return worldPosition;
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

    public void InitShadow(SpriteRenderer sourceRenderer, Vector2 footprintSize)
    {
        if (sourceRenderer == null || ShadowRenderer == null)
            throw new MissingComponentException("BuildingShadow 缺少 SpriteRenderer 引用");

        ShadowRenderer.sprite = sourceRenderer.sprite;
        ShadowRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        ShadowRenderer.sortingOrder = sourceRenderer.sortingOrder - 1;
        BoxColliderScale = new Vector2(
            Mathf.Max(0.05f, footprintSize.x),
            Mathf.Max(0.05f, footprintSize.y));
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
        // Unity 的 MissingReference 不等同于 C# null，不能使用 ??= 处理这里的引用。
        if (previewCollider == null)
            previewCollider = GetComponentInChildren<BoxCollider2D>(true);

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
        previewCollider.offset = Vector2.zero;
        previewCollider.enabled = true;
        previewCollider.isTrigger = true;
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
        return collision != null && !collision.isTrigger && collision.gameObject != gameObject &&
               !collision.CompareTag("Player") && collision.gameObject.tag != "IgnoreShadow" &&
               !collision.transform.IsChildOf(transform);
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

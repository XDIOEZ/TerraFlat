using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Force.DeepCloner;
using GameObject = UnityEngine.GameObject;

public class BuildingShadow : MonoBehaviour
{
    public List<GameObject> AroundObjects = new List<GameObject>();
    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new Color(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    [Header("碰撞体设置")]
    public Vector2 BoxColliderScale = Vector2.one;

    [Header("调试信息")]
    public Collider2D obstacleCollider; // 新增：记录障碍物碰撞体
    public GameObject firstObstacle;    // 新增：记录第一个障碍物

    private Tween moveTween;
    private Tween colorTween;
    private Tween alphaTween;

    public bool AroundHaveGameObject => AroundObjects.Count > 0;

    public void OnTriggerEnter2D(Collider2D collision)
    {
        // 只处理非Trigger对象
        if (!collision.isTrigger)
        {
            AroundObjects.Add(collision.gameObject);

            // 记录第一个障碍物用于调试
            if (obstacleCollider == null)
            {
                obstacleCollider = collision;
                firstObstacle = collision.gameObject;
            }
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.isTrigger)
        {
            AroundObjects.Remove(collision.gameObject);

            // 清除障碍物记录
            if (collision == obstacleCollider)
            {
                obstacleCollider = null;
                firstObstacle = null;

                // 如果有其他障碍物，更新为第一个
                if (AroundObjects.Count > 0)
                {
                    firstObstacle = AroundObjects[0];
                    obstacleCollider = firstObstacle.GetComponent<Collider2D>();
                }
            }
        }
    }

    public void OnDestroy()
    {
        AroundObjects.Clear();
        DOTween.Kill(transform);
        DOTween.Kill(ShadowRenderer);
        moveTween?.Kill();
        colorTween?.Kill();
        alphaTween?.Kill();
    }

    /// <summary>
    /// 更新阴影颜色
    /// </summary>
    /// <param name="hasOverlap">是否有障碍物</param>
    public void UpdateColor(bool hasOverlap)
    {
        Color targetColor = hasOverlap ? WarringColor : ShadowColor;
        if (ShadowRenderer != null)
        {
            colorTween?.Kill();
            colorTween = DOTween.To(
                () => ShadowRenderer.color,
                c => ShadowRenderer.color = c,
                targetColor,
                0.2f
            );
        }
    }

    /// <summary>
    /// 初始化阴影
    /// </summary>
    /// <param name="newRenderer">参考的渲染器</param>
    public void InitShadow(SpriteRenderer newRenderer)
    {
        if (newRenderer == null || ShadowRenderer == null)
        {
            Debug.LogWarning("BuildingShadow初始化失败: 缺少渲染器引用");
            return;
        }

        ShadowRenderer.sprite = newRenderer.sprite;
        ShadowRenderer.sortingOrder = newRenderer.sortingOrder - 1;
        ShadowRenderer.color = ShadowColor;

        // 检查精灵是否有效
        if (newRenderer.sprite == null)
        {
            Debug.LogWarning("BuildingShadow初始化失败: 精灵为空");
            return;
        }

        Bounds bounds = newRenderer.sprite.bounds;

        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider != null)
        {
            boxCollider.size = Vector2.Scale(bounds.size, BoxColliderScale);
            boxCollider.offset = bounds.center;

            // 添加调试信息
            Debug.Log($"[BuildingShadow] 碰撞体尺寸: {boxCollider.size}, 偏移: {boxCollider.offset}");
        }
        else
        {
            Debug.LogWarning("BuildingShadow初始化失败: 缺少BoxCollider2D组件");
        }
    }

    /// <summary>
    /// 更新透明度
    /// </summary>
    /// <param name="alpha">透明度值 (0-1)</param>
    public void UpdateAlpha(float alpha)
    {
        if (ShadowRenderer != null)
        {
            alphaTween?.Kill();

            // 创建平滑的透明度变化
            alphaTween = DOTween.To(
                () => ShadowRenderer.color.a,
                a => {
                    Color color = ShadowRenderer.color;
                    color.a = a;
                    ShadowRenderer.color = color;
                },
                alpha * 0.5f, // 最大0.5
                0.2f
            );

            ShadowRenderer.enabled = alpha > 0.01f;
        }
    }

    /// <summary>
    /// 平滑移动到目标位置
    /// </summary>
    /// <param name="targetPosition">目标位置</param>
    public void SmoothMove(Vector3 targetPosition)
    {
        moveTween?.Kill();
        moveTween = transform.DOMove(targetPosition, 0.1f)
            .SetEase(Ease.OutQuad)
            .OnUpdate(() => {
                // 添加调试信息
                Debug.DrawLine(transform.position, targetPosition, Color.green, 0.1f);
            });
    }

    /// <summary>
    /// 获取障碍物调试信息
    /// </summary>
    public string GetObstacleInfo()
    {
        if (firstObstacle != null)
        {
            return $"{firstObstacle.name} (位置: {firstObstacle.transform.position})";
        }
        return AroundObjects.Count > 0 ? "多个障碍物" : "未知障碍物";
    }
}
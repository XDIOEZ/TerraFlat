using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 单个世界格子的白色目标框。只负责表现，不参与命中判断；调用方必须把实际操作使用的同一 WorldCell 传进来。
/// </summary>
public sealed class WorldTileTargetOutline : MonoBehaviour
{
    private const float LineWidth = 0.04f;
    private const int SortingOrder = 32000;

    private LineRenderer lineRenderer;
    private Material lineMaterial;
    private Vector2Int currentCell;
    private bool hasCell;

    /// <summary>创建不写入场景和 Prefab 的运行时目标框。</summary>
    public static WorldTileTargetOutline Create(string objectName)
    {
        GameObject root = new GameObject(objectName)
        {
            hideFlags = HideFlags.DontSave
        };
        return root.AddComponent<WorldTileTargetOutline>();
    }

    /// <summary>显示指定整数世界格的四条边。</summary>
    public void Show(Vector2Int worldCell)
    {
        EnsureRenderer();
        if (lineRenderer == null)
            return;

        if (!hasCell || currentCell != worldCell)
        {
            currentCell = worldCell;
            hasCell = true;
            float x = worldCell.x;
            float y = worldCell.y;
            const float z = -0.05f;
            lineRenderer.SetPosition(0, new Vector3(x, y, z));
            lineRenderer.SetPosition(1, new Vector3(x + 1f, y, z));
            lineRenderer.SetPosition(2, new Vector3(x + 1f, y + 1f, z));
            lineRenderer.SetPosition(3, new Vector3(x, y + 1f, z));
        }

        lineRenderer.enabled = true;
    }

    /// <summary>立即隐藏目标框。</summary>
    public void Hide()
    {
        hasCell = false;
        if (lineRenderer != null)
            lineRenderer.enabled = false;
    }

    private void EnsureRenderer()
    {
        if (lineRenderer != null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[WorldTileTargetOutline] 无法找到可用的无光照线框 Shader。", this);
            enabled = false;
            return;
        }

        lineMaterial = new Material(shader)
        {
            name = "World Tile Target Outline (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.hideFlags = HideFlags.DontSave;
        lineRenderer.sharedMaterial = lineMaterial;
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.positionCount = 4;
        lineRenderer.startWidth = LineWidth;
        lineRenderer.endWidth = LineWidth;
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
        lineRenderer.numCornerVertices = 0;
        lineRenderer.numCapVertices = 0;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.sortingOrder = SortingOrder;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.lightProbeUsage = LightProbeUsage.Off;
        lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lineRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }
}

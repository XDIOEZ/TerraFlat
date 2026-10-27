using UnityEngine;
using UnityEngine.Rendering;
using FlatWorld.Diagnostics;

/// <summary>
/// 单个世界格子的白色目标框。只负责表现，不参与命中判断；调用方必须把实际操作使用的同一 WorldCell 传进来。
/// </summary>
public sealed class WorldTileTargetOutline : MonoBehaviour
{
    #region 目标框性能诊断
    // 建框、组件和材质是表现资源数量，不是背包物品数量。
    private static readonly PerformanceDebugPoint CreateDebug = new("FlatWorld.TargetOutline.Create");
    private static readonly PerformanceDebugPoint InitializeRendererDebug = new("FlatWorld.TargetOutline.InitializeRenderer");
    private static readonly PerformanceDebugPoint CreatedObjectsDebug = new("FlatWorld.TargetOutline.CreatedObjects");
    private static readonly PerformanceDebugPoint CreatedComponentsDebug = new("FlatWorld.TargetOutline.CreatedComponents");
    private static readonly PerformanceDebugPoint CreatedMaterialsDebug = new("FlatWorld.TargetOutline.CreatedMaterials");
    private static readonly PerformanceDebugPoint DestroyedMaterialsDebug = new("FlatWorld.TargetOutline.DestroyedMaterials");
    #endregion

    private const float LineWidth = 0.04f;
    private const int SortingOrder = 32000;

    private LineRenderer lineRenderer;
    private Material lineMaterial;
    private Vector2Int currentCell;
    private bool hasCell;

    /// <summary>创建不写入场景和 Prefab 的运行时目标框。</summary>
    public static WorldTileTargetOutline Create(string objectName)
    {
        using var createDebug = CreateDebug.Measure(1, objectName);
        GameObject root = new GameObject(objectName)
        {
            hideFlags = HideFlags.DontSave
        };
        CreatedObjectsDebug.AddWork(1);
        WorldTileTargetOutline outline = root.AddComponent<WorldTileTargetOutline>();
        CreatedComponentsDebug.AddWork(1);
        return outline;
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

        using var initializeDebug = InitializeRendererDebug.Measure(1);
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
        CreatedMaterialsDebug.AddWork(1);

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        CreatedComponentsDebug.AddWork(1);
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
        // 世界目标框始终绘制在地形和角色使用的排序层之上。
        WorldRenderingManager.Instance.ApplyRendererSorting(lineRenderer, WorldSortingManager.WorldEffectCategory, SortingOrder);
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.lightProbeUsage = LightProbeUsage.Off;
        lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lineRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
        {
            Destroy(lineMaterial);
            DestroyedMaterialsDebug.AddWork(1);
        }
    }
}

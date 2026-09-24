using UnityEngine;

/// <summary>
/// 铲子采挖的本地裂纹表现。根据权威地格的 0 到 1 工作量显示逐渐增多的裂缝；
/// 不保存对象，也不参与地形判定，重新指向地格时会从区块进度恢复。
/// </summary>
public sealed class GroundHarvestCrackOverlay : MonoBehaviour
{
    #region 裂纹形状与资源
    private static readonly Vector2[][] Paths =
    {
        new[] { new Vector2(0.22f, 0.24f), new Vector2(0.39f, 0.39f), new Vector2(0.33f, 0.57f), new Vector2(0.53f, 0.73f) },
        new[] { new Vector2(0.39f, 0.39f), new Vector2(0.59f, 0.34f), new Vector2(0.76f, 0.22f) },
        new[] { new Vector2(0.33f, 0.57f), new Vector2(0.21f, 0.73f), new Vector2(0.17f, 0.84f) }
    };
    private readonly LineRenderer[] lines = new LineRenderer[3];
    private Material material;
    private Vector2Int currentCell;
    private bool hasCell;
    #endregion

    #region 生命周期与显示
    /// <summary>按需创建一个不会写入场景的裂纹对象。</summary>
    public static GroundHarvestCrackOverlay Create()
    {
        GameObject root = new("Ground Harvest Cracks") { hideFlags = HideFlags.DontSave };
        return root.AddComponent<GroundHarvestCrackOverlay>();
    }

    /// <summary>在当前世界格绘制随进度递增的裂缝分支。</summary>
    public void Show(Vector2Int worldCell, float progress)
    {
        if (progress <= 0f)
        {
            Hide();
            return;
        }

        EnsureLines();
        if (material == null) return;
        int visibleCount = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(progress) * lines.Length), 1, lines.Length);
        if (!hasCell || currentCell != worldCell)
        {
            currentCell = worldCell;
            hasCell = true;
            for (int i = 0; i < lines.Length; i++)
                for (int j = 0; j < Paths[i].Length; j++)
                    lines[i].SetPosition(j, new Vector3(
                        worldCell.x + Paths[i][j].x, worldCell.y + Paths[i][j].y, -0.06f));
        }

        for (int i = 0; i < lines.Length; i++) lines[i].enabled = i < visibleCount;
    }

    /// <summary>离开目标地格时隐藏裂纹。</summary>
    public void Hide()
    {
        hasCell = false;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i] != null) lines[i].enabled = false;
    }

    /// <summary>创建三条浅棕色不发光裂缝，确保深色泥炭上可辨认。</summary>
    private void EnsureLines()
    {
        if (material != null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[GroundHarvestCrackOverlay] 无法找到裂纹 Shader。", this);
            return;
        }

        material = new Material(shader) { name = "Ground Harvest Cracks (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < lines.Length; i++)
        {
            GameObject branch = new($"Crack Branch {i + 1}");
            branch.transform.SetParent(transform, false);
            LineRenderer line = branch.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = Paths[i].Length;
            line.startWidth = 0.055f;
            line.endWidth = 0.035f;
            line.startColor = new Color(0.87f, 0.64f, 0.37f, 0.96f);
            line.endColor = new Color(0.69f, 0.44f, 0.22f, 0.96f);
            line.numCornerVertices = 1;
            line.sortingOrder = 31999;
            line.enabled = false;
            lines[i] = line;
        }
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
    #endregion
}

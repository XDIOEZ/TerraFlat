using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将食物 Sprite 的紧密网格光栅化为可见区域，并按当前轮廓依次生成圆形咬痕。
/// 该类只负责确定性几何计算，不持有 SpriteMask、Texture2D 或其他场景对象。
/// </summary>
internal sealed class FoodBiteMaskGenerator
{
    /// <summary>遮罩长边分辨率，兼顾圆弧质量与移动端开销。</summary>
    private const int LongAxisResolution = 96;

    /// <summary>防止极窄 Sprite 的短边分辨率不足以形成圆弧。</summary>
    private const int MinimumShortAxisResolution = 16;

    /// <summary>完全保留像素。</summary>
    private static readonly Color32 VisiblePixel = new Color32(255, 255, 255, 255);

    /// <summary>完全剔除像素。</summary>
    private static readonly Color32 HiddenPixel = new Color32(0, 0, 0, 0);

    /// <summary>原始 Sprite 的本地边界。</summary>
    private readonly Bounds spriteBounds;

    /// <summary>由 Sprite 紧密网格得到的初始可见单元。</summary>
    private readonly bool[] sourceCells;

    /// <summary>应用已有咬痕后的当前可见单元。</summary>
    private readonly bool[] visibleCells;

    /// <summary>提交给运行时遮罩纹理的像素。</summary>
    private readonly Color32[] maskPixels;

    /// <summary>当前可见轮廓上的候选边界点。</summary>
    private readonly List<Vector2> boundaryPoints = new List<Vector2>();

    /// <summary>单个遮罩像素对应的本地宽度。</summary>
    private readonly float cellWidth;

    /// <summary>单个遮罩像素对应的本地高度。</summary>
    private readonly float cellHeight;

    /// <summary>由食物面积和最大食用次数计算出的统一咬痕半径。</summary>
    private readonly float biteRadius;

    /// <summary>完成整份食物所需的实际整数口数。</summary>
    private readonly int maximumBiteCount;

    /// <summary>当前仍可见的 Sprite 网格单元数量。</summary>
    private int visibleCellCount;

    /// <summary>运行时遮罩纹理宽度。</summary>
    public int Width { get; }

    /// <summary>运行时遮罩纹理高度。</summary>
    public int Height { get; }

    /// <summary>原始 Sprite 的本地边界。</summary>
    public Bounds SpriteBounds => spriteBounds;

    /// <summary>最近一次构建完成的遮罩像素。</summary>
    public Color32[] MaskPixels => maskPixels;

    /// <summary>根据 Sprite 外形与最大口数初始化咬痕生成器。</summary>
    public FoodBiteMaskGenerator(Sprite sprite, int maximumBites)
    {
        if (sprite == null)
            throw new ArgumentNullException(nameof(sprite));

        spriteBounds = sprite.bounds;
        maximumBiteCount = Mathf.Max(1, maximumBites);

        float width = Mathf.Max(0.0001f, spriteBounds.size.x);
        float height = Mathf.Max(0.0001f, spriteBounds.size.y);
        ResolveMaskResolution(width, height, out int resolvedWidth, out int resolvedHeight);
        Width = resolvedWidth;
        Height = resolvedHeight;
        cellWidth = width / Width;
        cellHeight = height / Height;

        sourceCells = new bool[Width * Height];
        visibleCells = new bool[sourceCells.Length];
        maskPixels = new Color32[sourceCells.Length];
        RasterizeSpriteMesh(sprite);

        int sourceCellCount = CountSourceCells();
        if (sourceCellCount == 0)
        {
            FillSourceBounds();
            sourceCellCount = sourceCells.Length;
        }

        float sourceArea = sourceCellCount * cellWidth * cellHeight;
        float estimatedRadius = Mathf.Sqrt(
            2f * sourceArea / (Mathf.PI * maximumBiteCount));
        biteRadius = Mathf.Max(estimatedRadius, Mathf.Max(cellWidth, cellHeight) * 1.5f);
    }

    /// <summary>重建指定已食用口数对应的完整遮罩。</summary>
    public bool Build(int completedBites, int randomSeed)
    {
        ResetMask();
        int biteCount = Mathf.Clamp(completedBites, 0, maximumBiteCount);
        if (biteCount <= 0)
            return true;

        if (biteCount >= maximumBiteCount)
        {
            ClearMask();
            return false;
        }

        var random = new System.Random(randomSeed);
        for (int biteIndex = 0; biteIndex < biteCount; biteIndex++)
        {
            CollectBoundaryPoints();
            if (boundaryPoints.Count == 0)
                break;

            Vector2 center = boundaryPoints[random.Next(boundaryPoints.Count)];
            float safeRadius = ResolveSafeRadius(center);
            CarveCircle(center, safeRadius);
        }

        return visibleCellCount > 0;
    }

    /// <summary>按 Sprite 长宽比计算近似等距的遮罩网格。</summary>
    private static void ResolveMaskResolution(
        float width,
        float height,
        out int resolvedWidth,
        out int resolvedHeight)
    {
        if (width >= height)
        {
            resolvedWidth = LongAxisResolution;
            resolvedHeight = Mathf.Clamp(
                Mathf.RoundToInt(LongAxisResolution * height / width),
                MinimumShortAxisResolution,
                LongAxisResolution);
            return;
        }

        resolvedHeight = LongAxisResolution;
        resolvedWidth = Mathf.Clamp(
            Mathf.RoundToInt(LongAxisResolution * width / height),
            MinimumShortAxisResolution,
            LongAxisResolution);
    }

    /// <summary>把 Sprite 的渲染三角网格转换为初始可见单元。</summary>
    private void RasterizeSpriteMesh(Sprite sprite)
    {
        Vector2[] vertices = sprite.vertices;
        ushort[] triangles = sprite.triangles;
        if (vertices == null || vertices.Length < 3 || triangles == null || triangles.Length < 3)
            return;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector2 point = GetCellCenter(x, y);
                sourceCells[GetIndex(x, y)] = IsInsideSpriteMesh(point, vertices, triangles);
            }
        }
    }

    /// <summary>判断本地点是否位于 Sprite 任意渲染三角形内。</summary>
    private static bool IsInsideSpriteMesh(
        Vector2 point,
        Vector2[] vertices,
        ushort[] triangles)
    {
        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            Vector2 first = vertices[triangles[i]];
            Vector2 second = vertices[triangles[i + 1]];
            Vector2 third = vertices[triangles[i + 2]];
            if (IsInsideTriangle(point, first, second, third))
                return true;
        }

        return false;
    }

    /// <summary>通过有向面积判断点是否位于三角形内。</summary>
    private static bool IsInsideTriangle(Vector2 point, Vector2 first, Vector2 second, Vector2 third)
    {
        const float epsilon = 0.000001f;
        float firstCross = Cross(second - first, point - first);
        float secondCross = Cross(third - second, point - second);
        float thirdCross = Cross(first - third, point - third);
        bool hasNegative = firstCross < -epsilon || secondCross < -epsilon || thirdCross < -epsilon;
        bool hasPositive = firstCross > epsilon || secondCross > epsilon || thirdCross > epsilon;
        return !(hasNegative && hasPositive);
    }

    /// <summary>计算二维向量叉积。</summary>
    private static float Cross(Vector2 left, Vector2 right)
    {
        return left.x * right.y - left.y * right.x;
    }

    /// <summary>统计 Sprite 网格覆盖的单元数量。</summary>
    private int CountSourceCells()
    {
        int count = 0;
        for (int i = 0; i < sourceCells.Length; i++)
        {
            if (sourceCells[i])
                count++;
        }

        return count;
    }

    /// <summary>在缺少有效紧密网格时回退到完整 Sprite 边界。</summary>
    private void FillSourceBounds()
    {
        for (int i = 0; i < sourceCells.Length; i++)
            sourceCells[i] = true;
    }

    /// <summary>恢复未被咬过的可见区域和全白遮罩。</summary>
    private void ResetMask()
    {
        Array.Copy(sourceCells, visibleCells, sourceCells.Length);
        visibleCellCount = CountSourceCells();
        for (int i = 0; i < maskPixels.Length; i++)
            maskPixels[i] = VisiblePixel;
    }

    /// <summary>清空最终一口后的全部遮罩像素。</summary>
    private void ClearMask()
    {
        Array.Clear(visibleCells, 0, visibleCells.Length);
        for (int i = 0; i < maskPixels.Length; i++)
            maskPixels[i] = HiddenPixel;
        visibleCellCount = 0;
    }

    /// <summary>从当前可见单元与空白单元的交界处收集轮廓点。</summary>
    private void CollectBoundaryPoints()
    {
        boundaryPoints.Clear();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!IsVisibleCell(x, y))
                    continue;

                Vector2 center = GetCellCenter(x, y);
                if (!IsVisibleCell(x - 1, y))
                    boundaryPoints.Add(new Vector2(center.x - cellWidth * 0.5f, center.y));
                if (!IsVisibleCell(x + 1, y))
                    boundaryPoints.Add(new Vector2(center.x + cellWidth * 0.5f, center.y));
                if (!IsVisibleCell(x, y - 1))
                    boundaryPoints.Add(new Vector2(center.x, center.y - cellHeight * 0.5f));
                if (!IsVisibleCell(x, y + 1))
                    boundaryPoints.Add(new Vector2(center.x, center.y + cellHeight * 0.5f));
            }
        }
    }

    /// <summary>必要时收缩咬痕半径，确保最后一口之前仍保留可见区域。</summary>
    private float ResolveSafeRadius(Vector2 center)
    {
        float radius = biteRadius;
        float minimumRadius = Mathf.Max(cellWidth, cellHeight) * 0.5f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (CountVisibleCellsInsideCircle(center, radius) < visibleCellCount)
                return radius;

            radius *= 0.75f;
            if (radius < minimumRadius)
                break;
        }

        return CountVisibleCellsInsideCircle(center, radius) < visibleCellCount ? radius : 0f;
    }

    /// <summary>统计指定圆形咬痕会剔除的当前可见单元。</summary>
    private int CountVisibleCellsInsideCircle(Vector2 center, float radius)
    {
        if (radius <= 0f)
            return 0;

        float radiusSquared = radius * radius;
        int count = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = GetIndex(x, y);
                if (visibleCells[index] &&
                    (GetCellCenter(x, y) - center).sqrMagnitude <= radiusSquared)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>从当前可见区域和最终遮罩像素中剔除一个圆。</summary>
    private void CarveCircle(Vector2 center, float radius)
    {
        if (radius <= 0f)
            return;

        float radiusSquared = radius * radius;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if ((GetCellCenter(x, y) - center).sqrMagnitude > radiusSquared)
                    continue;

                int index = GetIndex(x, y);
                maskPixels[index] = HiddenPixel;
                if (!visibleCells[index])
                    continue;

                visibleCells[index] = false;
                visibleCellCount--;
            }
        }
    }

    /// <summary>判断网格坐标是否仍属于当前可见食物区域。</summary>
    private bool IsVisibleCell(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height && visibleCells[GetIndex(x, y)];
    }

    /// <summary>把遮罩网格坐标转换为一维索引。</summary>
    private int GetIndex(int x, int y)
    {
        return y * Width + x;
    }

    /// <summary>把遮罩单元中心转换为 Sprite 本地坐标。</summary>
    private Vector2 GetCellCenter(int x, int y)
    {
        return new Vector2(
            spriteBounds.min.x + (x + 0.5f) * cellWidth,
            spriteBounds.min.y + (y + 0.5f) * cellHeight);
    }
}

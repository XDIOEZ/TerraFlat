using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

internal static class CombinedTankColliderGeometryRegression
{
    #region 独立正式源码回归

    private static int checks;
    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static ChunkMergedObstacleRectangles BuildUnitGrid(bool[,] occupied)
    {
        var merged = new ChunkMergedObstacleRectangles();
        for (int y = 0; y < occupied.GetLength(1); y++)
        for (int x = 0; x < occupied.GetLength(0); x++)
            if (occupied[x, y]) merged.SetSourceRectangle(y * occupied.GetLength(0) + x + 1,
                new Vector2(x + .5f, y + .5f), Vector2.one);
        merged.RebuildMergedRectangles();
        return merged;
    }

    // 每个格中心和外缘采样直接对照输入占格，防止合并填洞或扩大范围。
    private static void VerifyOriginalGridCoverage(ChunkMergedObstacleRectangles merged, bool[,] occupied)
    {
        Require(merged.Rectangles.Count <= merged.SourceCount, "合并反而增加碰撞盒数量。");
        int width = occupied.GetLength(0), height = occupied.GetLength(1);
        for (int y = -1; y <= height; y++)
        for (int x = -1; x <= width; x++)
        {
            bool expected = x >= 0 && y >= 0 && x < width && y < height && occupied[x, y];
            foreach (Vector2 sampleOffset in new[] { new Vector2(.25f, .25f), new Vector2(.75f, .75f) })
            {
                Vector2 point = new Vector2(x, y) + sampleOffset;
                bool covered = false;
                for (int i = 0; i < merged.Rectangles.Count; i++) covered |= merged.Rectangles[i].Bounds.Contains(point);
                Require(covered == expected, $"合并前后覆盖不同：({point.x},{point.y})。");
            }
        }
    }

    private static void Main()
    {
        var solid = new bool[32, 16];
        for (int y = 0; y < 16; y++) for (int x = 0; x < 32; x++) solid[x, y] = true;
        var rectangle = BuildUnitGrid(solid);
        Require(rectangle.SourceCount == 512 && rectangle.Rectangles.Count == 1, "512格整片罐体没有合成1盒。");
        VerifyOriginalGridCoverage(rectangle, solid);

        var concave = new bool[3, 3];
        for (int i = 0; i < 3; i++) { concave[i, 0] = true; concave[0, i] = true; }
        var lShape = BuildUnitGrid(concave);
        Require(lShape.Rectangles.Count == 2, "L形罐体没有合成2盒。");
        VerifyOriginalGridCoverage(lShape, concave);

        var ring = new bool[3, 3];
        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) ring[x, y] = x != 1 || y != 1;
        var ringShape = BuildUnitGrid(ring);
        Require(ringShape.Rectangles.Count == 4, "空心罐体矩形数量异常。");
        VerifyOriginalGridCoverage(ringShape, ring);

        var screenshot = new bool[8, 6];
        int[,] rows = { { 0, 0, 7 }, { 1, 0, 7 }, { 2, 5, 7 }, { 3, 5, 6 }, { 4, 4, 6 }, { 5, 4, 5 } };
        for (int row = 0; row < rows.GetLength(0); row++)
            for (int x = rows[row, 1]; x <= rows[row, 2]; x++) screenshot[x, rows[row, 0]] = true;
        var screenshotShape = BuildUnitGrid(screenshot);
        Require(screenshotShape.SourceCount == 26 && screenshotShape.Rectangles.Count == 5, "截图同形26格罐体没有合成5盒。");
        VerifyOriginalGridCoverage(screenshotShape, screenshot);
        var screenshotLeft = new bool[5, 6];
        var screenshotRight = new bool[3, 6];
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 5; x++) screenshotLeft[x, y] = screenshot[x, y];
            for (int x = 0; x < 3; x++) screenshotRight[x, y] = screenshot[x + 5, y];
        }
        var leftChunk = BuildUnitGrid(screenshotLeft);
        var rightChunk = BuildUnitGrid(screenshotRight);
        Require(leftChunk.SourceCount == 12 && leftChunk.Rectangles.Count == 2 &&
            rightChunk.SourceCount == 14 && rightChunk.Rectangles.Count == 3, "截图实际跨区块后未按2+3盒保留几何。");
        VerifyOriginalGridCoverage(leftChunk, screenshotLeft);
        VerifyOriginalGridCoverage(rightChunk, screenshotRight);

        var spaced = new ChunkMergedObstacleRectangles();
        spaced.SetSourceRectangle(1, new Vector2(.45f, .5f), new Vector2(.9f, 1));
        spaced.SetSourceRectangle(2, new Vector2(1.45f, .5f), new Vector2(.9f, 1));
        spaced.RebuildMergedRectangles();
        Require(spaced.Rectangles.Count == 2, "合并跨过真实间隙。");
        foreach (var box in spaced.Rectangles) Require(!box.Bounds.Contains(new Vector2(.95f, .5f)), "间隙被填成实心。");
        var tinyGap = new ChunkMergedObstacleRectangles();
        float gap = 1f / 262144f;
        tinyGap.SetSourceRectangle(1, new Vector2(.5f, .5f), Vector2.one);
        tinyGap.SetSourceRectangle(2, new Vector2(1.5f + gap, .5f), Vector2.one);
        tinyGap.RebuildMergedRectangles();
        Require(tinyGap.Rectangles.Count == 2, "极小正间隙被容差吞并。");
        foreach (var box in tinyGap.Rectangles)
            Require(!box.Bounds.Contains(new Vector2(1 + gap * .5f, .5f)), "极小正间隙被填成实心。");
        var unequalHeights = new ChunkMergedObstacleRectangles();
        unequalHeights.SetSourceRectangle(1, new Vector2(.5f, 1), new Vector2(1, 2));
        unequalHeights.SetSourceRectangle(2, new Vector2(1.5f, .5f), Vector2.one);
        unequalHeights.RebuildMergedRectangles();
        Require(unequalHeights.Rectangles.Count == 2, "不等高相邻矩形被扩大为整块。");

        var split = new ChunkMergedObstacleRectangles();
        for (int i = 0; i < 3; i++) split.SetSourceRectangle(i + 1, new Vector2(i + .5f, .5f), Vector2.one);
        Require(split.RebuildMergedRectangles() && split.Rectangles.Count == 1, "初始三格没合并。");
        Require(split.RemoveSourceRectangle(2) && split.RebuildMergedRectangles() && split.Rectangles.Count == 2,
            "拆掉中间格后没有重建为两盒。");
        foreach (var box in split.Rectangles) Require(!box.Bounds.Contains(new Vector2(1.5f, .5f)), "拆罐后旧大盒残留。");
        Require(!split.RemoveSourceRectangle(2) && !split.RebuildMergedRectangles(), "重复删除无变化仍重建。");
        Require(split.RemoveSourcesExcept(new HashSet<int> { 3 }) == 1 && !split.ContainsSource(1) && split.ContainsSource(3),
            "全量重扫后未撤销旧来源。");
        split.RebuildMergedRectangles();
        Require(split.Rectangles.Count == 1 && split.Rectangles[0].Id == 3, "旧来源删除后输出身份不稳定。");
        split.SetSourceRectangle(3, new Vector2(2.5f, .5f), new Vector2(.5f, 1));
        split.RebuildMergedRectangles();
        Require(split.Rectangles.Count == 1 && split.Rectangles[0].Size.x == .5f, "改尺寸后保留旧盒。");
        Require(split.SetSourceRectangle(3, Vector2.zero, new Vector2(float.NaN, 1)) && split.RebuildMergedRectangles()
            && split.SourceCount == 0 && split.Rectangles.Count == 0, "无效输入没有撤销旧形状。");
        split.Clear(); Require(!split.RebuildMergedRectangles(), "空合并器重复清理仍重建。");

        var random = new System.Random(90210);
        int randomizedSamples = 64;
        for (int sample = 0; sample < randomizedSamples; sample++)
        {
            var occupied = new bool[19, 13];
            for (int y = 0; y < 13; y++) for (int x = 0; x < 19; x++) occupied[x, y] = random.Next(100) < 60;
            VerifyOriginalGridCoverage(BuildUnitGrid(occupied), occupied);
        }

        for (int warmup = 0; warmup < 8; warmup++) VerifyUnchangedGridDoesNotRebuild(rectangle, 32, 16);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool stable = true;
        for (int iteration = 0; iteration < 10000; iteration++) stable &= VerifyUnchangedGridDoesNotRebuild(rectangle, 32, 16);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(stable && allocated == 0, $"稳定输入仍重建或分配：{allocated}字节。");
        Console.WriteLine(JsonSerializer.Serialize(new { result = "passed", checks, randomizedSamples,
            solidSources = 512, solidBoxes = rectangle.Rectangles.Count,
            screenshotSources = screenshotShape.SourceCount, screenshotBoxes = screenshotShape.Rectangles.Count,
            actualChunkBoxes = new[] { leftChunk.Rectangles.Count, rightChunk.Rectangles.Count },
            unchangedIterations = 10000, unchangedAllocatedBytes = allocated,
            boundary = "正式纯托管合并器；未运行Unity原生碰撞、镜像或Lua" }));
    }

    private static bool VerifyUnchangedGridDoesNotRebuild(ChunkMergedObstacleRectangles merged, int width, int height)
    {
        bool stable = true;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            stable &= !merged.SetSourceRectangle(y * width + x + 1, new Vector2(x + .5f, y + .5f), Vector2.one);
        return !merged.RebuildMergedRectangles() && stable;
    }

    #endregion
}

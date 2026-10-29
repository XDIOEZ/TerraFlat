using System;

/// <summary>在一次区块阴影刷新内复用地表采样，并保留一格邻域。</summary>
internal sealed class TerrainShadowSurfaceSampleCache<TSurface>
{
    #region 缓存配置与读取

    private TSurface[] surfaceSamples = Array.Empty<TSurface>();
    private bool[] sampleAvailability = Array.Empty<bool>();
    private uint[] sampleStamps = Array.Empty<uint>();
    private int chunkWidth;
    private int chunkHeight;
    private int sampleStride;
    private uint currentStamp;

    internal void ConfigureForChunkWithOneCellHalo(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (chunkWidth == width && chunkHeight == height) return;

        chunkWidth = width;
        chunkHeight = height;
        sampleStride = checked(width + 2);
        int sampleCount = checked(sampleStride * checked(height + 2));
        surfaceSamples = new TSurface[sampleCount];
        sampleAvailability = new bool[sampleCount];
        sampleStamps = new uint[sampleCount];
        currentStamp = 1;
    }

    internal void BeginFlushPass() => AdvanceStamp();

    internal void InvalidateCurrentFlushPass() => AdvanceStamp();

    internal bool TryGetCachedSurfaceSample(int x, int y, out bool available, out TSurface surface)
    {
        available = false;
        surface = default;
        int index = GetSampleIndex(x, y);
        if (index < 0 || sampleStamps[index] != currentStamp) return false;

        available = sampleAvailability[index];
        surface = surfaceSamples[index];
        return true;
    }

    internal void StoreSurfaceSample(int x, int y, bool available, TSurface surface)
    {
        int index = GetSampleIndex(x, y);
        if (index < 0) return;

        sampleAvailability[index] = available;
        surfaceSamples[index] = surface;
        sampleStamps[index] = currentStamp;
    }

    #endregion

    #region 坐標與版本標記

    private int GetSampleIndex(int x, int y)
    {
        if (sampleStamps.Length == 0 || x < -1 || x > chunkWidth || y < -1 || y > chunkHeight) return -1;
        return (y + 1) * sampleStride + x + 1;
    }

    private void AdvanceStamp()
    {
        unchecked { currentStamp++; }
        if (currentStamp != 0) return;

        Array.Clear(sampleStamps, 0, sampleStamps.Length);
        currentStamp = 1;
    }

    #endregion
}

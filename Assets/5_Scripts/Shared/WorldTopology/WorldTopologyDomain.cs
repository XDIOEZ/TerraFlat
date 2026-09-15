using Unity.Mathematics;

/// <summary>
/// 循环坐标的唯一数学实现：Min 包含、Min + Span 不包含，默认值表示无限世界。
/// 只保存值类型，可直接复制给 Jobs/Burst；不读取活动世界或创建任何表现对象。
/// </summary>
public readonly struct WorldTopologyDomain
{
    public readonly int2 Min;
    public readonly int2 Span;
    public readonly int IsWrappedValue;

    public bool IsWrapped => IsWrappedValue != 0;

    public WorldTopologyDomain(int2 min, int2 span, bool isWrapped)
    {
        Min = min;
        Span = span;
        IsWrappedValue = isWrapped ? 1 : 0;
    }

    #region 坐标与距离

    public bool Contains(float2 position) => !IsWrapped ||
        (math.all(position >= Min) && math.all(position < Min + Span));

    public bool Contains(int2 position) => !IsWrapped ||
        (math.all(position >= Min) && math.all(position < Min + Span));

    public float2 Normalize(float2 position) => !IsWrapped ? position : new float2(
        Wrap(position.x, Min.x, Span.x), Wrap(position.y, Min.y, Span.y));

    /// <summary>后台连续坐标生成保留 double 精度，不经过 float 坐标降精度。</summary>
    public double2 Normalize(double2 position) => !IsWrapped ? position : new double2(
        Wrap(position.x, Min.x, Span.x), Wrap(position.y, Min.y, Span.y));

    /// <summary>Cell 与已对齐 Chunk 原点共用同一整数归一化规则。</summary>
    public int2 Normalize(int2 position) => new int2(NormalizeX(position.x), NormalizeY(position.y));

    public int NormalizeX(int value) => !IsWrapped ? value : Wrap(value, Min.x, Span.x);
    public int NormalizeY(int value) => !IsWrapped ? value : Wrap(value, Min.y, Span.y);

    public float2 ShortestDelta(float2 from, float2 to)
    {
        return !IsWrapped ? to - from : new float2(
            WrapFloatDelta((double)to.x - from.x, Span.x), WrapFloatDelta((double)to.y - from.y, Span.y));
    }

    /// <summary>
    /// 默认半周期取负方向；需要关于原始差值对称的连续曲线可显式保留半周期符号。
    /// 该差异仅是平局策略，不产生另一套 Wrap 算法。
    /// </summary>
    public double2 ShortestDelta(double2 from, double2 to, bool preserveHalfPeriodSign = false)
    {
        return !IsWrapped ? to - from : new double2(
            WrapDelta(to.x - from.x, Span.x, preserveHalfPeriodSign),
            WrapDelta(to.y - from.y, Span.y, preserveHalfPeriodSign));
    }

    public int2 ShortestDelta(int2 from, int2 to)
    {
        return !IsWrapped ? to - from : new int2(
            WrapDelta((long)to.x - from.x, Span.x), WrapDelta((long)to.y - from.y, Span.y));
    }

    public float Distance(float2 from, float2 to) => math.length(ShortestDelta(from, to));
    public float SqrDistance(float2 from, float2 to) => math.lengthsq(ShortestDelta(from, to));
    public float2 NearestImagePosition(float2 origin, float2 target) => origin + ShortestDelta(origin, target);

    #endregion

    #region 接缝镜像

    /// <summary>
    /// 根据坐标包围盒返回相邻镜像偏移，最多三个，顺序为水平、垂直、对角。
    /// seamBand 是调用方需要覆盖的接缝宽度；includeBoundary 决定恰好贴边是否产生镜像。
    /// 两侧同时满足时沿用最小边优先的既有规则，不包含原位置自身。
    /// </summary>
    public WorldTopologyImageOffsets GetRequiredImageOffsets(
        float2 areaMin, float2 areaMax, float seamBand = 0f, bool includeBoundary = true)
    {
        if (!IsWrapped)
            return default;

        int x = GetImageDirection(areaMin.x, areaMax.x, Min.x, Span.x, seamBand, includeBoundary);
        int y = GetImageDirection(areaMin.y, areaMax.y, Min.y, Span.y, seamBand, includeBoundary);
        float2x3 offsets = default;
        int count = 0;
        if (x != 0)
            offsets[count++] = new float2(x * Span.x, 0f);
        if (y != 0)
            offsets[count++] = new float2(0f, y * Span.y);
        if (x != 0 && y != 0)
            offsets[count++] = new float2(x * Span.x, y * Span.y);
        return new WorldTopologyImageOffsets(offsets, count);
    }

    private static int GetImageDirection(
        float areaMin, float areaMax, int min, int span, float band, bool includeBoundary)
    {
        float low = min + band;
        float high = min + span - band;
        if (includeBoundary ? areaMin <= low : areaMin < low)
            return 1;
        return (includeBoundary ? areaMax >= high : areaMax > high) ? -1 : 0;
    }

    #endregion

    #region 唯一 Wrap 算法

    private static float Wrap(float value, int min, int span)
    {
        float normalized = (float)Wrap((double)value, min, span);
        return normalized >= (double)min + span ? min : normalized;
    }

    private static double Wrap(double value, int min, int span)
    {
        // 先提升精度再减 Min；原坐标已在范围内时保持原值，避免接缝附近相减丢失低位。
        double max = (double)min + span;
        if (value >= min && value < max)
            return value;

        // 余数避免多周远距离坐标的 floor * span 相消；负余数回到 [0, span)。
        double wrapped = (value % span - min) % span;
        if (wrapped < 0d)
            wrapped += span;
        double normalized = min + wrapped;
        return normalized >= max ? min : normalized;
    }

    private static int Wrap(int value, int min, int span)
    {
        long wrapped = ((long)value - min) % span;
        if (wrapped < 0L)
            wrapped += span;
        return (int)(min + wrapped);
    }

    private static double WrapDelta(double delta, int span, bool preserveHalfPeriodSign = false)
    {
        double half = span * 0.5d;
        double wrapped = delta % span;
        if (wrapped >= half)
            wrapped -= span;
        else if (wrapped < -half)
            wrapped += span;
        if (preserveHalfPeriodSign && wrapped == -half && delta > 0d)
            return half;
        return wrapped;
    }

    private static float WrapFloatDelta(double delta, int span)
    {
        float result = (float)WrapDelta(delta, span);
        // 与旧 Bounds 一致：恰好半周期时选择负方向。
        return result >= span * 0.5d ? (float)(-span * 0.5d) : result;
    }

    private static int WrapDelta(long delta, int span)
    {
        long half = span / 2L;
        long wrapped = (delta + half) % span;
        if (wrapped < 0L)
            wrapped += span;
        return (int)(wrapped - half);
    }

    #endregion
}

/// <summary>最多三个镜像偏移的定长值类型结果；仅访问 [0, Count)，无数组或托管分配。</summary>
public readonly struct WorldTopologyImageOffsets
{
    private readonly float2x3 offsets;
    public int Count { get; }
    public float2 this[int index] => offsets[index];

    internal WorldTopologyImageOffsets(float2x3 offsets, int count)
    {
        this.offsets = offsets;
        Count = count;
    }
}

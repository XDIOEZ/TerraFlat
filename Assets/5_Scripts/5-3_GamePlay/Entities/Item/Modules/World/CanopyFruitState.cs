using System;
using System.Collections.Generic;

/// <summary>周期树果配置；默认每批 1～5 果、成熟后等待 1～1440 游戏秒，时间与世界绝对时钟同域。</summary>
[Serializable]
public sealed class CanopyFruitSettings
{
    #region 可配置规则
    public int MinimumCount = 1; // 每批最少果数。
    public int MaximumCount = 5; // 同时在冠最大果数。
    public double GrowthSeconds = 2000; // 单果基础生长秒数。
    public double GrowthJitterSeconds = 0; // 单果额外生长随机秒数。
    public double CycleSeconds = 2000; // 清空树冠到下一批的间隔。
    public int MinimumFallDelay = 1; // 成熟后的最短等待。
    public int MaximumFallDelay = 1440; // 成熟后的最长等待。
    public double FlightSeconds = 0.65; // 临时危险坠落时长。

    /// <summary>拒绝非法配置，防止事件循环不前进。</summary>
    public void Validate()
    {
        if (MinimumCount < 1 || MaximumCount < MinimumCount || MaximumCount > 64 ||
            !FinitePositive(GrowthSeconds) || !FinitePositive(CycleSeconds) ||
            !FinitePositive(FlightSeconds) || double.IsNaN(GrowthJitterSeconds) ||
            double.IsInfinity(GrowthJitterSeconds) || GrowthJitterSeconds < 0 ||
            MinimumFallDelay < 1 || MaximumFallDelay < MinimumFallDelay || MaximumFallDelay == int.MaxValue)
            throw new InvalidOperationException("树果数量或时间配置无效。");
    }

    /// <summary>持续时间必须有限且严格为正。</summary>
    private static bool FinitePositive(double value) => value > 0 && !double.IsInfinity(value);
    #endregion
}

/// <summary>单果持久记录：保存独立生长、到期、飞行和命中状态，不保存场景对象。</summary>
[Serializable]
public sealed class CanopyFruitRecord
{
    #region 持久状态
    public int Id, Slot; // 唯一记录号与冠内位置。
    public double BornAt, MatureAt, FallAt = -1, LandAt; // 独立生长和随机到期时刻。
    public float StartX, StartY, EndX, EndY; // 脱冠时冻结的世界轨迹。
    public bool HitConsumed, Split; // 一次性命中锁与半果结果。
    public int OutputAmount = -1; // 首次结算后冻结数量，重试不重抽倍率。

    /// <summary>读取独立生长进度供表现使用。</summary>
    public float Growth01(double now) => (float)Math.Max(0, Math.Min(1, (now - BornAt) / (MatureAt - BornAt)));
    #endregion
}

/// <summary>树冠存档；飞行记录独立于在冠库存，随机流与事件游标随存档恢复。</summary>
[Serializable]
public sealed class CanopyFruitState
{
    #region 持久状态
    public bool Initialized, Stopped; // 初始化与死亡停止状态。
    public uint RandomState; // 确定性随机流。
    public int NextId; // 不因新批次复用身份。
    public double Time, NextBatchAt; // 已消费时间与下一批时刻。
    public List<CanopyFruitRecord> Fruits = new(); // 在冠果。
    public List<CanopyFruitRecord> Flights = new(); // 已脱冠临时坠落。
    #endregion
}

/// <summary>时间线与世界副作用之间的扩展边界；成功移交后才删除记录。</summary>
public interface ICanopyFruitSink
{
    void BeginFall(CanopyFruitRecord fruit);
    void Land(CanopyFruitRecord fruit);
}

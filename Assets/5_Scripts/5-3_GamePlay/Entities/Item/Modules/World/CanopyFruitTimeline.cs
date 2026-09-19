using System;

/// <summary>事件时间线不依赖帧率；补算沿原随机流逐事件消费，同一时刻再次推进不重复产出。</summary>
public static class CanopyFruitTimeline
{
    #region 初始化与随机
    /// <summary>自然物使用稳定种子，已有存档不重新初始化。</summary>
    public static void Initialize(CanopyFruitState state, double now, uint seed)
    {
        if (state.Initialized) return;
        state.Initialized = true;
        state.RandomState = seed == 0 ? 0x9E3779B9u : seed;
        state.Time = now;
        state.NextBatchAt = now;
    }

    /// <summary>持久化随机流，范围含下界、不含上界。</summary>
    public static int Next(CanopyFruitState state, int minimum, int exclusiveMaximum)
    {
        uint value = state.RandomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        state.RandomState = value;
        return minimum + (int)(value % (uint)(exclusiveMaximum - minimum));
    }
    #endregion

    #region 时间推进
    /// <summary>预算只限制本帧工作量，剩余历史留在游标中，不丢弃到期产物。</summary>
    public static bool Advance(CanopyFruitState state, CanopyFruitSettings settings, double now,
        ICanopyFruitSink sink, int budget = 512, Func<int, int, int> random = null)
    {
        if (!state.Initialized || state.Stopped) return true;
        if (double.IsNaN(now) || double.IsInfinity(now) || now < state.Time)
            throw new InvalidOperationException("树果时钟不能倒退或使用非有限值。");
        for (int iteration = 0; iteration < budget; iteration++)
        {
            double next = state.Fruits.Count == 0 ? state.NextBatchAt : double.PositiveInfinity;
            foreach (CanopyFruitRecord fruit in state.Fruits)
                next = Math.Min(next, fruit.FallAt < 0 ? fruit.MatureAt : fruit.FallAt);
            foreach (CanopyFruitRecord flight in state.Flights) next = Math.Min(next, flight.LandAt);
            if (next > now) { state.Time = now; return true; }
            state.Time = next;
            for (int index = state.Flights.Count - 1; index >= 0; index--)
            {
                CanopyFruitRecord flight = state.Flights[index];
                if (flight.LandAt > next) continue;
                sink.Land(flight);
                state.Flights.RemoveAt(index);
            }
            for (int index = state.Fruits.Count - 1; index >= 0; index--)
            {
                CanopyFruitRecord fruit = state.Fruits[index];
                if (fruit.FallAt < 0 && fruit.MatureAt <= next)
                    fruit.FallAt = fruit.MatureAt + Draw(state, random, settings.MinimumFallDelay, settings.MaximumFallDelay + 1);
                if (fruit.FallAt < 0 || fruit.FallAt > next) continue;
                fruit.LandAt = fruit.FallAt + settings.FlightSeconds;
                sink.BeginFall(fruit);
                state.Flights.Add(fruit);
                state.Fruits.RemoveAt(index);
                if (state.Fruits.Count == 0) state.NextBatchAt = next + settings.CycleSeconds;
            }
            if (state.Fruits.Count == 0 && state.NextBatchAt <= next)
                CreateBatch(state, settings, next, random);
        }
        return false;
    }

    /// <summary>每批果实拥有独立生长时刻和身份，数量不超过配置容量。</summary>
    public static void CreateBatch(CanopyFruitState state, CanopyFruitSettings settings, double now, Func<int, int, int> random)
    {
        int count = Draw(state, random, settings.MinimumCount, settings.MaximumCount + 1);
        for (int slot = 0; slot < count; slot++)
        {
            double jitter = settings.GrowthJitterSeconds * Draw(state, random, 0, 1000000) / 1000000d;
            state.Fruits.Add(new CanopyFruitRecord { Id = ++state.NextId, Slot = slot,
                BornAt = now, MatureAt = now + settings.GrowthSeconds + jitter });
        }
    }

    /// <summary>诊断与 MOD 可注入随机源，越界结果立即拒绝。</summary>
    private static int Draw(CanopyFruitState state, Func<int, int, int> random, int minimum, int maximum)
    {
        int result = random == null ? Next(state, minimum, maximum) : random(minimum, maximum);
        if (result < minimum || result >= maximum) throw new InvalidOperationException("树果随机源超出范围。");
        return result;
    }

    /// <summary>砍伐只结算在当前时刻成熟的在冠果。</summary>
    public static bool CanDropOnDeath(CanopyFruitRecord fruit, double now) => fruit.MatureAt <= now;
    #endregion
}

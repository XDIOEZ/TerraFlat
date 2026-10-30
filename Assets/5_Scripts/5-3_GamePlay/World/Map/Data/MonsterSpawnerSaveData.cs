using System;
using System.Collections.Generic;
using MemoryPack;

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class MonsterSpawnerSaveData
{
    [MemoryPack.MemoryPackOrder(0)] public Dictionary<string, SpawnerProgressSaveData> ConfigStates = new();
    [MemoryPack.MemoryPackOrder(1)] public List<AiecsResidentSaveData> EntitiesResidents = new();
    [MemoryPack.MemoryPackOrder(2)] public SpawnerCountdownProgress NaturalCountdown = new();
}

/// <summary>正式 ECS 居民的持久玩法值；蜂巢成员仍由蜂巢存档持有。</summary>
[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class AiecsResidentSaveData
{
    [MemoryPack.MemoryPackOrder(8)] public int Guid;
    [MemoryPack.MemoryPackOrder(15)] public string SpeciesId;
     [MemoryPack.MemoryPackOrder(16)]     public float X;
     [MemoryPack.MemoryPackOrder(17)]     public float Y;
     [MemoryPack.MemoryPackOrder(11)]     public float Hp;
    [MemoryPack.MemoryPackOrder(5)] public List<float> BodyPartHp = new();
    [MemoryPack.MemoryPackOrder(13)] public float Nutrition = -1f;
    [MemoryPack.MemoryPackOrder(7)] public float FlightStamina = -1f;
    [MemoryPack.MemoryPackOrder(6)] public bool FlightRecovering;
    [MemoryPack.MemoryPackOrder(12)] public double NextBirthTime;
    [MemoryPack.MemoryPackOrder(14)] public bool Orphaned;
     [MemoryPack.MemoryPackOrder(9)]     public float HiveHomeX;
     [MemoryPack.MemoryPackOrder(10)]     public float HiveHomeY;
    [MemoryPack.MemoryPackOrder(2)] public int AdvanceTargetItemGuid;
     [MemoryPack.MemoryPackOrder(3)]     public float AdvanceTargetX;
     [MemoryPack.MemoryPackOrder(4)]     public float AdvanceTargetY;
     [MemoryPack.MemoryPackOrder(0)]     public float AdvanceArrivalDistance;
    [MemoryPack.MemoryPackOrder(1)] public bool AdvanceAttackActorsOnRoute;
}

[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class SpawnerProgressSaveData
{
    #region 生成队列进度

    [MemoryPack.MemoryPackOrder(2)] public int LastSpawnDay = -999;

    [MemoryPack.MemoryPackOrder(0)] public int AvailableBudget = -1;
    [MemoryPack.MemoryPackOrder(1)] public int LastBudgetRecoveryDay = -1;
    [MemoryPack.MemoryPackOrder(4)] public int PendingReplacementCount;

    [MemoryPack.MemoryPackOrder(5)] public int PendingSpawnCount;
    [MemoryPack.MemoryPackOrder(3)] public int LifetimeSpawnCount;

    /// <summary>独立概率落空也消耗一次排队机会，只有真正出生才累计出生数量。</summary>
    public void CompleteSpawnAttempt(bool spawned, int currentDay)
    {
        if (PendingSpawnCount > 0)
        {
            PendingSpawnCount--;
            if (spawned) LifetimeSpawnCount++;
        }
        else
        {
            PendingReplacementCount = Math.Max(0, PendingReplacementCount - 1);
        }
        LastSpawnDay = Math.Max(LastSpawnDay, currentDay);
    }

    #endregion
}

/// <summary>每个世界只保存一个自然刷新时钟，到期抽中的规则、物种和当前批次随世界存档。</summary>
[MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
[Serializable]
public partial class SpawnerCountdownProgress
{
    #region 持续倒计时

    [MemoryPack.MemoryPackOrder(8)] public double RemainingGameSeconds = -1d;
    [MemoryPack.MemoryPackOrder(6)] public double LastObservedGameSeconds = -1d;
    [MemoryPack.MemoryPackOrder(5)] public double IntervalGameSeconds;
    [MemoryPack.MemoryPackOrder(7)] public int PendingCount;
     [MemoryPack.MemoryPackOrder(9)]     public string SelectedConfigId;
     [MemoryPack.MemoryPackOrder(10)]     public string SelectedSpeciesId;
    [MemoryPack.MemoryPackOrder(4)] public bool HasBatchAnchor;
     [MemoryPack.MemoryPackOrder(1)]     public float BatchAnchorX;
     [MemoryPack.MemoryPackOrder(2)]     public float BatchAnchorY;
    [MemoryPack.MemoryPackOrder(0)] public string BatchAnchorScene;
    [MemoryPack.MemoryPackOrder(3)] public List<long> BatchSpawnCells = new();

    // 只推进在线观察到的游戏时间，等待落点时不累计第二轮。
    public bool Advance(double totalGameSeconds, double intervalGameSeconds)
    {
        if (double.IsNaN(totalGameSeconds) || double.IsInfinity(totalGameSeconds) || totalGameSeconds < 0d ||
            double.IsNaN(intervalGameSeconds) || double.IsInfinity(intervalGameSeconds) || intervalGameSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(intervalGameSeconds), "倒计时需要有限的游戏时间和正间隔。");
        if (IntervalGameSeconds > 0d && RemainingGameSeconds > 0d)
            RemainingGameSeconds *= intervalGameSeconds / IntervalGameSeconds;
        IntervalGameSeconds = intervalGameSeconds;
        if (RemainingGameSeconds < 0d) RemainingGameSeconds = intervalGameSeconds;
        double elapsed = LastObservedGameSeconds < 0d ? 0d : Math.Max(0d, totalGameSeconds - LastObservedGameSeconds);
        LastObservedGameSeconds = totalGameSeconds;
        if (PendingCount > 0) return false;
        RemainingGameSeconds = Math.Max(0d, RemainingGameSeconds - elapsed);
        return RemainingGameSeconds <= 0d;
    }

    public void Restart(double intervalGameSeconds)
    {
        if (double.IsNaN(intervalGameSeconds) || double.IsInfinity(intervalGameSeconds) || intervalGameSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(intervalGameSeconds));
        PendingCount = 0;
        SelectedConfigId = SelectedSpeciesId = null;
        IntervalGameSeconds = intervalGameSeconds;
        RemainingGameSeconds = intervalGameSeconds;
        ClearBatchAnchor();
    }

    // 同批出生只抽一次数量并保留落点，不能因逐帧创建重新抽成散落的单只。
    public void BeginBatch(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        PendingCount = count;
        RemainingGameSeconds = 0d;
        ClearBatchAnchor();
    }

    public void ClearBatchAnchor()
    {
        HasBatchAnchor = false;
        BatchAnchorX = BatchAnchorY = 0f;
        BatchAnchorScene = null;
        BatchSpawnCells ??= new();
        BatchSpawnCells.Clear();
    }

    public void RecordBatchBirth(float x, float y, string sceneName)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
            string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("批次出生锚点需要有效场景和有限坐标。");
        if (!HasBatchAnchor)
        {
            HasBatchAnchor = true;
            BatchAnchorX = x;
            BatchAnchorY = y;
            BatchAnchorScene = sceneName;
        }
        BatchSpawnCells ??= new();
        BatchSpawnCells.Add(GetBatchSpawnCell(x, y));
    }

    public static long GetBatchSpawnCell(float x, float y) =>
        ((long)(int)Math.Floor(x) << 32) | (uint)(int)Math.Floor(y);

    public void CompleteAttempt(double intervalGameSeconds)
    {
        PendingCount = Math.Max(0, PendingCount - 1);
        if (PendingCount == 0) Restart(intervalGameSeconds);
    }

    #endregion
}

using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>
/// 层数状态与真实水体暴露时钟。潮湿默认每秒增加一层，上限由真实地形水深与 JSON 共同决定；
/// 入水不会立即获得一层，连续跨水格不重置时钟，浅水也不会抹掉已经获得的高层潮湿。
/// </summary>
public partial class BuffManager
{
    #region 层数查询与水火关系

    private double waterStackElapsed; // 每个角色独立计时，不能放在共享 Tile_Water 上。
    private bool waterStackExposure;
    private int waterStackExitFrame = -1;

    public int GetBuffStacks(string buffId)
    {
        return TryGetBuff(buffId, out BuffInstance runtime) && runtime != null && !runtime.IsExpired
            ? runtime.StackCount : 0;
    }

    /// <summary>计算完整候选层数后再比较潮湿，避免多层攻击被拆成多次一层攻击。</summary>
    private int ResolveIncomingStacks(BuffDefinition definition, int stacks)
    {
        int existing = definition.StackMode == BuffStackMode.AddStacks ? GetBuffStacks(definition.Id) : 0;
        return (int)Math.Min(definition.MaxStacks, (long)existing + stacks);
    }

    /// <summary>同层水胜；火更强时允许点燃，成功应用之后才蒸发旧潮湿。</summary>
    public bool CanApplyStackedBuff(string buffId, int incomingStacks)
    {
        return !string.Equals(buffId, BurningBuffIds.Burning, StringComparison.OrdinalIgnoreCase) ||
               GetBuffStacks(WetBuffIds.Wet) < incomingStacks;
    }

    /// <summary>每 1/10 真实水深为一档；漂浮视觉深度不能替代地形深度。</summary>
    public static int ResolveWaterStackLimit(float depth, BuffDefinition definition)
    {
        if (definition == null || float.IsNaN(depth) || float.IsInfinity(depth) || depth <= 0f)
            return 0;
        int level = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(depth) * 10f - 0.00001f), 1, 10);
        return Mathf.Min(definition.MaxStacks, level * definition.WaterStacksPerDepthLevel);
    }

    #endregion

    #region 入水计时

    /// <summary>真实入/出水通知；同一帧跨越相邻水格保留尚未满一秒的进度。</summary>
    public void SetWaterStackExposure(bool exposed)
    {
        if (exposed && !waterStackExposure && waterStackExitFrame != Time.frameCount)
            waterStackElapsed = 0d;
        if (!exposed)
            waterStackExitFrame = Time.frameCount;
        waterStackExposure = exposed;
    }

    /// <summary>按模拟时间累计潮湿；弱潮湿可继续积累，达到当前火层数时才熄灭，避免永远无法入水灭火。</summary>
    public void AdvanceWaterWetness(float terrainDepth, float deltaTime)
    {
        if (!waterStackExposure || !GameNetwork.HasStateAuthority || deltaTime <= 0f ||
            float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;
        BuffDefinition definition = GameRes.Instance?.GetBuffDefinition(WetBuffIds.Wet);
        int limit = ResolveWaterStackLimit(terrainDepth, definition);
        if (limit <= 0 || definition.WaterStackIntervalSeconds <= 0f)
            return;

        waterStackElapsed += deltaTime;
        double interval = definition.WaterStackIntervalSeconds;
        double ticks = Math.Floor((waterStackElapsed + 0.0000001d) / interval);
        if (ticks < 1d)
            return;
        waterStackElapsed = Math.Max(0d, waterStackElapsed - ticks * interval);
        int current = GetBuffStacks(WetBuffIds.Wet);
        int added = (int)Math.Min(Math.Max(0, limit - current), ticks);
        if (added > 0)
            AddBuff(WetBuffIds.Wet, added);
        else if (TryGetBuff(WetBuffIds.Wet, out BuffInstance runtime) && runtime.RefreshDuration())
            BuffDurationChanged?.Invoke(runtime);
    }

    private void ResetWaterStackClock()
    {
        waterStackElapsed = 0d;
        waterStackExposure = false;
        waterStackExitFrame = -1;
    }

    #endregion
}

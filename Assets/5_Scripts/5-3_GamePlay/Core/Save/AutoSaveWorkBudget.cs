using System.Collections;
using UnityEngine;

/// <summary>自动保存各采集阶段共用的主线程分帧预算。</summary>
public sealed class AutoSaveWorkBudget
{
    #region 自动保存分帧预算

    public const double SliceSeconds = 0.0005d;
    public const int MaxWorkUnits = 16;
    public const int YieldFrames = 2;

    private double sliceStartTime;
    private int workUnits;

    public AutoSaveWorkBudget() : this(Time.realtimeSinceStartupAsDouble)
    {
    }

    public AutoSaveWorkBudget(double startTime)
    {
        Restart(startTime);
    }

    public bool ShouldYield() => ShouldYield(Time.realtimeSinceStartupAsDouble);

    public bool ShouldYield(double now)
    {
        workUnits++;
        return workUnits >= MaxWorkUnits || now - sliceStartTime >= SliceSeconds;
    }

    public void Restart() => Restart(Time.realtimeSinceStartupAsDouble);

    public void Restart(double now)
    {
        sliceStartTime = now;
        workUnits = 0;
    }

    public IEnumerator WaitForNextSlice()
    {
        // 让出两帧后再开始计时，避免把正常玩法耗时算进下一片保存预算。
        for (int i = 0; i < YieldFrames; i++)
            yield return null;
        Restart();
    }

    #endregion
}

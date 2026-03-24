using MemoryPack;
using UnityEngine;

[MemoryPackable]
[System.Serializable]
public partial class TimeData
{
    [Tooltip("当前时间点（单位/秒）")]
    public float CurrentTime = 0f;

    [Tooltip("一天时长（单位/秒）")]
    public float DayLength = 1440f;

    [Tooltip("光照参数曲线（时间比例到光照强度）")]
    [MemoryPackIgnore]
    public AnimationCurve LightParams = new AnimationCurve(
        new Keyframe(0f, 0.0f),
        new Keyframe(0.25f, 1.0f),
        new Keyframe(0.5f, 1.0f),
        new Keyframe(0.75f, 1.0f),
        new Keyframe(1f, 0.0f)
    );

    [Tooltip("昼夜颜色梯度（存档安全）")]
    [MemoryPackIgnore]
    public Gradient dayNightGradient = new Gradient()
    {
        colorKeys = new[]
                {
                new GradientColorKey(new Color32(30,40,90,255), 0.00f),
                new GradientColorKey(new Color32(70,50,100,255), 0.25f),
                new GradientColorKey(new Color32(255,245,230,255), 0.50f),
                new GradientColorKey(new Color32(255,150,80,255), 0.75f),
                new GradientColorKey(new Color32(30,40,90,255), 1.00f)
                },
        alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
    };

    [Tooltip("时间倍率（支持外部事件影响）")]
    public float TimeScaleModifier = 1f;

    [Tooltip("引用场景名（如果需要引用其他场景的时间/光照）")]
    public string ReferenceScene = "";
    
    [Tooltip("总游戏天数（记录游玩了多少天）")]
    public int TotalDays = 0;
    
    public TimeData() { }
    
    /// <summary>
    /// 获取当前天数（基于当前时间计算）
    /// </summary>
    public int GetCurrentDay()
    {
        return Mathf.FloorToInt(CurrentTime / DayLength) + TotalDays;
    }

    /// <summary>
    /// 获取总游戏时间（单位：秒）
    /// = 总天数 * 一天时长 + 当前天内经过时间
    /// </summary>
    public float GetTotalGameTime()
    {
        float currentTimeInDay = CurrentTime % DayLength;
        if (currentTimeInDay < 0f)
            currentTimeInDay += DayLength;

        return Mathf.Max(0, TotalDays) * DayLength + currentTimeInDay;
    }
}
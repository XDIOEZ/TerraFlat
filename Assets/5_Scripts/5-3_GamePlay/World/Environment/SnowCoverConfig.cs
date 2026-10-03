using UnityEngine;

/// <summary>季节积雪平衡配置；默认 0℃以下结雪，强降雪约 5.5 分钟积满，20℃约 7 分钟融净。</summary>
[CreateAssetMenu(menuName = "FlatWorld/Environment/Snow Cover")]
public sealed class SnowCoverConfig : ScriptableObject
{
    public float FreezingTemperature = 0f; // 降水结雪阈值。
    [Min(0f)] public float AccumulationPerSecond = 0.003f; // 满强度降雪速率。
    [Min(0f)] public float MeltPerDegreeSecond = 0.00012f; // 每度融化速度。
    [Range(0.01f, 1f)] public float FullDepthMoveSpeedMultiplier = 0.9f; // 十层积雪时的移速倍率。
    [Min(0.1f)] public float FootprintLifetime = 60f; // 踩雪后脚印完整保留时间。
}

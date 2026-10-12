using UnityEngine;

/// <summary>温度避险移动能力；温度模块提交方向引导目标，不接管具体移动实现。</summary>
public interface ITemperatureSafetyMovement
{
    int TemperatureSafetyMovementPriority { get; }
    bool ShouldAdvanceTemperatureSafetyDestination { get; } // 到达或不可达时允许选择下一段。
    void SetTemperatureSafetyDestination(Vector2 destination);
    void ClearTemperatureSafetyDestination();
}

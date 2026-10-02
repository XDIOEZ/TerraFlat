using UnityEngine;

/// <summary>温度避险移动能力；温度模块只提交高优先级安全目标，不接管具体移动实现。</summary>
public interface ITemperatureSafetyMovement
{
    int TemperatureSafetyMovementPriority { get; }
    bool ShouldAdvanceTemperatureSafetyDestination { get; }
    void SetTemperatureSafetyDestination(Vector2 destination);
    void ClearTemperatureSafetyDestination();
}

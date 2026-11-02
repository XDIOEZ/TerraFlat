using UnityEngine;

/// <summary>独立游戏时钟的唯一驱动，Unity 2D 原生物理由模拟阶段显式推进。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class GameplayFixedStepSimulationDriver : MonoBehaviour
{
    #region 固定步宿主生命周期
    private SimulationMode2D previousPhysicsSimulationMode;

    public static void EnsureForOwner(GameManager owner)
    {
        if (!owner.TryGetComponent<GameplayFixedStepSimulationDriver>(out _))
            owner.gameObject.AddComponent<GameplayFixedStepSimulationDriver>();
    }

    private void OnEnable()
    {
        previousPhysicsSimulationMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
    }

    private void OnDisable() => Physics2D.simulationMode = previousPhysicsSimulationMode;

    private void Update()
    {
        if (GameManager.ExistingInstance == null || GameManager.ExistingInstance.gameObject != gameObject) return;
        GameplayTickRateUserSettings.RefreshWhenNetworkStateChanges();
        GameplayFixedStepSimulation.SimulateFrame(Time.unscaledDeltaTime, Time.timeScale,
            GameplayTickRateUserSettings.EffectiveTicksPerSecond);
    }

    #endregion
}

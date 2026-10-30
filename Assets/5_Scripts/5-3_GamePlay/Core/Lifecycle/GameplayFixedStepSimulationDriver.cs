using UnityEngine;

/// <summary>游戏世界的唯一固定步驱动，不接管 Unity 的时间累积与补步。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class GameplayFixedStepSimulationDriver : MonoBehaviour
{
    #region 固定步宿主生命周期

    public static void EnsureForOwner(GameManager owner)
    {
        if (!owner.TryGetComponent<GameplayFixedStepSimulationDriver>(out _))
            owner.gameObject.AddComponent<GameplayFixedStepSimulationDriver>();
    }

    private void FixedUpdate()
    {
        if (GameManager.ExistingInstance == null || GameManager.ExistingInstance.gameObject != gameObject) return;
        GameplayFixedStepSimulation.SimulateFixedStep(Time.fixedDeltaTime);
    }

    #endregion
}

using UnityEngine;

/// <summary>把船体物理接触交给载具模块读取推动意图。</summary>
[DisallowMultipleComponent]
public sealed class CarrierPhysicsContact2D : MonoBehaviour, IGameplayFixedStepSimulationSystem
{
    #region 接触转发

    private Mod_Carrier carrier;

    public void Bind(Mod_Carrier target) => carrier = target;

    private void OnEnable() => GameplayFixedStepSimulation.Register(this, GameplayFixedStepSimulationPhase.AfterPhysics);
    private void OnDisable() => GameplayFixedStepSimulation.Unregister(this);
    public void SimulateFixedStep(float deltaTime) => carrier?.CommitCompletedPhysicsStep(deltaTime);

    private void OnCollisionEnter2D(Collision2D collision) => carrier?.HandlePhysicalContact(collision);
    private void OnCollisionStay2D(Collision2D collision) => carrier?.HandlePhysicalContact(collision);

    #endregion
}

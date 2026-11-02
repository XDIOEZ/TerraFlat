using UnityEngine;

/// <summary>独立物理宿主只转交原生接触事实，不持有伤害或物品业务规则。</summary>
internal sealed class WorldPhysicsItemContactRelay2D : MonoBehaviour
{
    #region 接触事实转发
    private IWorldPhysicsCollisionReceiver2D receiver;
    internal void Bind(IWorldPhysicsCollisionReceiver2D target) => receiver = target;
    internal void ClearBinding() => receiver = null;
    private void OnCollisionEnter2D(Collision2D collision)
        => receiver?.ReceivePhysicsCollision(collision, WorldPhysicsTriggerPhase2D.Enter);
    private void OnCollisionStay2D(Collision2D collision)
        => receiver?.ReceivePhysicsCollision(collision, WorldPhysicsTriggerPhase2D.Stay);
    #endregion
}

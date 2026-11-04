using System.Collections.Generic;
using UnityEngine;

/// <summary>独立物理宿主只转交原生接触事实，不持有伤害或物品业务规则。</summary>
internal sealed class WorldPhysicsItemContactRelay2D : MonoBehaviour
{
    #region 接触事实转发
    private readonly List<IWorldPhysicsCollisionReceiver2D> receivers = new();
    internal void Bind(Item item)
    {
        receivers.Clear();
        if (item.GetComponent<ItemPhysicsProjection2D>() is { } projection) receivers.Add(projection);
        foreach (Module module in item.Mods.Values)
            if (module is IWorldPhysicsCollisionReceiver2D receiver) receivers.Add(receiver);
    }
    internal void ClearBinding() => receivers.Clear();
    private void OnCollisionEnter2D(Collision2D collision)
        => ForwardCollision(collision, WorldPhysicsTriggerPhase2D.Enter);
    private void OnCollisionStay2D(Collision2D collision)
        => ForwardCollision(collision, WorldPhysicsTriggerPhase2D.Stay);
    private void ForwardCollision(Collision2D collision, WorldPhysicsTriggerPhase2D phase)
    {
        foreach (IWorldPhysicsCollisionReceiver2D receiver in receivers)
            if (receiver is not Module module || module.IsRuntimeLoaded) receiver.ReceivePhysicsCollision(collision, phase);
    }
    #endregion
}

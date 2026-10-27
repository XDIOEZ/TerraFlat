using UnityEngine;

public enum WorldPhysicsTriggerPhase2D : byte { Enter, Stay, Exit }

public interface IWorldPhysicsEntityRegionTriggerReceiver2D
{
    void ReceivePhysicsRegionTrigger(string regionId, string receiverModule, WorldPhysicsPurpose2D purpose,
        Collider2D other, WorldPhysicsTriggerPhase2D phase);
}

/// <summary>把原生区域触发回传给原实体能力，回池后清空所有身份与接收者。</summary>
[DisallowMultipleComponent]
public sealed class WorldPhysicsRegionTriggerRelay2D : MonoBehaviour
{
    #region 原生触发与实体接收
    private IWorldPhysicsEntityRegionTriggerReceiver2D receiver;
    private string regionId, receiverModule;
    private WorldPhysicsPurpose2D purpose;

    internal void Bind(IWorldPhysicsEntityRegionTriggerReceiver2D target, string id,
        string module, WorldPhysicsPurpose2D regionPurpose)
    { receiver = target; regionId = id; receiverModule = module; purpose = regionPurpose; }

    internal void ClearBinding()
    { receiver = null; regionId = receiverModule = null; purpose = 0; }

    private void OnTriggerEnter2D(Collider2D other) => Forward(other, WorldPhysicsTriggerPhase2D.Enter);
    private void OnTriggerStay2D(Collider2D other) => Forward(other, WorldPhysicsTriggerPhase2D.Stay);
    private void OnTriggerExit2D(Collider2D other) => Forward(other, WorldPhysicsTriggerPhase2D.Exit);
    private void Forward(Collider2D other, WorldPhysicsTriggerPhase2D phase)
        => receiver?.ReceivePhysicsRegionTrigger(regionId, receiverModule, purpose, other, phase);
    #endregion
}

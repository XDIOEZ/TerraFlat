using UnityEngine;

public partial class Mod_DamageReceiver : IItemPhysicsDamageReceiver2D
{
    #region 受击区域物理声明
    WorldPhysicsPurpose2D IItemPhysicsColliderOwner2D.PhysicsColliderPurpose
        => WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Trigger;
    bool IItemPhysicsColliderOwner2D.RequiresInitialPhysicsAnimationRebind => false;

    public void ConfigureOwnedPhysicsCollider(Collider2D collider)
    {
        // 只配置明确领取的受击框，不批量修改同节点的其它碰撞职责。
        CombatPhysicsChannels.AssignDamageReceiverCollider(collider);
    }
    #endregion
}

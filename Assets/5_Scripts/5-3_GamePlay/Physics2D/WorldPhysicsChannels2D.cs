using UnityEngine;

/// <summary>共享碰撞通道名称，物理代理只依赖通道而不依赖战斗实现。</summary>
public static class WorldPhysicsChannels2D
{
    #region 物理通道配置
    public const string DamageReceiverLayerName = "DamageReciver";
    public const string DamageSenderLayerName = "DamageSender";
    public static int DamageReceiverLayer => LayerMask.NameToLayer(DamageReceiverLayerName);
    public static int DamageSenderLayer => LayerMask.NameToLayer(DamageSenderLayerName);

    public static bool IsDamageReceiverCollider(Collider2D collider)
        => collider != null && collider.gameObject.layer == DamageReceiverLayer;
    #endregion
}

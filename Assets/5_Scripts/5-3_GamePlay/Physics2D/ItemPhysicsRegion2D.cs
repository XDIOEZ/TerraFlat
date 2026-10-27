using UnityEngine;

/// <summary>保留普通物品每个碰撞区域的用途、稳定身份和明确接收者。</summary>
[DisallowMultipleComponent]
public sealed class ItemPhysicsRegion2D : MonoBehaviour
{
    #region 区域归属
    public string RegionId { get; private set; }
    public WorldPhysicsPurpose2D Purpose { get; private set; }
    public Component Receiver { get; private set; }
    public bool ImmutableWorldFixedBlocking { get; private set; }
    private Collider2D shape;

    public void BindRegion(string regionId, WorldPhysicsPurpose2D purpose, Component receiver,
        bool immutableWorldFixedBlocking = false)
    {
        RegionId = regionId;
        Purpose = purpose;
        Receiver = receiver;
        ImmutableWorldFixedBlocking = immutableWorldFixedBlocking;
    }

    internal bool ReplacesCollider(Collider2D collider)
    {
        if (collider == null || collider.GetComponent<ItemPhysicsRegion2D>() != null) return false;
        if ((Purpose & WorldPhysicsPurpose2D.Blocking) != 0 && !collider.isTrigger) return true;
        if ((Purpose & WorldPhysicsPurpose2D.Damage) != 0 && Receiver is Mod_DamageReceiver &&
            collider.gameObject == Receiver.gameObject) return true;
        if ((Purpose & WorldPhysicsPurpose2D.Trigger) != 0 && Receiver is Module &&
            collider.isTrigger && collider.gameObject == Receiver.gameObject) return true;
        return (Purpose & WorldPhysicsPurpose2D.Interaction) != 0 && collider.isTrigger &&
               collider.GetComponent<Module>() == null;
    }
    #endregion

    #region 物理消息转发
    private void OnTriggerEnter2D(Collider2D other) => ForwardContact(nameof(OnTriggerEnter2D), other);
    private void OnTriggerStay2D(Collider2D other) => ForwardContact(nameof(OnTriggerStay2D), other);
    private void OnTriggerExit2D(Collider2D other) => ForwardContact(nameof(OnTriggerExit2D), other);
    private void OnCollisionEnter2D(Collision2D collision) => ForwardContact(nameof(OnCollisionEnter2D), collision);
    private void OnCollisionStay2D(Collision2D collision) => ForwardContact(nameof(OnCollisionStay2D), collision);
    private void OnCollisionExit2D(Collision2D collision) => ForwardContact(nameof(OnCollisionExit2D), collision);

    private void ForwardContact(string message, object contact)
    {
        if (ImmutableWorldFixedBlocking || Receiver == null || Receiver.gameObject == gameObject ||
            Receiver is Module module && !module.IsRuntimeLoaded) return;
        if (shape == null) shape = GetComponent<Collider2D>();
        if (shape != null && shape.attachedRigidbody != null &&
            shape.attachedRigidbody.gameObject == Receiver.gameObject) return;
        Receiver.gameObject.SendMessage(message, contact, SendMessageOptions.DontRequireReceiver);
    }
    #endregion
}

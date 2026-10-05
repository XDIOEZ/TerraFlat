using FlatWorld.Networking;
using UnityEngine;

/// <summary>把实体质量交给 Physics2D，并只回写允许的运行时物理结果。</summary>
[DisallowMultipleComponent]
public sealed class ItemPhysicsProjection2D : MonoBehaviour
{
    #region 数据与刚体同步

    private Item item;
    private Rigidbody2D body;
    private Vector2Int lastCell;
    private bool hasLastCell;

    /// <summary>移动模块统一补齐动态刚体与实体外形，原有攻击 Trigger 独立保留。</summary>
    public static Rigidbody2D EnsureMovable(Item target)
    {
        if (target == null) return null;
        Rigidbody2D rigidbody = target.GetComponent<Rigidbody2D>();
        if (rigidbody == null)
            rigidbody = target.gameObject.AddComponent<Rigidbody2D>();
        rigidbody.bodyType = RigidbodyType2D.Dynamic;
        rigidbody.gravityScale = 0f;
        rigidbody.constraints |= RigidbodyConstraints2D.FreezeRotation;
        rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        bool hasSolid = false;
        BoxCollider2D triggerShape = null;
        Collider2D[] rootColliders = target.GetComponents<Collider2D>();
        for (int i = 0; i < rootColliders.Length; i++)
        {
            Collider2D collider = rootColliders[i];
            if (collider.enabled && !collider.isTrigger) hasSolid = true;
            if (triggerShape == null && collider is BoxCollider2D box && box.enabled && box.isTrigger)
                triggerShape = box;
        }
        if (!hasSolid && triggerShape != null)
        {
            BoxCollider2D physical = target.gameObject.AddComponent<BoxCollider2D>();
            physical.offset = triggerShape.offset;
            physical.size = triggerShape.size;
            physical.isTrigger = false;
        }
        Ensure(target);
        return rigidbody;
    }

    public static void Ensure(Item target)
    {
        if (target == null || target is Map) return;
        Rigidbody2D rigidbody = target.GetComponent<Rigidbody2D>();
        ItemPhysicsProjection2D projection = target.GetComponent<ItemPhysicsProjection2D>();
        if (rigidbody == null || rigidbody.bodyType == RigidbodyType2D.Static)
        {
            projection?.Suspend();
            return;
        }
        if (projection == null)
            projection = target.gameObject.AddComponent<ItemPhysicsProjection2D>();
        projection.item = target;
        projection.body = rigidbody;
        projection.hasLastCell = false;
        projection.enabled = true;
        projection.ApplyMass();
    }

    private void Awake()
    {
        item = GetComponent<Item>();
        body = GetComponent<Rigidbody2D>();
    }

    internal void Suspend()
    {
        hasLastCell = false;
        enabled = false;
    }

    private void LateUpdate()
    {
        if (!CanWriteBack()) return;
        ApplyMass();
        ItemData data = item.itemData;
        data.PhysicsState ??= new ItemPhysicsRuntimeState();
        data.PhysicsState.Velocity = body.velocity;
        data.PhysicsState.AngularVelocity = body.angularVelocity;
        Vector2 logical = WorldTopologyRuntime.NormalizePosition(body.position);
        if (data.transform != null)
        {
            Vector3 previous = data.transform.position;
            data.transform.position = new Vector3(logical.x, logical.y, previous.z);
            data.transform.rotation = transform.rotation;
        }
        Vector2Int cell = new(Mathf.FloorToInt(logical.x), Mathf.FloorToInt(logical.y));
        if (hasLastCell && cell != lastCell)
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        lastCell = cell;
        hasLastCell = true;
    }

    private bool CanWriteBack() =>
        isActiveAndEnabled && item != null && body != null && body.simulated &&
        body.bodyType == RigidbodyType2D.Dynamic && item.itemData != null &&
        !item.itemData.inHand && GameNetwork.HasStateAuthority &&
        ItemMgr.Instance != null && ItemMgr.Instance.GetItemByGuid(item.itemData.Guid) == item;

    private void ApplyMass()
    {
        if (body == null || body.bodyType != RigidbodyType2D.Dynamic) return;
        ItemStack stack = item?.itemData?.Stack;
        if (stack == null || !float.IsFinite(stack.CurrentWeight) || stack.CurrentWeight <= 0f) return;
        float mass = Mathf.Max(0.01f, stack.CurrentWeight);
        if (!Mathf.Approximately(body.mass, mass)) body.mass = mass;
    }

    #endregion

    #region 接触事实

    private void OnCollisionEnter2D(Collision2D collision) => RecordContact(collision);
    private void OnCollisionStay2D(Collision2D collision) => RecordContact(collision);

    private void RecordContact(Collision2D collision)
    {
        if (!CanWriteBack() || collision.contactCount == 0) return;
        ContactPoint2D contact = collision.GetContact(0);
        Item other = collision.collider != null ? collision.collider.GetComponentInParent<Item>() : null;
        ItemPhysicsRuntimeState state = item.itemData.PhysicsState ??= new ItemPhysicsRuntimeState();
        // 接触事件只记录事实，目标资格与伤害仍由战斗数据逻辑裁定。
        state.LastContactPoint = contact.point;
        state.LastContactNormal = contact.normal;
        state.LastContactItemGuid = other?.itemData?.Guid ?? 0;
        state.ContactVersion++;
    }

    #endregion
}

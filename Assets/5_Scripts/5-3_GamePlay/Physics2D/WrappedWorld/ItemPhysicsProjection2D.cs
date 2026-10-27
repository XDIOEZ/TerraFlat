using FlatWorld.Networking;
using System.Collections.Generic;
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
    private float lastRotationDegrees;
    private readonly List<IItemWorldPhysicsPoseReceiver2D> poseReceivers = new();

    /// <summary>移动模块统一补齐动态刚体与实体外形，原有攻击 Trigger 独立保留。</summary>
    public static Rigidbody2D EnsureMovable(Item target)
    {
        if (target == null) return null;
        WorldPhysicsService2D physics = GameplayWorldRegistry.TryGetPhysics(GameplayWorldRegistry.GetItemWorldKey(target), out var owner)
            ? owner : GameplayWorldRegistry.CurrentPhysics;
        return physics.EnsureMovableItemBody(target);
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
        projection.poseReceivers.Clear();
        foreach (Module module in target.Mods.Values)
            if (module is IItemWorldPhysicsPoseReceiver2D receiver) projection.poseReceivers.Add(receiver);
    }

    private void Awake()
    {
        item = GetComponent<Item>();
        body = GetComponent<Rigidbody2D>();
    }

    private void OnDestroy() => WorldPhysicsService2D.ReleaseItem(item);

    internal void Suspend()
    {
        hasLastCell = false;
        enabled = false;
    }

    private void LateUpdate()
    {
        if (!CanWriteBack()) return;
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        if (GameplayWorldRegistry.TryGetPhysics(worldKey, out var owner) && owner.TryGetItemProxy(item, out _)) return;
        ApplyMass();
        ApplyWorldPhysicsPose(new WorldPhysicsEntityPose2D(body.position, body.rotation, body.velocity, body.angularVelocity));
    }

    internal void ApplyWorldPhysicsPose(WorldPhysicsEntityPose2D pose)
    {
        if (!CanWriteBack()) return;
        ItemData data = item.itemData;
        data.PhysicsState ??= new ItemPhysicsRuntimeState();
        data.PhysicsState.Velocity = pose.Velocity;
        data.PhysicsState.AngularVelocity = pose.AngularVelocity;
        Vector2 logical = WorldLocalPresentation.ToLogical(pose.Position);
        logical = WorldTopologyRuntime.NormalizePosition(logical);
        if (data.transform != null)
        {
            Vector3 previous = data.transform.position;
            data.transform.position = new Vector3(logical.x, logical.y, previous.z);
            data.transform.rotation = Quaternion.Euler(0f, 0f, pose.RotationDegrees);
        }
        Vector2Int cell = new(Mathf.FloorToInt(logical.x), Mathf.FloorToInt(logical.y));
        bool addressChanged = !hasLastCell || cell != lastCell;
        bool orientationChanged = !hasLastCell || !Mathf.Approximately(lastRotationDegrees, pose.RotationDegrees);
        for (int i = 0; i < poseReceivers.Count; i++) poseReceivers[i].OnWorldPhysicsPoseCommitted(item, pose);
        if (addressChanged || orientationChanged)
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        lastCell = cell;
        lastRotationDegrees = pose.RotationDegrees;
        hasLastCell = true;
        if (addressChanged) ItemMgr.Instance?.TryParkIndependentPhysicsWorldResidentOutsidePresentedChunks(item);
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
        Item other = GameplayPhysics2D.ResolveComponent<Item>(collision.collider);
        ItemPhysicsRuntimeState state = item.itemData.PhysicsState ??= new ItemPhysicsRuntimeState();
        // 接触事件只记录事实，目标资格与伤害仍由战斗数据逻辑裁定。
        state.LastContactPoint = contact.point;
        state.LastContactNormal = contact.normal;
        state.LastContactItemGuid = other?.itemData?.Guid ?? 0;
        state.ContactVersion++;
    }

    #endregion
}

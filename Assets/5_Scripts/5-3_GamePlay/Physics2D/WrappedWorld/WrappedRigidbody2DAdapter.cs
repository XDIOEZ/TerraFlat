using FlatWorld.Networking;
using UnityEngine;

/// <summary>GameObject 物理适配：把权威非玩家刚体应用到标准坐标，保留速度、角速度、索引和归属通知。</summary>
[DisallowMultipleComponent]
public sealed class WrappedRigidbody2DAdapter : MonoBehaviour
{
    private Item item;
    private Rigidbody2D body;

    public static void Ensure(Item target)
    {
        if (target == null || target is Player || target is Map)
            return;
        WrappedRigidbody2DAdapter topologyBody = target.GetComponent<WrappedRigidbody2DAdapter>();
        Rigidbody2D rigidbody = target.GetComponent<Rigidbody2D>();
        if (rigidbody == null || rigidbody.bodyType == RigidbodyType2D.Static)
        {
            topologyBody?.Suspend();
            return;
        }

        if (topologyBody == null)
            topologyBody = target.gameObject.AddComponent<WrappedRigidbody2DAdapter>();
        topologyBody.Bind(target, rigidbody);
    }

    private void Awake()
    {
        item = GetComponent<Item>();
        body = GetComponent<Rigidbody2D>();
    }

    private void Bind(Item target, Rigidbody2D rigidbody)
    {
        item = target;
        body = rigidbody;
        enabled = true;
    }

    internal void Suspend() => enabled = false;

    private void FixedUpdate()
    {
        TryWrapNow();
    }

    public bool TryWrapNow()
    {
        if (!WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
            return false;

        if (!isActiveAndEnabled || item == null || body == null ||
            body.bodyType == RigidbodyType2D.Static || item.itemData == null ||
            item.itemData.inHand || !GameNetwork.HasStateAuthority ||
            ItemMgr.Instance == null || ItemMgr.Instance.GetItemByGuid(item.itemData.Guid) != item)
        {
            return false;
        }

        Vector2 previous = body.position;
        if (bounds.Contains(previous) || !IsFinite(previous))
            return false;

        Vector2 velocity = body.velocity;
        float angularVelocity = body.angularVelocity;
        Vector2 normalized = bounds.NormalizePosition(previous);
        float z = transform.position.z;
        body.position = normalized;
        body.velocity = velocity;
        body.angularVelocity = angularVelocity;
        transform.position = new Vector3(normalized.x, normalized.y, z);
        if (item.itemData.transform != null)
            item.itemData.transform.position = transform.position;

        ItemMgr.Instance.NotifyRuntimeItemMoved(item);
        if (!ItemMgr.Instance.IsRuntimeAiEntity(item))
            ChunkMgr.Instance?.UpdateItem_ChunkOwner(item);
        WorldTopologyRuntime.NotifyPositionWrapped(previous, normalized);
        return true;
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}

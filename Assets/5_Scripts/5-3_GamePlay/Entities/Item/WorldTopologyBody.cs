using FlatWorld.Networking;
using UnityEngine;

/// <summary>Wraps authoritative non-player Rigidbody2D items into canonical space.</summary>
[DisallowMultipleComponent]
public sealed class WorldTopologyBody : MonoBehaviour
{
    private Item item;
    private Rigidbody2D body;

    public static void Ensure(Item target)
    {
        if (target == null || target is Player || target is Map)
            return;
        Rigidbody2D rigidbody = target.GetComponent<Rigidbody2D>();
        if (rigidbody == null || rigidbody.bodyType == RigidbodyType2D.Static)
            return;

        WorldTopologyBody topologyBody = target.GetComponent<WorldTopologyBody>();
        if (topologyBody == null)
            topologyBody = target.gameObject.AddComponent<WorldTopologyBody>();
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
    }

    private void FixedUpdate()
    {
        TryWrapNow();
    }

    public bool TryWrapNow()
    {
        if (!WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
            return false;

        if (item == null || body == null || item.itemData == null ||
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

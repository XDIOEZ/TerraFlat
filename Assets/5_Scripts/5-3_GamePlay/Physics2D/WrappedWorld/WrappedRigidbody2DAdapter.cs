using FlatWorld.Networking;
using UnityEngine;

/// <summary>GameObject 物理适配：刚体留在本机连续平面，只把权威数据写回规范逻辑坐标。</summary>
[DisallowMultipleComponent]
public sealed class WrappedRigidbody2DAdapter : MonoBehaviour
{
    private Item item;
    private Rigidbody2D body;
    private bool hasPresentationImage;
    private Vector2Int presentationImage;

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
        hasPresentationImage = false;
        enabled = true;
    }

    internal void Suspend()
    {
        hasPresentationImage = false;
        enabled = false;
    }

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

        Vector2 current = body.position;
        if (!IsFinite(current))
            return false;

        Vector2 logical = bounds.NormalizePosition(current);
        if (item.itemData.transform != null)
            item.itemData.transform.position = new Vector3(logical.x, logical.y, transform.position.z);

        Vector2Int currentImage = ResolvePresentationImage(bounds, current);
        if (!hasPresentationImage)
        {
            presentationImage = currentImage;
            hasPresentationImage = true;
            return false;
        }

        if (currentImage == presentationImage)
            return false;

        presentationImage = currentImage;

        ItemMgr.Instance.NotifyRuntimeItemMoved(item);
        if (!ItemMgr.Instance.IsRuntimeAiEntity(item))
            ChunkMgr.Instance?.UpdateItem_ChunkOwner(item);
        return true;
    }

    private static Vector2Int ResolvePresentationImage(WorldTopologyBounds bounds, Vector2 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt((position.x - bounds.Min.x) / bounds.Span.x),
            Mathf.FloorToInt((position.y - bounds.Min.y) / bounds.Span.y));
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}

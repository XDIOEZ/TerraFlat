using UnityEngine;

/// <summary>
/// 本地玩家的局部平面适配：Rigidbody/Transform 保持连续，只把逻辑坐标规范化到环世界域。
/// 保留 Prefab 脚本身份；不是数学核心，不能复制到 Jobs。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player), typeof(Rigidbody2D))]
public sealed class PlayerWorldWrapController : MonoBehaviour
{
    private Player player;
    private Rigidbody2D body;
    private Mod_ChunkLoader chunkLoader;
    private bool hasPresentationImage;
    private Vector2Int presentationImage;

    private void Awake()
    {
        player = GetComponent<Player>();
        body = GetComponent<Rigidbody2D>();
        chunkLoader = GetComponentInChildren<Mod_ChunkLoader>(true);
    }

    private void FixedUpdate()
    {
        TryWrapNow();
    }

    private void OnDisable()
    {
        WorldLocalPresentation.ClearAnchor(transform);
        hasPresentationImage = false;
    }

    /// <summary>检测局部平面跨周并刷新逻辑世界；不会搬动 Rigidbody。保留公开入口供聚焦测试。</summary>
    public bool TryWrapNow()
    {
        if (player == null || body == null || !player.IsLocalProfile ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            hasPresentationImage = false;
            return false;
        }

        Vector2 current = body.position;
        if (!IsFinite(current))
            return false;

        bool firstAnchorBinding = !WorldLocalPresentation.HasAnchor;
        WorldLocalPresentation.SetAnchor(transform);
        Vector2 logical = bounds.NormalizePosition(current);

        if (player.Data?.transform != null)
            player.Data.transform.position = new Vector3(logical.x, logical.y, transform.position.z);

        Vector2Int currentImage = ResolvePresentationImage(bounds, current);
        if (!hasPresentationImage)
        {
            presentationImage = currentImage;
            hasPresentationImage = true;
            if (firstAnchorBinding)
                WorldTopologyRuntime.NotifyLocalPlayerWrapped();
            return false;
        }

        if (currentImage == presentationImage)
            return false;

        presentationImage = currentImage;

        if (chunkLoader != null)
            chunkLoader.RefreshAfterWorldWrap();
        // 这里只通知逻辑跨周；Transform 没有瞬移，因此不能触发 Cinemachine 的 OnTargetObjectWarped。
        WorldTopologyRuntime.NotifyLocalPlayerWrapped();
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

using UnityEngine;

/// <summary>
/// Applies wrapped-world coordinate normalization to the locally authoritative
/// player. Other dynamic entities intentionally remain unchanged in phase one.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player), typeof(Rigidbody2D))]
public sealed class PlayerWorldWrapController : MonoBehaviour
{
    private Player player;
    private Rigidbody2D body;
    private Mod_ChunkLoader chunkLoader;

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

    /// <summary>Checks and applies one wrap operation. Public for focused runtime tests.</summary>
    public bool TryWrapNow()
    {
        if (player == null || body == null || !player.IsLocalProfile ||
            !WorldTopologyRuntime.TryGetActiveBounds(out WorldTopologyBounds bounds))
        {
            return false;
        }

        Vector2 current = body.position;
        if (!IsFinite(current) || bounds.Contains(current))
        {
            return false;
        }

        Vector2 velocity = body.velocity;
        Vector2 normalized = bounds.NormalizePosition(current);
        float z = transform.position.z;

        body.position = normalized;
        body.velocity = velocity;
        transform.position = new Vector3(normalized.x, normalized.y, z);

        if (player.Data?.transform != null)
        {
            player.Data.transform.position = transform.position;
        }

        if (chunkLoader != null)
            chunkLoader.RefreshAfterWorldWrap();
        WorldTopologyRuntime.NotifyLocalPlayerWrapped(current, normalized);
        return true;
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}

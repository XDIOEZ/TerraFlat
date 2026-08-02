using UnityEngine;

public sealed class DimensionPortal : MonoBehaviour, IInteractable
{
    private string targetDimensionId;
    private bool transitionRequested;
    private Vector2Int anchorCell;
    private bool initialized;

    public void Initialize(string targetDimension)
    {
        targetDimensionId = targetDimension;
        anchorCell = GetCurrentCell();
        initialized = true;
    }

    private void OnEnable()
    {
        // Chunk 对象池会复用父物体；若入口随旧 Chunk 被搬到新坐标，立即清理。
        if (initialized && GetCurrentCell() != anchorCell)
            Destroy(gameObject);
    }

    public void OnInteractStart(Item playerItem)
    {
        if (transitionRequested || playerItem is not Player player)
            return;

        transitionRequested = DimensionManager.Instance.TryBeginTransition(
            player,
            targetDimensionId,
            transform.position);
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    private Vector2Int GetCurrentCell()
    {
        return new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));
    }
}

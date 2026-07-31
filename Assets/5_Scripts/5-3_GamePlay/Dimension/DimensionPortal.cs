using UnityEngine;

public sealed class DimensionPortal : MonoBehaviour, IInteractable
{
    private string targetDimensionId;
    private bool transitionRequested;

    public void Initialize(string targetDimension)
    {
        targetDimensionId = targetDimension;
    }

    public void OnInteractStart(Item playerItem)
    {
        if (transitionRequested || playerItem is not Player player)
            return;

        transitionRequested = DimensionManager.Instance.TryBeginTransition(player, targetDimensionId);
    }

    public void OnInteractCancel(Item playerItem)
    {
    }
}

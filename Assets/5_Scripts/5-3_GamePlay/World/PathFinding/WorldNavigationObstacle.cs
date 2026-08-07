using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registers the footprint of explicit world obstacles once. Path queries never perform
/// Physics2D raycasts; collider bounds are converted to world cells only on lifecycle changes.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldNavigationObstacle : MonoBehaviour
{
    [SerializeField] private Collider2D[] obstacleColliders;

    private readonly HashSet<Vector2Int> occupiedCells = new();
    private WorldNavigationManager navigationManager;
    private bool registered;
    private bool initialized;

    private void Start()
    {
        initialized = true;
        RefreshRegistration();
    }

    private void OnEnable()
    {
        if (initialized)
            RefreshRegistration();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    public void RefreshRegistration()
    {
        navigationManager = navigationManager != null
            ? navigationManager
            : WorldNavigationManager.Instance;
        if (navigationManager == null)
            return;

        CollectOccupiedCells();
        navigationManager.RegisterObstacle(GetInstanceID(), occupiedCells);
        registered = occupiedCells.Count > 0;
    }

    private void CollectOccupiedCells()
    {
        occupiedCells.Clear();
        if (obstacleColliders == null || obstacleColliders.Length == 0)
            obstacleColliders = GetComponentsInChildren<Collider2D>(true);

        for (int i = 0; i < obstacleColliders.Length; i++)
        {
            Collider2D obstacle = obstacleColliders[i];
            if (obstacle == null || !obstacle.enabled || obstacle.isTrigger)
                continue;

            Bounds bounds = obstacle.bounds;
            int minX = Mathf.FloorToInt(bounds.min.x);
            int minY = Mathf.FloorToInt(bounds.min.y);
            int maxX = Mathf.CeilToInt(bounds.max.x);
            int maxY = Mathf.CeilToInt(bounds.max.y);

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                    occupiedCells.Add(new Vector2Int(x, y));
            }
        }
    }

    private void Unregister()
    {
        if (!registered)
            return;

        navigationManager?.UnregisterObstacle(GetInstanceID());
        registered = false;
        occupiedCells.Clear();
    }
}

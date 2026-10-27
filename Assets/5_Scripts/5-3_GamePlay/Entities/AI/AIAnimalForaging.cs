using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>统一地面动物对运行时草格与掉落食物的目标、移动和摄食流程。</summary>
internal sealed class AIAnimalForaging
{
    #region 运行时觅食
    private readonly Transform _actor;
    private readonly Mod_ItemDetector _detector;
    private readonly Mod_Food _food;
    private readonly Action _refreshDetector;
    private readonly Action<Vector3, float> _moveToInteractionTarget;
    private readonly Func<Transform, float> _distanceTo;
    private readonly Action _stopMovement;
    private readonly Action<Vector2Int> _onGrassConsumed;
    private Item _foodTarget;
    private ChunkTerrainData _grassTerrain;
    private Vector2Int _grassLocalCell;
    private Vector2Int _grassWorldCell;
    private bool _hasGrassTarget;
    private float _grassSearchCooldown;

    public Item FoodTarget => _foodTarget;
    public float FoodTargetDistance
    {
        get
        {
            Item target = FoodTarget;
            return target != null ? _distanceTo(target.transform) : float.PositiveInfinity;
        }
    }
    public float GrassTargetDistance => WorldTopologyRuntime.Distance(_actor.position, GrassTargetPosition);
    private Vector2 GrassTargetPosition => new(_grassWorldCell.x + 0.5f, _grassWorldCell.y + 0.5f);

    public AIAnimalForaging(
        Transform actor,
        Mod_ItemDetector detector,
        Mod_Food food,
        Action refreshDetector,
        Action<Vector3, float> moveToInteractionTarget,
        Func<Transform, float> distanceTo,
        Action stopMovement,
        Action<Vector2Int> onGrassConsumed)
    {
        _actor = actor;
        _detector = detector;
        _food = food;
        _refreshDetector = refreshDetector;
        _moveToInteractionTarget = moveToInteractionTarget;
        _distanceTo = distanceTo;
        _stopMovement = stopMovement;
        _onGrassConsumed = onGrassConsumed;
    }

    public void Reset()
    {
        _grassSearchCooldown = 0f;
        ClearTargets();
    }

    public void TickSearchCooldown(float deltaTime)
    {
        _grassSearchCooldown = Mathf.Max(0f, _grassSearchCooldown - deltaTime);
    }

    public bool TryAcquireGrassTarget(bool mealDue, float eatDistance, float searchRadius, float retryDelay)
    {
        if (!mealDue)
            return false;

        if (HasValidGrassTarget())
            return true;

        ClearGrassTarget();
        if (_grassSearchCooldown > 0f)
            return false;

        _grassSearchCooldown = Mathf.Max(0.05f, retryDelay);
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null ||
            !chunkManager.TryFindRuntimeGrassNear(
                _actor.position,
                Mathf.Max(eatDistance, searchRadius),
                out RuntimeTerrainTileSample runtimeGrass))
            return false;

        _grassTerrain = runtimeGrass.Terrain;
        _grassLocalCell = runtimeGrass.LocalCell;
        _grassWorldCell = runtimeGrass.WorldCell;
        _hasGrassTarget = true;
        _foodTarget = null;
        return true;
    }

    public bool TryFindFoodTarget(List<string> edibleTags, bool refreshDetector = false)
    {
        if (FoodTarget != null)
            return true;

        _foodTarget = null;
        if (refreshDetector)
            _refreshDetector();
        if (_detector == null || edibleTags == null)
            return false;

        _foodTarget = _detector.FindClosestItemByTags(edibleTags, _actor.position);
        return _foodTarget != null;
    }

    public void TickForage(
        bool grassMealDue,
        List<string> edibleTags,
        float eatDistance,
        float grassSearchRadius,
        float detectorRefreshInterval)
    {
        if (TryAcquireGrassTarget(grassMealDue, eatDistance, grassSearchRadius, detectorRefreshInterval))
        {
            _moveToInteractionTarget(GrassTargetPosition, eatDistance);
            return;
        }

        _refreshDetector();
        if (!TryFindFoodTarget(edibleTags))
        {
            _stopMovement();
            return;
        }

        _moveToInteractionTarget(FoodTarget.transform.position, eatDistance);
    }

    public void TickEat(
        bool grassMealDue,
        List<string> edibleTags,
        float eatDistance,
        float grassSearchRadius,
        float detectorRefreshInterval,
        float grassEatDuration,
        float stateElapsed)
    {
        if (TryAcquireGrassTarget(grassMealDue, eatDistance, grassSearchRadius, detectorRefreshInterval))
        {
            _moveToInteractionTarget(GrassTargetPosition, eatDistance);
            if (GrassTargetDistance > eatDistance || stateElapsed < grassEatDuration)
                return;

            Vector2Int grassPosition = _grassWorldCell;
            ClearGrassTarget();
            ChunkMgr chunkManager = ChunkMgr.Instance;
            if (chunkManager != null && chunkManager.TryConsumeRuntimeGrass(grassPosition))
            {
                _onGrassConsumed(grassPosition);
            }
            _stopMovement();
            return;
        }

        if (!TryFindFoodTarget(edibleTags))
        {
            _stopMovement();
            return;
        }

        Item target = FoodTarget;
        _moveToInteractionTarget(target.transform.position, eatDistance);
        if (_distanceTo(target.transform) > eatDistance)
            return;

        Mod_Food targetFood = target.GetComponentInChildren<Mod_Food>();
        if (targetFood == null)
        {
            ClearFoodTarget();
            _stopMovement();
            return;
        }

        _food.Eat(targetFood);
        if (_foodTarget == null || _foodTarget.itemData == null || _foodTarget.itemData.Stack.Amount <= 0f)
        {
            ClearFoodTarget();
            _stopMovement();
        }
    }

    public void ClearFoodTarget()
    {
        _foodTarget = null;
    }

    public void ClearTargets()
    {
        ClearFoodTarget();
        ClearGrassTarget();
    }

    private bool HasValidGrassTarget()
    {
        return _hasGrassTarget &&
               _grassTerrain != null &&
               !_grassTerrain.IsDisposed &&
               _grassTerrain.GetGrass(_grassLocalCell.x, _grassLocalCell.y) == ChunkTerrainData.GrassPresent;
    }

    private void ClearGrassTarget()
    {
        _grassTerrain = null;
        _grassLocalCell = default;
        _grassWorldCell = default;
        _hasGrassTarget = false;
    }

    #endregion
}

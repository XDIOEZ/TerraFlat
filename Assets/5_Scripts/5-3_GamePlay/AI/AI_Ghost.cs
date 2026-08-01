using System;
using MemoryPack;
using Pathfinding;
using Sirenix.OdinInspector;
using UnityEngine;

public enum GhostState
{
    Wander,
    Chase,
    RetreatFromLight,
    Radiance
}

[MemoryPackable]
[Serializable]
public partial class GhostAISaveData
{
    public Vector2 WanderTarget;
    public bool HasWanderTarget;
    public float WanderPauseRemaining;
}

/// <summary>
/// Ghost artificial intelligence module.
/// Uses A* for obstacle avoidance while selecting only completely dark destinations.
/// If there is nowhere dark to retreat to it enters a radiance state and takes periodic true damage.
/// </summary>
public class AI_Ghost : Module
{
    private const string ModuleId = "AI_Ghost";
    private const string RadianceBuffName = "光耀";
    private const float LightEpsilon = 0.0001f;

    public Ex_ModData_MemoryPackable ModData = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [SerializeField]
    private GhostAISaveData Data = new();

    [Header("感知与移动")]
    [SerializeField, Min(0.1f), Tooltip("主动发现玩家的距离，应覆盖幽灵的最大生成距离。")]
    private float perceptionRadius = 60f;
    [SerializeField, Min(0.1f)]
    private float wanderRadius = 6f;
    [SerializeField, Min(0.1f)]
    private float wanderSpeed = 1.25f;
    [SerializeField, Min(0.1f)]
    private float chaseSpeed = 2.5f;
    [SerializeField, Min(0.1f)]
    private float retreatSpeed = 2f;
    [SerializeField, Min(0.05f)]
    private float decisionInterval = 0.2f;
    [SerializeField, Min(0.1f)]
    private float stuckRepathDelay = 0.75f;
    [SerializeField, Min(1)]
    private int darkPositionSamples = 16;
    [SerializeField, Min(0.5f)]
    private float retreatSearchRadius = 6f;

    [Header("表现")]
    [SerializeField]
    private Transform visualTransform;
    [SerializeField]
    private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f)]
    private float bobHeight = 0.08f;
    [SerializeField, Min(0f)]
    private float bobFrequency = 1.6f;

    [ShowInInspector, ReadOnly]
    private GhostState _state;

    private DamageReceiver _damageReceiver;
    private BuffManager _buffManager;
    private Buff_Data _radianceBuff;
    private IAstarAI _pathAgent;
    private Vector3 _visualBaseLocalPosition;
    private Vector2 _moveTarget;
    private bool _hasMoveTarget;
    private float _decisionTimer;
    private float _stuckTimer;
    private Vector2 _lastAgentPosition;
    private bool _loggedMissingRadianceBuff;

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    public override void Awake()
    {
        if (ModData == null)
            ModData = new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
    }

    public override void Load()
    {
        Data ??= new GhostAISaveData();
        ModData.ReadData(ref Data);

        item.itemMods.GetMod_ByID(ModText.Hp, out _damageReceiver);
        _buffManager = item.GetComponentInChildren<BuffManager>(true);
        _radianceBuff = GameRes.Instance != null
            ? GameRes.Instance.GetBuffData(RadianceBuffName)
            : null;

        if (visualTransform == null && item.Sprite != null)
            visualTransform = item.Sprite.transform;
        if (spriteRenderer == null)
            spriteRenderer = item.Sprite != null
                ? item.Sprite
                : item.GetComponentInChildren<SpriteRenderer>(true);
        if (item.Sprite == null)
            item.Sprite = spriteRenderer;

        if (visualTransform != null)
            _visualBaseLocalPosition = visualTransform.localPosition;

        EnsurePathfindingComponents();
        _moveTarget = Data.WanderTarget;
        _hasMoveTarget = Data.HasWanderTarget;
        _state = GhostState.Wander;
        _decisionTimer = 0f;
        _lastAgentPosition = item.transform.position;
    }

    private void EnsurePathfindingComponents()
    {
        if (item.GetComponent<Seeker>() == null)
            item.gameObject.AddComponent<Seeker>();

        _pathAgent = item.GetComponent<IAstarAI>();
        if (_pathAgent == null)
            _pathAgent = item.gameObject.AddComponent<AILerp>();

        if (_pathAgent is AILerp aiLerp)
        {
            aiLerp.orientation = OrientationMode.YAxisForward;
            aiLerp.enableRotation = false;
        }

        _pathAgent.canMove = true;
        _pathAgent.canSearch = true;
        _pathAgent.isStopped = true;
    }

    public override void Save()
    {
        Data ??= new GhostAISaveData();
        Data.WanderTarget = _moveTarget;
        Data.HasWanderTarget = _hasMoveTarget && _state == GhostState.Wander;
        ModData.WriteData(Data);

        if (item?.itemData != null && !string.IsNullOrWhiteSpace(ModData.Name))
            item.itemData.ModuleDataDic[ModData.Name] = ModData;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (item == null ||
            item.itemData == null ||
            GameManager.Instance == null ||
            !GameManager.Instance.IsInGameWorld)
        {
            return;
        }

        UpdateVisual();

        _decisionTimer -= deltaTime;
        if (_decisionTimer <= 0f)
        {
            _decisionTimer = Mathf.Max(0.05f, decisionInterval);
            EvaluateState();
        }

        Move(deltaTime);
    }

    private void EvaluateState()
    {
        LightLayerMgr lightManager = LightLayerMgr.Instance;
        if (lightManager == null ||
            !lightManager.TryGetLightLevel(item.transform.position, out float currentLight))
        {
            _hasMoveTarget = false;
            return;
        }

        if (currentLight > LightEpsilon)
        {
            if (TryFindDarkPosition(item.transform.position, retreatSearchRadius, out Vector2 retreatTarget))
            {
                RemoveRadianceBuff();
                _state = GhostState.RetreatFromLight;
                _moveTarget = retreatTarget;
                _hasMoveTarget = true;
            }
            else
            {
                _state = GhostState.Radiance;
                _hasMoveTarget = false;
                EnsureRadianceBuff();
            }

            return;
        }

        RemoveRadianceBuff();

        Transform player = ItemMgr.Instance?.UserPlayerTransform;
        if (player != null)
        {
            float distanceSqr = ((Vector2)player.position - (Vector2)item.transform.position).sqrMagnitude;
            if (distanceSqr <= perceptionRadius * perceptionRadius &&
                lightManager.IsCompletelyDark(player.position))
            {
                _state = GhostState.Chase;
                _moveTarget = player.position;
                _hasMoveTarget = true;
                return;
            }
        }

        _state = GhostState.Wander;
        if (_hasMoveTarget &&
            Vector2.Distance(item.transform.position, _moveTarget) > 0.2f)
        {
            return;
        }

        _hasMoveTarget = TryFindDarkPosition(
            item.transform.position,
            wanderRadius,
            out _moveTarget);
    }

    private void Move(float deltaTime)
    {
        if (_pathAgent == null || !_hasMoveTarget || _state == GhostState.Radiance)
        {
            StopMoving();
            return;
        }

        Vector2 current = item.transform.position;
        if (_state == GhostState.Chase)
        {
            Transform player = ItemMgr.Instance?.UserPlayerTransform;
            if (player == null || !LightLayerMgr.Instance.IsCompletelyDark(player.position))
            {
                _hasMoveTarget = false;
                StopMoving();
                return;
            }

            _moveTarget = player.position;
        }

        float speed = _state switch
        {
            GhostState.Chase => chaseSpeed,
            GhostState.RetreatFromLight => retreatSpeed,
            _ => wanderSpeed
        };

        if (!CanMoveTo(_moveTarget))
        {
            _hasMoveTarget = false;
            StopMoving();
            return;
        }

        bool destinationChanged = ((Vector2)_pathAgent.destination - _moveTarget).sqrMagnitude > 0.01f;
        _pathAgent.maxSpeed = speed;
        _pathAgent.isStopped = false;
        _pathAgent.destination = _moveTarget;
        if (destinationChanged && !_pathAgent.pathPending)
            _pathAgent.SearchPath();

        HandleStuckRepath(current, deltaTime);

        UpdateChunkOwnership();

        if (Vector2.Distance(current, _moveTarget) <= 0.05f)
        {
            _hasMoveTarget = false;
            StopMoving();
        }
    }

    private void HandleStuckRepath(Vector2 currentPosition, float deltaTime)
    {
        if ((currentPosition - _lastAgentPosition).sqrMagnitude <= 0.0001f)
        {
            _stuckTimer += deltaTime;
            if (_stuckTimer >= stuckRepathDelay && !_pathAgent.pathPending)
            {
                _stuckTimer = 0f;
                _pathAgent.SearchPath();
            }
        }
        else
        {
            _stuckTimer = 0f;
            _lastAgentPosition = currentPosition;
        }
    }

    private void StopMoving()
    {
        _stuckTimer = 0f;
        if (_pathAgent != null)
            _pathAgent.isStopped = true;
    }

    private bool CanMoveTo(Vector2 next)
    {
        if (!TryGetActiveChunk(next, out _))
            return false;

        if (!IsWalkable(next))
            return false;

        LightLayerMgr lightManager = LightLayerMgr.Instance;
        if (lightManager == null)
            return true;

        if (!lightManager.TryGetLightLevel(next, out float nextLight))
        {
            return false;
        }

        return nextLight <= LightEpsilon;
    }

    private static bool IsWalkable(Vector2 worldPosition)
    {
        AstarGameManager astarManager = AstarGameManager.Instance;
        if (astarManager == null || !astarManager.IsGridGraphReady)
            return false;

        return astarManager.TryGetNodePenalty_GridGraphFast(worldPosition, out _, out bool walkable) && walkable;
    }

    private bool TryFindDarkPosition(Vector2 origin, float radius, out Vector2 position)
    {
        position = default;
        LightLayerMgr lightManager = LightLayerMgr.Instance;
        if (lightManager == null)
            return false;

        int samples = Mathf.Max(1, darkPositionSamples);
        float safeRadius = Mathf.Max(0.5f, radius);
        float phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        for (int ring = 1; ring <= 3; ring++)
        {
            float distance = safeRadius * ring / 3f;
            for (int i = 0; i < samples; i++)
            {
                float angle = phase + Mathf.PI * 2f * i / samples;
                Vector2 candidate = origin + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * distance;

                if (TryGetActiveChunk(candidate, out _) &&
                    lightManager.IsCompletelyDark(candidate) &&
                    IsWalkable(candidate))
                {
                    position = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetActiveChunk(Vector2 position, out Chunk chunk)
    {
        chunk = null;
        if (ChunkMgr.Instance == null)
            return false;

        return ChunkMgr.Instance.TryGetActiveChunkByPos(
                   Chunk.GetChunkPosition(position),
                   out chunk) &&
               chunk != null;
    }

    private void UpdateChunkOwnership()
    {
        if (!TryGetActiveChunk(item.transform.position, out Chunk targetChunk))
            return;

        Chunk currentChunk = item.GetComponentInParent<Chunk>();
        if (currentChunk == targetChunk)
            return;

        currentChunk?.RemoveItem(item);
        targetChunk.AddItem(item);
    }

    private void EnsureRadianceBuff()
    {
        if (_buffManager == null)
            _buffManager = item.GetComponentInChildren<BuffManager>(true);

        if (_radianceBuff == null && GameRes.Instance != null)
            _radianceBuff = GameRes.Instance.GetBuffData(RadianceBuffName);

        if (_buffManager != null && _radianceBuff != null)
        {
            if (!_buffManager.HasBuff(_radianceBuff.buff_ID))
                _buffManager.AddBuffRuntime(_radianceBuff, item);
            return;
        }

        if (!_loggedMissingRadianceBuff)
        {
            _loggedMissingRadianceBuff = true;
            Debug.LogWarning("[AI_Ghost] 找不到光耀 Buff 或 BuffManager。", this);
        }
    }

    private void RemoveRadianceBuff()
    {
        if (_buffManager != null && _buffManager.HasBuff(RadianceBuffName))
            _buffManager.RemoveBuff(RadianceBuffName);
    }

    private void UpdateVisual()
    {
        if (visualTransform == null)
            return;

        float offset = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobHeight;
        visualTransform.localPosition = _visualBaseLocalPosition + Vector3.up * offset;
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
    }
}

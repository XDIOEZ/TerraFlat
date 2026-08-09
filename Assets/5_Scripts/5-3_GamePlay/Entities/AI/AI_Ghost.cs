using System;
using MemoryPack;
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
/// 幽灵 AI：在感知范围内直接追击本地玩家，追击时不受地形可走性和玩家所在光照限制。
/// 幽灵自身所在位置的亮度严格大于 0.5 时持续添加“光耀” Buff，亮度小于等于 0.5 时移除；视觉子节点以低幅度正弦曲线上下浮动，
/// 浮动只作用于 Sprite，不改变根物体、碰撞体、寻路位置和伤害判定。
/// </summary>
public class AI_Ghost : Module, IAIActor
{
    private const string ModuleId = "AI_Ghost";
    private const string RadianceBuffId = "光耀";
    private const float LightEpsilon = 0.0001f;

    /// <summary>光照强度严格大于该值时，幽灵才持续受到光耀伤害。</summary>
    public const float LightDamageThreshold = 0.5f;

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
    [SerializeField, Min(0f), Tooltip("幽灵视觉上下浮动的高度，不影响根物体和碰撞体。")]
    private float bobHeight = 0.12f;
    [SerializeField, Min(0f)]
    private float bobFrequency = 1.4f;

    [ShowInInspector, ReadOnly]
    private GhostState _state;

    private DamageReceiver _damageReceiver;
    private BuffManager _buffManager;
    private WorldNavigationAgent _pathAgent;
    private Rigidbody2D _rigidbody;
    private Vector3 _visualBaseLocalPosition;
    private float _bobPhase;
    private bool _hasVisualBasePosition;
    private Vector2 _moveTarget;
    private bool _hasMoveTarget;
    private float _decisionTimer;
    private bool _loggedMissingRadianceBuff;

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Item ActorItem => item;
    public bool IsAlive => item != null && !item.DestructionHandled &&
                           (_damageReceiver == null || _damageReceiver.Hp > 0f);

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
        if (visualTransform == null && item.Sprite != null)
            visualTransform = item.Sprite.transform;
        if (spriteRenderer == null)
            spriteRenderer = item.Sprite != null
                ? item.Sprite
                : item.GetComponentInChildren<SpriteRenderer>(true);
        if (item.Sprite == null)
            item.Sprite = spriteRenderer;

        if (visualTransform != null)
        {
            if (!_hasVisualBasePosition)
            {
                _visualBaseLocalPosition = visualTransform.localPosition;
                _hasVisualBasePosition = true;
            }

            // 为不同幽灵分配稳定初始相位，避免同屏实体完全同步浮动。
            _bobPhase = ((GetInstanceID() & int.MaxValue) % 97) / 97f * Mathf.PI * 2f;
        }

        _rigidbody = item.GetComponent<Rigidbody2D>();
        EnsureNavigationAgent();
        _moveTarget = Data.WanderTarget;
        _hasMoveTarget = Data.HasWanderTarget;
        _state = GhostState.Wander;
        _decisionTimer = 0f;
    }

    private void EnsureNavigationAgent()
    {
        _pathAgent = item.GetComponent<WorldNavigationAgent>();
        if (_pathAgent == null)
            _pathAgent = item.gameObject.AddComponent<WorldNavigationAgent>();

        _pathAgent.Bind(item.GetComponent<Rigidbody2D>());
        _pathAgent.Configure(0.05f, 0.1f, stuckRepathDelay, 0.01f);
        _pathAgent.Stop(clearDestination: true);
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
        float currentLight = 0f;
        bool hasLightLevel = lightManager != null &&
                             lightManager.TryGetLightLevel(
                                 item.transform.position,
                                 out currentLight);

        if (hasLightLevel && ShouldTakeLightDamage(currentLight))
            EnsureRadianceBuff();
        else
            RemoveRadianceBuff();

        // 先锁定玩家，再处理避光状态，避免玩家站在有光区域时幽灵永远只会撤退。
        Transform player = ResolvePlayerTransform();
        if (player != null &&
            WorldTopologyRuntime.SqrDistance(item.transform.position, player.position) <=
            perceptionRadius * perceptionRadius)
        {
            _state = GhostState.Chase;
            _moveTarget = player.position;
            _hasMoveTarget = true;
            return;
        }

        if (!hasLightLevel)
        {
            _state = GhostState.Wander;
            _hasMoveTarget = false;
            StopMoving();
            return;
        }

        if (currentLight > LightEpsilon)
        {
            if (TryFindDarkPosition(item.transform.position, retreatSearchRadius, out Vector2 retreatTarget))
            {
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

        _state = GhostState.Wander;
        if (_hasMoveTarget &&
            WorldTopologyRuntime.Distance(item.transform.position, _moveTarget) > 0.2f)
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
        if (_state == GhostState.Chase)
        {
            Transform player = ResolvePlayerTransform();
            if (player == null)
            {
                _hasMoveTarget = false;
                StopMoving();
                return;
            }

            _moveTarget = player.position;
            MoveDirectlyTowards(_moveTarget, chaseSpeed, deltaTime);
            return;
        }

        if (_pathAgent == null || !_hasMoveTarget || _state == GhostState.Radiance)
        {
            StopMoving();
            return;
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

        _pathAgent.MaxSpeed = speed;
        _pathAgent.SetDestination(_moveTarget);
        _pathAgent.Tick(deltaTime);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);

        if (_pathAgent.ReachedDestination)
        {
            _hasMoveTarget = false;
            StopMoving();
        }
    }

    #region 直接追击

    /// <summary>亮度严格大于一半时才开启持续光照伤害。</summary>
    public static bool ShouldTakeLightDamage(float lightLevel)
    {
        return lightLevel > LightDamageThreshold;
    }

    /// <summary>直接沿世界最短方向移动，追击时跳过导航可走性和障碍物检查。</summary>
    private void MoveDirectlyTowards(Vector2 target, float speed, float deltaTime)
    {
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(item.transform.position, target);
        float distance = delta.magnitude;
        if (distance <= 0.05f)
        {
            _hasMoveTarget = false;
            StopMoving();
            return;
        }

        float stepDistance = Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);
        Vector2 nextPosition = distance <= stepDistance
            ? target
            : (Vector2)item.transform.position + delta / distance * stepDistance;
        Vector2 normalizedPosition = WorldTopologyRuntime.NormalizePosition(nextPosition);

        if (_rigidbody != null)
        {
            _rigidbody.position = normalizedPosition;
            _rigidbody.velocity = Vector2.zero;
        }

        item.transform.position = new Vector3(
            normalizedPosition.x,
            normalizedPosition.y,
            item.transform.position.z);
        if (item.itemData?.transform != null)
            item.itemData.transform.position = item.transform.position;

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    #endregion

    private void StopMoving()
    {
        _pathAgent?.Stop();
    }

    #region 玩家目标解析

    /// <summary>
    /// 获取当前本地玩家，优先使用 ItemMgr 的正式入口；当存档玩家名尚未同步时，
    /// 回退到 Player_DIC 中已标记为本地档案且仍处于激活状态的玩家，避免幽灵目标为空。
    /// </summary>
    private Transform ResolvePlayerTransform()
    {
        ItemMgr itemManager = ItemMgr.Instance;
        if (itemManager == null)
            return null;

        Transform playerTransform = itemManager.UserPlayerTransform;
        if (playerTransform != null && playerTransform.gameObject.activeInHierarchy)
            return playerTransform;

        foreach (Player player in itemManager.Player_DIC.Values)
        {
            if (player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy)
                return player.transform;
        }

        return null;
    }

    #endregion

    private bool CanMoveTo(Vector2 next)
    {
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
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null || !navigation.IsNavigationReady)
            return false;

        return navigation.TryGetCell(worldPosition, out _, out bool walkable) && walkable;
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

                if (lightManager.IsCompletelyDark(candidate) &&
                    IsWalkable(candidate))
                {
                    position = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureRadianceBuff()
    {
        if (_buffManager == null)
            _buffManager = item.GetComponentInChildren<BuffManager>(true);

        if (_buffManager != null)
        {
            if (_buffManager.HasBuff(RadianceBuffId) ||
                _buffManager.AddBuff(RadianceBuffId))
            {
                return;
            }
        }

        if (!_loggedMissingRadianceBuff)
        {
            _loggedMissingRadianceBuff = true;
            Debug.LogWarning("[AI_Ghost] 找不到光耀 Buff 或 BuffManager。", this);
        }
    }

    private void RemoveRadianceBuff()
    {
        if (_buffManager != null && _buffManager.HasBuff(RadianceBuffId))
            _buffManager.RemoveBuff(RadianceBuffId);
    }

    private void UpdateVisual()
    {
        if (visualTransform == null)
            return;

        float angle = Time.time * Mathf.Max(0f, bobFrequency) * Mathf.PI * 2f + _bobPhase;
        float offset = Mathf.Sin(angle) * Mathf.Max(0f, bobHeight);
        visualTransform.localPosition = _visualBaseLocalPosition + Vector3.up * offset;
    }

    private void OnValidate()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModuleId;
        bobHeight = Mathf.Max(0f, bobHeight);
        bobFrequency = Mathf.Max(0f, bobFrequency);
    }
}

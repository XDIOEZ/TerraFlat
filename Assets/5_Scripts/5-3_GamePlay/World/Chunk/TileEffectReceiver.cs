using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UltEvents;
using UnityEngine;

/// <summary>
/// 地块效果接收器。优先读取 ChunkRuntime/ChunkTerrainData 权威地形，进入、离开或地块来源变化时
/// 调用 Tile_Block 行为；旧 Map 仅作为尚未迁移场景的兼容回退。
/// </summary>
public class TileEffectReceiver : Module
{
    private static readonly Vector2Int[] WaterEdgeDirections =
    {
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.up
    };

    #region Inspector

    [Header("位置信息")]
    public Vector2Int lastGridPos;

    [Header("旧 Map 兼容缓存")]
    public Map Cache_map;

    [Header("Tile事件")]
    public UltEvent<TileData> OnTileEnterEvent = new();
    public UltEvent<TileData> OnTileExitEvent = new();

    [Tooltip("当前踩着的 TileData 缓存，供其他模块引用")]
    public TileData currentTileData;

    [Header("水中生存")]
    [Tooltip("地块水深超过该值时，有体力的角色主动漂浮并维持该身体淹没高度。")]
    [Range(0f, 1f)] [SerializeField] private float floatingImmersionLevel = 0.3f;
    [Tooltip("有效淹没达到或超过该高度后开始消耗氧气。")]
    [Range(0f, 1f)] [SerializeField] private float oxygenSafetyImmersionLevel = 0.6f;
    [Tooltip("体力耗尽后，每秒向地块自然淹没高度下沉的身体比例。")]
    [Min(0.01f)] [SerializeField] private float sinkingImmersionSpeed = 0.35f;

    [Header("无独立体力/氧气模块生物的水中储备")]
    [Min(0f)] [SerializeField] private float fallbackSwimStaminaMax = 100f;
    [Min(0f)] [SerializeField] private float fallbackSwimStaminaConsumePerSecond = 5f;
    [Min(0f)] [SerializeField] private float fallbackSwimStaminaRecoverPerSecond = 10f;
    [Min(0f)] [SerializeField] private float fallbackOxygenMax = 100f;
    [Min(0f)] [SerializeField] private float fallbackOxygenConsumePerSecond = 10f;
    [Min(0f)] [SerializeField] private float fallbackOxygenRecoverPerSecond = 25f;
    [Min(0f)] [SerializeField] private float fallbackDrowningDamagePerSecond = 10f;

    #endregion

    #region 运行时状态

    private Tile_Block activeTileBlock;
    private TileData activeTileData;
    private Map activeTileMap;
    private ChunkTerrainData activeRuntimeTerrain;
    private int activeRuntimeTileId;
    private Vector2Int activeGridPos;
    private bool hasActiveTileEffects;
    private bool activeTileIsEdgeInteractionOnly;
    private bool isPreparedForWorldTransition;
    private EnvironmentInteractionRunner environmentInteractions;
    private readonly System.Collections.Generic.HashSet<object> effectSuppressors = new();

    /// <summary>实例级地块接触开关；起飞先退出旧地块，释放后按当前位置重新进入。</summary>
    public void SetEffectsSuppressed(object owner, bool suppressed)
    {
        if (owner == null)
            throw new System.ArgumentNullException(nameof(owner));
        if (suppressed)
        {
            if (effectSuppressors.Add(owner))
                ExitCurrentTileEffects();
        }
        else if (effectSuppressors.Remove(owner) && effectSuppressors.Count == 0)
            RefreshCurrentTileEffects();
    }

    private Item waterVitalsItem;
    private Mod_Stamina waterStamina;
    private Mod_Oxygen waterOxygen;
    private DamageReceiver waterDamageReceiver;
    private bool fallbackWaterVitalsInitialized;
    private float fallbackSwimStamina;
    private float fallbackOxygen;
    private bool waterSurvivalActive;
    private float currentWaterImmersion;
    private int lastWaterExitFrame = -1;
    private bool lastWaterExitWasActive;

    public bool HasActiveTileEffects => hasActiveTileEffects;
    public bool IsActiveTileEdgeInteractionOnly => activeTileIsEdgeInteractionOnly;
    public EnvironmentInteractionRunner EnvironmentInteractions => EnsureEnvironmentInteractions();
    public float CurrentWaterImmersion => currentWaterImmersion;
    public override string CanonicalModuleId => ModText.TileEffectReceiver;

    #endregion

    #region 模块数据

    public Ex_ModData_MemoryPackable ModSaveData;

    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = (Ex_ModData_MemoryPackable)value;
    }

    #endregion

    #region 生命周期

    private void Start()
    {
        enabled = true;
        EnsureEnvironmentInteractions();
        RefreshCurrentTileEffects();
    }

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.TileEffectReceiver;

        floatingImmersionLevel = Mathf.Clamp01(floatingImmersionLevel);
        oxygenSafetyImmersionLevel = Mathf.Clamp(
            oxygenSafetyImmersionLevel,
            floatingImmersionLevel,
            1f);
        sinkingImmersionSpeed = Mathf.Max(0.01f, sinkingImmersionSpeed);
        fallbackSwimStaminaMax = Mathf.Max(0f, fallbackSwimStaminaMax);
        fallbackSwimStaminaConsumePerSecond = Mathf.Max(0f, fallbackSwimStaminaConsumePerSecond);
        fallbackSwimStaminaRecoverPerSecond = Mathf.Max(0f, fallbackSwimStaminaRecoverPerSecond);
        fallbackOxygenMax = Mathf.Max(0f, fallbackOxygenMax);
        fallbackOxygenConsumePerSecond = Mathf.Max(0f, fallbackOxygenConsumePerSecond);
        fallbackOxygenRecoverPerSecond = Mathf.Max(0f, fallbackOxygenRecoverPerSecond);
        fallbackDrowningDamagePerSecond = Mathf.Max(0f, fallbackDrowningDamagePerSecond);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (effectSuppressors.Count != 0)
            return;
        UpdateLegacyMapReference();
        Vector2Int currentGridPos = GetCurrentGridPos();
        if (currentGridPos != lastGridPos || !IsActiveSourceCurrent(currentGridPos))
        {
            ExitCurrentTileEffects();
            lastGridPos = currentGridPos;
            EnterTile(currentGridPos);
        }

        bool hasRealWater = hasActiveTileEffects &&
                            !activeTileIsEdgeInteractionOnly &&
                            activeTileData is TileData_Water;
        if (!hasRealWater)
            TickDryWaterSurvival(deltaTime);

        UpdateCurrentTile(deltaTime);
    }

    #endregion

    #region 模块接口

    public override void Load()
    {
        ModSaveData?.ReadData(ref lastGridPos);
        UpdateLegacyMapReference();
    }

    public override void Save()
    {
        // 自动保存不代表离开当前地块，保存过程不能撤销环境效果。
        ModSaveData?.WriteData(lastGridPos);
    }

    public override void Act()
    {
        base.Act();
    }

    #endregion

    #region 地块事件

    private bool EnterTile(Vector2Int gridPos)
    {
        if (item == null || !TryResolveTileEffect(gridPos, out TileEffectResolution resolution))
        {
            currentTileData = null;
            return false;
        }

        CacheActiveTile(gridPos, resolution);
        resolution.TileBlock.OnEnter(item, resolution.TileData, resolution.Map, this);
        OnTileEnterEvent.Invoke(resolution.TileData);
        return true;
    }

    /// <summary>使用进入时缓存退出，避免换维度后拿新地图错误查询旧坐标。</summary>
    private bool ExitCurrentTileEffects()
    {
        if (!hasActiveTileEffects)
        {
            currentTileData = null;
            return false;
        }

        Tile_Block tileBlock = activeTileBlock;
        TileData tileData = activeTileData;
        Map tileMap = activeTileMap;
        ClearActiveTile();

        if (item == null || tileBlock == null || tileData == null)
            return false;

        tileBlock.OnExit(item, tileData, tileMap, this);
        OnTileExitEvent.Invoke(tileData);
        return true;
    }

    private void UpdateCurrentTile(float deltaTime)
    {
        if (!hasActiveTileEffects || item == null || activeTileBlock == null || activeTileData == null)
            return;

        currentTileData = activeTileData;
        activeTileBlock.OnUpdate(item, activeTileData, activeTileMap, this, deltaTime);
    }

    /// <summary>世界切换保存前撤销脚下地块效果，防止环境 Buff 带到下一维度。</summary>
    public void PrepareForWorldTransition()
    {
        if (isPreparedForWorldTransition)
            return;

        isPreparedForWorldTransition = true;
        if (!hasActiveTileEffects && item != null)
        {
            Vector2Int gridPos = GetCurrentGridPos();
            if (TryResolveTileEffect(gridPos, out TileEffectResolution resolution))
            {
                CacheActiveTile(gridPos, resolution);
                isPreparedForWorldTransition = true;
                lastGridPos = gridPos;
            }
        }

        ExitCurrentTileEffects();
    }

    /// <summary>地图加载完成或切换失败恢复后，立即重新绑定脚下地块效果。</summary>
    public bool RefreshCurrentTileEffects()
    {
        if (effectSuppressors.Count != 0)
            return false;
        UpdateLegacyMapReference();
        if (item == null)
            return false;

        Vector2Int gridPos = GetCurrentGridPos();
        if (IsActiveSourceCurrent(gridPos))
        {
            lastGridPos = gridPos;
            return true;
        }

        ExitCurrentTileEffects();
        lastGridPos = gridPos;
        return EnterTile(gridPos);
    }

    #endregion

    #region 水中生存

    /// <summary>进入真实水体；地块水深超过 0.3 且有体力时从 0.3 漂浮线开始。</summary>
    public float EnterWaterSurvival(Item actor, float naturalImmersion)
    {
        ResolveWaterVitals(actor);
        EnsureFallbackWaterVitals();

        bool continuedAcrossWaterTiles = lastWaterExitWasActive &&
                                         lastWaterExitFrame == Time.frameCount;
        waterSurvivalActive = true;
        lastWaterExitWasActive = false;
        waterOxygen?.SetWaterExposure(true);

        naturalImmersion = Mathf.Clamp01(naturalImmersion);
        if (!continuedAcrossWaterTiles)
        {
            currentWaterImmersion = HasSwimStamina()
                ? Mathf.Min(naturalImmersion, floatingImmersionLevel)
                : naturalImmersion;
        }

        UpdateWaterBreathing(currentWaterImmersion, 0f);
        return currentWaterImmersion;
    }

    /// <summary>
    /// 推进通用水中生存：水深不超过 0.3 时不漂浮、不耗体力；深水有体力维持 0.3；体力耗尽后下沉到自然水深；
    /// 有效淹没达到 0.6 后开始消耗氧气。
    /// </summary>
    public float UpdateWaterSurvival(Item actor, float naturalImmersion, float deltaTime)
    {
        if (!waterSurvivalActive || waterVitalsItem != actor)
            EnterWaterSurvival(actor, naturalImmersion);

        naturalImmersion = Mathf.Clamp01(naturalImmersion);
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        float targetImmersion = naturalImmersion;

        if (naturalImmersion > WaterEnvironmentRules.SwimmingDepthThreshold && HasSwimStamina())
        {
            if (GameNetwork.HasStateAuthority && safeDeltaTime > 0f)
                ConsumeSwimStamina(safeDeltaTime, naturalImmersion);

            if (HasSwimStamina())
                targetImmersion = Mathf.Min(naturalImmersion, floatingImmersionLevel);
        }

        currentWaterImmersion = Mathf.MoveTowards(
            currentWaterImmersion,
            targetImmersion,
            Mathf.Max(0.01f, sinkingImmersionSpeed) * safeDeltaTime);

        UpdateWaterBreathing(currentWaterImmersion, safeDeltaTime);
        return currentWaterImmersion;
    }

    /// <summary>离开真实水体；连续水格同帧 Exit/Enter 会保留当前下沉进度。</summary>
    public void ExitWaterSurvival(Item actor)
    {
        if (waterVitalsItem != actor && actor != null)
            ResolveWaterVitals(actor);

        lastWaterExitWasActive = waterSurvivalActive;
        lastWaterExitFrame = Time.frameCount;
        waterSurvivalActive = false;
        waterOxygen?.SetWaterExposure(false);
    }

    /// <summary>不在真实水格时恢复通用生物的游泳储备与氧气；玩家普通体力仍由既有体力系统恢复。</summary>
    private void TickDryWaterSurvival(float deltaTime)
    {
        ResolveWaterVitals(item);
        EnsureFallbackWaterVitals();
        waterOxygen?.SetWaterExposure(false);

        if (!GameNetwork.HasStateAuthority)
            return;

        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (safeDeltaTime <= 0f)
            return;

        if (waterStamina == null)
        {
            fallbackSwimStamina = Mathf.Min(
                Mathf.Max(0f, fallbackSwimStaminaMax),
                fallbackSwimStamina + Mathf.Max(0f, fallbackSwimStaminaRecoverPerSecond) * safeDeltaTime);
        }

        if (waterOxygen != null)
        {
            waterOxygen.AddOxygen(Mathf.Max(0f, waterOxygen.oxygenRecoverPerSecond) * safeDeltaTime);
        }
        else
        {
            fallbackOxygen = Mathf.Min(
                Mathf.Max(0f, fallbackOxygenMax),
                fallbackOxygen + Mathf.Max(0f, fallbackOxygenRecoverPerSecond) * safeDeltaTime);
        }
    }

    private void ResolveWaterVitals(Item actor)
    {
        if (waterVitalsItem == actor)
            return;

        waterVitalsItem = actor;
        waterStamina = actor?.itemMods?.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        waterOxygen = actor?.itemMods?.GetMod_ByID<Mod_Oxygen>(ModText.Oxygen);
        waterDamageReceiver = actor?.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (waterDamageReceiver == null && actor != null)
            waterDamageReceiver = actor.GetComponentInChildren<DamageReceiver>(true);
    }

    private void EnsureFallbackWaterVitals()
    {
        if (fallbackWaterVitalsInitialized)
            return;

        fallbackSwimStamina = Mathf.Max(0f, fallbackSwimStaminaMax);
        fallbackOxygen = Mathf.Max(0f, fallbackOxygenMax);
        fallbackWaterVitalsInitialized = true;
    }

    private bool HasSwimStamina()
    {
        return waterStamina != null
            ? waterStamina.CurrentValue > 0f
            : fallbackSwimStamina > 0f;
    }

    private void ConsumeSwimStamina(float deltaTime, float naturalImmersion)
    {
        float consumePerSecond = waterOxygen != null
            ? Mathf.Max(0f, waterOxygen.staminaConsumePerSecond)
            : Mathf.Max(0f, fallbackSwimStaminaConsumePerSecond);

        consumePerSecond *= WaterEnvironmentRules.ResolveSwimmingMultiplier(naturalImmersion);

        if (waterStamina != null)
        {
            waterStamina.AddStamina(-consumePerSecond * deltaTime);
            return;
        }

        fallbackSwimStamina = Mathf.Max(0f, fallbackSwimStamina - consumePerSecond * deltaTime);
    }

    private void UpdateWaterBreathing(float immersionLevel, float deltaTime)
    {
        bool breathBlocked = immersionLevel >= oxygenSafetyImmersionLevel;
        waterOxygen?.SetBreathBlocked(breathBlocked);

        if (!GameNetwork.HasStateAuthority || deltaTime <= 0f)
            return;

        float oxygenConsume = waterOxygen != null
            ? Mathf.Max(0f, waterOxygen.oxygenConsumePerSecond)
            : Mathf.Max(0f, fallbackOxygenConsumePerSecond);
        float oxygenRecover = waterOxygen != null
            ? Mathf.Max(0f, waterOxygen.oxygenRecoverPerSecond)
            : Mathf.Max(0f, fallbackOxygenRecoverPerSecond);
        float oxygenBefore = waterOxygen != null ? waterOxygen.CurrentValue : fallbackOxygen;
        float drowningDeltaTime = 0f;

        if (waterOxygen != null)
        {
            waterOxygen.AddOxygen((breathBlocked ? -oxygenConsume : oxygenRecover) * deltaTime);
        }
        else if (breathBlocked)
        {
            fallbackOxygen = Mathf.Max(0f, fallbackOxygen - oxygenConsume * deltaTime);
        }
        else
        {
            fallbackOxygen = Mathf.Min(
                Mathf.Max(0f, fallbackOxygenMax),
                fallbackOxygen + oxygenRecover * deltaTime);
        }

        float currentOxygen = waterOxygen != null ? waterOxygen.CurrentValue : fallbackOxygen;
        if (breathBlocked && currentOxygen <= 0f)
        {
            if (oxygenBefore <= 0f)
            {
                drowningDeltaTime = deltaTime;
            }
            else if (oxygenConsume > 0f)
            {
                float secondsUntilEmpty = oxygenBefore / oxygenConsume;
                drowningDeltaTime = Mathf.Max(0f, deltaTime - secondsUntilEmpty);
            }
        }

        if (!breathBlocked || currentOxygen > 0f || waterDamageReceiver == null || waterDamageReceiver.Hp <= 0f)
            return;

        float damagePerSecond = waterOxygen != null
            ? Mathf.Max(0f, waterOxygen.drowningDamagePerSecond)
            : Mathf.Max(0f, fallbackDrowningDamagePerSecond);
        if (damagePerSecond > 0f && drowningDeltaTime > 0f)
            waterDamageReceiver.ForceHurt(damagePerSecond * drowningDeltaTime);
    }

    #endregion

    #region 查询与缓存

    private bool TryResolveTileEffect(Vector2Int gridPos, out TileEffectResolution resolution)
    {
        if (TryResolveTileEffectAtPosition(transform.position, gridPos, out TileEffectResolution exactResolution))
        {
            if (exactResolution.TileData is TileData_Water ||
                !TryResolveNearbyWater(gridPos, out resolution))
            {
                resolution = exactResolution;
            }

            return true;
        }

        if (TryResolveNearbyWater(gridPos, out resolution))
            return true;

        resolution = default;
        return false;
    }

    /// <summary>按指定世界采样点查询新版运行时地形或旧 Map 地块行为。</summary>
    private bool TryResolveTileEffectAtPosition(Vector2 samplePosition, Vector2Int gridPos,
        out TileEffectResolution resolution)
    {
        ChunkMgr manager = ChunkMgr.Instance;
        if (manager != null && manager.TryGetRuntimeTileEffect(samplePosition,
                out RuntimeTerrainTileSample sample, out TileData runtimeData,
                out Tile_Block runtimeBlock))
        {
            resolution = new TileEffectResolution(runtimeBlock, runtimeData, null,
                sample.Terrain, sample.TopTileId);
            return true;
        }

        TileData legacyData = Cache_map?.GetTile(gridPos);
        if (legacyData != null && GameRes.Instance != null)
        {
            Tile_Block legacyBlock = GameRes.Instance.GetTileBlock(legacyData.Name);
            if (legacyBlock != null)
            {
                resolution = new TileEffectResolution(legacyBlock, legacyData, Cache_map, null, 0);
                return true;
            }
        }

        resolution = default;
        return false;
    }

    /// <summary>
    /// 查找角色当前陆地格四邻域内的水格。
    /// 直接与水相邻的整格都视为岸边，角色无需把中心点挤到格子边缘才能俯身饮水。
    /// </summary>
    private bool TryResolveNearbyWater(Vector2Int gridPos, out TileEffectResolution resolution)
    {
        resolution = default;
        for (int i = 0; i < WaterEdgeDirections.Length; i++)
        {
            Vector2Int neighborGridPos = gridPos + WaterEdgeDirections[i];
            Vector2 samplePosition = new Vector2(neighborGridPos.x + 0.5f, neighborGridPos.y + 0.5f);
            if (!TryResolveTileEffectAtPosition(samplePosition, neighborGridPos,
                    out TileEffectResolution candidate) ||
                !(candidate.TileData is TileData_Water))
            {
                continue;
            }

            resolution = new TileEffectResolution(
                candidate.TileBlock,
                candidate.TileData,
                candidate.Map,
                candidate.RuntimeTerrain,
                candidate.RuntimeTileId,
                true);
            return true;
        }

        return false;
    }

    private bool IsActiveSourceCurrent(Vector2Int gridPos)
    {
        if (!hasActiveTileEffects || activeGridPos != gridPos)
            return false;

        if (!TryResolveTileEffect(gridPos, out TileEffectResolution currentResolution))
            return false;

        if (activeRuntimeTerrain != null)
            return ReferenceEquals(currentResolution.RuntimeTerrain, activeRuntimeTerrain) &&
                   currentResolution.RuntimeTileId == activeRuntimeTileId;

        return activeTileMap != null && activeTileMap == currentResolution.Map &&
               ReferenceEquals(activeTileData, currentResolution.TileData);
    }

    private void CacheActiveTile(Vector2Int gridPos, TileEffectResolution resolution)
    {
        activeTileBlock = resolution.TileBlock;
        activeTileData = resolution.TileData;
        activeTileMap = resolution.Map;
        activeRuntimeTerrain = resolution.RuntimeTerrain;
        activeRuntimeTileId = resolution.RuntimeTileId;
        activeGridPos = gridPos;
        activeTileIsEdgeInteractionOnly = resolution.EdgeInteractionOnly;
        hasActiveTileEffects = true;
        isPreparedForWorldTransition = false;
        currentTileData = resolution.TileData;
    }

    private void ClearActiveTile()
    {
        activeTileBlock = null;
        activeTileData = null;
        activeTileMap = null;
        activeRuntimeTerrain = null;
        activeRuntimeTileId = 0;
        activeTileIsEdgeInteractionOnly = false;
        hasActiveTileEffects = false;
        currentTileData = null;
    }

    /// <summary>运行时补装通用环境动作运行器；它只保存角色自己的动作实例，不写入存档。</summary>
    private EnvironmentInteractionRunner EnsureEnvironmentInteractions()
    {
        if (environmentInteractions == null)
            environmentInteractions = GetComponent<EnvironmentInteractionRunner>();
        if (environmentInteractions == null)
            environmentInteractions = gameObject.AddComponent<EnvironmentInteractionRunner>();

        environmentInteractions.Bind(item ?? GetComponentInParent<Item>());
        return environmentInteractions;
    }

    private void UpdateLegacyMapReference()
    {
        ChunkMgr manager = ChunkMgr.Instance;
        if (manager == null ||
            manager.TryGetRuntimeTerrainTile(transform.position, out _))
            return;

        manager.GetChunkBy_ItemPosition(transform.position, out Chunk chunk);
        Map currentMap = chunk?.Map;
        if (currentMap != null || Cache_map == null || !Cache_map.gameObject.activeInHierarchy)
            Cache_map = currentMap;
    }

    private Vector2Int GetCurrentGridPos()
    {
        if (Cache_map != null && Cache_map.tileMap != null)
        {
            Vector3Int cell = Cache_map.tileMap.WorldToCell(transform.position);
            return WorldTopologyRuntime.NormalizeCell(new Vector2Int(cell.x, cell.y));
        }

        Vector2 normalized = WorldTopologyRuntime.NormalizePosition(transform.position);
        return new Vector2Int(Mathf.FloorToInt(normalized.x), Mathf.FloorToInt(normalized.y));
    }

    public TileData GetCurrentTileData()
    {
        if (currentTileData != null)
            return currentTileData;

        return TryResolveTileEffect(GetCurrentGridPos(), out TileEffectResolution resolution)
            ? resolution.TileData
            : null;
    }

    private readonly struct TileEffectResolution
    {
        public TileEffectResolution(Tile_Block tileBlock, TileData tileData, Map map,
            ChunkTerrainData runtimeTerrain, int runtimeTileId, bool edgeInteractionOnly = false)
        {
            TileBlock = tileBlock;
            TileData = tileData;
            Map = map;
            RuntimeTerrain = runtimeTerrain;
            RuntimeTileId = runtimeTileId;
            EdgeInteractionOnly = edgeInteractionOnly;
        }

        public Tile_Block TileBlock { get; }
        public TileData TileData { get; }
        public Map Map { get; }
        public ChunkTerrainData RuntimeTerrain { get; }
        public int RuntimeTileId { get; }
        public bool EdgeInteractionOnly { get; }
    }

    #endregion
}

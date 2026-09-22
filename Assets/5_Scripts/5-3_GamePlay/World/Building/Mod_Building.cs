using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlatWorld.Gameplay.Building;
using FlatWorld.Gameplay.Progress;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum BuildingState
{
    NotInstalled,
    Installing,
    Installed,
    Damaged,
    Uninstalling,
    Uninstalled
}

/// <summary>同一个物品预制体的两种明确用途，禁止再通过血量推断。</summary>
public enum BuildingRole
{
    Summoner,
    PlacedBuilding
}

/// <summary>
/// 建筑召唤器是持久化载体，PlacedBuilding 是快照还原后的世界实例。
/// 拆除时先生成带快照的召唤器，成功后才删除原建筑。
/// </summary>
public partial class Mod_Building : Module, IIncomingDamageRule
{
    private const int CurrentDataVersion = 3;
    private const string StoneWallBuildingId = "Wall_Stone";
    private const string StoneWallTileBlockId = "TileBase_BuiltStoneWall";
    public const string SummonerPrefabSuffix = "_Summoner";
    public static int CurrentBuildingDataVersion => CurrentDataVersion;
    private const uint BlockedTilePenalty = 1000;
    private const float BoundsEpsilon = 0.001f;
    // 格心吸附相对准星原始落点在单轴上的最大偏差。
    private const float PlacementCellHalfExtent = 0.5f;
    private const int MaxEmbeddedSnapshotBytes = 320 * 1024;
    private const string StatefulSummonerPrefix = "building-snapshot:";
    private const string BuildingCollisionLayerName = "Collider";

    [Serializable]
    public class Building_Data
    {
        // 不设置字段初值，旧 JSON 缺少 Version 时保持 0，才能正确迁移。
        public int Version;
        public float maxVisibleDistance = Mod_InteractSender.DefaultMaxInteractDistance;
        public float minVisibleDistance = 1f;
        public BuildingState State = BuildingState.NotInstalled;
        public BuildingRole Role = BuildingRole.Summoner;
        public string SnapshotBase64;
        public string BuildingPrefabId;
        public string SummonerPrefabId;
        // 手持物与建筑共同持有的状态模块；放置/拆回时按稳定 ID 转移当前数据。
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string[] SharedModuleIds;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string TileBlockId;
    }

    public Building_Data Data = new();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public bool RequiresPlacementRequest; // 可手持使用的设施先从面板进入放置模式，避免与使用动作冲突。
    private bool _placementRequested;
    private GameObject _definitionPreviewSource;
    private string _definitionPreviewItemId;
    public BoxCollider2D boxCollider2D;
    // 伤害模块由 ItemMods 注册表提供，禁止序列化嵌套 Prefab 的组件引用。
    [NonSerialized]
    private DamageReceiver damageReceiver;
    [NonSerialized]
    private ShadowCaster2D _lightOccluder;
    private static readonly FieldInfo ShadowCasterShapePathField = ResolveShadowCasterField("m_ShapePath");
    private static readonly FieldInfo ShadowCasterShapePathHashField = ResolveShadowCasterField("m_ShapePathHash");
    public UltEvent StartInstall = new();
    public UltEvent StartUnInstall = new();
    public UltEvent<BuildingState, BuildingState> OnStateChanged = new();

    [SerializeField] private BuildingState _currentState = BuildingState.NotInstalled;
    private bool _eventsBound;
    private bool _isLoaded;
    private bool _placementPending;
    private bool _dismantlePending;
    private bool _ghostCreationFailed;
    private GameController _ownerController;
    private Player _placementActor;

    public override ModuleData _Data
    {
        get => BuildingData;
        set => BuildingData = (Ex_ModData)value;
    }

    public bool IsSummoner => Data != null && Data.Role == BuildingRole.Summoner;
    public bool IsItemInInventory => IsSummoner && item != null && item.itemData != null && item.InHand && item.Owner != null;
    public bool IsPlacementPending => _placementPending;
    public bool IsDismantlePending => _dismantlePending;
    public bool IsPlacementModeActive => IsSummoner && (!RequiresPlacementRequest || _placementRequested);

    /// <summary>落地建筑在完成自身防御结算后应用攻击方的建筑伤害倍率。</summary>
    public float GetDamageMultiplier(IDamageSender sender)
    {
        if (Data?.Role != BuildingRole.PlacedBuilding)
            return 1f;

        return sender is IBuildingDamageSource buildingDamageSource
            ? Mathf.Max(0f, buildingDamageSource.BuildingDamageMultiplier)
            : 1f;
    }

    /// <summary>
    /// 便携设施的召唤器和落地本体使用不同稳定 Item ID；当前实例角色必须与自己的载体 ID 一致，
    /// 不能沿用其它同类实例曾经写入的角色状态。
    /// </summary>
    public void ReconcileCarrierRoleWithItemIdentity()
    {
        if (Data == null || item?.itemData == null)
            return;

        string carrierId = item.itemData.IDName;
        if (string.IsNullOrWhiteSpace(carrierId) ||
            string.IsNullOrWhiteSpace(Data.BuildingPrefabId) ||
            string.IsNullOrWhiteSpace(Data.SummonerPrefabId) ||
            string.Equals(Data.BuildingPrefabId, Data.SummonerPrefabId, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(carrierId, Data.SummonerPrefabId, StringComparison.Ordinal))
            Data.Role = BuildingRole.Summoner;
        else if (string.Equals(carrierId, Data.BuildingPrefabId, StringComparison.Ordinal))
            Data.Role = BuildingRole.PlacedBuilding;
    }

    /// <summary>仅为快捷栏真实手持实例开启放置预览；切换物品后由卸载/加载生命周期取消。</summary>
    public bool BeginPlacement()
    {
        ReconcileCarrierRoleWithItemIdentity();
        if (!IsItemInInventory || _placementPending || item.DestructionHandled)
            return false;
        _placementRequested = true;
        return true;
    }

    /// <summary>便携设施在放置模式下独占本次使用动作，失败时也不能同时打开面板或装水。</summary>
    public bool TryHandlePlacementAction()
    {
        if (!RequiresPlacementRequest || !IsPlacementModeActive)
            return false;
        Install();
        return true;
    }
    /// <summary>进入放置模式即拥有右键；非法或隐藏的预览也必须进入安装校验以给出拒绝反馈。</summary>
    public bool IsPlacementActionAvailable
    {
        get
        {
            return IsItemInInventory && !_placementPending && IsPlacementModeActive;
        }
    }
    public bool CanCommitDismantle => Data?.Role == BuildingRole.PlacedBuilding &&
                                      CurrentState is BuildingState.Installed or BuildingState.Damaged or
                                          BuildingState.Uninstalling;

    public BuildingState CurrentState
    {
        get => _currentState;
        private set
        {
            if (_currentState == value)
                return;

            BuildingState previous = _currentState;
            _currentState = value;
            Data ??= new Building_Data();
            Data.State = value;
            OnStateChanged?.Invoke(previous, value);
        }
    }

    public override void Awake()
    {
        if (_Data != null)
            _Data.ID = ModText.Building;
    }

    protected void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.Building;
    }

    public override void Load()
    {
        _placementRequested = false;
        _ownerController = null;
        EnsureRuntimeReferences();
        Data = new Building_Data();
        BuildingData?.ReadData(ref Data);
        Data ??= new Building_Data();
        MigrateLegacyData(Data, item?.itemData?.IDName);
        ReconcileCarrierRoleWithItemIdentity();
        _currentState = Data.State;

        UnbindRuntimeEvents();
        BindRuntimeEvents();
        _isLoaded = true;
        SyncRuntimeState();
    }

    public override void Save()
    {
        if (BuildingData == null || item?.itemData?.ModuleDataDic == null)
            return;

        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.State = _currentState;
        BuildingData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = BuildingData;
        SaveDataMgr.Instance?.RecordRuntimeBuildingChange(item);
    }

    public override void ApplyNetworkData(ModuleData data)
    {
        if (data is not Ex_ModData networkData)
            return;

        BuildingData = networkData;
        Data = new Building_Data();
        BuildingData.ReadData(ref Data);
        Data ??= new Building_Data();
        MigrateLegacyData(Data, item?.itemData?.IDName);
        ReconcileCarrierRoleWithItemIdentity();
        _currentState = Data.State;

        if (item != null)
        {
            EnsureRuntimeReferences();
            SyncRuntimeState();
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!_isLoaded || item == null)
            return;

        if (!IsItemInInventory || _placementPending || !IsPlacementModeActive)
        {
            if (!IsItemInInventory)
                _placementRequested = false;
            CleanupGhost();
            return;
        }

        SetColliderMode(enabled: false, trigger: true);
        if (CurrentState == BuildingState.Uninstalled)
            CurrentState = BuildingState.NotInstalled;

        HandleGhostShadow();
    }

    public void OnEnable()
    {
        if (!_isLoaded)
            return;

        EnsureRuntimeReferences();
        BindRuntimeEvents();
        SyncNavigationOccupancy();
        SyncLightOccluder();
    }

    public void OnDisable()
    {
        CleanupGhost();
        BuildingOccupancyRegistry.Unregister(this);
        if (_lightOccluder != null)
            _lightOccluder.enabled = false;
        UnbindRuntimeEvents();
    }

    public void OnDestroy()
    {
        CleanupGhost();
        BuildingOccupancyRegistry.Unregister(this);
        UnbindRuntimeEvents();
    }

    [Button]
    public virtual void Install()
    {
        if (item == null || item.DestructionHandled || _placementPending || !IsItemInInventory || !IsPlacementModeActive)
            return;

        // 越界时虚影已被销毁，必须先用当前准线判定范围，不能提前返回“预览尚未就绪”。
        Vector3 pointedPlacement = NormalizePlacement(GetPointerWorldPosition());
        if (!IsWithinPlacementDistance(GetAuthorityPosition(), pointedPlacement, GetMaxPlacementDistance()))
        {
            BuildingPlacementFeedbackEvents.PublishPlacementRejected(
                ResolvePlacementActor(), BuildingPlacementFailureReason.OutOfRange);
            return;
        }

        if (!TryGetGhostPlacementPosition(out Vector3 placement))
        {
            Debug.LogWarning("[建筑安装] 放置预览尚未就绪", item);
            return;
        }

        if (!ValidatePlacement(placement, GetAuthorityPosition(), true, out string reason, out BuildingPlacementFailureReason failureReason))
        {
            BuildingPlacementFeedbackEvents.PublishPlacementRejected(
                ResolvePlacementActor(), failureReason);
            Debug.LogWarning($"[建筑安装] {reason}", item);
            return;
        }

        CurrentState = BuildingState.Installing;
        StartInstall?.Invoke();
        _placementPending = true;
        _placementActor = ResolvePlacementActor();

        if (ItemNetworkStateSerialization.BeginNetworkBuilding(this, placement))
        {
            CleanupGhost();
            return;
        }

        _placementPending = false;
        if (!string.IsNullOrWhiteSpace(Data.TileBlockId))
        {
            if (!TileBuildingSystem.TryPlace(
                    placement,
                    Data.TileBlockId,
                    out TileBuildingCell placedCell,
                    out reason))
            {
                _placementActor = null;
                CurrentState = BuildingState.NotInstalled;
                Save();
                Debug.LogWarning($"[格子建筑安装] {reason}", item);
                return;
            }

            Player actor = _placementActor;
            string buildingId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
            _placementActor = null;
            CurrentState = BuildingState.NotInstalled;
            Save();
            if (ConsumeOneSourceItem())
            {
                RuntimeGrassClearing.ClearAt(placement);
                GameplayProgressEvents.PublishBuildingPlaced(actor, buildingId);
                return;
            }

            TileBuildingSystem.TryRemove(placedCell, spawnDrop: false, out _);
            Debug.LogWarning("[格子建筑安装] 消耗建造材料失败，已回滚地块", item);
            return;
        }

        if (!TryCreateInstalledBuilding(placement, out Item building, out reason))
        {
            _placementActor = null;
            CurrentState = BuildingState.NotInstalled;
            Save();
            Debug.LogWarning($"[建筑安装] {reason}", item);
            return;
        }

        CompletePlacementTransaction(building);
    }

    /// <summary>服务端在候选建筑生成后调用，不依赖客户端预览。</summary>
    public bool ValidateAuthoritativePlacement(Vector3 authorityPosition, out string reason)
    {
        Vector3 position = item != null ? item.transform.position : transform.position;
        return ValidatePlacement(position, authorityPosition, false, out reason);
    }

    public void CompleteNetworkPlacement(float authoritativeRemainingAmount)
    {
        if (!_placementPending)
            return;

        Player actor = _placementActor;
        string buildingId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        _placementPending = false;
        _placementActor = null;
        CurrentState = BuildingState.NotInstalled;
        Save();
        ApplySourceAmount(authoritativeRemainingAmount);
        GameplayProgressEvents.PublishBuildingPlaced(actor, buildingId);
    }

    public void RejectNetworkPlacement(string reason)
    {
        if (!_placementPending)
            return;

        _placementPending = false;
        _placementActor = null;
        CurrentState = BuildingState.NotInstalled;
        Save();
        if (!string.IsNullOrWhiteSpace(reason))
            Debug.LogWarning($"[联机建造] 放置被拒绝：{reason}", item);
    }

    public bool TryCreateInstalledBuilding(Vector3 position, out Item building, out string reason)
    {
        building = null;
        reason = null;
        if (ItemMgr.Instance == null || item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
        {
            reason = "召唤器不足或物品管理器尚未就绪";
            return false;
        }

        item.Save();
        if (!TryCreatePlacementCandidateData(item.itemData, position, out ItemData placedData,
                out bool restoredSnapshot, out reason))
        {
            return false;
        }

        try
        {
            BuildingPlacementLifecycle.GetExtension(item)?.PreparePlacedData(placedData);
            building = ItemMgr.Instance.InstantiateItem(
                placedData,
                placedData.transform.position,
                placedData.transform.rotation,
                placedData.transform.scale);
            building.Load();

            Mod_Building module = building.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
            if (module == null)
                throw new MissingComponentException($"{building.name} 缺少建筑模块");

            module.SetAsInstalled(initializeHealth: !restoredSnapshot);
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            if (building != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(building, false);
            building = null;
            return false;
        }
    }

    /// <summary>只构造候选数据。联机服务端可先实例化、校验位置，再提交安装。</summary>
    public static bool TryCreatePlacementCandidateData(
        ItemData summonerData,
        Vector3 position,
        out ItemData placedData,
        out bool restoredSnapshot,
        out string reason)
    {
        placedData = null;
        restoredSnapshot = false;
        reason = null;

        if (!TryReadBuildingData(summonerData, out _, out Building_Data carrierState))
        {
            reason = "物品不包含有效建筑模块";
            return false;
        }

        MigrateLegacyData(carrierState, summonerData.IDName);
        if (carrierState.Role != BuildingRole.Summoner)
        {
            reason = "只有建筑召唤器可以安装建筑";
            return false;
        }

        string buildingPrefabId = ResolveBuildingPrefabId(summonerData.IDName, carrierState);
        if (string.IsNullOrWhiteSpace(buildingPrefabId) || GameRes.Instance?.GetPrefab(buildingPrefabId) == null)
        {
            reason = $"找不到召唤器对应的建筑预制体：{buildingPrefabId}";
            return false;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(carrierState.SnapshotBase64))
            {
                byte[] payload = Convert.FromBase64String(carrierState.SnapshotBase64);
                if (!ItemNetworkStateSerialization.TryDeserializeItemData(payload, out placedData))
                {
                    reason = "召唤器内的建筑快照无效";
                    return false;
                }

                restoredSnapshot = true;
            }
            else
            {
                placedData = GameRes.Instance.CreateItemData(buildingPrefabId);
            }

            // 即使带有拆除快照，也以当前手持容器的数据覆盖共享模块，不能恢复旧水量/旧库存。
            BuildingModuleStateTransfer.Copy(summonerData, placedData, carrierState.SharedModuleIds);
            CopySharedDurability(summonerData, placedData, carrierState.SharedModuleIds);
        }
        catch (Exception exception)
        {
            reason = $"构建建筑数据失败：{exception.Message}";
            return false;
        }

        if (placedData == null || !string.Equals(placedData.IDName, buildingPrefabId, StringComparison.Ordinal) ||
            placedData.Stack == null)
        {
            reason = "召唤器快照与建筑预制体不匹配";
            placedData = null;
            return false;
        }

        if (!WriteBuildingData(placedData, state =>
            {
                state.Version = CurrentDataVersion;
                state.Role = BuildingRole.PlacedBuilding;
                state.State = BuildingState.NotInstalled;
                state.SnapshotBase64 = null;
                state.BuildingPrefabId = buildingPrefabId;
                state.SummonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, carrierState);
            }))
        {
            reason = "建筑快照缺少建筑模块";
            placedData = null;
            return false;
        }

        placedData.Guid = GenerateUniqueRuntimeGuid();
        placedData.inHand = false;
        placedData.Stack.Amount = 1f;
        placedData.Stack.CanBePickedUp = false;
        placedData.transform ??= new ItemTransform();
        placedData.transform.position = NormalizePlacement(position);
        // 手持物会因交互表现产生旋转；落地建筑必须使用固定世界朝向，不能继承手持姿态或快照根旋转。
        placedData.transform.rotation = Quaternion.identity;
        placedData.transform.scale = Vector3.one;
        return true;
    }

    public static bool IsValidSummonerData(ItemData itemData, out string reason)
    {
        reason = null;
        if (!TryReadBuildingData(itemData, out _, out Building_Data data))
        {
            reason = "物品不包含建筑模块";
            return false;
        }

        MigrateLegacyData(data, itemData.IDName);
        if (data.Role != BuildingRole.Summoner)
        {
            reason = "物品不是建筑召唤器";
            return false;
        }

        if ((!string.IsNullOrWhiteSpace(data.SnapshotBase64) || data.SharedModuleIds?.Length > 0) &&
            itemData.Stack?.Amount != 1f)
        {
            reason = "带状态的建筑召唤器必须为单件";
            return false;
        }

        return true;
    }

    /// <summary>旧调用兼容：把候选数据显式标记为世界建筑。</summary>
    public static void PreparePlacementCandidateData(ItemData itemData)
    {
        if (!TryReadBuildingData(itemData, out _, out Building_Data currentState))
            return;

        MigrateLegacyData(currentState, itemData.IDName);
        string buildingPrefabId = ResolveBuildingPrefabId(itemData.IDName, currentState);
        WriteBuildingData(itemData, state =>
        {
            state.Version = CurrentDataVersion;
            state.Role = BuildingRole.PlacedBuilding;
            state.State = BuildingState.NotInstalled;
            state.SnapshotBase64 = null;
            state.BuildingPrefabId = buildingPrefabId;
            state.SummonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, state);
        });
        itemData.IDName = buildingPrefabId;
    }

    [Button]
    public virtual void UnInstall()
    {
        if (_dismantlePending || Data?.Role != BuildingRole.PlacedBuilding || !IsInstalled())
            return;

        BuildingState previousState = CurrentState;
        CurrentState = BuildingState.Uninstalling;
        StartUnInstall?.Invoke();
        _dismantlePending = true;

        if (ItemNetworkStateSerialization.BeginNetworkBuildingDismantle(this))
            return;

        _dismantlePending = false;
        string reason;
        bool created = DroppedItemService.UsesEntities
            ? TryCreateDismantledEcsDrop(out reason)
            : TryCreateDismantledSummoner(out _, out reason);
        if (!created)
        {
            CurrentState = previousState;
            Save();
            Debug.LogWarning($"[建筑拆除] {reason}", item);
            return;
        }

        BuildingOccupancyRegistry.Unregister(this);
        ItemMgr.Instance.DespawnItem(item, false);
    }

    /// <summary>生成带完整建筑快照的世界召唤器，不删除当前建筑。</summary>
    public bool TryCreateDismantledSummoner(out Item summoner, out string reason)
    {
        summoner = null;
        reason = null;
        if (ItemMgr.Instance == null || item?.itemData == null || Data?.Role != BuildingRole.PlacedBuilding)
        {
            reason = "当前对象不是可拆除的世界建筑";
            return false;
        }

        if (!TryCapturePlacedSnapshot(out string snapshotBase64, out reason))
            return false;

        try
        {
            Vector3 dropPosition = NormalizePlacement(item.transform.position);
            string buildingPrefabId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
            string summonerPrefabId = ResolveSummonerPrefabId(buildingPrefabId, Data);
            ItemData summonerData = GameRes.Instance?.CreateItemData(summonerPrefabId);
            if (summonerData == null)
                throw new InvalidOperationException($"找不到建筑召唤器预制体：{summonerPrefabId}");

            summonerData.IDName = summonerPrefabId;
            summonerData.Guid = GenerateUniqueRuntimeGuid();
            summonerData.inHand = false;
            summonerData.Stack ??= new ItemStack();
            summonerData.Stack.Amount = 1f;
            summonerData.Stack.CanBePickedUp = true;
            summonerData.transform ??= new ItemTransform();
            summonerData.transform.position = dropPosition;
            summonerData.transform.rotation = NormalizeBuildingRotation(item.transform.rotation);
            summonerData.transform.scale = DroppedItemService.ResolveDefaultWorldDropScale(summonerData);

            // 在 Load 之前还原模块数据，使拆回后的手持面板立即读到建筑中的最新状态。
            BuildingModuleStateTransfer.Copy(item.itemData, summonerData, Data.SharedModuleIds);
            CopySharedDurability(item.itemData, summonerData, Data.SharedModuleIds);

            summoner = ItemMgr.Instance.InstantiateItem(
                summonerData,
                dropPosition,
                summonerData.transform.rotation,
                summonerData.transform.scale);
            summoner.Load();

            Mod_Building summonerModule = summoner.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
            if (summonerModule == null)
                throw new MissingComponentException($"{summoner.name} 缺少建筑模块");

            summonerModule.ConfigureAsSummoner(snapshotBase64);
            summoner.DropInRange();
            summoner.Save();
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(summoner);
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            if (summoner != null && ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(summoner, false);
            summoner = null;
            return false;
        }
    }

    public void CompleteNetworkDismantle()
    {
        _dismantlePending = false;
    }

    /// <summary>迁移制作材料品质；手钻继续兼容其独立模块保存的动态耐久。</summary>
    private static void CopySharedDurability(ItemData source, ItemData target, IReadOnlyCollection<string> sharedModuleIds)
    {
        CraftedDurabilityQuality.CopyInstanceQuality(source, target);
        if (source == null || target == null || sharedModuleIds == null || !sharedModuleIds.Contains(Mod_HandDrill.ModuleId))
            return;
        target.MaxDurability = Mathf.Max(0f, source.MaxDurability);
        target.Durability = Mathf.Clamp(source.Durability, 0f, target.MaxDurability);
    }

    public void RejectNetworkDismantle(string reason)
    {
        if (!_dismantlePending)
            return;

        _dismantlePending = false;
        CurrentState = damageReceiver != null && damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        Save();
        if (!string.IsNullOrWhiteSpace(reason))
            Debug.LogWarning($"[联机拆除] 请求被拒绝：{reason}", item);
    }

    public void SetAsInstalled(bool initializeHealth = true)
    {
        EnsureRuntimeReferences();
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = BuildingRole.PlacedBuilding;
        Data.SnapshotBase64 = null;
        Data.BuildingPrefabId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        Data.SummonerPrefabId = ResolveSummonerPrefabId(Data.BuildingPrefabId, Data);

        item.transform.position = NormalizePlacement(item.transform.position);
        item.transform.rotation = Quaternion.identity;
        item.transform.localScale = Vector3.one;
        item.SetInHand(false);

        if (item.itemData?.Stack != null)
        {
            item.itemData.Stack.Amount = 1f;
            item.itemData.Stack.CanBePickedUp = false;
        }

        if (initializeHealth || damageReceiver.Hp <= 0f)
            damageReceiver.Hp = damageReceiver.MaxHp > 0f ? damageReceiver.MaxHp : 100f;

        SetColliderMode(enabled: true, trigger: false);
        CleanupGhost();
        CurrentState = damageReceiver.Hp > 0f && damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        SyncNavigationOccupancy();
        SyncLightOccluder();
        damageReceiver.Save();
        Save();
    }

#if UNITY_EDITOR
    [Button("设置为已安装状态（编辑器调试）")]
    public void SetAsInstalledEditor() => SetAsInstalled();
#endif

    public bool IsInstalled()
        => Data?.Role == BuildingRole.PlacedBuilding &&
           CurrentState is BuildingState.Installed or BuildingState.Damaged;

    public string GetStateDescription()
    {
        return CurrentState switch
        {
            BuildingState.NotInstalled => "未安装",
            BuildingState.Installing => "安装中",
            BuildingState.Installed => "已安装",
            BuildingState.Damaged => "损坏中",
            BuildingState.Uninstalling => "拆除中",
            BuildingState.Uninstalled => "召唤器",
            _ => "未知状态"
        };
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }

        if (_definitionPreviewSource != null)
        {
            Destroy(_definitionPreviewSource);
            _definitionPreviewSource = null;
            _definitionPreviewItemId = null;
        }
    }

    public void ReleasePlacementOccupancy()
    {
        BuildingOccupancyRegistry.Unregister(this);
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        Transform target = root != null ? root : item != null ? item.transform : transform;
        Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = enable;
    }

    private void ConfigureAsSummoner(string snapshotBase64)
    {
        EnsureRuntimeReferences();
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = BuildingRole.Summoner;
        Data.State = BuildingState.Uninstalled;
        Data.SnapshotBase64 = snapshotBase64;
        Data.BuildingPrefabId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        Data.SummonerPrefabId = ResolveSummonerPrefabId(Data.BuildingPrefabId, Data);
        _currentState = BuildingState.Uninstalled;

        item.SetInHand(false);
        item.itemData.inHand = false;
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = true;
        item.itemData.ItemSpecialData = StatefulSummonerPrefix + item.itemData.Guid;
        SetColliderMode(enabled: true, trigger: true, damageReceiverEnabled: false);
        BuildingOccupancyRegistry.Unregister(this);
        SyncLightOccluder();
        Save();
    }

    private bool TryCapturePlacedSnapshot(out string snapshotBase64, out string reason)
    {
        snapshotBase64 = null;
        reason = null;

        try
        {
            item.Save();
            ItemData snapshot = FastCloner.FastCloner.DeepClone(item.itemData);
            if (!WriteBuildingData(snapshot, state =>
                {
                    state.Version = CurrentDataVersion;
                    state.Role = BuildingRole.PlacedBuilding;
                    state.State = damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
                        ? BuildingState.Damaged
                        : BuildingState.Installed;
                    state.BuildingPrefabId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
                    state.SummonerPrefabId = ResolveSummonerPrefabId(state.BuildingPrefabId, Data);
                    // 防止快照递归包含自己。
                    state.SnapshotBase64 = null;
                }))
            {
                reason = "建筑数据缺少建筑模块";
                return false;
            }

            snapshot.inHand = false;
            snapshot.Stack.Amount = 1f;
            snapshot.Stack.CanBePickedUp = false;
            snapshot.transform ??= new ItemTransform();
            snapshot.transform.position = item.transform.position;
            snapshot.transform.rotation = item.transform.rotation;
            snapshot.transform.scale = item.transform.localScale;
            BuildingPlacementLifecycle.GetExtension(item)?.PrepareRepackedSnapshot(snapshot);

            if (!ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out byte[] payload) ||
                payload.Length > MaxEmbeddedSnapshotBytes)
            {
                reason = "建筑数据过大或无法序列化";
                return false;
            }

            snapshotBase64 = Convert.ToBase64String(payload);
            return true;
        }
        catch (Exception exception)
        {
            reason = $"保存建筑快照失败：{exception.Message}";
            return false;
        }
    }

    private void CompletePlacementTransaction(Item building)
    {
        if (building == null)
        {
            _placementActor = null;
            CurrentState = BuildingState.NotInstalled;
            Save();
            return;
        }

        CurrentState = BuildingState.NotInstalled;
        Save();
        if (ConsumeOneSourceItem())
        {
            RuntimeGrassClearing.ClearAt(building.transform.position);
            BuildingPlacementLifecycle.NotifyCommitted(building);
            GameplayProgressEvents.PublishBuildingPlaced(
                _placementActor,
                building.itemData?.IDName);
            _placementActor = null;
            return;
        }

        _placementActor = null;

        building.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy();
        ItemMgr.Instance?.DespawnItem(building, false);
    }

    private bool ConsumeOneSourceItem()
    {
        if (item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
            return false;

        if (!TryGetSourceHotBarSlot(out Inventory_HotBar hotBar, out ItemSlot sourceSlot) ||
            !hotBar.Data.TryConsumeFromSlot(sourceSlot, 1, out ItemData consumedData))
        {
            return false;
        }

        RefreshSourceHotBarSlot(hotBar, sourceSlot);

        bool depleted = consumedData?.Stack == null || consumedData.Stack.Amount <= 0f;
        if (depleted)
        {
            CleanupGhost();
            hotBar.RuntimeInventory.SyncHeldItemImmediately();
        }
        else
        {
            hotBar.NotifyOwnerNetworkStateChanged();
        }

        return true;
    }

    private Player ResolvePlacementActor()
    {
        Item owner = item?.Owner;
        return owner as Player ?? owner?.GetComponentInParent<Player>();
    }

    private void ApplySourceAmount(float amount)
    {
        if (item?.itemData?.Stack == null)
            return;

        if (!TryGetSourceHotBarSlot(out Inventory_HotBar hotBar, out ItemSlot sourceSlot))
        {
            Debug.LogError("[建筑安装] 无法解析召唤器所属快捷栏槽位，不能应用权威数量", item);
            return;
        }

        float normalizedAmount = Mathf.Max(0f, amount);
        if (!hotBar.Data.TrySetSlotItemAmount(sourceSlot, item.itemData, normalizedAmount))
        {
            Debug.LogError("[建筑安装] 应用召唤器权威数量失败", item);
            return;
        }

        RefreshSourceHotBarSlot(hotBar, sourceSlot);

        if (normalizedAmount > 0f)
        {
            hotBar.NotifyOwnerNetworkStateChanged();
            return;
        }

        CleanupGhost();
        hotBar.RuntimeInventory.SyncHeldItemImmediately();
    }

    /// <summary>建筑扣料后直接刷新快捷栏表现，不能只依赖通用库存事件等待后续 UI 同步。</summary>
    private static void RefreshSourceHotBarSlot(Inventory_HotBar hotBar, ItemSlot sourceSlot)
    {
        if (hotBar?.Data?.itemSlots == null || sourceSlot == null)
            return;

        int slotIndex = hotBar.Data.itemSlots.IndexOf(sourceSlot);
        if (slotIndex >= 0)
            hotBar.RefreshUI(slotIndex);
    }

    /// <summary>建筑召唤器只允许从玩家当前快捷栏真实槽位扣除。</summary>
    private bool TryGetSourceHotBarSlot(out Inventory_HotBar hotBar, out ItemSlot sourceSlot)
    {
        hotBar = item?.Owner?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        sourceSlot = hotBar?.CurrentSelectItemSlot;
        if (hotBar?.Data?.itemSlots == null || sourceSlot == null || !hotBar.Data.itemSlots.Contains(sourceSlot))
            return false;

        ItemData slotData = sourceSlot.itemData;
        ItemData heldData = item?.itemData;
        return slotData != null && heldData != null &&
               (ReferenceEquals(slotData, heldData) || (heldData.Guid != 0 && slotData.Guid == heldData.Guid));
    }

    private bool ValidatePlacement(
        Vector3 position,
        Vector3 authorityPosition,
        bool requireGhostClear,
        out string reason)
        => ValidatePlacement(position, authorityPosition, requireGhostClear, out reason, out _);

    private bool ValidatePlacement(Vector3 position, Vector3 authorityPosition, bool requireGhostClear,
        out string reason, out BuildingPlacementFailureReason failureReason)
    {
        reason = null;
        failureReason = BuildingPlacementFailureReason.InvalidPosition;
        position = NormalizePlacement(position);

        if (!IsFinite(position) || !IsFinite(authorityPosition))
        {
            reason = "坐标无效";
            return false;
        }

        if (item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
        {
            reason = "召唤器数量不足";
            return false;
        }

        float maximumPlacementDistance = GetMaxPlacementDistance();
        if (!IsWithinPlacementDistance(authorityPosition, position, maximumPlacementDistance))
        {
            reason = "目标超出建造距离";
            failureReason = BuildingPlacementFailureReason.OutOfRange;
            return false;
        }

        if (requireGhostClear && GhostShadow == null)
        {
            reason = "放置预览尚未就绪";
            return false;
        }

        Vector2Int placementCell = GetPlacementCell(position);
        IBuildingPlacementExtension placementExtension = BuildingPlacementLifecycle.GetExtension(item);
        if (placementExtension != null && !placementExtension.ValidatePlacement(placementCell, out reason))
            return false;
        if (!string.IsNullOrWhiteSpace(Data?.TileBlockId))
        {
            // 格子建筑的合法性以建筑阻挡层数据为准，不能再用 TilemapCollider2D 的边界判断相邻格。
            if (!TileBuildingSystem.CanPlace(position, Data.TileBlockId, out reason))
                return false;
            // 地表铺设独立校验来源格与占用，不能再用水格的原通行代价否决平台。
            if (TileBuildingSystem.IsGroundPlacement(Data.TileBlockId))
                return true;
        }

        // 动态可交互建筑与墙体统一按离散格层判断占用；物理 Collider 不再参与放置合法性。
        if (!BuildingOccupancyRegistry.CanPlace(placementCell, this, out reason))
        {
            return false;
        }

        return CheckTilePenalties(placementCell, out reason);
    }

    private bool CheckTilePenalties(Vector2Int worldCell, out string reason)
    {
        reason = null;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
        {
            reason = "区块管理器尚未就绪";
            return false;
        }

        worldCell = WorldTopologyRuntime.NormalizeCell(worldCell);
        Vector2 tileCenter = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
        if (chunkManager.TryGetRuntimeTerrainTile(
                tileCenter, out RuntimeTerrainTileSample runtimeTile))
        {
            if (runtimeTile.TopTileId == 0)
            {
                reason = $"地块 ({worldCell.x},{worldCell.y}) 不可建造";
                return false;
            }

            if (!runtimeTile.Terrain.IsWalkable(
                    runtimeTile.LocalCell.x, runtimeTile.LocalCell.y) ||
                runtimeTile.Cell.NavigationCost > BlockedTilePenalty)
            {
                reason = $"地块 ({worldCell.x},{worldCell.y}) 不可通行";
                return false;
            }
        }
        else
        {
            // 旧 Map 世界仍保留兼容回退；新区块权威不依赖表现对象。
            chunkManager.GetChunkBy_ItemPosition(tileCenter, out Chunk chunk);
            if (chunk?.Map?.Data == null)
            {
                reason = $"地块 ({worldCell.x},{worldCell.y}) 尚未加载";
                return false;
            }

            TileData topTile = chunk.Map.Data.GetTopTile(worldCell);
            if (topTile == null)
            {
                reason = $"地块 ({worldCell.x},{worldCell.y}) 不可建造";
                return false;
            }

            if (!topTile.IsWalkable || topTile.Penalty > BlockedTilePenalty)
            {
                reason = $"地块 ({worldCell.x},{worldCell.y}) 不可通行";
                return false;
            }
        }

        return true;
    }

    private void EnsureRuntimeReferences()
    {
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_Building] 未找到所属 Item");

        // 通用建筑本体的实体碰撞体只允许来自 Item 根节点；Module_Building 自身不再携带第二套碰撞体。
        if (boxCollider2D == null) boxCollider2D = item.GetComponent<BoxCollider2D>();
        if (boxCollider2D == null) boxCollider2D = GetComponent<BoxCollider2D>();
        if (boxCollider2D == null) boxCollider2D = item.GetComponentInChildren<BoxCollider2D>(true);

        damageReceiver = item.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[Mod_Building] {item.name} 缺少 DamageReceiver 模块");
    }

    #region 2D 光照遮挡

    /// <summary>
    /// 动态可交互建筑不是 Tilemap 的一部分，不能复用 ChunkLightOccluderRenderer；
    /// 落地后按主体 Sprite 的真实轮廓创建 ShadowCaster2D，使火把等 Point Light2D 能得到可见建筑阴影。
    /// </summary>
    private void SyncLightOccluder()
    {
        bool shouldOcclude = item != null &&
                             Data?.Role == BuildingRole.PlacedBuilding &&
                             !item.InHand &&
                             item.gameObject.activeInHierarchy;

        if (!shouldOcclude)
        {
            if (_lightOccluder != null)
                _lightOccluder.enabled = false;
            return;
        }

        SpriteRenderer sourceRenderer = item.Sprite;
        if (sourceRenderer == null || sourceRenderer.sprite == null)
            sourceRenderer = item.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(renderer => renderer != null && renderer.sprite != null);
        if (sourceRenderer == null)
        {
            if (_lightOccluder != null)
                _lightOccluder.enabled = false;
            return;
        }

        GameObject host = sourceRenderer.gameObject;
        if (_lightOccluder != null && _lightOccluder.gameObject != host)
        {
            _lightOccluder.enabled = false;
            _lightOccluder = null;
        }

        // 兼容上一版在物理碰撞体节点创建的运行时遮挡器；改用视觉轮廓后必须关闭旧矩形投影。
        ShadowCaster2D legacyColliderCaster = boxCollider2D != null
            ? boxCollider2D.GetComponent<ShadowCaster2D>()
            : null;
        if (legacyColliderCaster != null && legacyColliderCaster.gameObject != host)
            legacyColliderCaster.enabled = false;

        if (_lightOccluder == null)
            _lightOccluder = host.GetComponent<ShadowCaster2D>();
        if (_lightOccluder == null)
            _lightOccluder = host.AddComponent<ShadowCaster2D>();

        _lightOccluder.castsShadows = true;
        _lightOccluder.selfShadows = true;
        _lightOccluderSource = sourceRenderer;
        Light2DSortingLayerUtility.SetShadowLayers(_lightOccluder,
            Light2DSortingLayerUtility.ResolveLayerIds(ShadowTargetSortingLayers));
        RefreshLightOccluderGeometry(true);
    }

    private static void SyncShadowCasterShape(ShadowCaster2D shadowCaster, SpriteRenderer sourceRenderer)
    {
        Sprite sprite = sourceRenderer.sprite;
        int shapeCount = sprite.GetPhysicsShapeCount();
        if (shapeCount <= 0)
            throw new InvalidOperationException($"[Mod_Building] Sprite {sprite.name} 没有可用的物理轮廓，无法生成光照遮挡形状");

        var points = new List<Vector2>();
        Vector2[] bestPath = null;
        float bestArea = -1f;

        for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
        {
            points.Clear();
            sprite.GetPhysicsShape(shapeIndex, points);
            if (points.Count < 3)
                continue;

            float area = CalculatePolygonArea(points);
            if (area <= bestArea)
                continue;

            bestArea = area;
            bestPath = points.ToArray();
        }

        if (bestPath == null)
            throw new InvalidOperationException($"[Mod_Building] Sprite {sprite.name} 的物理轮廓无有效多边形");

        var shapePath = new Vector3[bestPath.Length];
        for (int i = 0; i < bestPath.Length; i++)
        {
            Vector2 point = bestPath[i];
            if (sourceRenderer.flipX)
                point.x = -point.x;
            if (sourceRenderer.flipY)
                point.y = -point.y;
            shapePath[i] = point;
        }

        if (sourceRenderer.flipX ^ sourceRenderer.flipY)
            Array.Reverse(shapePath);

        ShadowCasterShapePathField.SetValue(shadowCaster, shapePath);
        ShadowCasterShapePathHashField.SetValue(shadowCaster, CalculateShadowPathHash(shapePath));
    }

    private static float CalculatePolygonArea(IReadOnlyList<Vector2> points)
    {
        float area = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 current = points[i];
            Vector2 next = points[(i + 1) % points.Count];
            area += current.x * next.y - next.x * current.y;
        }

        return Mathf.Abs(area * 0.5f);
    }

    private static int CalculateShadowPathHash(IReadOnlyList<Vector3> shapePath)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < shapePath.Count; i++)
            {
                hash = hash * 31 + shapePath[i].x.GetHashCode();
                hash = hash * 31 + shapePath[i].y.GetHashCode();
            }

            return hash;
        }
    }

    private static FieldInfo ResolveShadowCasterField(string fieldName)
        => typeof(ShadowCaster2D).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingFieldException(typeof(ShadowCaster2D).FullName, fieldName);

    #endregion

    private void BindRuntimeEvents()
    {
        if (_eventsBound || damageReceiver == null || item == null)
            return;

        damageReceiver.OnAction += OnHit;
        BindFatalDamageTracking();
        damageReceiver.OnDead += OnDeath;
        if (!RequiresPlacementRequest)
            item.OnAct += Install;
        _eventsBound = true;
    }

    private void UnbindRuntimeEvents()
    {
        if (!_eventsBound)
            return;

        if (damageReceiver != null)
        {
            damageReceiver.OnAction -= OnHit;
            UnbindFatalDamageTracking();
            damageReceiver.OnDead -= OnDeath;
        }
        if (item != null)
            item.OnAct -= Install;
        _eventsBound = false;
    }

    private void OnHit(float hp)
    {
        if (Data?.Role != BuildingRole.PlacedBuilding)
            return;

        if (hp <= 0f)
            return;

        CurrentState = hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        Save();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
    }

    private void OnDeath()
    {
        if (Data?.Role != BuildingRole.PlacedBuilding || !IsInstalled())
            return;

        foreach (Module module in item.itemMods.Mods.Values)
        {
            if (module is IBuildingFatalDamagePolicy policy &&
                policy.UseDamageReceiverFatalResolution(damageReceiver))
            {
                return;
            }
        }

        if (FatalDamageWasHammer)
        {
            damageReceiver.ConsumeCurrentDeath();
            UnInstall();
            return;
        }

        DestroyFromNonHammerFatalDamage();
    }

    private void SyncRuntimeState()
    {
        if (Data.Role == BuildingRole.Summoner)
        {
            BuildingOccupancyRegistry.Unregister(this);
            if (item.itemData?.Stack != null)
                item.itemData.Stack.CanBePickedUp = true;

            SetColliderMode(
                enabled: !item.InHand,
                trigger: true,
                damageReceiverEnabled: false);
            CurrentState = item.InHand ? BuildingState.NotInstalled : BuildingState.Uninstalled;
            SyncLightOccluder();
            return;
        }

        if (item.InHand)
        {
            BuildingOccupancyRegistry.Unregister(this);
            SetColliderMode(enabled: false, trigger: true);
            SyncLightOccluder();
            return;
        }

        bool installed = Data.State is BuildingState.Installed or BuildingState.Damaged;
        if (!installed)
        {
            BuildingOccupancyRegistry.Unregister(this);
            // PlacedBuilding 已经是世界实体；即使处于短暂过渡态也必须保留实体碰撞，不能退化成 Trigger。
            SetColliderMode(enabled: true, trigger: false);
            SyncLightOccluder();
            return;
        }

        if (item.itemData?.Stack != null)
            item.itemData.Stack.CanBePickedUp = false;
        SetColliderMode(enabled: true, trigger: false);
        CurrentState = damageReceiver.Hp > 0f && damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        SyncNavigationOccupancy();
        SyncLightOccluder();
    }

    private void SetColliderMode(bool enabled, bool trigger, bool damageReceiverEnabled = true)
    {
        if (item == null)
            return;

        if (boxCollider2D != null && Data?.Role == BuildingRole.PlacedBuilding)
        {
            int colliderLayer = LayerMask.NameToLayer(BuildingCollisionLayerName);
            if (colliderLayer >= 0)
                boxCollider2D.gameObject.layer = colliderLayer;
        }

        Collider2D[] colliders = item.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = enabled;
            if (colliders[i] == boxCollider2D)
                colliders[i].isTrigger = trigger;
        }

        if (damageReceiver == null)
            return;

        Collider2D[] damageReceiverColliders = damageReceiver.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < damageReceiverColliders.Length; i++)
            damageReceiverColliders[i].enabled = enabled && damageReceiverEnabled;
    }

    private void HandleGhostShadow()
    {
        Vector3 mouse = NormalizePlacement(GetPointerWorldPosition());
        Vector3 authorityPosition = GetAuthorityPosition();
        float maximumPlacementDistance = GetMaxPlacementDistance();
        bool withinReach = IsWithinPlacementDistance(authorityPosition, mouse, maximumPlacementDistance);
        if (!withinReach)
        {
            // 虚影可见范围与实际可提交范围一致，桌面和手机均不保留越界红色虚影。
            CleanupGhost();
            return;
        }

        if (GhostShadow == null)
        {
            CreateGhostShadow();
            if (GhostShadow == null)
                return;
        }

        GhostShadow.transform.position = mouse;
        GhostShadow.UpdateAlpha(1f);
        BuildingPlacementLifecycle.GetExtension(item)?.ApplyPreview(GhostShadow);
        GhostShadow.UpdateColor(!withinReach || !ValidatePlacement(mouse, authorityPosition, false, out _));
    }

    /// <summary>按目标格子的最近边缘校验距离，保留每轴半格的格心吸附余量。</summary>
    public static bool IsWithinPlacementDistance(
        Vector3 authorityPosition,
        Vector3 placement,
        float maximumPlacementDistance)
    {
        Vector2 wrappedDelta = WorldTopologyRuntime.ShortestDelta(
            new Vector2(authorityPosition.x, authorityPosition.y),
            new Vector2(placement.x, placement.y));
        Vector2 distanceToCell = new(
            Mathf.Max(0f, Mathf.Abs(wrappedDelta.x) - PlacementCellHalfExtent),
            Mathf.Max(0f, Mathf.Abs(wrappedDelta.y) - PlacementCellHalfExtent));
        float allowedDistance = Mathf.Max(0f, maximumPlacementDistance) + BoundsEpsilon;
        return distanceToCell.sqrMagnitude <= allowedDistance * allowedDistance;
    }

    private void CreateGhostShadow()
    {
        if (_ghostCreationFailed || GameRes.Instance == null || item == null)
            return;

        GameObject shadowObject = null;
        try
        {
            shadowObject = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            GhostShadow = shadowObject != null
                ? shadowObject.GetComponentInChildren<BuildingShadow>(true)
                : null;

            if (GhostShadow == null ||
                !TryGetBuildingPreviewVisual(out SpriteRenderer source,
                    out Transform sourceRoot))
                throw new MissingComponentException("BuildingShadow 预制体或建筑 SpriteRenderer 配置不完整");

            GhostShadow.InitShadow(
                source,
                sourceRoot);
        }
        catch (Exception exception)
        {
            _ghostCreationFailed = true;
            if (shadowObject != null)
                Destroy(shadowObject);
            GhostShadow = null;
            Debug.LogError($"[建筑预览] 创建失败：{exception.Message}", item);
        }
    }

    private bool TryGetGhostPlacementPosition(out Vector3 position)
    {
        position = default;
        if (GhostShadow == null)
            return false;

        position = NormalizePlacement(GhostShadow.transform.position);
        return true;
    }

    private Vector3 GetPointerWorldPosition()
    {
        if (_ownerController == null && item?.Owner != null)
        {
            _ownerController = item.Owner.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
            if (_ownerController == null)
                _ownerController = item.Owner.GetComponent<GameController>();
        }

        return _ownerController != null
            ? _ownerController.GetMouseWorldPosition()
            : GetAuthorityPosition();
    }

    /// <summary>按同一层级返回预览图片和局部坐标根节点，避免跨对象计算出世界坐标偏移。</summary>
    private bool TryGetBuildingPreviewRenderer(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        sourceRenderer = null;
        sourceRoot = null;

        // 格子建筑直接读取最终 Tile 的图片和变换，保持高墙 Pivot、缩放与格心偏移一致。
        if (!string.IsNullOrWhiteSpace(Data?.TileBlockId))
        {
            GameObject tilePreview = GetTilePreviewSource();
            sourceRenderer = tilePreview.GetComponentInChildren<SpriteRenderer>(true);
            sourceRoot = tilePreview.transform;
            return true;
        }

        // 运行时物品定义可能复用通用 Prop 外壳；自身就是建筑时必须使用已应用定义 Sprite，
        // 否则建筑预览会误读 Prop 预制体上的默认贴图。
        if (UsesCurrentItemAsBuildingPrefab() && TryGetCurrentItemPreviewRenderer(out sourceRenderer, out sourceRoot))
            return true;

        if (TryGetDefinitionPreviewSource(out GameObject definitionPreview))
        {
            sourceRenderer = definitionPreview.GetComponentInChildren<SpriteRenderer>(true);
            sourceRoot = definitionPreview.transform;
            if (sourceRenderer != null && sourceRenderer.sprite != null)
                return true;
        }

        if (TryGetBuildingBodyPrefab(out GameObject buildingPrefab))
        {
            Item buildingItem = buildingPrefab.GetComponent<Item>();
            SpriteRenderer bodyRenderer = buildingItem != null ? buildingItem.Sprite : null;
            if (bodyRenderer != null && bodyRenderer.sprite != null)
            {
                sourceRenderer = bodyRenderer;
                sourceRoot = buildingPrefab.transform;
                return true;
            }

            SpriteRenderer[] bodyRenderers = buildingPrefab.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                SpriteRenderer candidate = bodyRenderers[i];
                if (candidate == null || candidate.sprite == null)
                    continue;

                sourceRenderer = candidate;
                sourceRoot = buildingPrefab.transform;
                return true;
            }
        }

        if (item == null)
            return false;

        if (item.Sprite != null && item.Sprite.sprite != null)
        {
            sourceRenderer = item.Sprite;
            sourceRoot = item.transform;
            return true;
        }

        SpriteRenderer[] itemRenderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            SpriteRenderer candidate = itemRenderers[i];
            if (candidate == null || candidate.sprite == null)
                continue;

            sourceRenderer = candidate;
            sourceRoot = item.transform;
            return true;
        }

        return false;
    }

    private bool TryGetCurrentItemPreviewRenderer(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        sourceRenderer = null;
        sourceRoot = null;
        if (item == null)
            return false;

        if (item.Sprite != null && item.Sprite.sprite != null)
        {
            sourceRenderer = item.Sprite;
            sourceRoot = item.transform;
            return true;
        }

        SpriteRenderer[] itemRenderers = item.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            SpriteRenderer candidate = itemRenderers[i];
            if (candidate == null || candidate.sprite == null)
                continue;

            sourceRenderer = candidate;
            sourceRoot = item.transform;
            return true;
        }

        return false;
    }

    /// <summary>返回真实放置预览使用的图片和局部坐标根节点；占地由独立格层维护。</summary>
    public bool TryGetBuildingPreviewVisual(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot)
    {
        TryGetBuildingPreviewRenderer(out sourceRenderer, out sourceRoot);
        return sourceRenderer != null && sourceRoot != null;
    }

    #region 格子建筑预览

    /// <summary>以格心为根节点复刻静态 Tile 的视觉变换，占地由独立格层记录。</summary>
    private GameObject GetTilePreviewSource()
    {
        string previewId = "tile:" + Data.TileBlockId;
        if (_definitionPreviewSource != null &&
            string.Equals(_definitionPreviewItemId, previewId, StringComparison.Ordinal))
            return _definitionPreviewSource;

        UnityEngine.Tilemaps.Tile tile = GameRes.Instance?.GetTileBlock(Data.TileBlockId)?.
            GetTileBaseAsset() as UnityEngine.Tilemaps.Tile;
        if (tile == null || tile.sprite == null)
            throw new InvalidOperationException($"格子建筑 {Data.TileBlockId} 缺少静态 Tile 或 Sprite。");

        if (_definitionPreviewSource != null)
            Destroy(_definitionPreviewSource);
        _definitionPreviewSource = new GameObject($"{Data.TileBlockId}_BuildingPreview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        GameObject renderObject = new GameObject("Render")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        renderObject.transform.SetParent(_definitionPreviewSource.transform, false);
        renderObject.transform.localPosition = tile.transform.GetColumn(3);
        renderObject.transform.localRotation = tile.transform.rotation;
        renderObject.transform.localScale = tile.transform.lossyScale;
        SpriteRenderer renderer = renderObject.AddComponent<SpriteRenderer>();
        renderer.sprite = tile.sprite;
        renderer.color = tile.color;
        _definitionPreviewSource.SetActive(false);
        _definitionPreviewItemId = previewId;
        return _definitionPreviewSource;
    }

    #endregion

    /// <summary>按建筑本体 JSON 创建一个无模块、不可见的轻量预览源。</summary>
    private bool TryGetDefinitionPreviewSource(out GameObject previewSource)
    {
        previewSource = null;
        string buildingId = Data?.BuildingPrefabId;
        if (string.IsNullOrWhiteSpace(buildingId))
            buildingId = GetBuildingPrefabId(item?.itemData?.IDName);
        if (string.IsNullOrWhiteSpace(buildingId) || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(buildingId, out RuntimeItemDefinition definition) ||
            definition?.Sprite == null)
        {
            return false;
        }

        if (_definitionPreviewSource == null ||
            !string.Equals(_definitionPreviewItemId, buildingId, StringComparison.Ordinal))
        {
            if (_definitionPreviewSource != null)
                Destroy(_definitionPreviewSource);
            _definitionPreviewSource = CreateDefinitionPreviewSource(definition);
            _definitionPreviewItemId = buildingId;
        }

        previewSource = _definitionPreviewSource;
        return previewSource != null;
    }

    /// <summary>把运行时定义中的图片与渲染参数还原到纯视觉预览对象。</summary>
    private static GameObject CreateDefinitionPreviewSource(RuntimeItemDefinition definition)
    {
        GameObject root = new GameObject($"{definition.Id}_BuildingPreview")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        GameObject renderObject = new GameObject("Render")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        renderObject.transform.SetParent(root.transform, false);

        ItemVisualDefinitionDto visual = definition.Visual;
        SpriteRenderer renderer = renderObject.AddComponent<SpriteRenderer>();
        renderer.sprite = definition.Sprite;
        if (definition.Material != null)
            renderer.sharedMaterial = definition.Material;
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        if (visual?.RendererLocalPosition.HasValue == true)
            renderObject.transform.localPosition = visual.RendererLocalPosition.Value;
        if (visual?.RendererLocalEulerAngles.HasValue == true)
            renderObject.transform.localEulerAngles = visual.RendererLocalEulerAngles.Value;
        if (visual?.RendererLocalScale.HasValue == true)
            renderObject.transform.localScale = visual.RendererLocalScale.Value;
        if (visual?.Color.HasValue == true)
            renderer.color = visual.Color.Value;
        if (visual?.FlipX.HasValue == true)
            renderer.flipX = visual.FlipX.Value;
        if (visual?.FlipY.HasValue == true)
            renderer.flipY = visual.FlipY.Value;
        if (!string.IsNullOrWhiteSpace(visual?.SortingLayerName))
            renderer.sortingLayerName = visual.SortingLayerName;
        if (visual?.SortingOrder.HasValue == true)
            renderer.sortingOrder = visual.SortingOrder.Value;

        root.SetActive(false);
        return root;
    }

    private bool UsesCurrentItemAsBuildingPrefab()
    {
        return item != null &&
               string.Equals(Data?.BuildingPrefabId, item.itemData?.IDName, StringComparison.Ordinal);
    }

    private bool TryGetBuildingBodyPrefab(out GameObject buildingPrefab)
    {
        buildingPrefab = null;
        string buildingPrefabId = Data?.BuildingPrefabId;
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            buildingPrefabId = GetBuildingPrefabId(item?.itemData?.IDName);

        return GameRes.Instance != null &&
               GameRes.Instance.AllPrefabs != null &&
               GameRes.Instance.AllPrefabs.TryGetValue(buildingPrefabId, out buildingPrefab) &&
               buildingPrefab != null;
    }

    private Vector3 GetAuthorityPosition()
        => item?.Owner != null ? item.Owner.transform.position : item != null ? item.transform.position : transform.position;

    /// <summary>建筑预览、放置校验和玩家准星统一使用同一最大建造距离。</summary>
    public float GetMaxPlacementDistance()
    {
        Mod_InteractSender interactionSender = item?.Owner?.GetComponentInChildren<Mod_InteractSender>(true);
        return interactionSender != null
            ? Mathf.Max(0.01f, interactionSender.maxInteractDistance * 2f)
            : Mathf.Max(0.01f, (Data?.maxVisibleDistance ?? Mod_InteractSender.DefaultMaxInteractDistance) * 2f);
    }

    private void SyncNavigationOccupancy()
    {
        if (!isActiveAndEnabled || item == null || !IsInstalled())
        {
            BuildingOccupancyRegistry.Unregister(this);
            return;
        }

        BuildingOccupancyRegistry.Register(this, GetPlacementCell(item.transform.position));
        Mod_Door door = item.itemMods?.GetMod_ByID<Mod_Door>("Door");
        if (door != null) BuildingOccupancyRegistry.SetPassable(this, door.Data.IsOpen);
    }

    /// <summary>动态可交互建筑与格子墙统一以吸附后的单个世界格作为放置槽。</summary>
    private static Vector2Int GetPlacementCell(Vector3 position)
        => WorldTopologyRuntime.NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y)));

    /// <summary>Editor tooling uses this to stamp a prefab as the world body or its matching summoner.</summary>
    public void ConfigurePrefabRole(BuildingRole role, string buildingPrefabId, string summonerPrefabId)
    {
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            throw new ArgumentException("建筑本体 ID 不能为空", nameof(buildingPrefabId));

        summonerPrefabId = string.IsNullOrWhiteSpace(summonerPrefabId)
            ? GetSummonerPrefabId(buildingPrefabId)
            : summonerPrefabId;

        string configuredTileBlockId = Data?.TileBlockId;
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = role;
        Data.State = BuildingState.NotInstalled;
        Data.SnapshotBase64 = null;
        Data.BuildingPrefabId = buildingPrefabId;
        Data.SummonerPrefabId = summonerPrefabId;
        Data.TileBlockId = string.IsNullOrWhiteSpace(configuredTileBlockId)
            ? GetDefaultTileBlockId(buildingPrefabId)
            : configuredTileBlockId;
        _currentState = Data.State;

        BuildingData ??= new Ex_ModData();
        BuildingData.ID = ModText.Building;
        BuildingData.WriteData(Data);
    }

    public static string GetSummonerPrefabId(string buildingPrefabId)
    {
        if (string.IsNullOrWhiteSpace(buildingPrefabId))
            return string.Empty;

        return buildingPrefabId.EndsWith(SummonerPrefabSuffix, StringComparison.Ordinal)
            ? buildingPrefabId
            : buildingPrefabId + SummonerPrefabSuffix;
    }

    public static string GetBuildingPrefabId(string itemPrefabId)
    {
        if (string.IsNullOrWhiteSpace(itemPrefabId))
            return string.Empty;

        return itemPrefabId.EndsWith(SummonerPrefabSuffix, StringComparison.Ordinal)
            ? itemPrefabId.Substring(0, itemPrefabId.Length - SummonerPrefabSuffix.Length)
            : itemPrefabId;
    }

    public static string GetDefaultTileBlockId(string buildingPrefabId)
    {
        return string.Equals(buildingPrefabId, StoneWallBuildingId, StringComparison.Ordinal)
            ? StoneWallTileBlockId
            : null;
    }

    public static bool TryReadBuildingData(
        ItemData itemData,
        out Ex_ModData moduleData,
        out Building_Data buildingState)
    {
        moduleData = null;
        buildingState = null;
        if (itemData?.ModuleDataDic == null)
            return false;

        foreach (ModuleData candidate in itemData.ModuleDataDic.Values)
        {
            if (candidate is not Ex_ModData exData ||
                !string.Equals(candidate.ID, ModText.Building, StringComparison.Ordinal))
            {
                continue;
            }

            Building_Data state = new();
            exData.ReadData(ref state);
            state ??= new Building_Data();
            moduleData = exData;
            buildingState = state;
            return true;
        }

        return false;
    }

    private static bool WriteBuildingData(ItemData itemData, Action<Building_Data> mutate)
    {
        if (!TryReadBuildingData(itemData, out Ex_ModData moduleData, out Building_Data state))
            return false;

        MigrateLegacyData(state, itemData?.IDName);
        mutate?.Invoke(state);
        moduleData.WriteData(state);
        return true;
    }

    private static void MigrateLegacyData(Building_Data state, string carrierItemId = null)
    {
        if (state == null)
            return;

        if (state.Version < 2)
        {
            state.Role = state.State is BuildingState.Installed or BuildingState.Damaged
                ? BuildingRole.PlacedBuilding
                : BuildingRole.Summoner;
            state.SnapshotBase64 = null;
        }

        if (string.IsNullOrWhiteSpace(state.BuildingPrefabId))
            state.BuildingPrefabId = GetBuildingPrefabId(carrierItemId);

        if (string.IsNullOrWhiteSpace(state.SummonerPrefabId))
            state.SummonerPrefabId = GetSummonerPrefabId(state.BuildingPrefabId);

        // 旧石墙召唤器/存档没有 TileBlockId，迁移后新放置直接进入格子建筑系统。
        if (string.IsNullOrWhiteSpace(state.TileBlockId) &&
            !string.IsNullOrWhiteSpace(state.BuildingPrefabId))
        {
            state.TileBlockId = GetDefaultTileBlockId(state.BuildingPrefabId);
        }

        state.Version = CurrentDataVersion;
    }

    private static string ResolveBuildingPrefabId(string carrierItemId, Building_Data state)
        => !string.IsNullOrWhiteSpace(state?.BuildingPrefabId)
            ? state.BuildingPrefabId
            : GetBuildingPrefabId(carrierItemId);

    private static string ResolveSummonerPrefabId(string buildingPrefabId, Building_Data state)
        => !string.IsNullOrWhiteSpace(state?.SummonerPrefabId)
            ? state.SummonerPrefabId
            : GetSummonerPrefabId(buildingPrefabId);

    private static Vector3 NormalizePlacement(Vector3 position)
        => WorldTopologyRuntime.NormalizePosition(
            new Vector3(Mathf.Floor(position.x) + 0.5f, Mathf.Floor(position.y) + 0.5f, 0f));

    /// <summary>建筑只保留 XY 平面旋转，避免无效四元数或三维倾斜污染 2D 世界。</summary>
    private static Quaternion NormalizeBuildingRotation(Quaternion rotation)
    {
        if (!IsFinite(rotation) ||
            rotation.x * rotation.x + rotation.y * rotation.y +
            rotation.z * rotation.z + rotation.w * rotation.w < 0.0001f)
            return Quaternion.identity;

        Vector3 euler = rotation.eulerAngles;
        return IsFinite(euler)
            ? Quaternion.Euler(0f, 0f, euler.z)
            : Quaternion.identity;
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
           !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
           !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool IsFinite(Quaternion value)
        => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
           !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
           !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
           !float.IsNaN(value.w) && !float.IsInfinity(value.w);

    private static int GenerateUniqueRuntimeGuid()
    {
        if (ItemMgr.Instance == null)
            throw new InvalidOperationException("ItemMgr 尚未就绪");

        int guid;
        do
        {
            guid = ItemMgr.Instance.GenerateGuid();
        }
        while (guid == 0 || ItemMgr.Instance.GetItemByGuid(guid) != null);
        return guid;
    }
}

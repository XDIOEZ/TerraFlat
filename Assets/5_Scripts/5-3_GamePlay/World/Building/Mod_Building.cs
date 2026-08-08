using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Progress;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;

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
public class Mod_Building : Module
{
    private const int CurrentDataVersion = 3;
    private const string StoneWallBuildingId = "Wall_Stone";
    private const string StoneWallTileBlockId = "TileBase_BuiltStoneWall";
    public const string SummonerPrefabSuffix = "_Summoner";
    public static int CurrentBuildingDataVersion => CurrentDataVersion;
    private const uint BlockedTilePenalty = 1000;
    private const float BoundsEpsilon = 0.001f;
    private const int MaxEmbeddedSnapshotBytes = 320 * 1024;
    private const string StatefulSummonerPrefix = "building-snapshot:";

    [Serializable]
    public class Building_Data
    {
        // 不设置字段初值，旧 JSON 缺少 Version 时保持 0，才能正确迁移。
        public int Version;
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
        public BuildingState State = BuildingState.NotInstalled;
        public BuildingRole Role = BuildingRole.Summoner;
        public string SnapshotBase64;
        public string BuildingPrefabId;
        public string SummonerPrefabId;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string TileBlockId;
    }

    public Building_Data Data = new();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;
    public DamageReceiver damageReceiver;
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
        EnsureRuntimeReferences();
        Data = new Building_Data();
        BuildingData?.ReadData(ref Data);
        Data ??= new Building_Data();
        MigrateLegacyData(Data, item?.itemData?.IDName);
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

        if (!IsItemInInventory || _placementPending || !IsSummoner)
        {
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
    }

    public void OnDisable()
    {
        CleanupGhost();
        BuildingOccupancyRegistry.Unregister(this);
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
        if (_placementPending || !IsItemInInventory)
            return;

        if (!TryGetGhostPlacementPosition(out Vector3 placement))
        {
            Debug.LogWarning("[建筑安装] 放置预览尚未就绪", item);
            return;
        }

        if (!ValidatePlacement(placement, GetAuthorityPosition(), true, out string reason))
        {
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
                GameplayProgressEvents.PublishBuildingPlaced(actor, buildingId);
                return;
            }

            TileBuildingSystem.TryRemove(
                placedCell.Map,
                placedCell.Position,
                spawnDrop: false,
                out _);
            Debug.LogWarning("[格子建筑安装] 消耗建造材料失败，已回滚墙体", item);
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
            building = ItemMgr.Instance.InstantiateItem(placedData, placedData.transform.position);
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
                placedData = FastCloner.FastCloner.DeepClone(summonerData);
                placedData.IDName = buildingPrefabId;
            }
        }
        catch (Exception exception)
        {
            reason = $"读取建筑快照失败：{exception.Message}";
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

        if (!string.IsNullOrWhiteSpace(data.SnapshotBase64) && itemData.Stack?.Amount != 1f)
        {
            reason = "带快照的建筑召唤器必须为单件";
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
        if (!TryCreateDismantledSummoner(out _, out string reason))
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
            summonerData.transform.rotation = Quaternion.identity;
            summonerData.transform.scale = Vector3.one;

            summoner = ItemMgr.Instance.InstantiateItem(summonerData, dropPosition);
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
        if (GhostShadow == null)
            return;

        Destroy(GhostShadow.gameObject);
        GhostShadow = null;
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

        ApplySourceAmount(item.itemData.Stack.Amount - 1f);
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

        Item owner = item.Owner;
        Inventory_HotBar hotBar = owner?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        ItemSlot selectedSlot = hotBar?.CurrentSelectItemSlot;

        item.itemData.Stack.Amount = Mathf.Max(0f, amount);
        bool depleted = item.itemData.Stack.Amount <= 0f;
        if (depleted && selectedSlot != null && ReferenceEquals(selectedSlot.itemData, item.itemData))
        {
            selectedSlot.ClearData();
            selectedSlot.RefreshUI();
        }

        item.OnUIRefresh?.Invoke();
        if (owner != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(owner);

        if (!depleted)
            return;

        CleanupGhost();
        if (ItemMgr.Instance != null)
            ItemMgr.Instance.DespawnItem(item, false);
        else
            item.DestroySelf();
    }

    private bool ValidatePlacement(
        Vector3 position,
        Vector3 authorityPosition,
        bool requireGhostClear,
        out string reason)
    {
        reason = null;
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

        if (WorldTopologyRuntime.Distance(authorityPosition, position) > Mathf.Max(1f, Data.maxVisibleDistance))
        {
            reason = "目标超出建造距离";
            return false;
        }

        if (requireGhostClear && GhostShadow == null)
        {
            reason = "放置预览尚未就绪";
            return false;
        }

        Bounds bounds = GetPlacementBounds(position);
        if (!CheckWorldObstacles(bounds, out reason))
            return false;

        return CheckTilePenalties(bounds, out reason);
    }

    private bool CheckWorldObstacles(Bounds bounds, out string reason)
    {
        reason = null;
        // 相邻格建筑只共享边界，不应被 Physics2D 当作重叠；轻微收缩查询框允许墙体连续铺设。
        Vector2 overlapSize = new(
            Mathf.Max(BoundsEpsilon, bounds.size.x - BoundsEpsilon * 2f),
            Mathf.Max(BoundsEpsilon, bounds.size.y - BoundsEpsilon * 2f));
        Collider2D[] overlaps = Physics2D.OverlapBoxAll(bounds.center, overlapSize, 0f);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider2D overlap = WorldTopologyColliderProxy.Resolve(overlaps[i]);
            if (overlap == null || overlap.isTrigger ||
                (item != null && overlap.transform.IsChildOf(item.transform)) ||
                overlap.CompareTag("Player") || overlap.gameObject.tag == "IgnoreShadow")
            {
                continue;
            }

            reason = $"目标被 {overlap.gameObject.name} 阻挡";
            return false;
        }

        return true;
    }

    private bool CheckTilePenalties(Bounds bounds, out string reason)
    {
        reason = null;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null)
        {
            reason = "区块管理器尚未就绪";
            return false;
        }

        int minX = Mathf.FloorToInt(bounds.min.x + BoundsEpsilon);
        int maxX = Mathf.FloorToInt(bounds.max.x - BoundsEpsilon);
        int minY = Mathf.FloorToInt(bounds.min.y + BoundsEpsilon);
        int maxY = Mathf.FloorToInt(bounds.max.y - BoundsEpsilon);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int worldCell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(x, y));
                Vector2 tileCenter = new(worldCell.x + 0.5f, worldCell.y + 0.5f);
                if (chunkManager.TryGetRuntimeTerrainTile(
                        tileCenter, out RuntimeTerrainTileSample runtimeTile))
                {
                    if (runtimeTile.TopTileId == 0)
                    {
                        reason = $"地块 ({x},{y}) 不可建造";
                        return false;
                    }

                    if (!runtimeTile.Terrain.IsWalkable(
                            runtimeTile.LocalCell.x, runtimeTile.LocalCell.y) ||
                        runtimeTile.Cell.NavigationCost > BlockedTilePenalty)
                    {
                        reason = $"地块 ({x},{y}) 不可通行";
                        return false;
                    }
                }
                else
                {
                    // 旧 Map 世界仍保留兼容回退；新区块权威不依赖表现对象。
                    chunkManager.GetChunkBy_ItemPosition(tileCenter, out Chunk chunk);
                    if (chunk?.Map?.Data == null)
                    {
                        reason = $"地块 ({x},{y}) 尚未加载";
                        return false;
                    }

                    TileData topTile = chunk.Map.Data.GetTopTile(worldCell);
                    if (topTile == null)
                    {
                        reason = $"地块 ({x},{y}) 不可建造";
                        return false;
                    }

                    if (!topTile.IsWalkable || topTile.Penalty > BlockedTilePenalty)
                    {
                        reason = $"地块 ({x},{y}) 不可通行";
                        return false;
                    }
                }

                if (BuildingOccupancyRegistry.IsOccupied(worldCell, this))
                {
                    reason = $"地块 ({x},{y}) 已被建筑占用";
                    return false;
                }
            }
        }

        return true;
    }

    private Bounds GetPlacementBounds(Vector3 position)
    {
        if (boxCollider2D == null)
            return new Bounds(position, Vector3.one * 0.9f);

        Transform itemTransform = item != null ? item.transform : transform;
        Bounds localBounds = GetColliderLocalBounds(itemTransform, boxCollider2D);
        localBounds.center += position;
        return localBounds;
    }

    private void EnsureRuntimeReferences()
    {
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_Building] 未找到所属 Item");

        boxCollider2D ??= item.GetComponent<BoxCollider2D>();
        boxCollider2D ??= GetComponent<BoxCollider2D>();
        boxCollider2D ??= item.GetComponentInChildren<BoxCollider2D>(true);

        damageReceiver ??= item.GetMod<DamageReceiver>();
        if (damageReceiver == null)
            throw new MissingComponentException($"[Mod_Building] {item.name} 缺少 DamageReceiver");
    }

    private void BindRuntimeEvents()
    {
        if (_eventsBound || damageReceiver == null || item == null)
            return;

        damageReceiver.OnAction += OnHit;
        damageReceiver.OnDead += OnDeath;
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

        damageReceiver.ConsumeCurrentDeath();
        UnInstall();
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
            return;
        }

        if (item.InHand)
        {
            BuildingOccupancyRegistry.Unregister(this);
            SetColliderMode(enabled: false, trigger: true);
            return;
        }

        bool installed = Data.State is BuildingState.Installed or BuildingState.Damaged;
        if (!installed)
        {
            BuildingOccupancyRegistry.Unregister(this);
            SetColliderMode(enabled: true, trigger: true);
            return;
        }

        if (item.itemData?.Stack != null)
            item.itemData.Stack.CanBePickedUp = false;
        SetColliderMode(enabled: true, trigger: false);
        CurrentState = damageReceiver.Hp > 0f && damageReceiver.Hp < damageReceiver.MaxHp * 0.5f
            ? BuildingState.Damaged
            : BuildingState.Installed;
        SyncNavigationOccupancy();
    }

    private void SetColliderMode(bool enabled, bool trigger, bool damageReceiverEnabled = true)
    {
        if (item == null)
            return;

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
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Vector3 mouse = NormalizePlacement(GetPointerWorldPosition(mainCamera));
        if (GhostShadow == null)
        {
            CreateGhostShadow();
            if (GhostShadow == null)
                return;
        }

        GhostShadow.transform.position = mouse;
        float distance = WorldTopologyRuntime.Distance(GetAuthorityPosition(), mouse);
        float maximumPlacementDistance = Mathf.Max(1f, Data.maxVisibleDistance);
        float alpha = Mathf.Clamp01(Mathf.InverseLerp(
            maximumPlacementDistance + 1.5f,
            maximumPlacementDistance,
            distance));
        GhostShadow.UpdateAlpha(alpha);
        GhostShadow.UpdateColor(!ValidatePlacement(mouse, GetAuthorityPosition(), false, out _));
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
                    out Transform sourceRoot, out Bounds footprint))
                throw new MissingComponentException("BuildingShadow 预制体或建筑 SpriteRenderer 配置不完整");

            GhostShadow.InitShadow(source, sourceRoot, footprint);
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

    private Vector3 GetPointerWorldPosition(Camera mainCamera)
    {
        if (_ownerController == null && item?.Owner != null)
        {
            _ownerController = item.Owner.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
            if (_ownerController == null)
                _ownerController = item.Owner.GetComponent<GameController>();
        }

        Vector2 screenPosition = _ownerController != null
            ? _ownerController.GetPointerScreenPosition()
            : (Vector2)Input.mousePosition;
        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(new Vector3(
            screenPosition.x,
            screenPosition.y,
            Mathf.Abs(mainCamera.transform.position.z)));
        worldPosition.z = 0f;
        return worldPosition;
    }

    private SpriteRenderer GetBuildingPreviewRenderer()
    {
        if (TryGetBuildingBodyPrefab(out GameObject buildingPrefab))
        {
            Item buildingItem = buildingPrefab.GetComponent<Item>();
            SpriteRenderer bodyRenderer = buildingItem != null ? buildingItem.Sprite : null;
            bodyRenderer ??= buildingPrefab.GetComponentInChildren<SpriteRenderer>(true);
            if (bodyRenderer != null)
                return bodyRenderer;
        }

        return item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>(true);
    }

    /// <summary>返回真实放置预览使用的图片、局部坐标根节点与占地范围。</summary>
    public bool TryGetBuildingPreviewVisual(
        out SpriteRenderer sourceRenderer,
        out Transform sourceRoot,
        out Bounds footprint)
    {
        sourceRenderer = GetBuildingPreviewRenderer();
        sourceRoot = TryGetBuildingBodyPrefab(out GameObject buildingPrefab)
            ? buildingPrefab.transform
            : item != null ? item.transform : null;
        footprint = GetBuildingPreviewBounds();
        return sourceRenderer != null && sourceRoot != null;
    }

    private Bounds GetBuildingPreviewBounds()
    {
        if (TryGetBuildingBodyPrefab(out GameObject buildingPrefab))
        {
            BoxCollider2D bodyCollider = buildingPrefab.GetComponent<BoxCollider2D>();
            bodyCollider ??= buildingPrefab.GetComponentInChildren<BoxCollider2D>(true);
            if (bodyCollider != null)
                return GetColliderLocalBounds(buildingPrefab.transform, bodyCollider);
        }

        return GetPlacementBounds(Vector3.zero);
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

    private static Bounds GetColliderLocalBounds(Transform root, BoxCollider2D collider)
    {
        Vector2 half = collider.size * 0.5f;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 colliderLocal = collider.offset + Vector2.Scale(half, new Vector2(x, y));
                Vector3 rootLocal = root.InverseTransformPoint(collider.transform.TransformPoint(colliderLocal));
                min = Vector2.Min(min, rootLocal);
                max = Vector2.Max(max, rootLocal);
            }
        }

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;
        return new Bounds(
            center,
            new Vector3(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y), 0.1f));
    }

    private Vector3 GetAuthorityPosition()
        => item?.Owner != null ? item.Owner.transform.position : item != null ? item.transform.position : transform.position;

    private void SyncNavigationOccupancy()
    {
        if (!isActiveAndEnabled || item == null || !IsInstalled())
        {
            BuildingOccupancyRegistry.Unregister(this);
            return;
        }

        BuildingOccupancyRegistry.Register(this, GetPlacementCells(item.transform.position));
    }

    private IEnumerable<Vector2Int> GetPlacementCells(Vector3 position)
    {
        Bounds bounds = GetPlacementBounds(position);
        int minX = Mathf.FloorToInt(bounds.min.x + BoundsEpsilon);
        int maxX = Mathf.FloorToInt(bounds.max.x - BoundsEpsilon);
        int minY = Mathf.FloorToInt(bounds.min.y + BoundsEpsilon);
        int maxY = Mathf.FloorToInt(bounds.max.y - BoundsEpsilon);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
                yield return new Vector2Int(x, y);
        }
    }

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

    private static bool TryReadBuildingData(
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

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
           !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
           !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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

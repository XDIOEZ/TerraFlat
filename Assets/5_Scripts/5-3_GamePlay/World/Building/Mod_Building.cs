using System;
using FlatWorld.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlatWorld.Gameplay.Building;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Networking;
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

/// <summary>贴地设施与实体建筑分层占地，机械扩展仍可指定自己的层。</summary>
public enum BuildingPlacementLayer
{
    Structure = 0,
    Ground = 2
}

/// <summary>
/// 建筑召唤器是持久化载体，PlacedBuilding 是快照还原后的世界实例。
/// 拆除时先生成带快照的召唤器，成功后才删除原建筑。
/// </summary>
public partial class Mod_Building : Module, IIncomingDamageRule, IIncomingDamageContextRule, IBuildingPreviewRotation, ILocalItemUseRequestHandler, IItemWorldPhysicsPoseReceiver2D
{
    #region 建筑模块状态与生命周期
    private const int CurrentDataVersion = 3;
    private const string StoneWallBuildingId = "Wall_Stone";
    private const string StoneWallTileBlockId = "TileBase_BuiltStoneWall";
    public const string SummonerPrefabSuffix = "_Summoner";
    public static int CurrentBuildingDataVersion => CurrentDataVersion;
    private const float BoundsEpsilon = 0.001f;
    // 格心吸附相对准星原始落点在单轴上的最大偏差。
    private const float PlacementCellHalfExtent = 0.5f;
    internal const int MaxEmbeddedSnapshotBytes = 320 * 1024;
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
        // 保存单件建筑实际消耗的材料，替代料不再靠配方反推。
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public SortedDictionary<string, float> CraftingMaterials;
        // 手持物与建筑共同持有的状态模块；放置/拆回时按稳定 ID 转移当前数据。
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string[] SharedModuleIds;
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string TileBlockId;
        // 以锚点格为左下角的离散矩形占地；缺省零值由读取端按一格处理。
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public int FootprintWidth;
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public int FootprintHeight;
        // 普通建筑可按配置逐次旋转九十度，预览和正式放置共用朝向。
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool AllowQuarterTurnPlacement;
        // 横向长建筑可在放置前通过 R 键只切换左右朝向，不改变占地宽高。
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool AllowHorizontalMirrorPlacement;
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public bool HorizontalMirrorX;
    }

    public Building_Data Data = new();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public bool RequiresPlacementRequest; // 可手持使用的设施先从面板进入放置模式，避免与使用动作冲突。
    public bool UseGameObjectBackend; // 组合能力需要完整物品生命周期时，明确保留 Item 建筑后端。
    private bool _placementRequested;
    private GameObject _definitionPreviewSource;
    private string _definitionPreviewItemId;
    public BoxCollider2D boxCollider2D;
    // 伤害模块由 ItemMods 注册表提供，禁止序列化嵌套 Prefab 的组件引用。
    [NonSerialized]
    private Mod_DamageReceiver damageReceiver;
    [NonSerialized]
    private ShadowCaster2D _lightOccluder;
    public UltEvent StartInstall = new();
    public UltEvent StartUnInstall = new();
    public UltEvent<BuildingState, BuildingState> OnStateChanged = new();

    [SerializeField] private BuildingState _currentState = BuildingState.NotInstalled;
    private bool _eventsBound;
    private bool _isLoaded;
    private bool _placementPending;
    private bool _dismantlePending;
    private bool _ghostCreationFailed;
    private Mod_GameController _ownerController;
    private Player _placementActor;
    private bool _placementMirrorX;
    private bool _rotationInputBound;
    private bool _ghostBaseFlipX;
    private bool _ghostBaseFlipCaptured;
    private bool _baseSpriteFlipX;
    private bool _baseSpriteFlipCaptured;

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
    public bool PlacementHorizontalMirrorX => _placementMirrorX;
    private int _placementQuarterTurns;
    public int PlacementQuarterTurns => GetLocalPlacementQuarterTurns();
    private bool AllowsQuarterTurnPlacement => Data?.AllowQuarterTurnPlacement == true || Mod_ShipPart.Read(item?.itemData) != null;
    public bool CanRotateHorizontalPlacement => (Data?.AllowHorizontalMirrorPlacement == true || AllowsQuarterTurnPlacement) && IsPlacementActionAvailable;
    public bool CanRotatePlacement => CanRotateHorizontalPlacement;
    [Tooltip("地板层设施允许同格放置实体建筑，不阻挡通行或视线。")]
    public BuildingPlacementLayer PlacementLayer = BuildingPlacementLayer.Structure;
    [Tooltip("无扩展通行策略时的建筑声明；关闭后保留交互/受击与占格，但不阻挡角色或视线。")]
    public bool BlocksMovementWhenPlaced = true;
    [Tooltip("完整墙面等实体建筑启用挡风；开门、可通行设施和地板不提供背风区。")]
    public bool BlocksWindWhenPlaced;
    public bool IsGroundFacility => BuildingTraversalRules.IsGroundLayer(BuildingOccupancyRegistry.GetPlacementLayer(this));
    public bool BlocksMovement => BuildingTraversalRules.BlocksMovement(
        BuildingOccupancyRegistry.GetPlacementLayer(this),
        BuildingPlacementLifecycle.GetTraversalPolicy(item)?.BlocksMovement ?? BlocksMovementWhenPlaced);

    /// <summary>建筑工具弱点由生命模块统一结算，旧建筑倍率接口保持中性。</summary>
    public float GetDamageMultiplier(IDamageSender sender)
    {
        return 1f;
    }

    public float GetDamageMultiplier(in FlatWorld.Combat.CombatDamageContext context) => 1f;

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
    public int LocalUsePriority => 100;
    public bool CanHandleLocalUse => IsPlacementActionAvailable;
    public void HandleLocalUse() => Install();

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

    protected override void OnLoad()
    {
        UnbindPlacementRotationInput();
        _placementRequested = false;
        _placementMirrorX = false;
        _placementQuarterTurns = 0;
        _ownerController = null;
        EnsureRuntimeReferences();
        CaptureBaseSpriteFlip();
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

    protected override void OnSave()
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
    /// <summary>资源重载后按当前空间层更新物理、占地与遮光，保留建筑的安装状态。</summary>
    public override void OnResourcesReloaded()
    {
        if (!_isLoaded || item == null) return;
        RefreshInstalledOrientationSources();
        EnsureRuntimeReferences();
        SyncRuntimeState();
        SyncNavigationOccupancy();
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
            {
                _placementRequested = false;
                _placementMirrorX = false;
                _placementQuarterTurns = 0;
                UnbindPlacementRotationInput();
            }
            CleanupGhost();
            return;
        }

        BindPlacementRotationInput();
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
    public void SetAsInstalled(bool initializeHealth = true)
    {
        EnsureRuntimeReferences();
        Data ??= new Building_Data();
        Data.Version = CurrentDataVersion;
        Data.Role = BuildingRole.PlacedBuilding;
        Data.SnapshotBase64 = null;
        Data.BuildingPrefabId = ResolveBuildingPrefabId(item?.itemData?.IDName, Data);
        Data.SummonerPrefabId = ResolveSummonerPrefabId(Data.BuildingPrefabId, Data);

        Vector3 logicalPlacement = NormalizePlacement(item.transform.position);
        item.transform.position = WorldLocalPresentation.ProjectPosition(logicalPlacement);
        if (item.itemData?.transform != null)
            item.itemData.transform.position = logicalPlacement;
        // 安装只读取候选数据中已确认的格子朝向，手持表现的旋转不会迁移到建筑。
        int placedQuarterTurns = Mathf.RoundToInt((item.itemData?.transform?.rotation ?? Quaternion.identity).eulerAngles.z / 90f) & 3;
        item.transform.rotation = Quaternion.Euler(0f, 0f, placedQuarterTurns * 90f);
        if (item.itemData?.transform != null) item.itemData.transform.rotation = item.transform.rotation;
        ApplyInstalledOrientation();
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
        _ghostBaseFlipCaptured = false;

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

    private Player ResolvePlacementActor()
    {
        Item owner = item?.Owner;
        return owner as Player ?? owner?.GetComponentInParent<Player>();
    }

    private void ApplySourceAmount(float amount)
    {
        if (item?.itemData?.Stack == null)
            return;

        if (!TryGetSourceHotBarSlot(out Mod_HotBar hotBar, out ItemSlot sourceSlot))
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
    private static void RefreshSourceHotBarSlot(Mod_HotBar hotBar, ItemSlot sourceSlot)
    {
        if (hotBar?.Data?.itemSlots == null || sourceSlot == null)
            return;

        int slotIndex = hotBar.Data.itemSlots.IndexOf(sourceSlot);
        if (slotIndex >= 0)
            hotBar.RefreshUI(slotIndex);
    }

    /// <summary>建筑召唤器只允许从玩家当前快捷栏真实槽位扣除。</summary>
    private bool TryGetSourceHotBarSlot(out Mod_HotBar hotBar, out ItemSlot sourceSlot)
    {
        hotBar = item?.Owner?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        sourceSlot = hotBar?.CurrentSelectItemSlot;
        if (hotBar?.Data?.itemSlots == null || sourceSlot == null || !hotBar.Data.itemSlots.Contains(sourceSlot))
            return false;

        ItemData slotData = sourceSlot.itemData;
        ItemData heldData = item?.itemData;
        return slotData != null && heldData != null &&
               (ReferenceEquals(slotData, heldData) || (heldData.Guid != 0 && slotData.Guid == heldData.Guid));
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

        damageReceiver = item.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        if (damageReceiver == null)
            throw new MissingComponentException($"[Mod_Building] {item.name} 缺少 Mod_DamageReceiver 模块");
    }

    private void BindRuntimeEvents()
    {
        if (_eventsBound || damageReceiver == null || item == null)
            return;

        damageReceiver.OnAction += OnHit;
        BindFatalDamageTracking();
        damageReceiver.OnDead += OnDeath;
        if (!RequiresPlacementRequest)
            item.OnAct += Install;
        BindPlacementRotationInput();
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
        UnbindPlacementRotationInput();
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
        ApplyInstalledOrientation();
        ApplyHorizontalMirrorVisual();
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

        // 可通行建筑保留 Trigger 供交互/受击查询，但不再形成角色物理阻挡。
        bool blocksMovement = BlocksMovement;
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
                colliders[i].isTrigger = trigger || !blocksMovement;
        }

        if (damageReceiver == null)
            return;

        Collider2D[] damageReceiverColliders = damageReceiver.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < damageReceiverColliders.Length; i++)
            damageReceiverColliders[i].enabled = enabled && damageReceiverEnabled;
    }

    private void SyncNavigationOccupancy()
    {
        // 起降交接按物品真实所属世界判断，船件身份预留不能阻止地表占地登记。
        if (!isActiveAndEnabled || item == null || !IsInstalled() ||
            FlatWorld.Spaceflight.SpaceSession.Current?.TryGetShipInteriorItem(
                item.itemData.Guid.ToString(System.Globalization.CultureInfo.InvariantCulture), out _) == true)
        {
            BuildingOccupancyRegistry.Unregister(this);
            return;
        }

        BuildingOccupancyRegistry.Register(this, GetFootprintCells(GetPlacementCell(item.transform.position)));
        Mod_Door door = item.itemMods?.GetMod_ByID<Mod_Door>("Door");
        if (door != null) BuildingOccupancyRegistry.SetPassable(this, door.Data.IsOpen);
    }
    private bool hasPhysicsOccupancyPose;
    private Vector2Int physicsOccupancyCell;
    private int physicsOccupancyQuarterTurns;
    private string physicsOccupancyWorld;

    public void OnWorldPhysicsPoseCommitted(Item owner, WorldPhysicsEntityPose2D pose)
    {
        if (owner != item || !_isLoaded) return;
        Vector2Int cell = GetPlacementCell(item.transform.position);
        int quarterTurns = Mathf.RoundToInt(pose.RotationDegrees / 90f) & 3;
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(item);
        if (hasPhysicsOccupancyPose && cell == physicsOccupancyCell && quarterTurns == physicsOccupancyQuarterTurns &&
            worldKey == physicsOccupancyWorld) return;
        physicsOccupancyCell = cell; physicsOccupancyQuarterTurns = quarterTurns; physicsOccupancyWorld = worldKey;
        hasPhysicsOccupancyPose = true;
        SyncNavigationOccupancy();
    }

    /// <summary>枚举建筑实际占用的世界格，所有格均按世界拓扑独立归一化。</summary>
    private IEnumerable<Vector2Int> GetFootprintCells(Vector2Int anchorCell)
    {
        GetUnrotatedFootprintSize(out int width, out int height);
        int quarterTurns = Data?.Role == BuildingRole.PlacedBuilding
            ? Mathf.RoundToInt((item?.itemData?.transform?.rotation ?? Quaternion.identity).eulerAngles.z / 90f) & 3
            : PlacementQuarterTurns;
        if ((quarterTurns & 1) != 0) (width, height) = (height, width);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                yield return WorldTopologyRuntime.NormalizeCell(new Vector2Int(anchorCell.x + x, anchorCell.y + y));
    }

    /// <summary>成功安装后按完整占地清草，单机和联机共用。</summary>
    internal void ClearPlacementGrass()
    {
        if (item == null) return;
        foreach (Vector2Int cell in GetFootprintCells(GetPlacementCell(item.transform.position)))
            RuntimeGrassClearing.ClearAt(new Vector3(cell.x + 0.5f, cell.y + 0.5f));
    }

    /// <summary>吸附后的格心是建筑占地的左下锚点。</summary>
    private static Vector2Int GetPlacementCell(Vector3 position)
        => WorldTopologyRuntime.NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y)));

    private Vector3 NormalizeRequestedPlacement(Vector3 position, Player actor = null)
        => FlatWorld.Spaceflight.SpaceSession.Current?.TrySnapPlacement(item?.itemData, position, PlacementQuarterTurns,
                out Vector2 snapped, out _, actor != null ? actor : ResolvePlacementActor()) == true
            ? new Vector3(snapped.x, snapped.y, 0f) : NormalizePlacement(position);

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

    internal static int GenerateUniqueRuntimeGuid()
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
    #endregion
}

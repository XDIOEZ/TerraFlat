using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>机械节点的 Item 表现与交互桥。普通齿轮旋转主 Sprite 并保留固定输入杆；变速箱的大小齿轮按输入 RPM 与半径比独立旋转；动力源用独立叶轮层，世界状态由 MachineWorld 整网管理。</summary>
public sealed partial class Mod_MechanicalNode : Module, IInteractable, IBuildingPlacementCommitted,
    IBuildingPlacementExtension, IBuildingTraversalPolicy, IBuildingSnapshotRepackPolicy, IBuildingPreviewRotation
{
    #region 配置与状态
    public const string ModuleId = "机械扭矩模块";
    private const string InputShaftState = "inputShaft";
    private const string InputShaftObjectName = "MechanicalInputShaft";
    private const string GearboxGearState = "gearboxGear";
    private const string GearboxLargeGearObjectName = "MechanicalGearboxLargeGear";
    private const string GearboxSmallGearObjectName = "MechanicalGearboxSmallGear";
    private const string AxisPortState = "axisPorts";
    private const string MirroredAxisPortLayout = "mirroredSingle";
    private const string CenteredShaftRingLayout = "centeredShaftRings";
    private const string AxisPortLeftObjectName = "MechanicalAxisPortLeft";
    private const string AxisPortRightObjectName = "MechanicalAxisPortRight";
    private const string AxisPortRingsObjectName = "MechanicalAxisPortRings";
    private const string RotorState = "rotor";
    private const string RotorObjectName = "MechanicalRotor";
    private const string ShaftCoreState = "shaftRollingCore";
    private const string ShaftRingsState = "shaftEndRings";
    private const string ShaftCoreObjectName = "MechanicalShaftCore";
    private const string ShaftRingsObjectName = "MechanicalShaftEndRings";
    private const string CrossShaftLeftCoreObjectName = "MechanicalCrossShaftLeftCore";
    private const string CrossShaftRightCoreObjectName = "MechanicalCrossShaftRightCore";
    private const string MechanicalShaftSortingLayerName = "MechanicalShaft";
    private const float ShaftCoreWorldWidth = 116f / 128f;
    private const float ShaftCoreWorldHeight = 28f / 128f;
    private const float CrossShaftCoreWorldWidth = 24f / 128f; // 轴承窗口中露出的滚动木芯长度。
    private const float CrossShaftCoreOffsetX = 44f / 128f; // 左右木芯中心距跨轴器中心的偏移。
    private const float AlternateGearPhaseDegrees = 22.5f;
    private const float GearboxMeshPhaseDegrees = 22.5f;
    private static readonly int MainTextureScaleOffset = Shader.PropertyToID("_MainTex_ST");
    public string DefinitionId; // 对应机械配置目录的稳定节点 ID
    public float InputShaftOffsetX = -0.45f; // 固定输入杆中心的局部横向偏移，保留轮盘下的隐藏连接段。
    public string AxisPortLayout; // 选择镜像接头、居中双端铁环或独立单端接口。
    public float AxisPortOffset = 0.45f; // 镜像单端接头各自相对建筑中心的偏移。
    public float AxisPortOffsetY; // 轴端口相对主体锚点的纵向偏移。
    public bool AxisPortDrawOnTop; // 外露轴环安装在支架表面时，绘制在主体前方。
    public Vector3 InputShaftLocalPosition = new Vector3(-0.55f, 0f, 0f); // 风箱轴口坐标，按风车独立图层方式贴合主体轴心。
    public float BellowsCompression = 0.32f; // 风囊皮革相对展开高度的最大收缩比例，和机械相位同步。
    public Vector3 RotorLocalPosition; // 叶轮中心相对主体轴心锚点的位置，由物品配置提供。
    public bool RotorBehindBody; // 嵌入式转子放在机身后方，由前沿遮住下部。
    public float ReciprocatingStroke; // 往复件行程由机械图层读取，同时作为 JSON 模块参数严格校验的字段。
    public Vector3 GearboxLargeGearLocalPosition = new Vector3(-12f / 128f, 0f, 0f); // 左侧大齿轮中心，按 128 像素格配置。
    public Vector3 GearboxSmallGearLocalPosition = new Vector3(24f / 128f, 0f, 0f); // 右侧小齿轮中心，按 128 像素格配置。
    public float GearboxLargeGearScale = 0.875f; // 大齿轮尺寸，和齿轮半径共同决定传动比。
    public float GearboxSmallGearScale = 0.4375f; // 小齿轮尺寸为大齿轮的一半。
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => placed && Node != null &&
        (shaftCoreRenderer != null && shaftCoreRenderer.enabled ||
         crossShaftLeftCoreRenderer != null && crossShaftLeftCoreRenderer.enabled ||
         crossShaftRightCoreRenderer != null && crossShaftRightCoreRenderer.enabled ||
         (Definition?.Kind == "gear" && inputShaftSprite != null) ||
         (Definition?.Kind == "gearbox" && gearboxLargeGearRenderer != null && gearboxLargeGearRenderer.enabled) ||
         (rotorRenderer != null && rotorRenderer.enabled))
        ? ModuleTickMode.EveryFrame : ModuleTickMode.Disabled;
    public MachineDefinition Definition { get; private set; }
    public MachineEntity Node { get; private set; }
    public MachineState LocalState { get; private set; }
    public int PlacementQuarterTurns { get; private set; } // 当前手持预览的逆时针九十度步数。
    public bool CanRotatePlacement => !placed && item != null && item.InHand && Definition != null && Definition.Rotatable;
    public int OccupancyLayer => ResolveDefinition()?.Layer ?? 0;
    public bool BlocksMovement => ResolveDefinition()?.BlocksMovement ?? true;
    public float PlayerMoveSpeedMultiplier => ResolveDefinition()?.PlayerMoveSpeedMultiplier ?? 1f;
    private MechanicalPanelSession panel;
    private GameController controller;
    private SpriteRenderer spriteRenderer;
    private SortingGroup buildingSortingGroup; // 建筑根节点的整体深度排序组。
    private bool hasOriginalBuildingSortingGroup;
    private bool originalBuildingSortingGroupEnabled;
    private int originalBuildingSortingLayer;
    private bool mechanicalShaftSortingLayerApplied;
    private bool mechanicalShaftSortingLayerResolved;
    private int mechanicalShaftSortingLayerId;
    private Quaternion originalRotation;
    private Vector3 originalSpriteLocalPosition;
    private int originalSortingOrder;
    private bool placed;
    private Sprite inputShaftSprite; // 由物品 visual.spriteStates 预载的固定杆。
    private SpriteRenderer inputShaftRenderer; // 独立于旋转齿轮的静态图层。
    private Sprite gearboxGearSprite; // 由物品 visual.spriteStates 预载的箱内旋转齿轮。
    private SpriteRenderer gearboxLargeGearRenderer; // 左侧大齿轮独立旋转图层。
    private SpriteRenderer gearboxSmallGearRenderer; // 右侧小齿轮独立旋转图层。
    private float gearboxLargeGearAngleDegrees; // 大齿轮表现相位，不参与机械网络存档。
    private float gearboxSmallGearAngleDegrees; // 小齿轮表现相位，不参与机械网络存档。
    private Sprite axisPortSprite; // 由物品 visual.spriteStates 预载的轴端图层。
    private SpriteRenderer axisPortLeftRenderer; // 镜像单端接头的局部负向图层。
    private SpriteRenderer axisPortRightRenderer; // 镜像单端接头的局部正向图层。
    private SpriteRenderer axisPortRingsRenderer; // 居中整格传动杆的双端铁环图层。
    private float gearAngleDegrees; // 本地表现相位，不参与机械网络存档。
    private Sprite shaftCoreSprite; // 可平铺的木杆纹理周期。
    private Sprite shaftRingsSprite; // 固定在传动轴两端的金属环。
    private SpriteRenderer shaftCoreRenderer; // 按机械转速滚动木纹的独立图层。
    private SpriteRenderer shaftRingsRenderer; // 覆盖木芯两端的静态金属图层。
    private SpriteRenderer crossShaftLeftCoreRenderer; // 跨轴器左侧按转速滚动的木芯。
    private SpriteRenderer crossShaftRightCoreRenderer; // 跨轴器右侧按转速滚动的木芯。
    private MaterialPropertyBlock shaftCorePropertyBlock; // 只修改木芯材质 UV 偏移，不实例化材质。
    private float shaftTextureOffset; // 传动轴木纹循环相位。
    private bool shaftVisualConfigured;
    private bool originalShaftSpriteEnabled;
    private Sprite rotorSprite; // 由物品 visual.spriteStates 预载的可旋转叶轮。
    private SpriteRenderer rotorRenderer; // 与塔体共享排序组的叶轮图层。
    private SortingGroup rotorSortingGroup;
    private bool originalRotorGroupEnabled;
    private int originalRotorGroupLayer;
    private int originalRotorGroupOrder;
    private float rotorAngleDegrees; // 叶轮表现相位，不参与机械网络存档。
    private float manualInteractionElapsed; // 本次手摇轮按住时长。
    private bool manualInteractionActive; // 当前是否处于手摇轮交互按住阶段。
    private bool manualInteractionCranking; // 本次按住是否越过起摇阈值。

    /// <summary>Load 前也允许建筑模块读取机械通行配置，避免模块加载顺序影响碰撞语义。</summary>
    private MachineDefinition ResolveDefinition()
        => Definition ?? MachineCatalog.Get(string.IsNullOrWhiteSpace(DefinitionId) ? item?.itemData?.IDName : DefinitionId);
    #endregion

    #region 生命周期
    public override void Load()
    {
        Definition = MachineCatalog.Get(string.IsNullOrWhiteSpace(DefinitionId) ? item.itemData.IDName : DefinitionId)
            ?? throw new InvalidOperationException("机械定义不存在：" + DefinitionId);
        LocalState = Data.GetData<MachineState>() ?? new MachineState();
        PlacementQuarterTurns = 0;
        manualInteractionElapsed = 0f;
        manualInteractionActive = false;
        manualInteractionCranking = false;
        placed = Mod_Building.TryReadBuildingData(item.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding;
        spriteRenderer = item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalRotation = spriteRenderer.transform.localRotation;
            originalSpriteLocalPosition = spriteRenderer.transform.localPosition;
            originalSortingOrder = spriteRenderer.sortingOrder;
        }
        buildingSortingGroup = item.GetComponent<SortingGroup>();
        hasOriginalBuildingSortingGroup = buildingSortingGroup != null;
        if (hasOriginalBuildingSortingGroup)
        {
            originalBuildingSortingGroupEnabled = buildingSortingGroup.enabled;
            originalBuildingSortingLayer = buildingSortingGroup.sortingLayerID;
        }
        gearboxLargeGearAngleDegrees = 0f;
        gearboxSmallGearAngleDegrees = GearboxMeshPhaseDegrees;
        ConfigureGearVisual();
        ConfigureGearboxVisual();
        ConfigureRotorVisual();
        ConfigureAxisPortVisual();
        ConfigureElectricalPortVisual();
        ConfigureShaftVisual();
        item.OnInHandChanged += OnHandChanged;
        if (!placed) BindRotationInput();
        if (placed && building.State is BuildingState.Installed or BuildingState.Damaged) AttachWorld();
        ApplyVisual();
    }
    public override void Save()
    {
        if (placed) Data.WriteData(Node?.State ?? LocalState);
        // Summoner 不保存临时朝向，保持同堆物品身份一致。
    }
    public override void Unload()
    {
        panel?.Dispose(); panel = null;
        if (item != null) item.OnInHandChanged -= OnHandChanged;
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = null;
        MachineWorld.Detach(this);
        Node = null; PlacementQuarterTurns = 0;
        manualInteractionElapsed = 0f;
        manualInteractionActive = false;
        manualInteractionCranking = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = originalRotation;
            spriteRenderer.transform.localPosition = originalSpriteLocalPosition;
            spriteRenderer.sortingOrder = originalSortingOrder;
        }
        if (inputShaftRenderer != null) inputShaftRenderer.enabled = false;
        if (axisPortLeftRenderer != null) axisPortLeftRenderer.enabled = false;
        if (axisPortRightRenderer != null) axisPortRightRenderer.enabled = false;
        if (axisPortRingsRenderer != null) axisPortRingsRenderer.enabled = false;
        ClearElectricalPortVisual();
        if (gearboxLargeGearRenderer != null) gearboxLargeGearRenderer.enabled = false;
        if (gearboxSmallGearRenderer != null) gearboxSmallGearRenderer.enabled = false;
        if (rotorRenderer != null) rotorRenderer.enabled = false;
        RestoreShaftVisual();
        RestoreRotorSortingGroup();
        RestoreMechanicalShaftSortingLayer();
        buildingSortingGroup = null;
        hasOriginalBuildingSortingGroup = false;
        mechanicalShaftSortingLayerResolved = false;
        inputShaftSprite = null;
        inputShaftRenderer = null;
        gearboxGearSprite = null;
        gearboxLargeGearRenderer = null;
        gearboxSmallGearRenderer = null;
        gearboxLargeGearAngleDegrees = 0f;
        gearboxSmallGearAngleDegrees = 0f;
        axisPortSprite = null;
        axisPortLeftRenderer = null;
        axisPortRightRenderer = null;
        axisPortRingsRenderer = null;
        gearAngleDegrees = 0f;
        shaftCoreSprite = null;
        shaftRingsSprite = null;
        shaftCoreRenderer = null;
        shaftRingsRenderer = null;
        crossShaftLeftCoreRenderer = null;
        crossShaftRightCoreRenderer = null;
        shaftCorePropertyBlock = null;
        shaftTextureOffset = 0f;
        rotorSprite = null;
        rotorRenderer = null;
        rotorAngleDegrees = 0f;
    }
    public override void OnResourcesReloaded()
    {
        ConfigureGearVisual();
        ConfigureGearboxVisual();
        ConfigureRotorVisual();
        ConfigureAxisPortVisual();
        ConfigureElectricalPortVisual();
        ConfigureShaftVisual();
        ApplyVisual();
        InvalidateTickSchedule();
    }
    public void OnBuildingPlacementCommitted()
    {
        placed = true;
        SetInitialGearPhase();
        ConfigureGearboxVisual();
        ConfigureRotorVisual();
        ConfigureAxisPortVisual();
        ConfigureElectricalPortVisual();
        ConfigureShaftVisual();
        AttachWorld();
        InvalidateTickSchedule();
    }
    private void AttachWorld() { Node = MachineWorld.Attach(this); if (Node != null) LocalState = Node.State; ApplyVisual(); }
    private void BindRotationInput()
    {
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = item.Owner?.GetComponentInChildren<GameController>();
        if (controller != null) controller.BuildingRotationRequested += RotatePlacement;
    }
    private void OnHandChanged(bool held)
    {
        PlacementQuarterTurns = 0;
        if (held) BindRotationInput();
        else if (controller != null) { controller.BuildingRotationRequested -= RotatePlacement; controller = null; }
    }
    #endregion

    #region 放置朝向
    /// <summary>轴类切换横竖，输入输出有方向的设备逐次切换四个朝向。</summary>
    public void RotatePlacement()
    {
        if (!CanRotatePlacement) return;
        int rotationSteps = Definition.Kind == "gear" || Definition.Kind == "gearbox" ||
                            Definition.Kind == "bellows" || Definition.Kind == "converter" ? 4 : 2;
        PlacementQuarterTurns = (PlacementQuarterTurns + 1) % rotationSteps;
        var building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        ApplyPreview(building?.GhostShadow);
    }
    public void ApplyPreview(BuildingShadow shadow)
    {
        if (shadow?.ShadowRenderer == null) return;
        Quaternion placementRotation = Quaternion.Euler(0f, 0f, PlacementQuarterTurns * 90f);
        if (Definition.Rotatable)
            shadow.ShadowRenderer.transform.localRotation = placementRotation;
        if (inputShaftSprite != null)
        {
            Vector3 inputShaftOffset = ResolveInputShaftLocalPosition();
            SpriteRenderer previewShaft = shadow.EnsureOverlay(InputShaftObjectName, inputShaftSprite,
                shadow.ShadowRenderer.transform.localPosition + placementRotation * inputShaftOffset,
                spriteRenderer?.sharedMaterial);
            if (previewShaft != null)
            {
                previewShaft.transform.localRotation = placementRotation;
                previewShaft.flipX = spriteRenderer != null && spriteRenderer.flipX;
                previewShaft.flipY = spriteRenderer != null && spriteRenderer.flipY;
            }
        }
        if (rotorSprite != null)
        {
            SpriteRenderer previewRotor = shadow.EnsureOverlay(RotorObjectName, rotorSprite,
                shadow.ShadowRenderer.transform.localPosition + placementRotation * RotorLocalPosition,
                spriteRenderer?.sharedMaterial);
            if (previewRotor != null)
            {
                previewRotor.transform.localRotation = placementRotation;
                previewRotor.sortingOrder = shadow.ShadowRenderer.sortingOrder + (RotorBehindBody ? -1 : 1);
            }
        }
        ApplyGearboxPreview(shadow, placementRotation);
        ApplyAxisPortPreview(shadow, placementRotation);
        ApplyElectricalPortPreview(shadow, placementRotation);
    }

    /// <summary>在放置虚影上叠加两枚齿轮，并随建筑 R 键朝向同步旋转。</summary>
    private void ApplyGearboxPreview(BuildingShadow shadow, Quaternion placementRotation)
    {
        if (gearboxGearSprite == null || Definition?.Kind != "gearbox") return;
        Vector3 basePosition = shadow.ShadowRenderer.transform.localPosition;
        ApplyGearboxPreviewGear(shadow, GearboxLargeGearObjectName, GearboxLargeGearLocalPosition,
            GearboxLargeGearScale, gearboxLargeGearAngleDegrees, basePosition, placementRotation);
        ApplyGearboxPreviewGear(shadow, GearboxSmallGearObjectName, GearboxSmallGearLocalPosition,
            GearboxSmallGearScale, gearboxSmallGearAngleDegrees, basePosition, placementRotation);
    }

    /// <summary>设置放置预览中的单枚齿轮位置、尺寸、朝向和箱体上层排序。</summary>
    private void ApplyGearboxPreviewGear(BuildingShadow shadow, string objectName, Vector3 localPosition,
        float scale, float angleDegrees, Vector3 basePosition, Quaternion placementRotation)
    {
        SpriteRenderer previewGear = shadow.EnsureOverlay(objectName, gearboxGearSprite,
            basePosition + placementRotation * localPosition, spriteRenderer?.sharedMaterial);
        if (previewGear == null) return;
        previewGear.transform.localRotation = placementRotation * Quaternion.Euler(0f, 0f, angleDegrees);
        previewGear.transform.localScale = Vector3.one * scale;
        previewGear.sortingOrder = shadow.ShadowRenderer.sortingOrder + 1;
    }

    /// <summary>按配置把轴端口叠加到建筑虚影，并与主体预览使用相同朝向。</summary>
    private void ApplyAxisPortPreview(BuildingShadow shadow, Quaternion placementRotation)
    {
        if (axisPortSprite == null) return;
        Vector3 basePosition = shadow.ShadowRenderer.transform.localPosition;
        if (AxisPortLayout == CenteredShaftRingLayout || AxisPortLayout == SingleAxisPortLayout)
        {
            SpriteRenderer portRings = shadow.EnsureOverlay(AxisPortRingsObjectName, axisPortSprite,
                basePosition + placementRotation * ResolveSingleAxisPortPosition(), spriteRenderer?.sharedMaterial);
            if (portRings != null)
            {
                portRings.transform.localRotation = placementRotation;
                portRings.transform.localScale = Vector3.one;
                portRings.sortingOrder = shadow.ShadowRenderer.sortingOrder + (AxisPortDrawOnTop ? 1 : -1);
            }
            return;
        }

        float offset = Mathf.Abs(AxisPortOffset);
        SpriteRenderer leftPort = shadow.EnsureOverlay(AxisPortLeftObjectName, axisPortSprite,
            basePosition + placementRotation * new Vector3(-offset, AxisPortOffsetY, 0f), spriteRenderer?.sharedMaterial);
        SpriteRenderer rightPort = shadow.EnsureOverlay(AxisPortRightObjectName, axisPortSprite,
            basePosition + placementRotation * new Vector3(offset, AxisPortOffsetY, 0f), spriteRenderer?.sharedMaterial);
        if (leftPort != null)
        {
            leftPort.transform.localRotation = placementRotation;
            leftPort.flipX = spriteRenderer != null && spriteRenderer.flipX;
        }
        if (rightPort != null)
        {
            rightPort.transform.localRotation = placementRotation;
            rightPort.flipX = spriteRenderer == null || !spriteRenderer.flipX;
        }
    }

    public bool ValidatePlacement(Vector2Int cell, out string reason)
        => MachineWorld.ValidatePlacement(Definition, cell, (PlacementQuarterTurns & 1) != 0, out reason);

    /// <summary>候选本体建立后、Load 前写世界朝向；覆盖拆回快照中的旧方向。</summary>
    public void PreparePlacedData(ItemData data)
    {
        var state = MachineWorld.ReadMachineState(data);
        state.RotationQuarterTurns = PlacementQuarterTurns;
        MachineWorld.WriteMachineState(data, state);
    }
    /// <summary>拆回快照只重置世界朝向，不改动仍在世界中的节点，也不丢弃机器库存。</summary>
    public void PrepareRepackedSnapshot(ItemData snapshot)
    {
        var state = MachineWorld.ReadMachineState(snapshot);
        state.RotationQuarterTurns = 0;
        MachineWorld.WriteMachineState(snapshot, state);
    }

    /// <summary>无额外机械状态的传动轴可由定义重建，省略快照后能与新制传动轴合堆。</summary>
    public bool CanOmitRepackedSnapshot(ItemData normalizedSnapshot)
    {
        if (Definition?.Kind != "shaft" || normalizedSnapshot == null ||
            !string.Equals(normalizedSnapshot.IDName, Definition.Id, StringComparison.Ordinal) ||
            normalizedSnapshot.Stack == null ||
            !normalizedSnapshot.Stack.Stackable || normalizedSnapshot.ModuleDataDic == null ||
            normalizedSnapshot.ModuleDataDic.Count != 2 ||
            MachinePersistence.HasCustomState(normalizedSnapshot) ||
            !string.IsNullOrEmpty(normalizedSnapshot.FactionId) ||
            GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(normalizedSnapshot.IDName, out RuntimeItemDefinition itemDefinition) ||
            !MatchesItemDefinition(normalizedSnapshot, itemDefinition.CreateItemData()) ||
            !Mod_Building.TryReadBuildingData(normalizedSnapshot, out _, out Mod_Building.Building_Data buildingData) ||
            (buildingData.SharedModuleIds != null && buildingData.SharedModuleIds.Length != 0) ||
            !MachineWorld.TryGetModuleData(normalizedSnapshot, out Ex_ModData_MemoryPackable module))
            return false;

        int buildingModuleCount = 0;
        int mechanicalModuleCount = 0;
        foreach (var entry in normalizedSnapshot.ModuleDataDic)
        {
            if (entry.Value == null) return false;
            if (string.Equals(entry.Value.ID, ModText.Building, StringComparison.Ordinal))
                buildingModuleCount++;
            else if (string.Equals(entry.Value.ID, ModuleId, StringComparison.Ordinal))
                mechanicalModuleCount++;
            else
                return false;
        }

        if (buildingModuleCount != 1 || mechanicalModuleCount != 1) return false;
        MachineState state = module.GetData<MachineState>();
        return IsDefinitionDefaultShaftState(state);
    }

    /// <summary>确保不会因省略快照而丢掉物品定义之外的耐久、标签、堆叠或自定义数据。</summary>
    private static bool MatchesItemDefinition(ItemData snapshot, ItemData definitionData)
    {
        if (definitionData == null || snapshot.GetType() != definitionData.GetType() ||
            !string.Equals(snapshot.GameName, definitionData.GameName, StringComparison.Ordinal) ||
            !Mathf.Approximately(snapshot.Durability, definitionData.Durability) ||
            !Mathf.Approximately(snapshot.MaxDurability, definitionData.MaxDurability) ||
            !Mathf.Approximately(snapshot.CraftedDurabilityMultiplier, definitionData.CraftedDurabilityMultiplier) ||
            !string.Equals(snapshot.FactionId, definitionData.FactionId, StringComparison.Ordinal) ||
            !TagsMatch(snapshot.Tags, definitionData.Tags) ||
            snapshot.Stack == null || definitionData.Stack == null ||
            !Mathf.Approximately(snapshot.Stack.Weight, definitionData.Stack.Weight) ||
            !Mathf.Approximately(snapshot.Stack.Volume, definitionData.Stack.Volume) ||
            snapshot.Stack.Stackable != definitionData.Stack.Stackable ||
            snapshot.Stack.CanBePickedUp != definitionData.Stack.CanBePickedUp)
            return false;

        return snapshot is not Data_GeneralItem snapshotGeneral ||
               definitionData is Data_GeneralItem definitionGeneral &&
               string.Equals(snapshotGeneral.code, definitionGeneral.code, StringComparison.Ordinal);
    }

    /// <summary>逐项比较标签，避免物品定义之外的运行时标签被静默丢弃。</summary>
    private static bool TagsMatch(System.Collections.Generic.List<string> snapshotTags,
        System.Collections.Generic.List<string> definitionTags)
    {
        if (ReferenceEquals(snapshotTags, definitionTags)) return true;
        if (snapshotTags == null || definitionTags == null || snapshotTags.Count != definitionTags.Count)
            return false;

        for (int i = 0; i < snapshotTags.Count; i++)
            if (!string.Equals(snapshotTags[i], definitionTags[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    /// <summary>只省略可由节点定义完整重建的默认状态；库存、运行状态或损伤都保留快照。</summary>
    private static bool IsDefinitionDefaultShaftState(MachineState state)
    {
        if (state == null) return true;
        if (state.RotationQuarterTurns != 0 || !state.Engaged || state.RatioIndex != 1 ||
            !Mathf.Approximately(state.ManualSeconds, 0f))
            return false;

        RecipeProcessingState processing = state.Processing;
        if (processing == null) return true;
        if (!string.IsNullOrEmpty(processing.RecipeId) || !Mathf.Approximately(processing.Progress, 0f))
            return false;

        return IsEmptyInventory(processing.Input) && IsEmptyInventory(processing.Output);
    }

    /// <summary>有物品、缺失槽位或损坏库存结构时保留快照，不静默丢掉模块内容。</summary>
    private static bool IsEmptyInventory(Inventory_Data inventory)
    {
        if (inventory == null || inventory.itemSlots == null) return inventory == null;
        foreach (ItemSlot slot in inventory.itemSlots)
            if (slot == null || slot.itemData != null) return false;
        return true;
    }

    private void ApplyVisual()
    {
        if (spriteRenderer == null) return;
        ApplyMechanicalShaftSortingLayer();
        spriteRenderer.transform.localPosition = originalSpriteLocalPosition;
        if (placed)
            spriteRenderer.transform.localRotation = originalRotation *
                Quaternion.Euler(0f, 0f, LocalState.RotationQuarterTurns * 90f + gearAngleDegrees);
        if (placed && Definition.Layer == 1) spriteRenderer.sortingOrder = 2;
        if (inputShaftRenderer != null)
        {
            // 固定杆保留原排序，主齿轮高一层；不依赖相机的透明物体 Z 排序模式。
            spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, originalSortingOrder + 1);
            inputShaftRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            inputShaftRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
            if (Definition.Kind == "bellows")
            {
                inputShaftRenderer.transform.localPosition = InputShaftLocalPosition;
                inputShaftRenderer.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Quaternion shaftRotation = Quaternion.Euler(0f, 0f,
                    placed ? LocalState.RotationQuarterTurns * 90f : 0f);
                inputShaftRenderer.transform.localPosition = shaftRotation * new Vector3(InputShaftOffsetX, 0f, 0f);
                inputShaftRenderer.transform.localRotation = shaftRotation;
            }
        }
        ApplyAxisPortVisual();
        ApplyElectricalPortVisual();
        ApplyGearboxVisual();
        ApplyShaftSorting();
        if (rotorRenderer == null) return;
        rotorRenderer.transform.localPosition = RotorLocalPosition;
        rotorRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotorAngleDegrees);
        rotorRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        rotorRenderer.sortingOrder = RotorBehindBody ? -1 : 1;
        // 把塔与叶轮当作同一个世界实体排序，叶轮只在组内盖过塔体。
        rotorSortingGroup.sortingLayerID = spriteRenderer.sortingLayerID;
        rotorSortingGroup.sortingOrder = originalSortingOrder;
        spriteRenderer.sortingOrder = 0;
    }
    #endregion

    #region 传动轴深度排序
    /// <summary>让已放置的传动轴与跨轴器使用地面式独立排序层，并由根组按 Y 轴决定前后关系。</summary>
    private void ApplyMechanicalShaftSortingLayer()
    {
        if (!IsMechanicalShaftDepthSorted())
        {
            RestoreMechanicalShaftSortingLayer();
            return;
        }

        if (!hasOriginalBuildingSortingGroup || buildingSortingGroup == null)
            throw new InvalidOperationException("传动轴建筑缺少根 SortingGroup，无法按地面深度排序。");

        int sortingLayerId = GetMechanicalShaftSortingLayerId();
        if (buildingSortingGroup.sortingLayerID != sortingLayerId)
            buildingSortingGroup.sortingLayerID = sortingLayerId;
        if (!buildingSortingGroup.enabled)
            buildingSortingGroup.enabled = true;
        mechanicalShaftSortingLayerApplied = true;
    }

    /// <summary>只对已放置的传动轴类建筑启用专属排序层。</summary>
    private bool IsMechanicalShaftDepthSorted()
        => placed && Definition != null &&
           (Definition.Kind == "shaft" || Definition.Kind == "bridge" && Definition.Id == "CrossShaft");

    /// <summary>按项目排序层配置解析专属层，缺失时明确报错而不退回默认层。</summary>
    private int GetMechanicalShaftSortingLayerId()
    {
        if (mechanicalShaftSortingLayerResolved) return mechanicalShaftSortingLayerId;

        foreach (SortingLayer sortingLayer in SortingLayer.layers)
        {
            if (sortingLayer.name != MechanicalShaftSortingLayerName) continue;
            mechanicalShaftSortingLayerId = sortingLayer.id;
            mechanicalShaftSortingLayerResolved = true;
            return mechanicalShaftSortingLayerId;
        }

        throw new InvalidOperationException("项目缺少 Sorting Layer：" + MechanicalShaftSortingLayerName);
    }

    /// <summary>卸载或离开传动轴状态时恢复根排序组的原始层与启用状态。</summary>
    private void RestoreMechanicalShaftSortingLayer()
    {
        if (!mechanicalShaftSortingLayerApplied) return;
        if (buildingSortingGroup != null)
        {
            buildingSortingGroup.sortingLayerID = originalBuildingSortingLayer;
            buildingSortingGroup.enabled = originalBuildingSortingGroupEnabled;
        }
        mechanicalShaftSortingLayerApplied = false;
    }
    #endregion

    #region 传动轴连接图层
    /// <summary>读取物品声明的传动轴 Sprite 状态并创建独立连接图层。</summary>
    private void ConfigureGearVisual()
    {
        inputShaftSprite = null;
        if ((Definition.Kind != "gear" && Definition.Kind != "bellows") || spriteRenderer == null ||
            GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(InputShaftState, out inputShaftSprite)) return;

        Transform overlayParent = Definition.Kind == "bellows" ? spriteRenderer.transform : item.transform;
        Transform existing = overlayParent.Find(InputShaftObjectName);
        if (existing == null && overlayParent != item.transform)
            existing = item.transform.Find(InputShaftObjectName);
        if (existing == null)
        {
            var overlay = new GameObject(InputShaftObjectName);
            overlay.transform.SetParent(overlayParent, false);
            existing = overlay.transform;
        }
        else if (existing.parent != overlayParent)
            existing.SetParent(overlayParent, false);
        existing.gameObject.layer = item.gameObject.layer;
        inputShaftRenderer = existing.GetComponent<SpriteRenderer>();
        if (inputShaftRenderer == null) inputShaftRenderer = existing.gameObject.AddComponent<SpriteRenderer>();
        inputShaftRenderer.sprite = inputShaftSprite;
        inputShaftRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
        inputShaftRenderer.color = spriteRenderer.color;
        inputShaftRenderer.maskInteraction = spriteRenderer.maskInteraction;
        inputShaftRenderer.spriteSortPoint = SpriteSortPoint.Pivot;
        inputShaftRenderer.enabled = true;
        if (Definition.Kind == "gear") SetInitialGearPhase();
    }

    /// <summary>齿轮保持原局部横向偏移，风箱轴口使用独立坐标并随主体预览旋转。</summary>
    private Vector3 ResolveInputShaftLocalPosition()
        => Definition.Kind == "bellows" ? InputShaftLocalPosition : new Vector3(InputShaftOffsetX, 0f, 0f);

    /// <summary>相邻格错开半齿距，旋转方向交替，使八齿在视觉上交错。</summary>
    private void SetInitialGearPhase()
    {
        if (Definition?.Kind != "gear" || inputShaftSprite == null || !placed) return;
        Vector2Int cell = Node?.Cell ?? MachineWorld.CellOf(item.transform.position);
        gearAngleDegrees = ((cell.x + cell.y) & 1) == 0 ? 0f : AlternateGearPhaseDegrees;
    }

    #endregion

    #region 变速箱齿轮图层
    /// <summary>读取变速箱齿轮 Sprite 状态，并在已放置建筑中建立两枚独立渲染层。</summary>
    private void ConfigureGearboxVisual()
    {
        gearboxGearSprite = null;
        if (gearboxLargeGearRenderer != null) gearboxLargeGearRenderer.enabled = false;
        if (gearboxSmallGearRenderer != null) gearboxSmallGearRenderer.enabled = false;
        if (Definition?.Kind != "gearbox" || spriteRenderer == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(GearboxGearState, out gearboxGearSprite) || !placed) return;

        gearboxLargeGearRenderer = GetOrCreateGearboxGearRenderer(GearboxLargeGearObjectName);
        gearboxSmallGearRenderer = GetOrCreateGearboxGearRenderer(GearboxSmallGearObjectName);
        ApplyGearboxVisual();
    }

    /// <summary>取得或建立挂在箱体 Sprite 下的独立齿轮渲染器。</summary>
    private SpriteRenderer GetOrCreateGearboxGearRenderer(string objectName)
    {
        Transform gearTransform = spriteRenderer.transform.Find(objectName);
        if (gearTransform == null)
        {
            var gearObject = new GameObject(objectName);
            gearTransform = gearObject.transform;
            gearTransform.SetParent(spriteRenderer.transform, false);
        }
        gearTransform.gameObject.layer = item.gameObject.layer;
        SpriteRenderer renderer = gearTransform.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = gearTransform.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = gearboxGearSprite;
        renderer.sharedMaterial = spriteRenderer.sharedMaterial;
        renderer.color = spriteRenderer.color;
        renderer.maskInteraction = spriteRenderer.maskInteraction;
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.enabled = true;
        return renderer;
    }

    /// <summary>应用齿轮中心、尺寸和独立角度；左大右小时角速度比固定为二比一。</summary>
    private void ApplyGearboxVisual()
    {
        if (spriteRenderer == null || gearboxGearSprite == null) return;
        ApplyGearboxGear(gearboxLargeGearRenderer, GearboxLargeGearLocalPosition,
            GearboxLargeGearScale, gearboxLargeGearAngleDegrees);
        ApplyGearboxGear(gearboxSmallGearRenderer, GearboxSmallGearLocalPosition,
            GearboxSmallGearScale, gearboxSmallGearAngleDegrees);
    }

    /// <summary>设置箱内单枚齿轮的渲染顺序和局部变换。</summary>
    private void ApplyGearboxGear(SpriteRenderer renderer, Vector3 localPosition, float scale, float angleDegrees)
    {
        if (renderer == null || !renderer.enabled) return;
        renderer.transform.localPosition = localPosition;
        renderer.transform.localScale = Vector3.one * scale;
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        renderer.sortingLayerID = spriteRenderer.sortingLayerID;
        renderer.sortingOrder = spriteRenderer.sortingOrder + 1;
    }
    #endregion

    #region 轴端口图层表现
    /// <summary>读取物品声明的轴端贴图，并按端口布局创建独立子图层。</summary>
    private void ConfigureAxisPortVisual()
    {
        axisPortSprite = null;
        if (axisPortLeftRenderer != null) axisPortLeftRenderer.enabled = false;
        if (axisPortRightRenderer != null) axisPortRightRenderer.enabled = false;
        if (axisPortRingsRenderer != null) axisPortRingsRenderer.enabled = false;
        if (Definition?.Ports != "axis" || spriteRenderer == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(AxisPortState, out axisPortSprite)) return;

        if (AxisPortLayout != MirroredAxisPortLayout && AxisPortLayout != CenteredShaftRingLayout &&
            AxisPortLayout != SingleAxisPortLayout)
            throw new InvalidOperationException($"机械端口图层布局未配置或无效：{item.itemData.IDName} / {AxisPortLayout}");
        if (!placed) return;
        switch (AxisPortLayout)
        {
            case MirroredAxisPortLayout:
                RemoveAxisPortRenderer(AxisPortRingsObjectName, ref axisPortRingsRenderer);
                axisPortLeftRenderer = GetOrCreateAxisPortRenderer(AxisPortLeftObjectName, spriteRenderer.flipX);
                axisPortRightRenderer = GetOrCreateAxisPortRenderer(AxisPortRightObjectName, !spriteRenderer.flipX);
                break;
            case CenteredShaftRingLayout:
            case SingleAxisPortLayout:
                RemoveAxisPortRenderer(AxisPortLeftObjectName, ref axisPortLeftRenderer);
                RemoveAxisPortRenderer(AxisPortRightObjectName, ref axisPortRightRenderer);
                axisPortRingsRenderer = GetOrCreateAxisPortRenderer(AxisPortRingsObjectName, spriteRenderer.flipX);
                break;
        }
    }

    /// <summary>切换端口布局时移除旧子图层，避免资源刷新后保留多余接口图片。</summary>
    private void RemoveAxisPortRenderer(string objectName, ref SpriteRenderer renderer)
    {
        Transform existing = spriteRenderer.transform.Find(objectName);
        if (existing != null)
        {
            SpriteRenderer previous = existing.GetComponent<SpriteRenderer>();
            if (previous != null) previous.enabled = false;
            Destroy(existing.gameObject);
        }
        renderer = null;
    }

    /// <summary>取得或建立与本体共用材质的机械端口图层。</summary>
    private SpriteRenderer GetOrCreateAxisPortRenderer(string objectName, bool flipX, Sprite portSprite = null)
    {
        Transform existing = spriteRenderer.transform.Find(objectName);
        if (existing == null)
        {
            var overlay = new GameObject(objectName);
            overlay.transform.SetParent(spriteRenderer.transform, false);
            existing = overlay.transform;
        }

        existing.gameObject.layer = item.gameObject.layer;
        existing.localPosition = Vector3.zero;
        existing.localRotation = Quaternion.identity;
        existing.localScale = Vector3.one;
        SpriteRenderer renderer = existing.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = existing.gameObject.AddComponent<SpriteRenderer>();
        ConfigureShaftRenderer(renderer, portSprite != null ? portSprite : axisPortSprite);
        renderer.flipX = flipX;
        renderer.enabled = true;
        return renderer;
    }

    /// <summary>按布局居中隐藏整格传动杆，或把镜像单端接头挂在轴线两侧。</summary>
    private void ApplyAxisPortVisual()
    {
        if (axisPortSprite == null || spriteRenderer == null) return;
        if (AxisPortLayout == CenteredShaftRingLayout || AxisPortLayout == SingleAxisPortLayout)
        {
            if (axisPortRingsRenderer == null || !axisPortRingsRenderer.enabled) return;
            if (!AxisPortDrawOnTop)
                spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, originalSortingOrder + 1);
            axisPortRingsRenderer.transform.localPosition = ResolveSingleAxisPortPosition();
            axisPortRingsRenderer.transform.localRotation = Quaternion.identity;
            axisPortRingsRenderer.transform.localScale = Vector3.one;
            ApplyAxisPortSorting(axisPortRingsRenderer, AxisPortDrawOnTop ? 1 : -1);
            return;
        }

        if (axisPortLeftRenderer == null || axisPortRightRenderer == null ||
            !axisPortLeftRenderer.enabled || !axisPortRightRenderer.enabled) return;
        spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, originalSortingOrder + 1);
        float offset = Mathf.Abs(AxisPortOffset);
        axisPortLeftRenderer.transform.localPosition = new Vector3(-offset, AxisPortOffsetY, 0f);
        axisPortRightRenderer.transform.localPosition = new Vector3(offset, AxisPortOffsetY, 0f);
        axisPortLeftRenderer.transform.localRotation = Quaternion.identity;
        axisPortRightRenderer.transform.localRotation = Quaternion.identity;
        axisPortLeftRenderer.flipX = spriteRenderer.flipX;
        axisPortRightRenderer.flipX = !spriteRenderer.flipX;
        ApplyAxisPortSorting(axisPortLeftRenderer);
        ApplyAxisPortSorting(axisPortRightRenderer);
    }

    /// <summary>轴端图层保持低于箱体，并沿用箱体的 SortingLayer。</summary>
    private void ApplyAxisPortSorting(SpriteRenderer renderer, int orderOffset = -1)
    {
        renderer.sortingLayerID = spriteRenderer.sortingLayerID;
        renderer.sortingOrder = spriteRenderer.sortingOrder + orderOffset;
    }
    #endregion

    #region 传动轴滚动表现
    /// <summary>把原整轴拆为可平铺木芯与静态端环；UV 滚动表现轴体自转。</summary>
    private void ConfigureShaftVisual()
    {
        if (shaftCoreRenderer != null) shaftCoreRenderer.enabled = false;
        if (shaftRingsRenderer != null) shaftRingsRenderer.enabled = false;
        if (crossShaftLeftCoreRenderer != null) crossShaftLeftCoreRenderer.enabled = false;
        if (crossShaftRightCoreRenderer != null) crossShaftRightCoreRenderer.enabled = false;
        bool isShaft = Definition?.Kind == "shaft";
        bool isCrossShaft = Definition?.Kind == "bridge" && Definition?.Id == "CrossShaft";
        if (!placed || (!isShaft && !isCrossShaft) || spriteRenderer == null ||
            GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(ShaftCoreState, out shaftCoreSprite) ||
            ((isShaft || isCrossShaft) && !definition.TryGetVisualStateSprite(ShaftRingsState, out shaftRingsSprite)))
        {
            if (shaftVisualConfigured) RestoreShaftVisual();
            return;
        }

        if (!shaftVisualConfigured)
        {
            originalShaftSpriteEnabled = spriteRenderer.enabled;
            shaftVisualConfigured = true;
            shaftTextureOffset = 0f;
        }

        if (isShaft)
        {
            shaftCoreRenderer = GetOrCreateShaftRenderer(ShaftCoreObjectName);
            shaftRingsRenderer = GetOrCreateShaftRenderer(ShaftRingsObjectName);
            ConfigureShaftRenderer(shaftCoreRenderer, shaftCoreSprite);
            ConfigureShaftRenderer(shaftRingsRenderer, shaftRingsSprite);
            ConfigureRollingCoreRenderer(shaftCoreRenderer, new Vector2(ShaftCoreWorldWidth, ShaftCoreWorldHeight));
            shaftRingsRenderer.drawMode = SpriteDrawMode.Simple;
            shaftCoreRenderer.enabled = true;
            shaftRingsRenderer.enabled = true;
            spriteRenderer.enabled = false;
        }
        else
        {
            shaftRingsRenderer = GetOrCreateShaftRenderer(ShaftRingsObjectName);
            crossShaftLeftCoreRenderer = GetOrCreateShaftRenderer(CrossShaftLeftCoreObjectName);
            crossShaftRightCoreRenderer = GetOrCreateShaftRenderer(CrossShaftRightCoreObjectName);
            ConfigureShaftRenderer(shaftRingsRenderer, shaftRingsSprite);
            shaftRingsRenderer.drawMode = SpriteDrawMode.Simple;
            shaftRingsRenderer.enabled = true;
            ConfigureShaftRenderer(crossShaftLeftCoreRenderer, shaftCoreSprite);
            ConfigureShaftRenderer(crossShaftRightCoreRenderer, shaftCoreSprite);
            Vector2 coreSize = new Vector2(CrossShaftCoreWorldWidth, ShaftCoreWorldHeight);
            ConfigureRollingCoreRenderer(crossShaftLeftCoreRenderer, coreSize);
            ConfigureRollingCoreRenderer(crossShaftRightCoreRenderer, coreSize);
            crossShaftLeftCoreRenderer.transform.localPosition = new Vector3(-CrossShaftCoreOffsetX, 0f, 0f);
            crossShaftRightCoreRenderer.transform.localPosition = new Vector3(CrossShaftCoreOffsetX, 0f, 0f);
            crossShaftLeftCoreRenderer.enabled = true;
            crossShaftRightCoreRenderer.enabled = true;
            spriteRenderer.enabled = originalShaftSpriteEnabled;
        }

        shaftCorePropertyBlock ??= new MaterialPropertyBlock();
        SetShaftTextureOffset(shaftTextureOffset);
        ApplyShaftSorting();
    }

    /// <summary>统一传动轴与跨轴器的平铺尺寸和滚动采样方式。</summary>
    private static void ConfigureRollingCoreRenderer(SpriteRenderer renderer, Vector2 size)
    {
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        renderer.size = size;
    }

    /// <summary>取得或建立與本體同中心的機械軸視覺子圖層。</summary>
    private SpriteRenderer GetOrCreateShaftRenderer(string objectName)
    {
        Transform child = spriteRenderer.transform.Find(objectName);
        if (child == null)
        {
            GameObject overlay = new(objectName);
            overlay.transform.SetParent(spriteRenderer.transform, false);
            child = overlay.transform;
        }

        // 建筑根对象位于 Collider 层，机械视觉子层须继承可见的主 SpriteRenderer 图层。
        child.gameObject.layer = spriteRenderer.gameObject.layer;
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
        return renderer;
    }

    /// <summary>新建子渲染器时沿用本体材质、色调与遮罩设置。</summary>
    private void ConfigureShaftRenderer(SpriteRenderer renderer, Sprite sprite)
    {
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteRenderer.sharedMaterial;
        renderer.color = spriteRenderer.color;
        renderer.maskInteraction = spriteRenderer.maskInteraction;
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.flipX = spriteRenderer.flipX;
        renderer.flipY = spriteRenderer.flipY;
    }

    /// <summary>同步金属环与木芯的 SortingLayer，端环固定高于木芯。</summary>
    private void ApplyShaftSorting()
    {
        if (spriteRenderer == null) return;
        if (shaftCoreRenderer != null)
        {
            shaftCoreRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            shaftCoreRenderer.sortingOrder = spriteRenderer.sortingOrder;
        }
        if (shaftRingsRenderer != null)
        {
            shaftRingsRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            shaftRingsRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
        }
        if (crossShaftLeftCoreRenderer != null)
        {
            crossShaftLeftCoreRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            crossShaftLeftCoreRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        }
        if (crossShaftRightCoreRenderer != null)
        {
            crossShaftRightCoreRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            crossShaftRightCoreRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        }
    }

    /// <summary>按材质 UV 位移更新循环相位，使用每物件属性块避免复制共用材质。</summary>
    private void SetShaftTextureOffset(float offset)
    {
        if (shaftCoreRenderer == null && crossShaftLeftCoreRenderer == null && crossShaftRightCoreRenderer == null) return;
        shaftCorePropertyBlock ??= new MaterialPropertyBlock();
        SetRollingCoreTextureOffset(shaftCoreRenderer, offset);
        SetRollingCoreTextureOffset(crossShaftLeftCoreRenderer, offset);
        SetRollingCoreTextureOffset(crossShaftRightCoreRenderer, offset);
    }

    /// <summary>为单个滚动木芯写入连续纹理偏移。</summary>
    private void SetRollingCoreTextureOffset(SpriteRenderer renderer, float offset)
    {
        if (renderer == null) return;
        renderer.GetPropertyBlock(shaftCorePropertyBlock);
        shaftCorePropertyBlock.SetVector(MainTextureScaleOffset, new Vector4(1f, 1f, offset, 0f));
        renderer.SetPropertyBlock(shaftCorePropertyBlock);
    }

    /// <summary>物品回池或切换定义时还原整轴，避免保留上一实例的分层状态。</summary>
    private void RestoreShaftVisual()
    {
        if (shaftCoreRenderer != null)
        {
            shaftCoreRenderer.enabled = false;
            shaftCoreRenderer.SetPropertyBlock(null);
        }
        if (shaftRingsRenderer != null) shaftRingsRenderer.enabled = false;
        if (crossShaftLeftCoreRenderer != null)
        {
            crossShaftLeftCoreRenderer.enabled = false;
            crossShaftLeftCoreRenderer.SetPropertyBlock(null);
        }
        if (crossShaftRightCoreRenderer != null)
        {
            crossShaftRightCoreRenderer.enabled = false;
            crossShaftRightCoreRenderer.SetPropertyBlock(null);
        }
        if (shaftVisualConfigured && spriteRenderer != null)
            spriteRenderer.enabled = originalShaftSpriteEnabled;
        shaftVisualConfigured = false;
        shaftTextureOffset = 0f;
    }
    #endregion

    #region 叶轮双层表现
    /// <summary>从当前物品定义读取叶轮；放置后的叶轮作为塔体子图层独立旋转。</summary>
    private void ConfigureRotorVisual()
    {
        rotorSprite = null;
        if (rotorRenderer != null) rotorRenderer.enabled = false;
        if (spriteRenderer == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(RotorState, out rotorSprite) || !placed) return;

        Transform existing = spriteRenderer.transform.Find(RotorObjectName);
        if (existing == null)
        {
            var rotorObject = new GameObject(RotorObjectName);
            rotorObject.transform.SetParent(spriteRenderer.transform, false);
            existing = rotorObject.transform;
        }
        existing.gameObject.layer = item.gameObject.layer;
        rotorRenderer = existing.GetComponent<SpriteRenderer>();
        if (rotorRenderer == null) rotorRenderer = existing.gameObject.AddComponent<SpriteRenderer>();
        rotorRenderer.sprite = rotorSprite;
        rotorRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
        rotorRenderer.color = spriteRenderer.color;
        rotorRenderer.maskInteraction = spriteRenderer.maskInteraction;
        rotorRenderer.spriteSortPoint = SpriteSortPoint.Pivot;
        rotorRenderer.enabled = true;

        if (rotorSortingGroup == null)
        {
            rotorSortingGroup = spriteRenderer.GetComponent<SortingGroup>();
            if (rotorSortingGroup == null)
            {
                rotorSortingGroup = spriteRenderer.gameObject.AddComponent<SortingGroup>();
                originalRotorGroupEnabled = false;
            }
            else originalRotorGroupEnabled = rotorSortingGroup.enabled;
            originalRotorGroupLayer = rotorSortingGroup.sortingLayerID;
            originalRotorGroupOrder = rotorSortingGroup.sortingOrder;
        }
        rotorSortingGroup.enabled = true;
        ApplyVisual();
    }

    /// <summary>卸载或回池时还原原有排序组状态。</summary>
    private void RestoreRotorSortingGroup()
    {
        if (rotorSortingGroup == null) return;
        rotorSortingGroup.sortingLayerID = originalRotorGroupLayer;
        rotorSortingGroup.sortingOrder = originalRotorGroupOrder;
        rotorSortingGroup.enabled = originalRotorGroupEnabled;
        rotorSortingGroup = null;
    }
    #endregion

    #region 机械动画
    /// <summary>节点停转时保持当前相位，持续有转速时分别驱动齿轮和叶轮。</summary>
    public override void ModUpdate(float deltaTime)
    {
        float rpm = Node?.Rpm ?? 0f;
        if (Mathf.Approximately(rpm, 0f) || spriteRenderer == null) return;
        if (Definition.Kind == "gearbox" && gearboxGearSprite != null)
            AdvanceGearboxVisual(Mathf.Abs(rpm), deltaTime);
        if (rpm > 0f && Definition.Kind == "gear" && inputShaftSprite != null) AdvanceGearVisual(rpm, deltaTime);
        if ((shaftCoreRenderer != null && shaftCoreRenderer.enabled) ||
            (crossShaftLeftCoreRenderer != null && crossShaftLeftCoreRenderer.enabled) ||
            (crossShaftRightCoreRenderer != null && crossShaftRightCoreRenderer.enabled))
            AdvanceShaftVisual(Mathf.Abs(rpm), deltaTime);
        if (rpm > 0f && rotorRenderer != null && rotorRenderer.enabled) AdvanceRotorVisual(rpm, deltaTime);
        ApplyVisual();
    }

    /// <summary>输入侧齿轮跟随节点输入转速，另一侧按 2:1 半径比反向转动。</summary>
    private void AdvanceGearboxVisual(float inputRpm, float deltaTime)
    {
        if (Node == null || Node.EntryDirection < 0) return;
        int localInputDirection = (Node.EntryDirection - Node.RotationQuarterTurns + 4) % 4;
        bool inputOnLargeGear = localInputDirection == 2;
        bool inputOnSmallGear = localInputDirection == 0;
        if (!inputOnLargeGear && !inputOnSmallGear) return;

        float inputDegrees = inputRpm * 6f * deltaTime;
        float gearRatio = GearboxLargeGearScale / GearboxSmallGearScale;
        if (inputOnLargeGear)
        {
            gearboxLargeGearAngleDegrees = Mathf.Repeat(gearboxLargeGearAngleDegrees + inputDegrees, 360f);
            gearboxSmallGearAngleDegrees = Mathf.Repeat(gearboxSmallGearAngleDegrees - inputDegrees * gearRatio, 360f);
        }
        else
        {
            gearboxSmallGearAngleDegrees = Mathf.Repeat(gearboxSmallGearAngleDegrees + inputDegrees, 360f);
            gearboxLargeGearAngleDegrees = Mathf.Repeat(gearboxLargeGearAngleDegrees - inputDegrees / gearRatio, 360f);
        }
    }

    /// <summary>每转一圈滚过一个木纹周期，供传动轴和跨轴器共用。</summary>
    private void AdvanceShaftVisual(float rpm, float deltaTime)
    {
        shaftTextureOffset = Mathf.Repeat(shaftTextureOffset + rpm * deltaTime / 60f, 1f);
        SetShaftTextureOffset(shaftTextureOffset);
    }

    /// <summary>相邻齿轮反向转动，保持现有齿距相位。</summary>
    private void AdvanceGearVisual(float rpm, float deltaTime)
    {
        Vector2Int cell = Node.Cell;
        float direction = ((cell.x + cell.y) & 1) == 0 ? 1f : -1f;
        gearAngleDegrees = Mathf.Repeat(gearAngleDegrees + direction * rpm * 6f * deltaTime, 360f);
    }

    /// <summary>叶轮按节点每分钟转数顺时针旋转，塔体不参与。</summary>
    private void AdvanceRotorVisual(float rpm, float deltaTime)
    {
        rotorAngleDegrees = Mathf.Repeat(rotorAngleDegrees - rpm * 6f * deltaTime, 360f);
    }
    #endregion

    #region 操作面板
    /// <summary>机械节点面板通过靠近后的交互键打开，不响应鼠标左键点选。</summary>
    public bool CanPointerInteract(Item actor) => false;

    public void OnInteractStart(Item actor)
    {
        if (!placed || Node == null || !GameNetwork.HasStateAuthority) return;
        if (Definition.Source == "manual")
        {
            BeginManualInteraction();
            return;
        }

        ToggleMechanicalPanel(actor);
    }
    public void OnInteractUpdate(Item actor)
    {
        if (Definition.Source != "manual" || !manualInteractionActive || !placed ||
            Node?.State == null || !GameNetwork.HasStateAuthority) return;

        manualInteractionElapsed += Time.deltaTime;
        if (manualInteractionElapsed < MachineCatalog.Settings.ManualHoldThresholdSeconds) return;
        manualInteractionCranking = true;

        // 配置继续保留 Pulse/Reserve 字段兼容 MOD；默认值只覆盖约两个机械 Tick。
        float holdBufferSeconds = Mathf.Min(
            MachineCatalog.Settings.ManualReserveSeconds,
            Mathf.Max(MachineCatalog.Settings.ManualPulseSeconds, MachineCatalog.Settings.TickSeconds * 2f));
        LocalState.ManualSeconds = Mathf.Max(LocalState.ManualSeconds, holdBufferSeconds);
    }

    /// <summary>短按释放时打开手摇轮面板；达到阈值的长按只负责持续供能。</summary>
    public void OnInteractEnd(Item actor)
    {
        if (Definition.Source != "manual" || !manualInteractionActive) return;

        bool openPanel = !manualInteractionCranking;
        ResetManualInteraction();
        if (openPanel && placed && Node != null && GameNetwork.HasStateAuthority)
            ToggleMechanicalPanel(actor);
    }

    /// <summary>取消目标时清除手摇输入，手摇轮面板独立维持自己的 UI 生命周期。</summary>
    public void OnInteractCancel(Item actor)
    {
        if (Definition?.Source == "manual") ResetManualInteraction();
        else panel?.Close();
    }

    /// <summary>开始计时；尚未达到阈值时不供能，释放后可识别为短按。</summary>
    private void BeginManualInteraction()
    {
        manualInteractionElapsed = 0f;
        manualInteractionActive = true;
        manualInteractionCranking = false;
    }

    /// <summary>清除单次手摇输入的临时状态。</summary>
    private void ResetManualInteraction()
    {
        manualInteractionElapsed = 0f;
        manualInteractionActive = false;
        manualInteractionCranking = false;
    }

    /// <summary>创建并切换到通用机械面板。</summary>
    private void ToggleMechanicalPanel(Item actor)
    {
        panel ??= new MechanicalPanelSession("UI_Mechanical", item, Node.Processor,
            PerformOperation, GetStatus, GetActionLabel, CanPerformOperation);
        panel.Toggle(actor);
    }
    public void RefreshPanel() => panel?.Refresh();
    public string GetActionLabel()
    {
        if (Definition.Kind == "clutch") return LocalState.Engaged ? "断开" : "接合";
        if (Definition.Kind == "gearbox" && Definition.Ratios.Length > 1) return "切换传动比";
        if (Definition.ManualDriveTorque > 0) return "手动研磨";
        if (MachineDefinition.Positive(Definition.ManualWorkSecondsPerPress) && Node?.Processor?.Preview().Success == true)
            return "手动推进";
        return string.Empty;
    }
    /// <summary>手推按钮是否可用由机械世界的输入动力状态决定。</summary>
    private bool CanPerformOperation()
        => Definition.ManualDriveTorque <= 0 || MachineWorld.CanManualDrive(Node);
    private void PerformOperation(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Node?.State == null) return;
        if (Definition.Kind == "clutch") { LocalState.Engaged = !LocalState.Engaged; MachineWorld.TopologyChanged(Node); }
        else if (Definition.Kind == "gearbox" && Definition.Ratios.Length > 1)
        { LocalState.RatioIndex = (LocalState.RatioIndex + 1) % Definition.Ratios.Length; MachineWorld.TopologyChanged(Node); }
        else if (Definition.ManualDriveTorque > 0)
            MachineWorld.TryManualDrive(Node);
        else if (MachineDefinition.Positive(Definition.ManualWorkSecondsPerPress) && Node.Processor?.Preview().Success == true)
            Node.Processor.AdvanceManually(Definition.ManualWorkSecondsPerPress, actor);
        Save(); panel?.Refresh();
    }
    private string GetStatus()
    {
        string state = FlatWorldLocalizationService.GetUiText(Node?.GetOperatingStatus() ?? "停止");
        float torqueSupply = 0f, torqueDemand = 0f;
        if (Node != null) Node.GetLocalTorque(out torqueSupply, out torqueDemand);
        string status = FlatWorldLocalizationService.GetUiFormat("{0} · 转速 {1:0} · 扭矩 {2:0.#}/{3:0.#}", state,
            Node?.Rpm ?? 0, torqueSupply, torqueDemand);
        if (Definition.Kind == "consumer" || Definition.Kind == "bellows")
            status += FlatWorldLocalizationService.GetUiFormat(" · 工作效率 {0:0.#}%（需求 {1:0} RPM）",
                MachineWorld.GetWorkEfficiency(Node) * 100f, Definition.RequiredRpm);
        if (Definition.Kind == "gearbox" && Node != null && Node.FlowVisited && Node.EntryDirection >= 0)
        {
            Definition.GetTransmission(Node.IsGearboxSmallGearInput(), LocalState.RatioIndex,
                out float speedRatio, out float torqueRatio);
            status += FlatWorldLocalizationService.GetUiFormat(" · 转速倍率 {0:0.##} · 扭矩倍率 {1:0.##}",
                speedRatio, torqueRatio);
        }
        return status;
    }
    #endregion
}

using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>机械节点的 Item 表现与交互桥。齿轮旋转主 Sprite 并保留固定输入杆；风车等动力源可用独立叶轮图层按节点 RPM 转动，塔体保持静止。世界状态由 MechanicalWorld 整网管理。</summary>
public sealed class Mod_MechanicalNode : Module, IInteractable, IBuildingPlacementCommitted, IBuildingPlacementExtension
{
    #region 配置与状态
    public const string ModuleId = "机械动力模块";
    private const string InputShaftState = "inputShaft";
    private const string InputShaftObjectName = "MechanicalInputShaft";
    private const string RotorState = "rotor";
    private const string RotorObjectName = "MechanicalRotor";
    private const float AlternateGearPhaseDegrees = 22.5f;
    public string DefinitionId; // 对应机械配置目录的稳定节点 ID
    public float InputShaftOffsetX = -0.45f; // 固定输入杆中心的局部横向偏移，保留轮盘下的隐藏连接段。
    public Vector3 RotorLocalPosition; // 叶轮中心相对建筑底部的本地位置，由物品配置提供。
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => placed && Node != null &&
        (inputShaftSprite != null || (rotorRenderer != null && rotorRenderer.enabled))
        ? ModuleTickMode.EveryFrame : ModuleTickMode.Disabled;
    public MechanicalDefinition Definition { get; private set; }
    public MechanicalNode Node { get; private set; }
    public MechanicalNodeState LocalState { get; private set; }
    public bool PlacementVertical { get; private set; }
    public int OccupancyLayer => (Definition ?? MechanicalCatalog.Get(DefinitionId))?.Layer ?? 0;
    private MechanicalPanelSession panel;
    private GameController controller;
    private SpriteRenderer spriteRenderer;
    private Quaternion originalRotation;
    private int originalSortingOrder;
    private bool placed;
    private Sprite inputShaftSprite; // 由物品 visual.spriteStates 预载的固定杆。
    private SpriteRenderer inputShaftRenderer; // 独立于旋转齿轮的静态图层。
    private float gearAngleDegrees; // 本地表现相位，不参与机械网络存档。
    private Sprite rotorSprite; // 由物品 visual.spriteStates 预载的可旋转叶轮。
    private SpriteRenderer rotorRenderer; // 与塔体共享排序组的叶轮图层。
    private SortingGroup rotorSortingGroup;
    private bool originalRotorGroupEnabled;
    private int originalRotorGroupLayer;
    private int originalRotorGroupOrder;
    private float rotorAngleDegrees; // 叶轮表现相位，不参与机械网络存档。
    #endregion

    #region 生命周期
    public override void Load()
    {
        Definition = MechanicalCatalog.Get(string.IsNullOrWhiteSpace(DefinitionId) ? item.itemData.IDName : DefinitionId)
            ?? throw new InvalidOperationException("机械定义不存在：" + DefinitionId);
        LocalState = Data.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        PlacementVertical = false;
        placed = Mod_Building.TryReadBuildingData(item.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding;
        spriteRenderer = item.Sprite != null ? item.Sprite : item.GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalRotation = spriteRenderer.transform.localRotation;
            originalSortingOrder = spriteRenderer.sortingOrder;
        }
        ConfigureGearVisual();
        ConfigureRotorVisual();
        item.OnInHandChanged += OnHandChanged;
        if (!placed) BindRotationInput();
        if (placed && building.State is BuildingState.Installed or BuildingState.Damaged) AttachWorld();
        ApplyVisual();
    }
    public override void Save()
    {
        if (placed) Data.WriteData(Node?.State ?? LocalState);
        // Summoner 不保存 PlacementVertical，保持同堆物品身份一致。
    }
    public override void Unload()
    {
        panel?.Dispose(); panel = null;
        if (item != null) item.OnInHandChanged -= OnHandChanged;
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = null;
        MechanicalWorld.Detach(this);
        Node = null; PlacementVertical = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.transform.localRotation = originalRotation;
            spriteRenderer.sortingOrder = originalSortingOrder;
        }
        if (inputShaftRenderer != null) inputShaftRenderer.enabled = false;
        if (rotorRenderer != null) rotorRenderer.enabled = false;
        RestoreRotorSortingGroup();
        inputShaftSprite = null;
        inputShaftRenderer = null;
        gearAngleDegrees = 0f;
        rotorSprite = null;
        rotorRenderer = null;
        rotorAngleDegrees = 0f;
    }
    public override void OnResourcesReloaded()
    {
        ConfigureRotorVisual();
        ApplyVisual();
        InvalidateTickSchedule();
    }
    public void OnBuildingPlacementCommitted()
    {
        placed = true;
        SetInitialGearPhase();
        ConfigureRotorVisual();
        AttachWorld();
        InvalidateTickSchedule();
    }
    private void AttachWorld() { Node = MechanicalWorld.Attach(this); if (Node != null) LocalState = Node.State; ApplyVisual(); }
    private void BindRotationInput()
    {
        if (controller != null) controller.BuildingRotationRequested -= RotatePlacement;
        controller = item.Owner?.GetComponentInChildren<GameController>();
        if (controller != null) controller.BuildingRotationRequested += RotatePlacement;
    }
    private void OnHandChanged(bool held)
    {
        PlacementVertical = false;
        if (held) BindRotationInput();
        else if (controller != null) { controller.BuildingRotationRequested -= RotatePlacement; controller = null; }
    }
    #endregion

    #region 放置朝向
    /// <summary>R 从统一控制器转发，仅影响当前真实手持的放置过程。</summary>
    public void RotatePlacement()
    {
        if (placed || !item.InHand || !Definition.Rotatable) return;
        PlacementVertical = !PlacementVertical;
        var building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        ApplyPreview(building?.GhostShadow);
    }
    public void ApplyPreview(BuildingShadow shadow)
    {
        if (shadow?.ShadowRenderer == null) return;
        if (Definition.Rotatable)
            shadow.ShadowRenderer.transform.localRotation = Quaternion.Euler(0, 0, PlacementVertical ? 90 : 0);
        if (inputShaftSprite != null)
            shadow.EnsureOverlay(InputShaftObjectName, inputShaftSprite,
                new Vector3(InputShaftOffsetX, 0f, 0f), spriteRenderer?.sharedMaterial);
        if (rotorSprite != null)
        {
            SpriteRenderer previewRotor = shadow.EnsureOverlay(RotorObjectName, rotorSprite,
                RotorLocalPosition, spriteRenderer?.sharedMaterial);
            if (previewRotor != null) previewRotor.sortingOrder = shadow.ShadowRenderer.sortingOrder + 1;
        }
    }
    public bool ValidatePlacement(Vector2Int cell, out string reason)
        => MechanicalWorld.ValidatePlacement(Definition, cell, PlacementVertical, out reason);

    /// <summary>候选本体建立后、Load 前写世界朝向；覆盖拆回快照中的旧方向。</summary>
    public void PreparePlacedData(ItemData data)
    {
        if (!MechanicalWorld.TryGetModuleData(data, out var module)) throw new InvalidOperationException("机械本体缺少模块。");
        var state = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        state.Vertical = PlacementVertical;
        if (Definition.Source == "water") state.WaterSupported = MechanicalWorld.HasWaterNeighbor(MechanicalWorld.CellOf(data.transform.position));
        module.WriteData(state);
    }
    /// <summary>拆回快照只重置世界朝向，不改动仍在世界中的节点，也不丢弃机器库存。</summary>
    public void PrepareRepackedSnapshot(ItemData snapshot)
    {
        if (!MechanicalWorld.TryGetModuleData(snapshot, out var module)) return;
        var state = module.GetData<MechanicalNodeState>() ?? new MechanicalNodeState();
        state.Vertical = false;
        module.WriteData(state);
    }
    private void ApplyVisual()
    {
        if (spriteRenderer == null) return;
        if (placed)
            spriteRenderer.transform.localRotation = originalRotation *
                Quaternion.Euler(0, 0, (LocalState.Vertical ? 90f : 0f) + gearAngleDegrees);
        if (placed && Definition.Layer == 1) spriteRenderer.sortingOrder = 2;
        if (inputShaftRenderer != null)
        {
            // 固定杆保留原排序，主齿轮高一层；不依赖相机的透明物体 Z 排序模式。
            spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, originalSortingOrder + 1);
            inputShaftRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            inputShaftRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;
            inputShaftRenderer.transform.localPosition = new Vector3(InputShaftOffsetX, 0f, 0f);
        }
        if (rotorRenderer == null) return;
        rotorRenderer.transform.localPosition = RotorLocalPosition;
        rotorRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotorAngleDegrees);
        rotorRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        rotorRenderer.sortingOrder = 1;
        // 把塔与叶轮当作同一个世界实体排序，叶轮只在组内盖过塔体。
        rotorSortingGroup.sortingLayerID = spriteRenderer.sortingLayerID;
        rotorSortingGroup.sortingOrder = originalSortingOrder;
        spriteRenderer.sortingOrder = 0;
    }
    #endregion

    #region 齿轮双层表现
    /// <summary>读取额外状态 Sprite；其他齿轮也可在配置中声明同名图层。</summary>
    private void ConfigureGearVisual()
    {
        inputShaftSprite = null;
        if (Definition.Kind != "gear" || spriteRenderer == null ||
            GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(item.itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetVisualStateSprite(InputShaftState, out inputShaftSprite)) return;

        Transform existing = item.transform.Find(InputShaftObjectName);
        if (existing == null)
        {
            var overlay = new GameObject(InputShaftObjectName);
            overlay.transform.SetParent(item.transform, false);
            existing = overlay.transform;
        }
        existing.gameObject.layer = item.gameObject.layer;
        inputShaftRenderer = existing.GetComponent<SpriteRenderer>();
        if (inputShaftRenderer == null) inputShaftRenderer = existing.gameObject.AddComponent<SpriteRenderer>();
        inputShaftRenderer.sprite = inputShaftSprite;
        inputShaftRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
        inputShaftRenderer.color = spriteRenderer.color;
        inputShaftRenderer.spriteSortPoint = SpriteSortPoint.Pivot;
        inputShaftRenderer.enabled = true;
        SetInitialGearPhase();
    }

    /// <summary>相邻格错开半齿距，旋转方向交替，使八齿在视觉上交错。</summary>
    private void SetInitialGearPhase()
    {
        if (inputShaftSprite == null || !placed) return;
        Vector2Int cell = Node?.Cell ?? MechanicalWorld.CellOf(item.transform.position);
        gearAngleDegrees = ((cell.x + cell.y) & 1) == 0 ? 0f : AlternateGearPhaseDegrees;
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
        if (rpm <= 0f || spriteRenderer == null) return;
        if (inputShaftSprite != null) AdvanceGearVisual(rpm, deltaTime);
        if (rotorRenderer != null && rotorRenderer.enabled) AdvanceRotorVisual(rpm, deltaTime);
        ApplyVisual();
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
    public void OnInteractStart(Item actor)
    {
        if (!placed || Node == null || !GameNetwork.HasStateAuthority) return;
        // 手摇轮改为按住世界交互持续供能，不再通过机械面板按钮脉冲供能。
        if (Definition.Source == "manual") return;
        panel ??= new MechanicalPanelSession("UI_Mechanical", item, Node.Processor, PerformOperation, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }
    public void OnInteractUpdate(Item actor)
    {
        if (!placed || Node?.State == null || !GameNetwork.HasStateAuthority || Definition.Source != "manual") return;

        // 配置继续保留 Pulse/Reserve 字段兼容 MOD；默认值只覆盖约两个机械 Tick。
        float holdBufferSeconds = Mathf.Min(
            MechanicalCatalog.Settings.ManualReserveSeconds,
            Mathf.Max(MechanicalCatalog.Settings.ManualPulseSeconds, MechanicalCatalog.Settings.TickSeconds * 2f));
        LocalState.ManualSeconds = Mathf.Max(LocalState.ManualSeconds, holdBufferSeconds);
    }
    public void OnInteractCancel(Item actor) => panel?.Close();
    public void RefreshPanel() => panel?.Refresh();
    public string GetActionLabel()
    {
        if (Definition.Kind == "clutch") return LocalState.Engaged ? "断开" : "接合";
        if (Definition.Kind == "gearbox") return "切换传动比";
        return string.Empty;
    }
    private void PerformOperation(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Node?.State == null) return;
        if (Definition.Kind == "clutch") { LocalState.Engaged = !LocalState.Engaged; MechanicalWorld.TopologyChanged(Node); }
        else if (Definition.Kind == "gearbox") { LocalState.RatioIndex = (LocalState.RatioIndex + 1) % Definition.Ratios.Length; MechanicalWorld.TopologyChanged(Node); }
        Save(); panel?.Refresh();
    }
    private string GetStatus()
    {
        string state = FlatWorldLocalizationService.GetUiText(Node?.Network?.Status ?? "停止");
        string status = FlatWorldLocalizationService.GetUiFormat("{0} · 转速 {1:0} · 动力 {2:0.#}/{3:0.#}", state,
            Node?.Rpm ?? 0, Node?.Network?.Supply ?? 0, Node?.Network?.Demand ?? 0);
        if (Definition.Kind == "gearbox")
            status += FlatWorldLocalizationService.GetUiFormat(" · 传动比 {0:0.##}", Definition.Ratios[Mathf.Clamp(LocalState.RatioIndex, 0, Definition.Ratios.Length - 1)]);
        return status;
    }
    #endregion
}

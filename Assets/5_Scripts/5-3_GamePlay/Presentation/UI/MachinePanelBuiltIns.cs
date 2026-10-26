using System;
using System.Collections;
using System.Globalization;
using FlatWorld.Localization;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>内置机器的表现组合入口，具体控件绑定由各自会话负责。</summary>
internal static class MachinePanelBuiltIns
{
    #region 内置表现登记

    internal static void Register()
    {
        MachinePanelFactoryRegistry.AddBuiltIn("cultivator", entity => new CultivatorPanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("ship-device", CreateShipDevice);
        MachinePanelFactoryRegistry.AddBuiltIn("workbench", entity => new WorkbenchMachinePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("furnace", entity => new FurnaceMachinePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("induction-stove", entity => new InductionStovePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("mortar", entity => new MortarMachinePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("fire-drill", entity => new FireDrillMachinePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("manual-processing", CreateManualProcessing);
        MachinePanelFactoryRegistry.AddBuiltIn("hand-drill", CreateHandDrill);
        MachinePanelFactoryRegistry.AddBuiltIn("vessel", entity => new VesselMachinePanelSession((VesselLogic)entity.Logic));
        MachinePanelFactoryRegistry.AddBuiltIn("fluid", entity => new FluidMachinePanelSession((FluidMachineLogic)entity.Logic));
        MachinePanelFactoryRegistry.AddBuiltIn("electric-heater", entity =>
            new MechanicalPanelSession(entity.Definition.Heating.PanelId, entity, null, null,
                () => entity.Logic.Status, () => entity.Logic.ActionLabel, () => entity.Logic.CanAct,
                refreshStatusPeriodically: true));
    }

    private static IMachinePanelSession CreateShipDevice(MachineEntity entity)
    {
        if (entity.Logic is ShipDeviceLogic { HasChemicalPropellantStorage: true })
            return new ShipChemicalEnginePanelSession(entity);
        // 地面只有控制台打开船控，其余设备沿用普通机器状态面板。
        if (entity.ScopeKey?.StartsWith("ship:", StringComparison.Ordinal) == true ||
            FlatWorld.Spaceflight.SpaceSession.ResolvePartConfiguration(entity.Snapshot)?.Kind == FlatWorld.Spaceflight.ShipPieceKind.Console)
            return new ShipPanelSession(entity);
        return new MechanicalPanelSession("UI_Mechanical", entity, null, null,
            () => entity.Logic.Status, () => string.Empty, () => false, refreshStatusPeriodically: true);
    }

    private static IMachinePanelSession CreateManualProcessing(MachineEntity entity)
    {
        return new ManualProcessingMachinePanelSession(entity);
    }

    private static IMachinePanelSession CreateHandDrill(MachineEntity entity)
    {
        var logic = (HandDrillLogic)entity.Logic;
        return new MechanicalPanelSession("UI_HandDrill", entity, logic.Processor,
            actor => MachineWorld.RequestOperation(entity, "work", "", actor),
            () => logic.Status, () => logic.ActionLabel, () => logic.CanAct);
    }

    #endregion
}

/// <summary>化学引擎复用工业正式面板，只显示本机两相库存和供电状态。</summary>
internal sealed class ShipChemicalEnginePanelSession : MachinePanelSession
{
    #region 化学引擎气液读数
    private readonly BasePanel panel;
    private readonly MechanicalPanelView view;
    private Coroutine readings;

    public ShipChemicalEnginePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            panel = CreatePanel(GameRes.Instance.GetPrefab("UI_IndustrialFluid"));
            view = panel.GetComponent<MechanicalPanelView>() ?? throw new InvalidOperationException("化学引擎缺少正式工业面板。");
            ConfigureChemicalPropellantStorageLayout(view);
            view.CloseButton.onClick.AddListener(Close);
            panel.Closed += StopRefreshingChemicalPropellantReadings;
        }
        catch { Dispose(); throw; }
    }

    internal static void ConfigureChemicalPropellantStorageLayout(MechanicalPanelView view)
    {
        BasePanel panel = view.GetComponent<BasePanel>();
        view.SetProcessingVisible(false);
        view.SetBatteryVisible(false);
        view.SetFluidLayout(true, false, false);
        view.SetGasCapacityVisible(true);
        view.SetActionVisible(false);
        foreach (TMP_InputField field in panel.GetComponentsInChildren<TMP_InputField>(true)) field.gameObject.SetActive(false);
        foreach (string id in new[] { "FWUI_ApplyFluidSettings", "FWUI_FluidPhase", "FWUI_RotateFluid" })
            panel.GetButton(id)?.gameObject.SetActive(false);
        // 两条容量条并排留在正文左侧，状态滚动区独占其余宽度。
        RectTransform gas = (RectTransform)view.GasCapacity.transform;
        RectTransform liquid = (RectTransform)view.WaterCapacity.transform;
        gas.anchorMin = liquid.anchorMin; gas.anchorMax = liquid.anchorMax; gas.pivot = liquid.pivot;
        gas.sizeDelta = liquid.sizeDelta; gas.anchoredPosition = liquid.anchoredPosition + new Vector2(104f, 0f);
        RectTransform status = (RectTransform)view.StatusScroll.transform;
        status.offsetMin = new Vector2(222f, 12f); status.offsetMax = new Vector2(-12f, -12f);
    }

    protected override void RefreshPresentation()
    {
        if (view == null) return;
        using var nodeScope = MachineWorld.UseNodeScope(Entity);
        FluidInventory contents = MachineWorld.GetFluidInventory(Entity);
        view.Title.text = Entity.Definition.Content?.Definition.DisplayName ?? Entity.Definition.Id;
        view.Status.text = Logic.Status;
        view.SetWaterCapacity((float)contents.GetLiquidLiters(), (float)MachineWorld.GetFluidVolumeLiters(Entity));
        view.SetGasCapacity((float)((double)FluidUnits.MolToStandardLiters(contents.GasMoles) /
            MachineWorld.GetFluidGasCapacityStandardLiters(Entity)));
    }

    protected override void OnOpened()
    {
        StopRefreshingChemicalPropellantReadings();
        readings = panel.StartCoroutine(RefreshChemicalPropellantReadingsWhileOpen());
    }

    private IEnumerator RefreshChemicalPropellantReadingsWhileOpen()
    {
        var delay = new WaitForSecondsRealtime(.2f);
        while (IsOpen) { yield return delay; Refresh(); }
        readings = null;
    }

    private void StopRefreshingChemicalPropellantReadings()
    {
        if (readings != null && panel != null) panel.StopCoroutine(readings);
        readings = null;
    }

    protected override void DisposePresentation()
    {
        StopRefreshingChemicalPropellantReadings();
        if (panel == null) return;
        panel.Closed -= StopRefreshingChemicalPropellantReadings;
        if (view != null) view.CloseButton.onClick.RemoveListener(Close);
    }
    #endregion
}

/// <summary>手动工作站复用正式配方列表，只绑定实际存在的输入输出槽。</summary>
internal sealed class ManualProcessingMachinePanelSession : MachinePanelSession
{
    #region 手动工作站绑定
    private readonly CraftingStationController controller;
    private readonly ManualProcessingLogic manual;
    private readonly TMP_Text durability;

    public ManualProcessingMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            manual = (ManualProcessingLogic)Logic;
            BasePanel panel = CreatePanel(manual.PanelPrefab);
            if (manual.ToolInventory != null) BindSlots(panel, manual.ToolInventory, "工具", matchSlotVisibility: true);
            BindSlots(panel, manual.Processor.Input, "输入", matchSlotVisibility: true);
            BindSlots(panel, manual.Processor.Output, "输出", allowDirectionalDeposit: false, matchSlotVisibility: true);
            durability = panel.GetText("FWUI_FooterHint");
            durability?.GetComponent<LocalizedTextBinder>()?.ConfigureRuntime(() => manual.Status);
            controller = new CraftingStationController(panel, manual.Processor.Input, manual.Processor.Output,
                manual.Processor.Capabilities, () => Mathf.CeilToInt(manual.Processor.RequiredWork / manual.WorkPerClick), () => Actor,
                authoritativeProcessor: manual.Processor,
                selectRecipe: id => MachineWorld.RequestOperation(Entity, "select", id, Actor),
                performWork: () => MachineWorld.RequestOperation(Entity, "work", "", Actor));
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    { if (durability != null) durability.text = manual.Status; }
    protected override void DisposePresentation() => controller?.Dispose();
    #endregion
}

/// <summary>工作台表现只绑定工作台领域公开的库存和加工入口。</summary>
internal sealed class WorkbenchMachinePanelSession : MachinePanelSession
{
    #region 工作台绑定

    private readonly CraftingStationController controller;
    private readonly WorkbenchLogic workbench;
    private readonly BasePanel workbenchPanel;
    private readonly ItemSlot_UI sharedSlot;
    private InventorySlotMirrorBinding sharedBinding;

    public WorkbenchMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            var logic = (WorkbenchLogic)Logic;
            workbench = logic;
            BasePanel panel = CreatePanel(logic.PanelPrefab);
            workbenchPanel = panel;
            BindSlots(panel, logic.Input, "输入");
            BindSlots(panel, logic.Output, "输出", allowDirectionalDeposit: false);
            sharedSlot = panel.GetButton("输入_6")?.GetComponent<ItemSlot_UI>()
                ?? throw new InvalidOperationException("工作台面板缺少工具柜共享槽。");
            sharedSlot.gameObject.SetActive(false);
            controller = new CraftingStationController(panel, logic.Processor.Input, logic.Output,
                logic.Processor.Capabilities, () => logic.RequiredClicks, () => Actor,
                authoritativeProcessor: logic.Processor,
                selectRecipe: id => MachineWorld.RequestOperation(Entity, "select", id, Actor),
                performWork: () => MachineWorld.RequestOperation(Entity, "work", "", Actor));
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    {
        if (!ReferenceEquals(sharedBinding?.Inventory, workbench.SharedInput))
        {
            sharedBinding?.Dispose();
            sharedBinding = workbench.SharedInput != null
                ? new InventorySlotMirrorBinding(workbench.SharedInput, sharedSlot) : null;
        }
        if (sharedBinding != null)
        {
            sharedBinding.Inventory.DefaultTarget_Inventory = workbench.Input.DefaultTarget_Inventory;
            sharedBinding.Inventory.SyncQuickTransferTarget(workbenchPanel);
        }
    }

    protected override void DisposePresentation()
    { sharedBinding?.Dispose(); controller?.Dispose(); }

    #endregion
}

/// <summary>熔炉表现只读取炉温、燃料和供热状态，按钮发送权威操作。</summary>
internal sealed class FurnaceMachinePanelSession : MachinePanelSession
{
    #region 熔炉绑定与刷新

    private readonly FurnaceLogic furnace;
    private readonly Slider fuel;
    private readonly TMP_Text temperature;
    private readonly TMP_Text capacity;
    private readonly TMP_Text hint;

    public FurnaceMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            furnace = (FurnaceLogic)Logic;
            BasePanel panel = CreatePanel(furnace.PanelPrefab);
            BindSlots(panel, furnace.Input, "输入", matchSlotVisibility: true);
            BindSlots(panel, furnace.Output, "输出", allowDirectionalDeposit: false);
            BindSlots(panel, furnace.FuelInventory, "燃料");
            BindActionButton(panel.GetButton("合成按钮")
                ?? throw new InvalidOperationException("熔炉面板缺少合成按钮。"));
            fuel = panel.GetSlider("燃料显示条");
            temperature = panel.GetText("FWUI_FurnaceTemperatureValue");
            capacity = panel.GetText("FWUI_FurnaceInputCapacity");
            hint = panel.GetText("FWUI_FooterHint");
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    {
        if (fuel != null) fuel.value = furnace.FuelRatio;
        if (temperature != null) temperature.text = furnace.Status;
        if (capacity != null) capacity.text = FurnaceInputCapacityFeedback.Describe(furnace.Input);
        if (hint != null) hint.text = FlatWorldLocalizationService.GetUiFormat(
            "升温系数 {0:0}% · {1}", furnace.HeatingRatePercent, furnace.GetHeatingHint());
        for (int i = 0; i < furnace.FuelInventory.itemSlot_UI.Count; i++)
            furnace.FuelInventory.itemSlot_UI[i]?.SetProcessingProgress(furnace.GetFuelAbsorptionProgress(i));
    }

    #endregion
}

/// <summary>电磁炉复用库存面板，温控草稿通过权威操作提交并等待真实回执。</summary>
internal sealed class InductionStovePanelSession : MachinePanelSession
{
    #region 电磁炉绑定
    private readonly InductionStoveLogic stove;
    private readonly BasePanel inductionPanel;
    private readonly TMP_Text temperature;
    private readonly TMP_Text power;
    private readonly TMP_Text capacity;
    private readonly TMP_Text hint;
    private readonly TMP_InputField targetInput;
    private readonly UnityEngine.UI.Button applyTarget;
    private readonly TMP_Text applyLabel;
    private readonly TMP_Text targetValue;
    private readonly TMP_Text settingFeedback;
    private bool draftDirty;
    private bool targetPending;
    private bool targetAccepted;
    private uint pendingToken;
    private float pendingTemperature;
    private string pendingArgument;
    private string feedbackSource;
    private string feedbackReason;
    private Coroutine targetConfirmationWatch;

    public InductionStovePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            stove = (InductionStoveLogic)Logic;
            BasePanel panel = CreatePanel(stove.PanelPrefab);
            inductionPanel = panel;
            BindSlots(panel, stove.Input, "输入", matchSlotVisibility: true);
            BindSlots(panel, stove.Output, "输出", allowDirectionalDeposit: false, matchSlotVisibility: true);
            // 电力炉没有燃料或点火操作，共用面板只保留实际使用的控件。
            foreach (ItemSlot_UI slot in panel.GetComponentsInChildren<ItemSlot_UI>(true))
                if (slot.name.StartsWith("燃料_", StringComparison.Ordinal) || slot.name.StartsWith("燃料 ", StringComparison.Ordinal))
                    slot.gameObject.SetActive(false);
            Button ignition = panel.GetButton("合成按钮");
            if (ignition != null) ignition.gameObject.SetActive(false);
            Slider fuel = panel.GetSlider("燃料显示条");
            if (fuel != null) fuel.gameObject.SetActive(false);
            TMP_Text title = panel.GetText("FWUI_标题");
            if (title != null)
            {
                title.text = FlatWorldLocalizationService.GetUiText("电磁炉");
                title.GetComponent<LocalizedTextBinder>()?.ConfigureRuntime(() => FlatWorldLocalizationService.GetUiText("电磁炉"));
            }
            temperature = panel.GetText("FWUI_FurnaceTemperatureValue");
            capacity = panel.GetText("FWUI_FurnaceInputCapacity");
            power = panel.GetText("FWUI_FurnaceFuelLabel");
            hint = panel.GetText("FWUI_FooterHint");
            targetInput = panel.GetInputField("FWUI_InductionTargetInput")
                ?? throw new InvalidOperationException("电磁炉正式面板缺少目标温度输入框。");
            applyTarget = panel.GetButton("FWUI_ApplyInductionTarget")
                ?? throw new InvalidOperationException("电磁炉正式面板缺少温度应用按钮。");
            targetValue = panel.GetText("FWUI_InductionTargetValue");
            settingFeedback = panel.GetText("FWUI_InductionSettingFeedback");
            applyLabel = applyTarget.GetComponentInChildren<TMP_Text>(true);
            targetInput.transform.parent.gameObject.SetActive(true);
            targetInput.onValueChanged.AddListener(TargetDraftChanged);
            targetInput.onSubmit.AddListener(SubmitTargetTemperature);
            applyTarget.onClick.AddListener(ApplyTargetTemperature);
            panel.Closed += ClearTargetConfirmation;
            MachineWorld.OperationCompleted += HandleOperationCompleted;
            if (power != null)
            {
                power.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 42f);
                power.enableWordWrapping = true;
            }
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    {
        if (temperature != null) temperature.text = FlatWorldLocalizationService.GetUiFormat(
            "当前炉温 {0:0.#}°C", stove.Temperature);
        if (capacity != null) capacity.text = FurnaceInputCapacityFeedback.Describe(stove.Input);
        if (power != null) power.text = FlatWorldLocalizationService.GetUiFormat(
            "需求功率 {0:0.#} W\n实际供电 {1:0.#} W", stove.RequestedWatts, stove.SuppliedWatts);
        if (hint != null) hint.text = FlatWorldLocalizationService.GetUiFormat(
            "升温系数 {0:0}% · {1}", stove.HeatingRatePercent,
            FlatWorldLocalizationService.GetUiText(stove.Temperature > stove.TargetTemperature + .1f ? "自然冷却" :
                stove.SuppliedWatts <= 0f ? "没有电" : stove.Temperature < stove.TargetTemperature ? "正在加热" : "已达到目标温度"));
        RefreshTargetControls();
    }

    protected override void OnOpened()
    {
        ClearTargetConfirmation();
        draftDirty = false;
        feedbackSource = feedbackReason = string.Empty;
        targetInput.SetTextWithoutNotify(stove.TargetTemperature.ToString("0.#", CultureInfo.InvariantCulture));
        RefreshTargetControls();
    }

    private void TargetDraftChanged(string _)
    {
        draftDirty = true;
        if (!targetPending) feedbackSource = feedbackReason = string.Empty;
        RefreshTargetControls();
    }

    private void SubmitTargetTemperature(string _) => ApplyTargetTemperature();

    private bool TryGetTargetTemperature(out float target) =>
        float.TryParse(targetInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out target) &&
        float.IsFinite(target) && target >= stove.MinimumTargetTemperature && target <= stove.MaximumTargetTemperature;

    private void ApplyTargetTemperature()
    {
        if (!IsOpen || targetPending || !TryGetTargetTemperature(out float target)) return;
        targetPending = true;
        targetAccepted = false;
        pendingTemperature = target;
        pendingArgument = target.ToString("R", CultureInfo.InvariantCulture);
        feedbackSource = feedbackReason = string.Empty;
        // 先登记本次草稿，再发命令，兼容主机同步回执与客户端异步确认。
        if (!MachineWorld.RequestOperation(Entity, InductionStoveLogic.SetTargetTemperatureOperation,
            pendingArgument, Actor, out pendingToken) && targetPending)
        {
            targetPending = false;
            draftDirty = true;
            feedbackSource = "请求未发出，请重试";
        }
        RefreshTargetControls();
    }

    private void HandleOperationCompleted(MachineOperationReceipt receipt)
    {
        if (!targetPending || receipt.Token != pendingToken || receipt.EntityId != Entity.Id ||
            receipt.Operation != InductionStoveLogic.SetTargetTemperatureOperation || receipt.Argument != pendingArgument) return;
        if (receipt.Confirmed && receipt.Accepted) targetAccepted = true;
        else
        {
            targetPending = false;
            draftDirty = true;
            StopTargetConfirmationWatch();
            feedbackSource = receipt.Confirmed ? "温度设置被拒绝：{0}" : "温度设置结果未确认：{0}";
            feedbackReason = receipt.Reason;
        }
        if (IsOpen) RefreshTargetControls();
        if (IsOpen && targetPending && targetAccepted && targetConfirmationWatch == null)
            targetConfirmationWatch = inductionPanel.StartCoroutine(WaitForTargetState(pendingToken));
    }

    private IEnumerator WaitForTargetState(uint token)
    {
        // 回执确认和状态快照各自收尾，不能因收到确认包就永久等待旧状态。
        yield return new WaitForSecondsRealtime(8f);
        targetConfirmationWatch = null;
        if (IsOpen) RefreshTargetControls();
        if (!targetPending || !targetAccepted || pendingToken != token) yield break;
        targetPending = false;
        draftDirty = true;
        feedbackSource = "设备状态尚未同步，请重新打开面板确认";
        feedbackReason = string.Empty;
        if (IsOpen) RefreshTargetControls();
    }

    private void StopTargetConfirmationWatch()
    {
        if (targetConfirmationWatch != null && inductionPanel != null)
            inductionPanel.StopCoroutine(targetConfirmationWatch);
        targetConfirmationWatch = null;
    }

    private void ClearTargetConfirmation()
    {
        StopTargetConfirmationWatch();
        targetPending = targetAccepted = false;
        pendingToken = 0;
    }

    private void RefreshTargetControls()
    {
        if (targetInput == null) return;
        if (targetPending && targetAccepted && Mathf.Approximately(stove.TargetTemperature, pendingTemperature))
        {
            targetPending = false;
            StopTargetConfirmationWatch();
            draftDirty = !TryGetTargetTemperature(out float draft) || !Mathf.Approximately(draft, pendingTemperature);
            feedbackSource = "目标温度已更新";
        }
        if (!targetInput.isFocused && !draftDirty && !targetPending)
            targetInput.SetTextWithoutNotify(stove.TargetTemperature.ToString("0.#", CultureInfo.InvariantCulture));
        string range = FlatWorldLocalizationService.GetUiFormat("请输入 {0:0.#}～{1:0.#}°C",
            stove.MinimumTargetTemperature, stove.MaximumTargetTemperature);
        if (targetInput.placeholder is TMP_Text placeholder) placeholder.text =
            stove.MinimumTargetTemperature.ToString("0.#", CultureInfo.InvariantCulture) + "–" +
            stove.MaximumTargetTemperature.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
        if (targetValue != null) targetValue.text = FlatWorldLocalizationService.GetUiFormat("目标温度 {0:0.#}°C", stove.TargetTemperature);
        if (applyLabel != null) applyLabel.text = FlatWorldLocalizationService.GetUiText("应用");
        bool valid = TryGetTargetTemperature(out _);
        applyTarget.interactable = !targetPending && valid;
        if (settingFeedback != null) settingFeedback.text = targetPending
            ? FlatWorldLocalizationService.GetUiText(targetAccepted ? "设备已确认，等待状态同步" : "等待设备确认")
            : !valid && draftDirty ? range
            : string.IsNullOrEmpty(feedbackSource) ? string.Empty
            : FlatWorldLocalizationService.GetUiFormat(feedbackSource, FlatWorldLocalizationService.GetUiText(feedbackReason ?? string.Empty));
    }

    protected override void DisposePresentation()
    {
        ClearTargetConfirmation();
        if (inductionPanel != null) inductionPanel.Closed -= ClearTargetConfirmation;
        MachineWorld.OperationCompleted -= HandleOperationCompleted;
        if (targetInput != null)
        {
            targetInput.onValueChanged.RemoveListener(TargetDraftChanged);
            targetInput.onSubmit.RemoveListener(SubmitTargetTemperature);
        }
        if (applyTarget != null) applyTarget.onClick.RemoveListener(ApplyTargetTemperature);
    }
    #endregion
}

/// <summary>炉面板只读取库存现有容量规则，重量与体积不在 UI 中重新计算。</summary>
internal static class FurnaceInputCapacityFeedback
{
    #region 输入容量读数
    public static string Describe(Inventory inventory)
    {
        Inventory_Data data = inventory?.Data;
        return data?.HasCarryCapacity == true ? FlatWorldLocalizationService.GetUiFormat(
            "输入重量 {0:0.##}/{1:0.##} kg\n输入体积 {2:0.##}/{3:0.##} L",
            data.CurrentCarryWeight, data.MaxCarryWeight, data.CurrentCarryVolume, data.MaxCarryVolume) : string.Empty;
    }
    #endregion
}

/// <summary>石臼表现把手势转成加工请求，不在手势视图中结算配方。</summary>
internal sealed class MortarMachinePanelSession : MachinePanelSession
{
    #region 石臼手势绑定

    private readonly MortarLogic mortar;
    private readonly MortarInteractionView view;

    public MortarMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            mortar = (MortarLogic)Logic;
            BasePanel panel = CreatePanel(mortar.PanelPrefab);
            view = panel.GetComponentInChildren<MortarInteractionView>(true)
                ?? throw new InvalidOperationException("石臼正式面板缺少手势视图。");
            TrackInventory(mortar.Bowl, panel);
            view.SyncSlots(mortar.Bowl);
            view.Struck += Strike;
        }
        catch { Dispose(); throw; }
    }

    private void Strike()
    {
        if (RequestWork() && view != null) view.PlayProcessingDust();
    }

    protected override void OnOpened()
    {
        if (view != null) view.ResetPresentation();
    }

    protected override void RefreshPresentation()
    {
        if (view != null) view.SyncSlots(mortar.Bowl);
    }

    protected override void DisposePresentation()
    {
        if (view != null) view.Struck -= Strike;
    }

    #endregion
}

/// <summary>钻木取火表现复用真实加热器库存和输出预览。</summary>
internal sealed class FireDrillMachinePanelSession : MachinePanelSession
{
    #region 取火绑定与刷新

    private readonly FireDrillLogic fireDrill;
    private readonly CraftingOutputPreview preview;
    private readonly TMP_Text temperature;

    public FireDrillMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            fireDrill = (FireDrillLogic)Logic;
            BasePanel panel = CreatePanel(fireDrill.PanelPrefab);
            BindSlots(panel, fireDrill.Heater.Input, "输入");
            BindSlots(panel, fireDrill.Heater.Output, "输出", allowDirectionalDeposit: false);
            BindActionButton(panel.GetButton("合成按钮")
                ?? throw new InvalidOperationException("取火面板缺少摩擦按钮。"));
            preview = CraftingOutputPreview.Attach(panel, fireDrill.Heater.Output.itemSlot_UI[0]);
            temperature = panel.GetText("FWUI_FooterHint");
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    {
        if (preview == null) return;
        ItemData output = fireDrill.Heater.PreviewOutput();
        if (output != null) preview.Show(output, fireDrill.Progress01);
        else preview.Clear();
        if (temperature != null) temperature.text = fireDrill.Status;
    }

    protected override void DisposePresentation() => preview?.Clear();

    #endregion
}

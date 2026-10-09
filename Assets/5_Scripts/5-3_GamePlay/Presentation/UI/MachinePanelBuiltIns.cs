using System;
using TMPro;
using UnityEngine.UI;

/// <summary>内置机器的表现组合入口，具体控件绑定由各自会话负责。</summary>
internal static class MachinePanelBuiltIns
{
    #region 内置表现登记

    internal static void Register()
    {
        MachinePanelFactoryRegistry.AddBuiltIn("workbench", entity => new WorkbenchMachinePanelSession(entity));
        MachinePanelFactoryRegistry.AddBuiltIn("furnace", entity => new FurnaceMachinePanelSession(entity));
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

    private static IMachinePanelSession CreateManualProcessing(MachineEntity entity)
    {
        var logic = (ManualProcessingLogic)entity.Logic;
        return new MechanicalPanelSession("UI_HandDrill", entity, logic.Processor,
            actor => MachineWorld.RequestOperation(entity, "work", "", actor),
            () => logic.Status, () => logic.ActionLabel, () => logic.CanAct);
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

/// <summary>工作台表现只绑定工作台领域公开的库存和加工入口。</summary>
internal sealed class WorkbenchMachinePanelSession : MachinePanelSession
{
    #region 工作台绑定

    private readonly CraftingStationController controller;

    public WorkbenchMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            var logic = (WorkbenchLogic)Logic;
            BasePanel panel = CreatePanel(logic.PanelPrefab);
            BindSlots(panel, logic.Input, "输入");
            BindSlots(panel, logic.Output, "输出");
            controller = new CraftingStationController(panel, logic.Input, logic.Output,
                logic.Processor.Capabilities, () => logic.RequiredClicks, () => Actor,
                authoritativeProcessor: logic.Processor,
                selectRecipe: id => MachineWorld.RequestOperation(Entity, "select", id, Actor),
                performWork: () => MachineWorld.RequestOperation(Entity, "work", "", Actor));
        }
        catch { Dispose(); throw; }
    }

    protected override void DisposePresentation() => controller?.Dispose();

    #endregion
}

/// <summary>熔炉表现只读取炉温、燃料和加工状态，按钮发送权威操作。</summary>
internal sealed class FurnaceMachinePanelSession : MachinePanelSession
{
    #region 熔炉绑定与刷新

    private readonly FurnaceLogic furnace;
    private readonly Slider progress;
    private readonly Slider fuel;
    private readonly TMP_Text temperature;
    private readonly TMP_Text hint;

    public FurnaceMachinePanelSession(MachineEntity entity) : base(entity, bindDefaultInventories: false)
    {
        try
        {
            furnace = (FurnaceLogic)Logic;
            BasePanel panel = CreatePanel(furnace.PanelPrefab);
            BindSlots(panel, furnace.Input, "输入");
            BindSlots(panel, furnace.Output, "输出");
            BindSlots(panel, furnace.FuelInventory, "燃料");
            BindActionButton(panel.GetButton("合成按钮")
                ?? throw new InvalidOperationException("熔炉面板缺少合成按钮。"));
            progress = panel.GetSlider("熔炼进度条");
            fuel = panel.GetSlider("燃料显示条");
            temperature = panel.GetText("FWUI_FurnaceTemperatureValue");
            hint = panel.GetText("FWUI_FooterHint");
        }
        catch { Dispose(); throw; }
    }

    protected override void RefreshPresentation()
    {
        if (progress != null) progress.value = furnace.Progress01;
        if (fuel != null) fuel.value = furnace.FuelRatio;
        if (temperature != null) temperature.text = furnace.Status;
        if (hint != null) hint.text = furnace.GetProcessingHint();
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
            BindSlots(panel, fireDrill.Heater.Output, "输出");
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

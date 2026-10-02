using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>纯数据机械的短期交互代理；没有 GameObject、Collider、SpriteRenderer 或逐节点 Update。</summary>
public sealed class MachineInteractionTarget : IWorldInteractionTarget, IWorldInteractionPreview, IDisposable
{
    #region 节点交互
    private readonly MachineEntity node;
    private IMachinePanelSession panel;
    private bool disposed;
    private float manualElapsed;
    private bool manualActive;
    private bool manualCranking;
    private float nextRemoteCrank;

    public MachineInteractionTarget(MachineEntity node)
        => this.node = node ?? throw new ArgumentNullException(nameof(node));

    public Vector3 WorldPosition => node.Snapshot.transform.position;
    public int TargetGuid => node.Id;
    public bool IsValid => !disposed && MachineWorld.Contains(node);
    public bool CanInteract(Item actor) => IsValid;
    public bool CanPointerInteract(Item actor) => false;

    // 描边属于本地表现状态，不进入机器存档和网络同步。
    public bool IsInteractionHighlighted { get; private set; }
    public event Action<bool> InteractionHighlightChanged;

    public void SetInteractionHighlighted(bool highlighted)
    {
        if (disposed || IsInteractionHighlighted == highlighted) return;
        IsInteractionHighlighted = highlighted;
        InteractionHighlightChanged?.Invoke(highlighted);
    }

    /// <summary>短按打开面板，手摇轮长按进入连续供能。</summary>
    public void OnInteractStart(Item actor)
    {
        if (!CanInteract(actor)) return;
        if (GameNetwork.HasStateAuthority) MachineWorld.WakeForInteraction(node);
        if (node.Definition.Source == "manual")
        {
            manualElapsed = 0f;
            manualCranking = false;
            manualActive = true;
            return;
        }
        TogglePanel(actor);
    }

    /// <summary>手摇轮达到按住阈值后向节点状态持续补入短时扭矩。</summary>
    public void OnInteractUpdate(Item actor)
    {
        if (!manualActive || !IsValid || node.State == null) return;
        manualElapsed += Time.deltaTime;
        if (manualElapsed < MachineCatalog.Settings.ManualHoldThresholdSeconds) return;
        manualCranking = true;
        if (!GameNetwork.HasStateAuthority)
        {
            if (Time.unscaledTime >= nextRemoteCrank)
            { nextRemoteCrank = Time.unscaledTime + .1f; MachineWorld.RequestOperation(node, "crank", "", actor as Player); }
            return;
        }
        float buffer = Mathf.Min(MachineCatalog.Settings.ManualReserveSeconds,
            Mathf.Max(MachineCatalog.Settings.ManualPulseSeconds, MachineCatalog.Settings.TickSeconds * 2f));
        node.State.ManualSeconds = Mathf.Max(node.State.ManualSeconds, buffer);
    }

    /// <summary>手摇轮短按释放时打开面板，长按仅给动力。</summary>
    public void OnInteractEnd(Item actor)
    {
        if (!manualActive) return;
        bool openPanel = !manualCranking;
        manualActive = false;
        manualCranking = false;
        if (openPanel && IsValid) TogglePanel(actor);
    }

    /// <summary>切换目标时结束手摇或关闭非手摇设备面板。</summary>
    public void OnInteractCancel(Item actor)
    {
        manualActive = false;
        manualCranking = false;
        if (node.Definition.Source != "manual") panel?.Close();
    }

    /// <summary>按节点数据创建并切换通用机械面板。</summary>
    private void TogglePanel(Item actor)
    {
        if (panel != null && !panel.IsAlive) { panel.Dispose(); panel = null; }
        if (node.Logic != null)
        {
            panel ??= MachinePanelSession.Create(node);
            panel.Toggle(actor);
            return;
        }
        panel ??= new MechanicalPanelSession("UI_Mechanical", node,
            node.Processor,
            PerformOperation, GetStatus, GetActionLabel, CanPerformOperation,
            refreshStatusPeriodically: node.Definition.Electrical?.IsBattery == true);
        panel.Toggle(actor);
    }

    /// <summary>机械 Tick 后只刷新已打开面板的状态。</summary>
    public void RefreshPanel() => panel?.Refresh();

    /// <summary>面板按钮反映节点定义和加工器当前状态。</summary>
    private string GetActionLabel()
    {
        if (node.State == null) return string.Empty;
        if (node.Definition.Kind == "clutch") return node.State.Engaged ? "断开" : "接合";
        if (node.Definition.Kind == "gearbox" && node.Definition.Ratios.Length > 1) return "切换传动比";
        if (node.Definition.ManualDriveTorque > 0) return "手动研磨";
        return MachineDefinition.Positive(node.Definition.ManualWorkSecondsPerPress) &&
            node.Processor?.Preview().Success == true ? "手动推进" : string.Empty;
    }

    /// <summary>手推石磨的操作资格随外部输入动力实时更新。</summary>
    private bool CanPerformOperation()
        => node.Definition.ManualDriveTorque <= 0 || !GameNetwork.HasStateAuthority || MachineWorld.CanManualDrive(node);

    /// <summary>面板操作直接修改权威节点，拓扑变更在下一次机械 Tick 重构。</summary>
    private void PerformOperation(Player actor)
    {
        if (!GameNetwork.HasStateAuthority)
        { MachineWorld.RequestOperation(node, "work", "", actor); return; }
        if (!IsValid || !GameNetwork.HasStateAuthority || node.State == null) return;
        if (node.Definition.Kind == "clutch")
        {
            node.State.Engaged = !node.State.Engaged;
            MachineWorld.TopologyChanged(node);
        }
        else if (node.Definition.Kind == "gearbox" && node.Definition.Ratios.Length > 1)
        {
            node.State.RatioIndex = (node.State.RatioIndex + 1) % node.Definition.Ratios.Length;
            MachineWorld.TopologyChanged(node);
        }
        else if (node.Definition.ManualDriveTorque > 0)
            MachineWorld.TryManualDrive(node);
        else if (MachineDefinition.Positive(node.Definition.ManualWorkSecondsPerPress) &&
                 node.Processor?.Preview().Success == true)
            node.Processor.AdvanceManually(node.Definition.ManualWorkSecondsPerPress, actor);
        panel?.Refresh();
    }

    /// <summary>状态文本仅读取权威网络解算结果，不保存第二份面板状态。</summary>
    private string GetStatus()
    {
        string state = FlatWorldLocalizationService.GetUiText(node.GetOperatingStatus());
        string status = state;
        if (node.Definition.HasMechanicalPorts)
        {
            node.GetLocalTorque(out float torqueSupply, out float torqueDemand);
            status = FlatWorldLocalizationService.GetUiFormat(
                "{0} · 转速 {1:0} · 扭矩 {2:0.#}/{3:0.#}",
                state, node.SpeedRpm, torqueSupply, torqueDemand);
            status += " · " + FlatWorldLocalizationService.GetUiText(node.GetRotationStatus());
        }
        if (node.Definition.HasMechanicalPorts &&
            (node.Definition.Kind == "consumer" || node.Definition.Kind == "bellows"))
            status += FlatWorldLocalizationService.GetUiFormat(
                " · 工作效率 {0:0.#}%（需求 {1:0} RPM）",
                MachineWorld.GetWorkEfficiency(node) * 100f, node.Definition.RequiredRpm);
        ElectricalNetwork electrical = MachineWorld.GetElectricalNetwork(node);
        if (node.Definition.Electrical != null && electrical != null)
        {
            if (node.Definition.IsConverter)
                status += " · " + (node.ConversionMode switch
                {
                    ElectricalConversionMode.Motor => node.ElectricalSuppliedWatts > 0f
                        ? "电力输入 → 机械输出" : "等待电力输入",
                    ElectricalConversionMode.Generator => "机械输入 → 电力输出",
                    _ => "电机待机"
                });
            status += FlatWorldLocalizationService.GetUiFormat(
                " · 电网 {0:0.#}V {1:0.#}A · 功率 {2:0.#}/{3:0.#}W · {4}",
                electrical.Voltage, electrical.CurrentAmps, electrical.DeliveredWatts,
                electrical.DemandWatts, electrical.Status);
        }
        // 储能属于电池本身，未接入电网时也必须显示真实余量。
        if (node.Definition.Electrical?.IsBattery == true)
            status += FlatWorldLocalizationService.GetUiFormat(
                " · 储能 {0:0}/{1:0}J", node.ElectricalStoredJoules,
                node.Definition.Electrical.CapacityJoules);
        return status;
    }

    /// <summary>节点移除或切换世界时释放 UI 引用。</summary>
    public void Dispose()
    {
        if (disposed) return;
        SetInteractionHighlighted(false);
        disposed = true;
        InteractionHighlightChanged = null;
        panel?.Dispose();
        panel = null;
    }
    #endregion
}

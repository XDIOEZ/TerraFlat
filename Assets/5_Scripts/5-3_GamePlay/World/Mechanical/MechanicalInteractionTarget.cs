using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>纯数据机械的短期交互代理；没有 GameObject、Collider、SpriteRenderer 或逐节点 Update。</summary>
public sealed class MechanicalInteractionTarget : IWorldInteractionTarget, IDisposable
{
    #region 节点交互
    private readonly MechanicalNode node;
    private MechanicalPanelSession panel;
    private bool disposed;
    private float manualElapsed;
    private bool manualActive;
    private bool manualCranking;

    public MechanicalInteractionTarget(MechanicalNode node)
        => this.node = node ?? throw new ArgumentNullException(nameof(node));

    public Vector3 WorldPosition => node.Snapshot.transform.position;
    public int TargetGuid => node.Id;
    public bool IsValid => !disposed && MechanicalWorld.Contains(node);
    public bool CanInteract(Item actor) => IsValid;
    public bool CanPointerInteract(Item actor) => false;

    /// <summary>短按打开面板，手摇轮长按进入连续供能。</summary>
    public void OnInteractStart(Item actor)
    {
        if (!CanInteract(actor)) return;
        if (GameNetwork.HasStateAuthority) MechanicalWorld.WakeForInteraction(node);
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
        if (!manualActive || !IsValid || node.State == null || !GameNetwork.HasStateAuthority) return;
        manualElapsed += Time.deltaTime;
        if (manualElapsed < MechanicalCatalog.Settings.ManualHoldThresholdSeconds) return;
        manualCranking = true;
        float buffer = Mathf.Min(MechanicalCatalog.Settings.ManualReserveSeconds,
            Mathf.Max(MechanicalCatalog.Settings.ManualPulseSeconds, MechanicalCatalog.Settings.TickSeconds * 2f));
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
        panel ??= new MechanicalPanelSession("UI_Mechanical", node,
            GameNetwork.HasStateAuthority ? node.Processor : null,
            PerformOperation, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }

    /// <summary>机械 Tick 后只刷新已打开面板的状态。</summary>
    public void RefreshPanel() => panel?.Refresh();

    /// <summary>面板按钮反映节点定义和加工器当前状态。</summary>
    private string GetActionLabel()
    {
        if (!GameNetwork.HasStateAuthority) return string.Empty;
        if (node.State == null) return string.Empty;
        if (node.Definition.Kind == "clutch") return node.State.Engaged ? "断开" : "接合";
        if (node.Definition.Kind == "gearbox" && node.Definition.Ratios.Length > 1) return "切换传动比";
        return MechanicalDefinition.Positive(node.Definition.ManualWorkSecondsPerPress) &&
            node.Processor?.Preview().Success == true ? "手动推进" : string.Empty;
    }

    /// <summary>面板操作直接修改权威节点，拓扑变更在下一次机械 Tick 重构。</summary>
    private void PerformOperation(Player actor)
    {
        if (!IsValid || !GameNetwork.HasStateAuthority || node.State == null) return;
        if (node.Definition.Kind == "clutch")
        {
            node.State.Engaged = !node.State.Engaged;
            MechanicalWorld.TopologyChanged(node);
        }
        else if (node.Definition.Kind == "gearbox" && node.Definition.Ratios.Length > 1)
        {
            node.State.RatioIndex = (node.State.RatioIndex + 1) % node.Definition.Ratios.Length;
            MechanicalWorld.TopologyChanged(node);
        }
        else if (MechanicalDefinition.Positive(node.Definition.ManualWorkSecondsPerPress) &&
                 node.Processor?.Preview().Success == true)
            node.Processor.AdvanceManually(node.Definition.ManualWorkSecondsPerPress, actor);
        panel?.Refresh();
    }

    /// <summary>状态文本仅读取权威网络解算结果，不保存第二份面板状态。</summary>
    private string GetStatus()
    {
        string state = FlatWorldLocalizationService.GetUiText(node.Network?.Status ?? "停止");
        string status = FlatWorldLocalizationService.GetUiFormat(
            "{0} · 转速 {1:0} · 扭矩 {2:0.#}/{3:0.#}",
            state, node.Rpm, node.Network?.TorqueSupply ?? 0, node.Network?.TorqueDemand ?? 0);
        if (node.Definition.Kind == "consumer" || node.Definition.Kind == "bellows")
            status += FlatWorldLocalizationService.GetUiFormat(
                " · 工作效率 {0:0.#}%（需求 {1:0} RPM）",
                MechanicalWorld.GetWorkEfficiency(node) * 100f, node.Definition.RequiredRpm);
        return status;
    }

    /// <summary>节点移除或切换世界时释放 UI 引用。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        panel?.Dispose();
        panel = null;
    }
    #endregion
}

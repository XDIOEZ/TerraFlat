using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>通用手动加工台模块；站点配方来自 MechanicalCatalog，WorkPerClick 由物品定义独立配置，便携物与建筑共用进度。</summary>
public sealed class Mod_ManualProcessor : Module, IInteractable
{
    #region 配置与状态
    public const string ModuleId = "手动加工模块";
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public string Station = "hand_forge"; // 由机械目录选择手动加工站点。
    public float WorkPerClick = 1f; // 每次手动操作推进的工作秒数。
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public MechanicalProcessor Processor { get; private set; }

    private MechanicalPanelSession panel;
    #endregion

    #region 生命周期与交互
    public override void Load()
    {
        if (string.IsNullOrWhiteSpace(Station) || WorkPerClick <= 0f ||
            float.IsNaN(WorkPerClick) || float.IsInfinity(WorkPerClick))
            throw new InvalidOperationException("手动加工台的站点或单次工作量无效。");

        Data ??= new Ex_ModData_MemoryPackable { ID = ModuleId };
        MechanicalProcessingState state = Data.BitData != null && Data.BitData.Length > 0
            ? Data.GetData<MechanicalProcessingState>()
            : new MechanicalProcessingState();
        Processor = new MechanicalProcessor(Station, state ?? new MechanicalProcessingState());
        item.OnAct += OnItemAct;
    }

    public override void Save()
    {
        if (Processor != null)
            Data.WriteData(Processor.State);
    }

    public override void Unload()
    {
        if (item != null)
            item.OnAct -= OnItemAct;

        panel?.Dispose();
        panel = null;
        Save();
        Processor?.Dispose();
        Processor = null;
    }

    /// <summary>放置请求优先于打开加工面板。</summary>
    private void OnItemAct()
    {
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true)
            return;
        if (item.Owner != null)
            OnInteractStart(item.Owner);
    }

    public void OnInteractStart(Item actor)
    {
        if (!GameNetwork.HasStateAuthority)
            return;

        panel ??= new MechanicalPanelSession("UI_HandDrill", item, Processor, PerformWork, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }

    public void OnInteractCancel(Item actor) => panel?.Close();

    /// <summary>手动锻打只推进目录已声明且输出可容纳的加工配方。</summary>
    private void PerformWork(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Processor == null)
            return;

        Processor.AdvanceManually(WorkPerClick, actor);
        Save();
        panel?.Refresh();
    }

    private string GetStatus()
        => FlatWorldLocalizationService.GetUiFormat("加工进度 {0:0}%", (Processor?.Progress01 ?? 0f) * 100f);

    private static string GetActionLabel() => "锻打";
    #endregion
}

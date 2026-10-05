using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>落地手动加工台模块；站点配方来自 MachineCatalog，WorkPerClick 表示单次工作秒数。召唤器只携带加工状态用于放置和拆回，完成安装的建筑才能打开面板与加工。</summary>
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
    public RecipeProcessor Processor { get; private set; }

    private MechanicalPanelSession panel;
    #endregion

    #region 生命周期与交互
    public override void Load()
    {
        if (string.IsNullOrWhiteSpace(Station) || WorkPerClick <= 0f ||
            float.IsNaN(WorkPerClick) || float.IsInfinity(WorkPerClick))
            throw new InvalidOperationException("手动加工台的站点或单次工作量无效。");

        Data ??= new Ex_ModData_MemoryPackable { ID = ModuleId };
        RecipeProcessingState state = Data.BitData != null && Data.BitData.Length > 0
            ? Data.GetData<RecipeProcessingState>()
            : new RecipeProcessingState();
        Processor = new RecipeProcessor(Station, state ?? new RecipeProcessingState());
    }

    public override void Save()
    {
        if (Processor != null)
            Data.WriteData(Processor.State);
    }

    public override void Unload()
    {
        panel?.Dispose();
        panel = null;
        Save();
        Processor?.Dispose();
        Processor = null;
    }

    /// <summary>重型加工台必须完成安装，手持和地面掉落的召唤器都不能加工。</summary>
    private bool CanUsePlacedStation()
    {
        if (item == null || item.InHand)
            return false;
        Mod_Building building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        return building != null && building.IsInstalled();
    }

    public void OnInteractStart(Item actor)
    {
        if (!GameNetwork.HasStateAuthority || !CanUsePlacedStation())
            return;

        panel ??= new MechanicalPanelSession("UI_HandDrill", item, Processor, PerformWork, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }

    public void OnInteractCancel(Item actor) => panel?.Close();

    /// <summary>手动锻打只推进目录已声明且输出可容纳的加工配方。</summary>
    private void PerformWork(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Processor == null || !CanUsePlacedStation())
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

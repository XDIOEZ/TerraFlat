using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>便携手钻：每次钻孔动作增加一秒工作量，默认四次完成；输入、输出和进度通过共享模块随放置、拆回与存档迁移。</summary>
public sealed class Mod_HandDrill : Module, IInteractable
{
    #region 配置与状态
    public const string ModuleId = "手钻模块";
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public float WorkPerClick = 1f; // 每次操作的工作量，秒
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public MechanicalProcessor Processor { get; private set; }
    private MechanicalPanelSession panel;
    #endregion

    #region 生命周期与交互
    public override void Load()
    {
        Processor = new MechanicalProcessor("hand_drill", Data.GetData<MechanicalProcessingState>() ?? new MechanicalProcessingState());
        item.OnAct += OnItemAct;
    }
    public override void Save() { if (Processor != null) Data.WriteData(Processor.State); }
    public override void Unload()
    {
        if (item != null) item.OnAct -= OnItemAct;
        panel?.Dispose(); panel = null;
        Processor?.Dispose(); Processor = null;
    }
    private void OnItemAct()
    {
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true) return;
        if (item.Owner != null) OnInteractStart(item.Owner);
    }
    public void OnInteractStart(Item actor)
    {
        if (!GameNetwork.HasStateAuthority) return;
        panel ??= new MechanicalPanelSession("UI_HandDrill", item, Processor, Drill, GetStatus, GetActionLabel);
        panel.Toggle(actor);
    }
    public void OnInteractCancel(Item actor) => panel?.Close();
    public bool TryDrill(Player actor = null)
    {
        if (!GameNetwork.HasStateAuthority || Processor == null) return false;
        bool result = Processor.Advance(WorkPerClick, actor);
        Save();
        return result;
    }
    private void Drill(Player actor) { TryDrill(actor); panel?.Refresh(); }
    private string GetActionLabel() => "钻孔";
    /// <summary>旧占位手钻没有建筑角色，统一转入便携载体；正式落地本体保留 HandDrill 身份。</summary>
    public static string ResolveCarrierDefinition(ItemData data)
        => data.IDName == "HandDrill" && !Mod_Building.TryReadBuildingData(data, out _, out _)
            ? "HandDrill_Summoner" : data.IDName.Trim();
    private string GetStatus() => FlatWorldLocalizationService.GetUiFormat("加工进度 {0:0}%", (Processor?.Progress01 ?? 0) * 100);
    #endregion
}

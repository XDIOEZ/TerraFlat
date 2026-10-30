using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>落地手动加工台模块；站点配方来自正式制作目录，召唤器只携带加工状态用于放置和拆回。</summary>
public sealed class Mod_ManualProcessor : Module, IInteractable, IInventory
{
    #region 配置与状态
    public const string ModuleId = "手动加工模块";
    public Ex_ModData_MemoryPackable Data = new() { ID = ModuleId };
    public string Station = "hand_forge"; // 由机械目录选择手动加工站点。
    public float WorkPerClick = 1f; // 每次手动操作推进的工作秒数。
    public float DurabilityPerClick; // 每次有效工作消耗的耐久。
    public float BreakRecoveryFraction = .5f; // 用坏后按自身制作配方返还的材料比例。
    public ResourceToolKind RequiredToolKind; // 没有要求的设备继续只使用材料槽。
    public string RequiredToolTag = string.Empty;
    public float ToolDurabilityPerClick = 1f;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public RecipeProcessor Processor { get; private set; }
    public Inventory ToolInventory => runtime?.Tool;
    public Inventory GetDefaultTargetInventory() => Processor?.Input;

    private ManualProcessingItemPanelSession panel;
    private ManualProcessingRuntime runtime;
    #endregion

    #region 生命周期与交互
    protected override void OnLoad()
    {
        if (string.IsNullOrWhiteSpace(Station) || WorkPerClick <= 0f ||
            float.IsNaN(WorkPerClick) || float.IsInfinity(WorkPerClick))
            throw new InvalidOperationException("手动加工台的站点或单次工作量无效。");

        Data ??= new Ex_ModData_MemoryPackable { ID = ModuleId };
        ManualProcessingState state = Data.BitData != null && Data.BitData.Length > 0
            ? Data.GetData<ManualProcessingState>()
            : new ManualProcessingState();
        runtime = new ManualProcessingRuntime(() => item.itemData, Station, state, DurabilityPerClick,
            RequiredToolKind == ResourceToolKind.None ? null :
                new ManualProcessingToolRequirement(RequiredToolKind, RequiredToolTag, ToolDurabilityPerClick));
        Processor = runtime.Processor;
    }

    protected override void OnSave()
    {
        if (Processor != null)
            Data.WriteData(runtime.State);
    }

    protected override void OnUnload()
    {
        panel?.Dispose();
        panel = null;
        Save();
        runtime?.Dispose();
        runtime = null;
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

    public bool CanInteract(Item actor) => actor != null && GameNetwork.HasStateAuthority && CanUsePlacedStation();

    public void OnInteractStart(Item actor)
    {
        if (!CanInteract(actor))
            return;

        panel ??= new ManualProcessingItemPanelSession(item, runtime, WorkPerClick, PerformWork);
        panel.Toggle(actor);
    }

    public void OnInteractCancel(Item actor) => panel?.Close();

    /// <summary>手动锻打只推进目录已声明且输出可容纳的加工配方。</summary>
    private void PerformWork(Player actor)
    {
        if (!GameNetwork.HasStateAuthority || Processor == null || !CanUsePlacedStation())
            return;

        Processor.AdvanceManually(WorkPerClick, actor, out bool worked);
        Save();
        if (worked) ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        if (runtime.Exhausted)
        {
            if (!TryDestroyWornStation(out string reason)) Debug.LogError("[工作站损毁] " + reason);
            return;
        }
        panel?.Refresh();
    }

    private bool TryDestroyWornStation(out string reason)
    {
        reason = null;
        string carrier = Mod_Building.GetSummonerPrefabId(item.itemData.IDName);
        if (!BuildingMaterialSalvage.TryCreateProportionalMaterialSalvage(carrier, BreakRecoveryFraction,
                out var drops, out reason, item.itemData)) return false;
        foreach (Inventory inventory in new[] { ToolInventory, Processor.Input, Processor.Output })
            if (inventory != null)
            foreach (ItemSlot slot in inventory.Data.itemSlots)
                if (slot?.itemData?.Stack?.Amount > 0f) drops.Add(ItemInstanceDataFactory.CloneRuntime(slot.itemData));
        var spawned = new System.Collections.Generic.List<DroppedItemHandle>();
        try
        {
            foreach (ItemData drop in drops)
            {
                DroppedItemHandle handle = DroppedItemService.Spawn(drop, item.transform.position, sourceItem: item);
                if (!handle.IsValid) throw new InvalidOperationException("工作站损毁材料生成失败。");
                spawned.Add(handle);
            }
            item.DestroySelf();
            return true;
        }
        catch (Exception exception)
        {
            foreach (DroppedItemHandle handle in spawned) DroppedItemService.Remove(handle);
            reason = exception.Message;
            return false;
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>堆肥规则内聚处理整槽计时与转化，保存完整物品数据，不再截断模块状态。</summary>
public class CompostLogic : MachineLogic
{
    #region 堆肥
    public SlotProcessingState State { get; }
    public Inventory Inventory { get; }
    public override GameObject PanelPrefab { get; }
    public float CompostSeconds { get; }
    private readonly string outputId;
    private readonly List<string> ids;
    private readonly List<string> tags;
    private readonly List<string> keywords;
    private readonly bool useKeywords;

    public CompostLogic(MachineEntity entity) : base(entity)
    {
        var config = entity.Definition.Content.Find<Mod_CompostBin>()
            ?? throw new InvalidOperationException("堆肥设施缺少内容配置。");
        var source = (Mod_CompostBin)config.Authoring;
        State = MachinePersistence.Read<SlotProcessingState>(entity.Snapshot, "compost") ?? new SlotProcessingState();
        int slots = Mathf.Max(1, config.Value("SlotCount", source.SlotCount));
        Inventory = Track(MachineInventory.Create(source.CompostInventory, State.Inventory, "堆肥桶", slots));
        State.Inventory = Inventory.Data;
        State.EnsureSlots(Inventory.Data.itemSlots.Count);
        PanelPrefab = source.UI_Prefab != null ? source.UI_Prefab : GameRes.Instance.GetPrefab("UI_CompostBin");
        Inventory.InventoryPanel_Prefab = PanelPrefab;
        CompostSeconds = config.Value("CompostSeconds", source.CompostSeconds);
        if (!MachineDefinition.Positive(CompostSeconds)) throw new InvalidOperationException("堆肥时长无效。");
        ids = config.Value("AcceptItemIds", source.AcceptItemIds) ?? new();
        tags = config.Value("AcceptTags", source.AcceptTags) ?? new();
        keywords = config.Value("AcceptKeywords", source.AcceptKeywords) ?? new();
        useKeywords = config.Value("EnableKeywordFallback", source.EnableKeywordFallback);
        string configuredId = config.Value("OutputItemId", source.OutputItemId);
        if (!string.IsNullOrWhiteSpace(configuredId) && GameRes.Instance.TryGetItemDefinition(configuredId, out _)) outputId = configuredId;
        if (outputId == null)
            foreach (string candidate in config.Value("OutputItemFallbackIds", source.OutputItemFallbackIds) ?? new List<string>())
                if (GameRes.Instance.TryGetItemDefinition(candidate, out _)) { outputId = candidate; break; }
        if (string.IsNullOrWhiteSpace(outputId)) throw new InvalidOperationException("堆肥产物没有有效定义。");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool CanCompost(ItemData item)
    {
        if (item == null || item.IDName == outputId) return false;
        foreach (string id in ids)
            if (string.Equals(id, item.IDName, StringComparison.OrdinalIgnoreCase) || string.Equals(id, item.GameName, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.Tags != null && item.Tags.ContainsAnyTag(tags)) return true;
        if (!useKeywords) return false;
        string description = item.IDName + " " + item.GameName + " " + item.Description;
        foreach (string word in keywords)
            if (!string.IsNullOrWhiteSpace(word) && description.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public override void Tick(float seconds)
    {
        State.EnsureSlots(Inventory.Data.itemSlots.Count);
        bool changed = false;
        for (int i = 0; i < Inventory.Data.itemSlots.Count; i++)
        {
            ItemData item = Inventory.Data.itemSlots[i].itemData;
            State.Observe(i, item);
            if (!CanCompost(item) || Inventory.IsSlotBeingDragged(i)) continue;
            State.Elapsed[i] += seconds;
            changed = true;
            if (State.Elapsed[i] < CompostSeconds) continue;
            int amount = Mathf.Max(1, Mathf.RoundToInt(item.Stack.Amount));
            if (CraftingService.TransformSlot(Inventory, i, outputId, amount, "machine.compost").Success)
                State.Reset(i);
        }
        if (changed) NotifyChanged();
    }
    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "compost", State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        State.ApplyRemote(MachinePersistence.Read<SlotProcessingState>(snapshot, "compost"), Inventory);
        NotifyRemoteChanged();
        return true;
    }
    #endregion
}

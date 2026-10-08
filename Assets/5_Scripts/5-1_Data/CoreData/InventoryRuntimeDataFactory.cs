using System;
using System.Collections.Generic;

/// <summary>库存运行态保留当前布局并直接创建内嵌物品，不经过库存或物品快照编解码。</summary>
public static class InventoryRuntimeDataFactory
{
    #region 库存内存工厂

    public static Func<Dictionary<string, Inventory_Data>> CompileDictionary(IDictionary<string, Inventory_Data> source)
    {
        var entries = new List<(string Name, Func<Inventory_Data> Create)>(source?.Count ?? 0);
        if (source != null)
            foreach (KeyValuePair<string, Inventory_Data> pair in source)
                entries.Add((pair.Key, pair.Value == null ? null : Compile(pair.Value)));
        IEqualityComparer<string> comparer = source is Dictionary<string, Inventory_Data> dictionary
            ? dictionary.Comparer : StringComparer.Ordinal;
        return () =>
        {
            var result = new Dictionary<string, Inventory_Data>(entries.Count, comparer);
            foreach (var entry in entries) result.Add(entry.Name, entry.Create?.Invoke());
            return result;
        };
    }

    public static Func<Inventory_Data> Compile(Inventory_Data source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.GetType() != typeof(Inventory_Data))
        {
            Inventory_Data frozen = CloneExtension(source);
            return () => CloneExtension(frozen);
        }
        string name = source.Name;
        int index = source.Index;
        bool injected = source.IsInjected;
        var panelPosition = source.PanelPosition;
        bool panelOpen = source.PanelIsOpen;
        bool depositBlocked = source.IsDepositBlocked;
        string toggleAction = source.ToggleActionName;
        string uiPrefab = source.UIPrefabName;
        var slots = new List<Func<ItemSlot>>(source.itemSlots?.Count ?? 0);
        if (source.itemSlots != null)
            foreach (ItemSlot slot in source.itemSlots) slots.Add(slot == null ? null : CompileSlot(slot));
        return () =>
        {
            var itemSlots = new List<ItemSlot>(slots.Count);
            foreach (Func<ItemSlot> create in slots) itemSlots.Add(create?.Invoke());
            return new Inventory_Data(itemSlots, name)
            {
                Index = index, IsInjected = injected, PanelPosition = panelPosition, PanelIsOpen = panelOpen,
                IsDepositBlocked = depositBlocked, ToggleActionName = toggleAction, UIPrefabName = uiPrefab
            };
        };
    }

    public static Inventory_Data CloneRuntime(Inventory_Data source) => source == null ? null : Compile(source)();

    private static Func<ItemSlot> CompileSlot(ItemSlot source)
    {
        if (source.GetType() != typeof(ItemSlot))
        {
            ItemSlot frozen = CloneExtension(source);
            return () => CloneExtension(frozen);
        }
        int index = source.Index;
        float volume = source.SlotMaxVolume;
        string[] tags = source.CanAcceptTags?.ToArray();
        Func<ItemData> item = source.itemData == null ? null : ItemInstanceDataFactory.CompileRuntimeCopy(source.itemData);
        return () => new ItemSlot(index)
        {
            SlotMaxVolume = volume,
            CanAcceptTags = tags == null ? null : new List<string>(tags),
            // 冷数据仍由入包入口提升到当前定义，不能用默认复制伪装已挂接配置。
            itemData = item?.Invoke()
        };
    }

    private static T CloneExtension<T>(T source) where T : class
    {
        // 扩展布局保留原先的完整类型复制，不能套用内建字段计划后悄悄丢掉扩展状态。
        T clone = FastCloner.FastCloner.DeepClone<object>(source) as T;
        if (clone == null || clone.GetType() != source.GetType() || ReferenceEquals(clone, source))
            throw new InvalidOperationException($"扩展库存运行态复制失败：{source.GetType().FullName}");
        return clone;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

/// <summary>持有运行时身份与密集索引，外部只能通过注册和注销改变集合。</summary>
internal sealed class ItemRuntimeRegistry
{
    #region 注册状态与只读视图

    private sealed class ItemGroup
    {
        public readonly List<Item> Items = new();
        public readonly ReadOnlyCollection<Item> View;

        public ItemGroup() => View = Items.AsReadOnly();
    }

    private sealed class Registration
    {
        public int Guid;
        public string GroupId;
        public ItemGroup Group;
        public int ItemIndex;
        public int GroupIndex;
        public ulong Token;
    }

    private readonly Dictionary<int, Item> itemsByGuid = new();
    private readonly Dictionary<string, ItemGroup> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<Item>> groupViews = new(StringComparer.Ordinal);
    private readonly List<Item> items = new();
    private readonly Dictionary<Item, Registration> registrations = new(ItemReferenceComparer.Instance);
    private ulong registrationSequence;

    public IReadOnlyDictionary<int, Item> ItemsByGuid { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<Item>> Groups { get; }
    public IReadOnlyList<Item> Items { get; }

    public ItemRuntimeRegistry()
    {
        ItemsByGuid = new ReadOnlyDictionary<int, Item>(itemsByGuid);
        Groups = new ReadOnlyDictionary<string, IReadOnlyList<Item>>(groupViews);
        Items = items.AsReadOnly();
    }

    #endregion

    #region 注册与注销

    /// <summary>相同实例重复注册不改变身份；不同实例占用同一 GUID 必须明确失败。</summary>
    public bool Register(Item item, Func<int> generateGuid)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        ItemData data = item.itemData ?? throw new InvalidOperationException("运行时 Item 缺少数据。");
        if (string.IsNullOrWhiteSpace(data.IDName))
            throw new InvalidOperationException("运行时 Item 的定义 ID 不能为空。");

        if (registrations.TryGetValue(item, out Registration existing))
        {
            if (existing.Guid != data.Guid || !string.Equals(existing.GroupId, data.IDName, StringComparison.Ordinal))
                throw new InvalidOperationException($"已注册 Item 的身份不能原位修改：{existing.GroupId}/{existing.Guid} → {data.IDName}/{data.Guid}。");
            return false;
        }

        int guid = data.Guid;
        if (guid == 0)
        {
            if (generateGuid == null) throw new ArgumentNullException(nameof(generateGuid));
            guid = generateGuid();
            if (guid == 0)
                throw new InvalidOperationException("运行时 Item 的 GUID 生成器返回了保留值 0。");
        }

        if (itemsByGuid.TryGetValue(guid, out Item conflicting))
            throw new InvalidOperationException($"Item GUID 冲突 {guid}：{conflicting?.itemData?.IDName} 与 {data.IDName}，请修复生成或所有权交接入口。");

        if (registrationSequence == ulong.MaxValue)
            throw new InvalidOperationException("Item 注册版本已耗尽。");
        ulong token = ++registrationSequence;

        if (!groups.TryGetValue(data.IDName, out ItemGroup group))
        {
            group = new ItemGroup();
            groups.Add(data.IDName, group);
            groupViews.Add(data.IDName, group.View);
        }

        Registration registration = new()
        {
            Guid = guid,
            GroupId = data.IDName,
            Group = group,
            ItemIndex = items.Count,
            GroupIndex = group.Items.Count,
            Token = token
        };
        data.Guid = guid;
        itemsByGuid.Add(guid, item);
        registrations.Add(item, registration);
        items.Add(item);
        group.Items.Add(item);
        return true;
    }

    public bool Contains(Item item) => !ReferenceEquals(item, null) && registrations.ContainsKey(item);

    public ulong GetRegistrationToken(Item item) => registrations[item].Token;

    public bool IsRegistrationCurrent(Item item, ulong token) =>
        !ReferenceEquals(item, null) && registrations.TryGetValue(item, out Registration registration) &&
        registration.Token == token;

    public bool TryGetRegisteredGuid(Item item, out int guid)
    {
        guid = 0;
        if (ReferenceEquals(item, null) || !registrations.TryGetValue(item, out Registration registration))
            return false;
        guid = registration.Guid;
        return true;
    }

    public bool TryGetRegisteredIdentity(Item item, out int guid, out string definitionId)
    {
        guid = 0;
        definitionId = null;
        if (ReferenceEquals(item, null) || !registrations.TryGetValue(item, out Registration registration))
            return false;
        guid = registration.Guid;
        definitionId = registration.GroupId;
        return true;
    }

    public void ValidateIdentityAvailable(ItemData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.IDName))
            throw new ArgumentException("ItemData.IDName 不能为空。", nameof(data));
        if (data.Guid != 0 && itemsByGuid.ContainsKey(data.Guid))
            throw new InvalidOperationException($"Item GUID {data.Guid} 已被运行时实例占用，不能再次生成 {data.IDName}。");
    }

    public bool Remove(Item item)
    {
        if (ReferenceEquals(item, null) || !registrations.Remove(item, out Registration registration))
            return false;

        // 注销使用登记时的身份，数据被误改或 Unity 已销毁时也不会遗留旧索引。
        itemsByGuid.Remove(registration.Guid);
        int lastItemIndex = items.Count - 1;
        if (registration.ItemIndex != lastItemIndex)
        {
            Item moved = items[lastItemIndex];
            items[registration.ItemIndex] = moved;
            registrations[moved].ItemIndex = registration.ItemIndex;
        }
        items.RemoveAt(lastItemIndex);

        List<Item> groupItems = registration.Group.Items;
        int lastGroupIndex = groupItems.Count - 1;
        if (registration.GroupIndex != lastGroupIndex)
        {
            Item moved = groupItems[lastGroupIndex];
            groupItems[registration.GroupIndex] = moved;
            registrations[moved].GroupIndex = registration.GroupIndex;
        }
        groupItems.RemoveAt(lastGroupIndex);
        if (groupItems.Count == 0)
        {
            groups.Remove(registration.GroupId);
            groupViews.Remove(registration.GroupId);
        }
        return true;
    }

    #endregion

    #region 失效清理

    public void CleanupNullItems()
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i] == null)
                Remove(items[i]);
    }

    #endregion
}

/// <summary>使用托管引用区分池化实例，不受 Unity 已销毁对象的相等运算影响。</summary>
internal sealed class ItemReferenceComparer : IEqualityComparer<Item>
{
    public static readonly ItemReferenceComparer Instance = new();

    private ItemReferenceComparer() { }

    public bool Equals(Item x, Item y) => ReferenceEquals(x, y);
    public int GetHashCode(Item item) => RuntimeHelpers.GetHashCode(item);
}

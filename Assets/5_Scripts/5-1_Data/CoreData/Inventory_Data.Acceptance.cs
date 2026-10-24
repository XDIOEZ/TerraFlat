using System;
using FastCloner.Code;
using MemoryPack;
using Newtonsoft.Json;

public partial class Inventory_Data
{
    #region 库存接收策略
    [NonSerialized, MemoryPackIgnore, FastClonerIgnore, JsonIgnore]
    private Func<ItemData, ItemSlot, bool> itemAcceptance;

    /// <summary>容器在运行时装配接收能力，权威转移和界面预检使用同一规则。</summary>
    public void SetItemAcceptancePolicy(Func<ItemData, ItemSlot, bool> policy) => itemAcceptance = policy;

    public bool CanAcceptItem(ItemData incoming, ItemSlot target = null)
    {
        if (incoming == null) return true;
        if (itemAcceptance?.Invoke(incoming, target) == false) return false;
        return target?.CanAcceptTags == null || target.CanAcceptTags.Count == 0 ||
            incoming.Tags != null && incoming.Tags.ContainsAnyTag(target.CanAcceptTags);
    }
    #endregion
}

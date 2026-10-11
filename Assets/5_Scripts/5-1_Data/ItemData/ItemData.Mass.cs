using System;
using FastCloner.Code;
using MemoryPack;
using Newtonsoft.Json;

public abstract partial class ItemData
{
    #region 实际质量版本
    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] private ItemStack observedMassStack;
    [NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] private ulong massRevision;
    [MemoryPackIgnore, JsonIgnore] public ulong MassRevision => massRevision;
    [field: NonSerialized, MemoryPackIgnore, JsonIgnore, FastClonerIgnore] public event Action<ItemData> MassChanged;

    // 载荷来源通过这个边界通知真实质量变化，不借用存档或界面刷新事件。
    public void NotifyMassChanged()
    {
        unchecked { massRevision++; }
        MassChanged?.Invoke(this);
    }
    private void BindMassStack()
    {
        if (ReferenceEquals(observedMassStack, stack)) return;
        UnbindMassStack(); observedMassStack = stack;
        if (observedMassStack != null) observedMassStack.MassChanged += HandleStackMassChanged;
    }
    private void UnbindMassStack()
    {
        if (observedMassStack != null) observedMassStack.MassChanged -= HandleStackMassChanged;
        observedMassStack = null;
    }
    private void HandleStackMassChanged(ItemStack _) => NotifyMassChanged();
    #endregion
}

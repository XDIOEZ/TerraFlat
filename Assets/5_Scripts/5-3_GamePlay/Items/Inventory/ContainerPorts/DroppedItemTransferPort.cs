using System;
using FlatWorld.Networking;

/// <summary>完整世界实物在两端成功前只暂扣数量，最终提交后才销毁空载体。</summary>
public sealed class DroppedItemTransferPort : ContainerPortBase, IItemTransferPort
{
    #region 世界物品来源
    private readonly Item source;
    public DroppedItemTransferPort(Item source)
        : base($"item:{source.itemData.Guid}", "dropped-output", new ContainerPortConfiguration
        { Id = "dropped-output", Type = "core:dropped_item", Direction = ContainerPortDirection.Output, Access = ContainerAccessKind.Machine, Reach = 2f }, Guard(source), source)
    { this.source = source; }
    private static Func<bool> Guard(Item item)
    { uint generation = item.RuntimeGeneration; return () => item != null && !item.DestructionHandled && item.RuntimeGeneration == generation && !item.InHand && item.Owner == null && item.itemData?.Stack?.CanBePickedUp == true; }
    public override object StorageIdentity => source.itemData;
    public override long StateVersion => source.itemData.Stack.Amount.GetHashCode();
    public ItemData PeekItem(string itemId = null, string tag = null)
        => IsValid && source.itemData.Stack.Amount >= 1f && (itemId == null || source.itemData.IDName == itemId) &&
            (tag == null || source.itemData.Tags.Contains(tag)) ? source.itemData : null;
    public int GetReceivableItems(ItemData item, int requested) => 0;
    public bool InsertItems(ItemData item, int amount) => false;
    public bool ExtractItems(ItemData expected, int amount, out ItemData extracted)
    {
        extracted = null;
        if (!IsValid || !ReferenceEquals(expected, source.itemData) || amount <= 0 || source.itemData.Stack.Amount < amount) return false;
        extracted = ItemInstanceDataFactory.CloneRuntime(source.itemData); extracted.Stack.Amount = amount;
        if (source.itemData.Stack.Amount > amount) { int id; do id = Guid.NewGuid().GetHashCode(); while (id == 0 || id == extracted.Guid); extracted.Guid = id; }
        source.itemData.Stack.Amount -= amount; return true;
    }
    public override object CaptureState() => source.itemData.Stack.Amount;
    public override void RestoreState(object snapshot) { source.itemData.Stack.Amount = (float)snapshot; source.OnUIRefresh?.Invoke(); }
    public override void PublishState()
    { if (source.itemData.Stack.Amount <= 0f) source.DestroySelf(); else { source.OnUIRefresh?.Invoke(); ItemNetworkStateSerialization.NotifyRuntimeStateChanged(source); } }
    #endregion
}

/// <summary>
/// 允许物品模块显式声明可使用临时实例处理库存槽位中的动作。
/// 未实现此契约的物品必须先进入快捷栏，以真实手持实例执行 Act。
/// </summary>
public interface IInventoryContextUseHandler
{
    /// <summary>把本次使用绑定到真实库存槽位，确保消耗和状态写回原数据。</summary>
    void BindInventoryUseContext(Inventory_Data inventoryData, ItemSlot slot, int slotIndex);
}

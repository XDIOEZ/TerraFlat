using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;

/// <summary>
/// 独立保存玩家创造背包的容量豁免状态；库存内容仍由 Mod_Inventory 持久化。
/// 玩家主背包本身默认就是动态扩容，创造模式只额外解除重量与体积上限。
/// </summary>
public static class CreativeInventoryState
{
    #region 持久化状态

    private const string NamespaceKey = "flatworld.creativeInventory";
    // 历史字段名继续沿用，当前语义只表示创造模式容量豁免，避免无意义改写已有玩家特殊数据。
    private const string CreativeCapacityKey = "unlimitedSlots";

    /// <summary>管理员启用创造背包时立即保存状态并解除重量/体积上限。</summary>
    public static void Enable(Player player, Inventory inventory)
    {
        JObject state = ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey);
        state[CreativeCapacityKey] = true;
        ItemSpecialDataJsonStore.WriteNamespace(player.Data, NamespaceKey, state);
        inventory.Data.SetUnlimitedCarryCapacity(true);
    }

    /// <summary>库存加载后只恢复创造模式容量豁免；主背包动态扩容由普通背包策略统一开启。</summary>
    public static void Restore(Inventory inventory)
    {
        bool enabled = inventory.item is Player player && inventory.Data.Name == ModText.Bag &&
                       ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey)
                           .Value<bool?>(CreativeCapacityKey) == true;
        inventory.Data.SetUnlimitedCarryCapacity(enabled);
    }

    #endregion
}

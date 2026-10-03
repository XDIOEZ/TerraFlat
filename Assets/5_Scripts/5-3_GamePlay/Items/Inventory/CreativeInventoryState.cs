using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;

/// <summary>
/// 独立保存玩家创造背包的容量豁免状态；库存内容仍由 Mod_Inventory 持久化。
/// 生存背包固定 20 格，只有创造背包同时开启动态槽位和携带容量豁免。
/// </summary>
public static class CreativeInventoryState
{
    #region 持久化状态

    private const string NamespaceKey = "flatworld.creativeInventory";
    // 创造状态统一控制动态槽位及重量、体积豁免。
    private const string CreativeCapacityKey = "unlimitedSlots";

    /// <summary>管理员启用创造背包时立即保存状态，并开启动态槽位及容量豁免。</summary>
    public static void Enable(Player player, Inventory inventory)
    {
        JObject state = ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey);
        state[CreativeCapacityKey] = true;
        ItemSpecialDataJsonStore.WriteNamespace(player.Data, NamespaceKey, state);
        inventory.Data.SetUnlimitedCarryCapacity(true);
        inventory.Data.SetUnlimitedSlots(true);
    }

    /// <summary>库存加载后恢复创造状态，主背包初始化据此选择固定或动态格数。</summary>
    public static void Restore(Inventory inventory)
    {
        bool enabled = inventory.item is Player player && inventory.Data.Name == ModText.Bag &&
                       ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey)
                           .Value<bool?>(CreativeCapacityKey) == true;
        inventory.Data.SetUnlimitedCarryCapacity(enabled);
    }

    #endregion
}

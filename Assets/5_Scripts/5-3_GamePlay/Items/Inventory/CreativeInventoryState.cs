using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;

/// <summary>
/// 独立保存玩家创造背包的无限格数状态；库存内容仍由 Mod_Inventory 持久化。
/// 仅玩家主背包应用此策略，快捷栏、装备容器与鼠标携带槽保持各自容量。
/// </summary>
public static class CreativeInventoryState
{
    #region 持久化状态

    private const string NamespaceKey = "flatworld.creativeInventory";
    private const string UnlimitedSlotsKey = "unlimitedSlots";

    /// <summary>管理员启用创造背包时立即保存状态并预留空槽。</summary>
    public static void Enable(Player player, Inventory inventory)
    {
        JObject state = ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey);
        state[UnlimitedSlotsKey] = true;
        ItemSpecialDataJsonStore.WriteNamespace(player.Data, NamespaceKey, state);
        inventory.Data.SetUnlimitedSlots(true);
    }

    /// <summary>库存加载后按所属玩家恢复容量策略，避免对象复用残留上一位玩家的状态。</summary>
    public static void Restore(Inventory inventory)
    {
        bool enabled = inventory.item is Player player && inventory.Data.Name == ModText.Bag &&
                       ItemSpecialDataJsonStore.ReadNamespace(player.Data, NamespaceKey)
                           .Value<bool?>(UnlimitedSlotsKey) == true;
        inventory.Data.SetUnlimitedSlots(enabled);
    }

    #endregion
}

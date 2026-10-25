using System.Collections.Generic;
using InputSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 光标物品快速拿取规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterWorldItemQuickPickRules()
        => Register("player", "world_item_quick_pick", CaptureWorldItemQuickPickRules);

    private static JObject CaptureWorldItemQuickPickRules(JObject args)
    {
        string mode = Text(args, "mode", "empty");
        ItemData incoming = new Data_GeneralItem
        {
            IDName = "probe:wanted", Guid = 601,
            Stack = new ItemStack { Amount = 3f, Weight = 1f, Volume = 1f, Stackable = true },
            Durability = 13f, ItemSpecialData = "preserved-state"
        };
        ItemData previous = new Data_GeneralItem
        {
            IDName = "probe:previous", Guid = 602,
            Stack = new ItemStack { Amount = 1f, Weight = 1f, Volume = 1f, Stackable = false }
        };
        var bag = new Inventory { Data = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = incoming } }, "quick-pick-bag") };
        var hotbar = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = previous }, new(1), new(2) }, "quick-pick-hotbar") };
        var target = new WorldItemQuickPickTarget("probe:wanted");
        if (mode == "full" || mode == "return_blocked")
            for (int i = 1; i < 3; i++) hotbar.Data.itemSlots[i].itemData = new Data_GeneralItem
            { IDName = "probe:filler" + i, Guid = 610 + i, Stack = new ItemStack { Amount = 1f, Weight = 1f, Volume = 1f } };
        if (mode == "existing" || mode == "current" || mode == "existing_dragged")
            hotbar.Data.itemSlots[mode == "current" ? 0 : 2].itemData = incoming;
        if (mode == "partial") hotbar.Data.itemSlots[1].SlotMaxVolume = 1;
        if (mode == "missing") target = new WorldItemQuickPickTarget("probe:missing");
        if (mode == "invalid") target = default;
        if (mode == "locked") bag.RuntimeAccessGuard = () => false;
        if (mode == "return_blocked") bag.Data.itemSlots[0].CanAcceptTags.Add("probe:unacceptable");
        if (mode == "building" || mode == "tile")
        {
            incoming.IDName = "probe:carrier";
            var module = new Ex_ModData { ID = ModText.Building, Name = ModText.Building };
            module.WriteData(new Mod_Building.Building_Data
            {
                Version = Mod_Building.CurrentBuildingDataVersion, Role = BuildingRole.Summoner,
                BuildingPrefabId = "probe:building", SummonerPrefabId = "probe:carrier", TileBlockId = "probe:tile"
            });
            incoming.ModuleDataDic = new ModuleDataCollection { [ModText.Building] = module };
            target = mode == "tile" ? new WorldItemQuickPickTarget(null, "probe:tile") : new WorldItemQuickPickTarget("probe:building");
        }
        InventoryDragTransaction drag = mode == "dragged" ? bag.OnMouseDragBegin(0) :
            mode == "existing_dragged" ? hotbar.OnMouseDragBegin(2) : null;
        if (mode == "existing_dragged") bag.RuntimeAccessGuard = () => false;
        try
        {
            // 夹具只准备独立库存，搬运、拒绝和交换均调用正式快速拿取服务。
            bool accepted = WorldItemQuickPickService.TryMoveMatchingBagItemToHotbar(target, bag, hotbar, 0, 3,
                out int selected, out int expectedGuid);
            using var actions = new PlayerInputActions();
            using var bindings = new InputBindingService(actions.asset, new InputSettingsMemoryStore());
            var action = actions.asset.FindAction("Win10/PickWorldItem", true);
            bool rebindable = false;
            foreach (InputBindingEntry entry in bindings.Entries)
                if (entry.Action == action) rebindable = true;
            return new JObject
            {
                ["accepted"] = accepted, ["selected"] = selected, ["expectedGuid"] = expectedGuid,
                ["bagIncoming"] = ReferenceEquals(bag.Data.itemSlots[0].itemData, incoming),
                ["bagPrevious"] = ReferenceEquals(bag.Data.itemSlots[0].itemData, previous),
                ["currentPrevious"] = ReferenceEquals(hotbar.Data.itemSlots[0].itemData, previous),
                ["destinationIncoming"] = selected >= 0 && ReferenceEquals(hotbar.Data.itemSlots[selected].itemData, incoming),
                ["destinationAmount"] = selected >= 0 ? hotbar.Data.itemSlots[selected].itemData?.Stack?.Amount ?? 0f : 0f,
                ["destinationSpecial"] = selected >= 0 ? hotbar.Data.itemSlots[selected].itemData?.ItemSpecialData : null,
                ["amount"] = incoming.Stack.Amount, ["durability"] = incoming.Durability,
                ["special"] = incoming.ItemSpecialData, ["binding"] = action.bindings[0].path,
                ["rebindable"] = rebindable, ["dragStarted"] = drag != null
            };
        }
        finally { drag?.Complete(false); }
    }
    #endregion
}

using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

public static partial class GameplayTestRuntimeProbe
{
    #region 手持装备组合键真实输入采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterQuickEquipInputRuntime() => RegisterQuickEquipInput();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterQuickEquipInputEditor() => RegisterQuickEquipInput();
#endif
    private static void RegisterQuickEquipInput()
        => Register("inventory", "held_equipment_shortcut", CaptureHeldEquipmentShortcut);

    private static JObject CaptureHeldEquipmentShortcut(Player player, JObject args)
    {
        string mode = GameplayTestSystemProbe.Text(args, "mode", "left_shift");
        Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        Mod_Equipment equipment = player.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        Mod_GameController controller = player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (hotbar?.Data?.itemSlots == null || equipment?.EquipmentInventory == null ||
            controller?._inputActions == null || controller.IsGameplayInputLocked)
            throw new InvalidOperationException("手持装备回归需要已加载且未被面板锁定的隔离玩家。");

        Inventory_Equipment gear = equipment.EquipmentInventory;
        int sourceIndex = hotbar.CurrentIndex;
        int eyes = gear.GetSlotIndex(EquipmentSlotCatalog.Eyes);
        int head = gear.GetSlotIndex(EquipmentSlotCatalog.Head);
        hotbar.CurentSelectItem?.ModuleSave();
        ItemData originalSource = hotbar.Data.itemSlots[sourceIndex].itemData;
        ItemData originalEyes = gear.Data.itemSlots[eyes].itemData;
        ItemData originalHead = gear.Data.itemSlots[head].itemData;
        Mod_Mover mover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        bool originalRun = mover?.RunPreference ?? false;
        Keyboard previousKeyboard = Keyboard.current;
        Mouse previousMouse = Mouse.current;
        Keyboard keyboard = null;
        Mouse mouse = null;
        Item held = null;
        var lockOwner = new object();
        int acts = 0, releases = 0;
        void Act() => acts++;
        void Release() => releases++;
        var result = new JObject();
        try
        {
            ItemData incoming = CreateDragItem(mode == "plain_item" ? "Twine" : "Engineering_Goggles");
            ItemData previous = mode == "empty" ? null : CreateDragItem("Engineering_Goggles");
            ItemData helmet = CreateDragItem("Spacesuit_Head");
            if (mode == "blocked")
                helmet.Tags.Add(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Eyes);
            gear.Data.SetOne_ItemData(head, helmet);
            gear.Data.SetOne_ItemData(eyes, previous);
            hotbar.Data.SetOne_ItemData(sourceIndex, incoming);
            if (!hotbar.TrySelectSlot(sourceIndex))
                throw new InvalidOperationException("手持装备回归无法同步当前真实快捷栏槽。");
            held = hotbar.CurentSelectItem;
            if (held == null || !ReferenceEquals(held.itemData, incoming))
                throw new InvalidOperationException("手持装备回归未获得本轮物品的正式手持实例。");
            held.OnAct += Act;
            controller.RightClickUp += Release;
            keyboard = UnityInputSystem.AddDevice<Keyboard>();
            mouse = UnityInputSystem.AddDevice<Mouse>();
            UnityInputSystem.AddDeviceUsage(keyboard, Mod_GameController.ExternalGameplayInputDeviceUsage);
            UnityInputSystem.AddDeviceUsage(mouse, Mod_GameController.ExternalGameplayInputDeviceUsage);
            Vector2 pointer = new(Screen.width * .5f, Screen.height * .5f);
            keyboard.MakeCurrent();
            UnityInputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            UnityInputSystem.Update();
            if (controller.IsPointerOverUI())
                throw new InvalidOperationException("手持装备回归的中央指针被现有 UI 遮挡。");
            if (mode == "locked") controller.AcquireGameplayInputLock(lockOwner);
            if (mode != "normal")
                UnityInputSystem.QueueStateEvent(keyboard,
                    new KeyboardState(mode == "right_shift" ? UnityEngine.InputSystem.Key.RightShift : UnityEngine.InputSystem.Key.LeftShift));
            UnityInputSystem.Update();
            UnityInputSystem.QueueStateEvent(mouse,
                new MouseState { position = pointer }.WithButton(MouseButton.Right));
            UnityInputSystem.Update();
            ItemData sourceAfterPress = hotbar.Data.itemSlots[sourceIndex].itemData;
            ItemData eyesAfterPress = gear.Data.itemSlots[eyes].itemData;
            result["sourceIsPrevious"] = ReferenceEquals(sourceAfterPress, previous);
            result["eyesIsIncoming"] = ReferenceEquals(eyesAfterPress, incoming);
            result["unchanged"] = ReferenceEquals(sourceAfterPress, incoming) && ReferenceEquals(eyesAfterPress, previous);
            result["heldAfterPress"] = controller.IsRightClickHeld;
            result["requestLeaked"] = controller.IsShiftRightClickRequested;
            UnityInputSystem.Update();
            UnityInputSystem.QueueStateEvent(keyboard, default(KeyboardState));
            UnityInputSystem.Update();
            UnityInputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            UnityInputSystem.Update();
            result["stableUntilRelease"] = ReferenceEquals(sourceAfterPress, hotbar.Data.itemSlots[sourceIndex].itemData) &&
                ReferenceEquals(eyesAfterPress, gear.Data.itemSlots[eyes].itemData);
            result["helmetUnchanged"] = ReferenceEquals(gear.Data.itemSlots[head].itemData, helmet);
            result["acts"] = acts;
            result["releases"] = releases;
            result["heldAfterRelease"] = controller.IsRightClickHeld;
        }
        finally
        {
            controller.ReleaseGameplayInputLock(lockOwner);
            controller.RightClickUp -= Release;
            if (held != null) held.OnAct -= Act;
            if (mouse != null) UnityInputSystem.RemoveDevice(mouse);
            if (keyboard != null) UnityInputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            mover?.SetRunState(originalRun);
            // 同步归还原引用和当前手持实例，不让输入夹具污染后续 Lua 的装备与快捷栏。
            gear.Data.SetOne_ItemData(head, originalHead);
            gear.Data.SetOne_ItemData(eyes, originalEyes);
            hotbar.Data.SetOne_ItemData(sourceIndex, originalSource);
            hotbar.TrySelectSlot(sourceIndex);
        }
        result["restored"] = ReferenceEquals(hotbar.Data.itemSlots[sourceIndex].itemData, originalSource) &&
            ReferenceEquals(gear.Data.itemSlots[head].itemData, originalHead) &&
            ReferenceEquals(gear.Data.itemSlots[eyes].itemData, originalEyes);
        return result;
    }

    #endregion
}

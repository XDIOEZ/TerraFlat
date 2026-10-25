using UnityEngine;

public partial class Mod_HotBar
{
    #region 光标物品快速拿取
    private int pendingWorldItemQuickPickIndex = -1;
    private WorldItemQuickPickTarget pendingWorldItemQuickPickTarget;

    public bool TryQuickPickWorldItemAtPointer()
    {
        if (runtimeUnloading || IsGameplayInputLocked() || IsPointerOverUI() ||
            ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction() ||
            _inputController == null || !_inputController.isActiveAndEnabled ||
            !_inputController.TryGetMouseWorldPosition(out Vector3 pointer)) return false;
        Mod_InteractSender sender = item?.GetComponentInChildren<Mod_InteractSender>(true);
        WorldItemQuickPickTarget target = default;
        bool hasPointerTarget = sender != null && sender.TryGetWorldItemQuickPickTargetAtPointer(pointer, out target);
        if (!hasPointerTarget && !WorldItemQuickPickService.TryResolveWorldCellTarget(pointer, item, out target)) return false;
        Inventory bag = item?.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.inventory;
        // 交换前保存当前手持物的即时模块状态，正式库存事务负责容量和双向接收校验。
        CurentSelectItem?.ModuleSave();
        if (!WorldItemQuickPickService.TryMoveMatchingBagItemToHotbar(target, bag, RuntimeInventory,
                CurrentIndex, HotBarMaxVolume, out int index, out _)) return false;
        pendingWorldItemQuickPickIndex = index;
        pendingWorldItemQuickPickTarget = target;
        CompletePendingWorldItemQuickPickSelection();
        return true;
    }

    private void CompletePendingWorldItemQuickPickSelection()
    {
        if (pendingWorldItemQuickPickIndex < 0 || ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction()) return;
        int index = pendingWorldItemQuickPickIndex;
        pendingWorldItemQuickPickIndex = -1;
        if (Data?.itemSlots == null || index >= Data.itemSlots.Count ||
            !WorldItemQuickPickService.MatchesWorldTarget(Data.itemSlots[index]?.itemData, pendingWorldItemQuickPickTarget) ||
            IsGameplayInputLocked()) return;
        SwitchItem(index);
    }
    #endregion
}

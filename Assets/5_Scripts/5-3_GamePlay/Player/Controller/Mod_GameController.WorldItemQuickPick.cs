using UnityEngine.InputSystem;

public partial class Mod_GameController
{
    #region 光标物品快速拿取输入
    private void HandleWorldItemQuickPickInput(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        if (!context.performed || !IsGameplayInputAllowed(context) || IsGameplayInputLocked ||
            IsPointerOverUI() || EventSystemGuard.IsTextInputFocused || EventSystemGuard.IsGamepadUISelectionActive)
            return;
        // 快速拿取只调用玩家库存，不触发世界交互或凭空生成物品。
        item?.itemMods?.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.TryQuickPickWorldItemAtPointer();
    }
    #endregion
}

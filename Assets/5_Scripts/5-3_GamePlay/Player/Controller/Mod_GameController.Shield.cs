using UnityEngine.InputSystem;

public partial class Mod_GameController
{
    #region 副手弹反输入
    private void HandleParryInput(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        if (!context.performed || !IsGameplayInputAllowed(context) || IsGameplayInputLocked ||
            EventSystemGuard.IsTextInputFocused || EventSystemGuard.IsGamepadUISelectionActive) return;
        ParryRequested?.Invoke();
    }
    #endregion
}

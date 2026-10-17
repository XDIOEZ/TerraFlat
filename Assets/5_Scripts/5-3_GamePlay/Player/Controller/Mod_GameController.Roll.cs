using UnityEngine.InputSystem;

public partial class Mod_GameController
{
    #region 翻滚输入
    private void HandleRollInput(InputAction.CallbackContext context)
    {
        UpdateCurrentInputDevice(context);
        if (!context.performed || !IsGameplayInputAllowed(context) || IsGameplayInputLocked ||
            EventSystemGuard.IsTextInputFocused || EventSystemGuard.IsGamepadUISelectionActive) return;
        RollRequested?.Invoke();
    }
    #endregion
}

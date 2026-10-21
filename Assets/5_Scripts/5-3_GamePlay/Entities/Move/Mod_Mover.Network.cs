using UnityEngine;

public partial class Mod_Mover
{
    #region 网络移动与生存消耗分离
    private bool networkAuthorityMovement, networkMoving, networkRunning;
    public void EnableNetworkAuthorityMovement() => networkAuthorityMovement = true;
    public void ApplyNetworkMovementActivity(bool moving, bool running)
    { networkMoving = moving; networkRunning = running; }
    private void TickNetworkMovementCosts(float seconds)
    {
        SetRunState(networkRunning);
        if (networkMoving && stamina != null)
            stamina.ConsumeStaminaPerSecond(IsRunning ? StaminaConsumptionSources.MovementRun : StaminaConsumptionSources.MovementWalk,
                IsRunning ? RunStaminaConsume : MoveStaminaConsume, seconds);
        hungerActionInstance?.SetMovementState(networkMoving, IsRunning);
    }
    #endregion
}

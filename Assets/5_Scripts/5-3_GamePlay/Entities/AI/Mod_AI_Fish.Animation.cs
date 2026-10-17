public sealed partial class Mod_AI_Fish : IActorAnimationMotionSource
{
    #region 单图游动与离水表现
    public bool TryGetAnimationMotion(out ActorAnimationMotion motion)
    {
        motion = default;
        if (!IsAlive || presentation == null) return false;
        // 钓线与捕食者位移不算主动游动，已有离水高度与翻摆统一交给动画层组合。
        motion.IsMoving = presentation.IsSwimming && !IsHooked && predatorCarrier == null;
        motion.IsRunning = motion.IsMoving && (machine.CurrentState == Behaviour.Flee || temperatureSafetyRetreat);
        motion.WorldOffset = presentation.VisualWorldOffset;
        motion.RollDegrees = presentation.VisualRollDegrees;
        return true;
    }
    #endregion
}

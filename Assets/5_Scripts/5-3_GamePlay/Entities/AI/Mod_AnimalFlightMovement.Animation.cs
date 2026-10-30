using UnityEngine;

public sealed partial class Mod_AnimalFlightMovement : IActorAnimationMotionSource
{
    #region 单图飞行动作
    private Mod_AnimatorController animationBody;
    private Vector2 animationFlightDisplacement;
    private float animationFlightSeconds;

    public static string ResolveAnimationStateName(BirdFlightPhase phase, bool isMoving)
    {
        return phase switch
        {
            BirdFlightPhase.Ground => isMoving ? "Walk" : "Ground",
            BirdFlightPhase.RunUp => "Walk",
            BirdFlightPhase.TakingOff => "TakingOff",
            BirdFlightPhase.Flying => "Flying",
            BirdFlightPhase.Landing => "Landing",
            _ => throw new System.ArgumentOutOfRangeException(nameof(phase), phase, "未知鸟类飞行阶段。")
        };
    }

    public bool TryGetAnimationMotion(out ActorAnimationMotion motion)
    {
        motion = default;
        if (!loaded || !IsAlive) return false;
        // 飞行只累计正式主动步进，悬停时的风推动不冒充游动或行走。
        bool flying = IsAirborne;
        motion.IsMoving = flying ? animationFlightDisplacement.sqrMagnitude > 0.000001f : mover.IsActuallyMoving;
        motion.IsRunning = motion.IsMoving && (flying
            ? animationFlightSeconds > 0f && animationFlightDisplacement.magnitude / animationFlightSeconds > flightSpeed * 0.75f
            : state.Phase == BirdFlightPhase.RunUp || mover.IsRunning);
        return true;
    }

    private void PlayPredationAnimation(Item target)
    {
        if (target != null)
            animationBody?.PlayAttack(WorldTopologyRuntime.ShortestDelta(item.transform.position, target.transform.position));
    }
    #endregion
}

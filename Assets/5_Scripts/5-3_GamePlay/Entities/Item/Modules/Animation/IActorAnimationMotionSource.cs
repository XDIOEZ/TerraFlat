using UnityEngine;

#region 生物动画表现信号
// 特殊移动只报告自己的主动状态和已有视觉位移，动画模块不识别具体物种。
public interface IActorAnimationMotionSource
{
    bool TryGetAnimationMotion(out ActorAnimationMotion motion);
}

public struct ActorAnimationMotion
{
    public bool IsMoving;
    public bool IsRunning;
    public Vector3 WorldOffset;
    public float RollDegrees;
}
#endregion

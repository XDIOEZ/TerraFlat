using Cinemachine;
using UnityEngine;

public sealed class CameraFollowRotation : CinemachineExtension
{
    #region 跟随目标朝向
    public Transform FollowTarget { get; set; }
    public bool FollowTargetRotation { get; set; } = true;
    private Quaternion followOrientation;
    private bool hasFollowOrientation;

    public override void PrePipelineMutateCameraStateCallback(
        CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
    {
        if (FollowTargetRotation && FollowTarget != null)
        {
            followOrientation = Quaternion.Euler(0f, 0f, FollowTarget.eulerAngles.z);
            hasFollowOrientation = true;
        }
        else if (!hasFollowOrientation)
        {
            followOrientation = state.RawOrientation;
            hasFollowOrientation = true;
        }

        // 在镜头管线内设置绝对朝向，避免主相机与虚拟相机共用 Transform 时重复叠加旋转。
        state.RawOrientation = followOrientation;
        state.Lens.Dutch = 0f;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
    }
    #endregion
}

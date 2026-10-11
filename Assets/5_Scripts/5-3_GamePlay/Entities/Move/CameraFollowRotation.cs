using Cinemachine;
using UnityEngine;

public sealed class CameraFollowRotation : CinemachineExtension
{
    #region 跟随目标朝向
    public Transform FollowTarget { get; set; }
    public bool FollowTargetRotation { get; set; } = true;

    public override void PrePipelineMutateCameraStateCallback(
        CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
    {
        // 固定镜头恢复朝上，不能继续保留此前跟随飞船留下的倾斜角度。
        state.RawOrientation = FollowTargetRotation && FollowTarget != null
            ? Quaternion.Euler(0f, 0f, FollowTarget.eulerAngles.z) : Quaternion.identity;
        state.Lens.Dutch = 0f;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
    }
    #endregion
}

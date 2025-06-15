using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMouse : Organ, IFunction_FaceToMouse
{
    public IRotationSpeed RoData;
    
    public IFocusPoint FocusPoint;

    public float RotationSpeeds
    {
        get
        {
            return RoData.RotationSpeed;
        }

        set
        {
            RoData.RotationSpeed  = value;
        }
    }

    public void Start()
    {
        FocusPoint = GetComponentInParent<IFocusPoint>();
        RoData = GetComponentInParent<IRotationSpeed>();
    }

    public void FaceToMouse(Vector3 targetPosition)
    {
        // 计算目标方向
        Vector2 direction = targetPosition - transform.parent.position;

        // 计算目标角度
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // 如果存在父级对象，并且Y轴旋转角度接近180度，修正目标角度
        Transform grandParent = transform.parent.parent;
        if (grandParent != null && Mathf.Abs(grandParent.localEulerAngles.y - 180f) < 90f)
        {
            targetAngle = 180f - targetAngle;
        }

        // 平滑插值当前角度和目标角度
        float currentAngle = transform.parent.localEulerAngles.z;
        float angleStep = RotationSpeeds * Time.deltaTime; // 使用角速度计算步长
        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, angleStep);

        // 应用平滑后的旋转角度
        transform.parent.localRotation = Quaternion.Euler(0, 0, smoothedAngle);
    }

    public override void StartWork()
    {
        FaceToMouse(FocusPoint.FocusPointPosition);
    }

    public override void UpdateWork()
    {
        FaceToMouse(FocusPoint.FocusPointPosition);
    }

    public override void StopWork()
    {
        // 停止工作时，保持当前角度不变
    }
}

public interface IFocusPoint
{
    Vector3 FocusPointPosition { get; set; }
}

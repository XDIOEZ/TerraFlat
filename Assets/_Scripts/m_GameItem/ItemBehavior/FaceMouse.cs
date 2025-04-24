using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceMouse : Organ, IFunction_FaceToMouse
{
public IRotationSpeed RoData;
    public Vector2 Direction;

    public float RotationSpeed
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
        if (RoData == null)
        {
            RoData = GetComponentInParent<IRotationSpeed>();
        }
    }

    /*
public void FaceToMouses(Vector3 mousePos)
{
// 确保鼠标位置在世界坐标中
mousePos.z = Camera.main.nearClipPlane;
Vector3 worldMousePos = Camera.main.ScreenToWorldPoint(mousePos);

// 计算目标方向
Vector3 direction = (worldMousePos - transform.position).normalized;

// 获取目标旋转
Quaternion targetRotation;
if (worldMousePos.x < transform.position.x)
{
// 鼠标在左侧，目标旋转面向左
targetRotation = Quaternion.Euler(0f, 180f, 0f);
}
else
{
// 鼠标在右侧，目标旋转面向右
targetRotation = Quaternion.Euler(0f, 0f, 0f);
}

// 使用 RotateTowards 平滑旋转
transform.parent.rotation = Quaternion.RotateTowards(
transform.parent.rotation,
targetRotation,
rotationSpeed * Time.deltaTime // 每帧旋转的角度
);

// 更新 Direction 表示当前是否面向目标方向
Direction = transform.parent.right;
}*/
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
        float angleStep = RotationSpeed * Time.deltaTime; // 使用角速度计算步长
        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, angleStep);

        // 应用平滑后的旋转角度
        transform.parent.localRotation = Quaternion.Euler(0, 0, smoothedAngle);

        // 更新 Direction 表示当前是否面向目标方向
        Direction = transform.parent.right;
    }
}

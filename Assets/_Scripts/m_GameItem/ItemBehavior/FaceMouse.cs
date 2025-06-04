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
        // ����Ŀ�귽��
        Vector2 direction = targetPosition - transform.parent.position;

        // ����Ŀ��Ƕ�
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // ������ڸ������󣬲���Y����ת�ǶȽӽ�180�ȣ�����Ŀ��Ƕ�
        Transform grandParent = transform.parent.parent;
        if (grandParent != null && Mathf.Abs(grandParent.localEulerAngles.y - 180f) < 90f)
        {
            targetAngle = 180f - targetAngle;
        }

        // ƽ����ֵ��ǰ�ǶȺ�Ŀ��Ƕ�
        float currentAngle = transform.parent.localEulerAngles.z;
        float angleStep = RotationSpeeds * Time.deltaTime; // ʹ�ý��ٶȼ��㲽��
        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, angleStep);

        // Ӧ��ƽ�������ת�Ƕ�
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
        // ֹͣ����ʱ�����ֵ�ǰ�ǶȲ���
    }
}

public interface IFocusPoint
{
    Vector3 FocusPointPosition { get; set; }
}

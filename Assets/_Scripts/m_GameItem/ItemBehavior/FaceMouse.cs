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
// ȷ�����λ��������������
mousePos.z = Camera.main.nearClipPlane;
Vector3 worldMousePos = Camera.main.ScreenToWorldPoint(mousePos);

// ����Ŀ�귽��
Vector3 direction = (worldMousePos - transform.position).normalized;

// ��ȡĿ����ת
Quaternion targetRotation;
if (worldMousePos.x < transform.position.x)
{
// �������࣬Ŀ����ת������
targetRotation = Quaternion.Euler(0f, 180f, 0f);
}
else
{
// ������Ҳ࣬Ŀ����ת������
targetRotation = Quaternion.Euler(0f, 0f, 0f);
}

// ʹ�� RotateTowards ƽ����ת
transform.parent.rotation = Quaternion.RotateTowards(
transform.parent.rotation,
targetRotation,
rotationSpeed * Time.deltaTime // ÿ֡��ת�ĽǶ�
);

// ���� Direction ��ʾ��ǰ�Ƿ�����Ŀ�귽��
Direction = transform.parent.right;
}*/
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
        float angleStep = RotationSpeed * Time.deltaTime; // ʹ�ý��ٶȼ��㲽��
        float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, angleStep);

        // Ӧ��ƽ�������ת�Ƕ�
        transform.parent.localRotation = Quaternion.Euler(0, 0, smoothedAngle);

        // ���� Direction ��ʾ��ǰ�Ƿ�����Ŀ�귽��
        Direction = transform.parent.right;
    }
}

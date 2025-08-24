using UnityEngine;

public class CharacterAnimationTest : MonoBehaviour
{
    [Header("��������")]
    public float angle = 10f;     // �����ת�Ƕȣ�����ҡ�ķ��ȣ�
    public float frequency = 2f;  // �ڶ�Ƶ�ʣ�ÿ�����ؼ��Σ�

    private void Update()
    {
        // ���Һ����� [-1,1] ֮��仯
        float rotationZ = Mathf.Sin(Time.time * frequency) * angle;

        // ��ֵ�� localRotation��ֻ�� Z ��ҡ�ڣ�2D ��ɫһ���� XY ƽ�棩
        transform.localRotation = Quaternion.Euler(0, 0, rotationZ);
    }
}

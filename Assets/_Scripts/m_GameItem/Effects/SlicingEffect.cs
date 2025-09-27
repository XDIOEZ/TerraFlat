using System.Collections;
using UnityEngine;

public class SlicingEffect : GameEffect
{
    [Header("��Ч����")]
    public int sampleFrames = 2; // ����֡��
    public float lifetime = 0.3f; // ��Ч��������
    
    private Transform weaponTransform; // �����任���
    private Vector2 startWeaponPosition; // ������ʼλ��
    private bool hasStarted = false;

    public override void Effect(Transform Sender, object args)
    {
        // ��¼������ʼλ�úͱ任���
        weaponTransform = Sender;
        startWeaponPosition = Sender.position;
        hasStarted = true;
        
        // ������Ч�������Э��
        StartCoroutine(CalculateDirectionAndRotate());
    }

    private IEnumerator CalculateDirectionAndRotate()
    {
        // �ȴ�ָ��֡���Ի�ø�׼ȷ�ķ���
        for (int i = 0; i < sampleFrames; i++)
            yield return null;

        // ����������������
        Vector2 endWeaponPosition = weaponTransform.position;
        Vector2 dir = (endWeaponPosition - startWeaponPosition).normalized;

        // �����������̫С����ʹ��Ĭ�Ϸ���
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;

        // ������ת�Ƕ�
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Vector3 euler = transform.rotation.eulerAngles;
        euler.z += angle;
        transform.rotation = Quaternion.Euler(euler);

        // ������Ч
        Destroy(gameObject, lifetime);
    }

    // Start is called before the first frame update
    void Start()
    {
        // ���û��ͨ��Effect������ʼ������ʹ��Ĭ������
        if (!hasStarted)
        {
            startWeaponPosition = transform.position;
            weaponTransform = transform;
            hasStarted = true;
            StartCoroutine(CalculateDirectionAndRotate());
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
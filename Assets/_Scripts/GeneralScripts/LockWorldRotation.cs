using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LockWorldRotation : MonoBehaviour
{
    private Quaternion initialRotation;

    void Start()
    {
        // ��¼��ʼ��������ת
        initialRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        // ÿ֡ǿ�ƻ�ԭΪ��ʼ������ת
        transform.rotation = initialRotation;
    }
}

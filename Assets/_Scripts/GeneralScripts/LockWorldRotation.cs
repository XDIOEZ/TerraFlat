using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LockWorldRotation : MonoBehaviour
{
    private Quaternion initialRotation;

    void Start()
    {
        // 记录初始的世界旋转
        initialRotation = transform.rotation;
    }

    void FixedUpdate()
    {
        // 每帧强制还原为初始世界旋转
        transform.rotation = initialRotation;
    }
}

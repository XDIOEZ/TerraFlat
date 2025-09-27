using System.Collections;
using UnityEngine;

public class SlicingEffect : GameEffect
{
    [Header("特效设置")]
    public int sampleFrames = 2; // 采样帧数
    public float lifetime = 0.3f; // 特效生命周期
    
    private Transform weaponTransform; // 武器变换组件
    private Vector2 startWeaponPosition; // 武器初始位置
    private bool hasStarted = false;

    public override void Effect(Transform Sender, object args)
    {
        // 记录武器初始位置和变换组件
        weaponTransform = Sender;
        startWeaponPosition = Sender.position;
        hasStarted = true;
        
        // 启动特效方向计算协程
        StartCoroutine(CalculateDirectionAndRotate());
    }

    private IEnumerator CalculateDirectionAndRotate()
    {
        // 等待指定帧数以获得更准确的方向
        for (int i = 0; i < sampleFrames; i++)
            yield return null;

        // 计算武器方向向量
        Vector2 endWeaponPosition = weaponTransform.position;
        Vector2 dir = (endWeaponPosition - startWeaponPosition).normalized;

        // 如果方向向量太小，则使用默认方向
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;

        // 计算旋转角度
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Vector3 euler = transform.rotation.eulerAngles;
        euler.z += angle;
        transform.rotation = Quaternion.Euler(euler);

        // 销毁特效
        Destroy(gameObject, lifetime);
    }

    // Start is called before the first frame update
    void Start()
    {
        // 如果没有通过Effect方法初始化，则使用默认设置
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
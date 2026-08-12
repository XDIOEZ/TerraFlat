using System.Collections;
using UnityEngine;

public class SlicingEffect : GameEffect
{
    [Header("特效参数")]
    public int sampleFrames = 2; // 采样帧数
    public float lifetime = 0.3f; // 特效持续时间
    
    private Transform weaponTransform; // 武器变换组件
    private Vector2 startWeaponPosition; // 武器初始位置
    private bool hasStarted = false;
    private bool returnScheduled;
    private float returnAtTime;
    private Animator effectAnimator;
    private Quaternion initialLocalRotation;

    #region Lifecycle

    /// <summary>缓存 Prefab 的初始朝向和动画组件，供对象池重复播放时恢复。</summary>
    private void Awake()
    {
        effectAnimator = GetComponent<Animator>();
        initialLocalRotation = transform.localRotation;
    }

    #endregion

    #region Pool Lifecycle

    public override void OnSpawnedFromPool()
    {
        StopAllCoroutines();
        weaponTransform = null;
        hasStarted = false;
        returnScheduled = false;
        returnAtTime = 0f;
        transform.localRotation = initialLocalRotation;

        // 回收对象重新激活后 Animator 仍可能停留在结束帧，强制回到动画首帧。
        if (effectAnimator != null)
        {
            effectAnimator.Rebind();
            effectAnimator.Update(0f);
        }
    }

    public override void OnReturnedToPool()
    {
        StopAllCoroutines();
        weaponTransform = null;
        hasStarted = false;
        returnScheduled = false;
        returnAtTime = 0f;
    }

    #endregion

    public override void Effect(Transform Sender, object args)
    {
        StopAllCoroutines();
        returnScheduled = false;

        if (Sender == null)
        {
            ReturnToPoolOrDestroy();
            return;
        }

        // 记录武器初始位置和变换组件
        weaponTransform = Sender;
        startWeaponPosition = Sender.position;
        hasStarted = true;
        
        // 启动特效持续时间协程
        StartCoroutine(CalculateDirectionAndRotate());
    }

    private IEnumerator CalculateDirectionAndRotate()
    {
        // 等待指定帧数以获取准确的方向
        for (int i = 0; i < sampleFrames; i++)
        {
            // 检查weaponTransform是否存在，如果不存在则销毁特效
            if (weaponTransform == null)
            {
                ReturnToPoolOrDestroy();
                yield break;
            }
            yield return null;
        }

        // 检查weaponTransform是否存在，如果不存在则销毁特效
        if (weaponTransform == null)
        {
            ReturnToPoolOrDestroy();
            yield break;
        }

        // 计算武器移动方向
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

        // 由 Update 统一调度回收，避免每次播放创建延迟销毁对象。
        returnAtTime = Time.time + Mathf.Max(0f, lifetime);
        returnScheduled = true;
    }

    // Start is called before the first frame update
    void Start()
    {
        // 如果没有通过Effect方法初始化，则使用默认参数
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
        // 检查weaponTransform是否存在，如果不存在则回收特效
        if (weaponTransform == null)
        {
            if (hasStarted)
                ReturnToPoolOrDestroy();
            return;
        }

        if (returnScheduled && Time.time >= returnAtTime)
        {
            ReturnToPoolOrDestroy();
        }
    }
}

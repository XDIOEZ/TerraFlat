
using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;

public class TurnBody : Organ, ITurnBody
{
    #region 公开字段（Inspector配置）
    [Header("转向控制")]
    [SerializeField]
    [Tooltip("旋转速度（值越大，旋转越快）")]
    [Range(1f, 10f)]
    private float rotationSpeed = 3f; // 默认旋转速度

    [SerializeField]
    [Tooltip("需要控制旋转的目标对象，默认为父对象")]
    private Transform controlledTransform; // 可在 Inspector 设置

    [SerializeField]
    [Tooltip("当前面向方向，默认右方")]
    private Vector2 direction = Vector2.right;

    [SerializeField]
    [Tooltip("是否正在转身（由脚本自动控制）")]
    [ReadOnly] // 确保编辑器中不可手动修改
    private bool isTurning = false;

    public IFocusPoint focusPoint;

    #endregion

    public void Start()
    {
        focusPoint = GetComponentInParent<IFocusPoint>();
        // 初始化 controlledTransform（默认为父对象）
        if (controlledTransform == null)
        {
            controlledTransform = transform.parent;
            if (controlledTransform == null)
            {
                Debug.LogError("ControlledTransform 未设置且父对象不存在！请手动配置目标对象。");
            }
        }
    }

    private Coroutine turnCoroutine; // 确保不会多次启动旋转

    public void TurnBodyToDirection(Vector2 targetDirection)
    {
        // 防止无效操作
        if (controlledTransform == null)
        {
            Debug.LogWarning("ControlledTransform 未设置，无法执行转向！");
            return;
        }

        if (isTurning) return; // 正在旋转时直接返回
        if (targetDirection == Vector2.zero) return; // 避免无效输入

        // 确定目标方向
        Vector2 desiredDirection = targetDirection.x > 0 ? Vector2.right : Vector2.left;
        if (direction == desiredDirection) return; // 方向未变，无需旋转

        // 停止未完成的协程
        if (turnCoroutine != null)
        {
            StopCoroutine(turnCoroutine);
        }

        // 启动旋转协程
        direction = desiredDirection;
        turnCoroutine = StartCoroutine(RotateToTarget(desiredDirection));
    }

    private IEnumerator RotateToTarget(Vector2 desiredDirection)
    {
        isTurning = true; // 标记为正在旋转

        float targetAngle = (desiredDirection == Vector2.right) ? 0f : 180f;
        float startAngle = controlledTransform.eulerAngles.y; // 使用自定义目标对象
        float elapsedTime = 0f;
        float duration = 1f / rotationSpeed; // 旋转时间 = 1秒 / 速度

        while (elapsedTime < duration)
        {
            // 线性插值旋转
            float newAngle = Mathf.LerpAngle(startAngle, targetAngle, elapsedTime / duration);
            controlledTransform.rotation = Quaternion.Euler(0f, newAngle, 0f);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // 确保最终角度精确
        controlledTransform.rotation = Quaternion.Euler(0f, targetAngle, 0f);
        isTurning = false; // 旋转结束
    }

    public override void StartWork()
    {
        Vector2 dir = (Vector2)focusPoint.FocusPointPosition - (Vector2)transform.position;
        TurnBodyToDirection(dir);
    }

    public override void UpdateWork()
    {
        Vector2 dir = (Vector2)focusPoint.FocusPointPosition - (Vector2)transform.position;
        TurnBodyToDirection(dir);
    }


    public override void StopWork()
    {
        throw new System.NotImplementedException();
    }
}
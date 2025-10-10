using System.Collections;
using UnityEngine;

public class Hand : MonoBehaviour
{
    public float MinRange = 0.3f;
    public float MoveSpeed = 10f;       // 移动速度
    public float RotationSpeed = 360f;  // 旋转速度
    public float AttackRange = 1f;      // 攻击范围
    public Transform Target;            // 目标位置
    public Transform BeUseItem;         // 使用物品的目标

    private bool handMove;
    private Vector3 relativePosition;

    void Start()
    {
        relativePosition = transform.localPosition;
    }

    void Update()
    {
        // 每帧进行旋转
        if (Vector3.Distance(Target.position, BeUseItem.position) > MinRange)
        {
            RotateTowardsTarget(BeUseItem);
        }

        // 检测鼠标右键点击来执行物品使用动画
        if (Input.GetMouseButtonDown(1) && !handMove)
        {
            HandleItemUsage();
        }
    }

    // 旋转手部朝向目标
    private void RotateTowardsTarget(Transform targetTransform)
    {
        Vector3 direction = Target.position - targetTransform.position;
        direction.z = 0; // 限制在XY平面内旋转

        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float angle = Mathf.LerpAngle(targetTransform.eulerAngles.z, targetAngle, RotationSpeed * Time.deltaTime);

        targetTransform.rotation = Quaternion.Euler(0, 0, angle);
    }

    // 处理物品使用动画
    private void HandleItemUsage()
    {
        PlayUseItemAnimation(AttackRange);
    }

    // 执行物品使用动画
    private void PlayUseItemAnimation(float attackRange)
    {
        Transform parent = transform.parent;
        if (parent == null)
        {
            Debug.LogWarning("Hand's parent is null. Cannot play use item animation.");
            return;
        }

        relativePosition = transform.localPosition;
        Vector3 targetLocalPosition = GetTargetLocalPosition(attackRange, parent);  // 获取目标位置

        float moveDuration = 1 / MoveSpeed;

        handMove = true;

        StartCoroutine(MoveHandToAndBackFromPosition(targetLocalPosition, moveDuration, parent));
    }

    // 获取物体到鼠标位置的方向，并计算目标本地位置
    private Vector3 GetTargetLocalPosition(float attackRange, Transform parent)
    {
        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPosition.z = 0;  // 确保 z 坐标是 0，以便在 2D 平面内进行计算

        Vector3 handWorldPosition = transform.position;
        Vector3 direction = mouseWorldPosition - handWorldPosition;
        direction.Normalize();
        Vector3 moveVector = direction * attackRange;

        Vector3 localMoveVector = parent.TransformVector(moveVector);
        return relativePosition + localMoveVector; // 返回目标本地位置
    }

    // 手部移动到目标位置并返回
    private IEnumerator MoveHandToAndBackFromPosition(Vector3 targetLocalPosition, float moveDuration, Transform parent)
    {
        float elapsedTime = 0;
        Vector3 startPosition = transform.localPosition;

        // 移动到目标位置
        while (elapsedTime < moveDuration)
        {
            // 每一帧都获取鼠标位置，并计算新的目标本地位置
            Vector3 updatedTargetLocalPosition = GetTargetLocalPosition(AttackRange, parent);

            // 使用更新后的目标本地位置进行插值
            transform.localPosition = Vector3.Lerp(startPosition, updatedTargetLocalPosition, elapsedTime / moveDuration);

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.1f); // 小等待

        // 返回起始位置
        elapsedTime = 0;
        while (elapsedTime < moveDuration)
        {
            transform.localPosition = Vector3.Lerp(targetLocalPosition, startPosition, elapsedTime / moveDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = startPosition;
        handMove = false;
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine.EventSystems;
using static AttackTrigger;
using System.Collections.Generic;

public partial class Mod_ColdWeapon : Module
{
    public GameObject AttackEffect;//攻击特效
    public Ex_ModData_MemoryPackable Data;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }

    [SerializeField] public ColdWeapon_SaveData weaponData = new ColdWeapon_SaveData();

    public GameValue_float Damage { get => weaponData.Damage; set => weaponData.Damage = value; }
    public float AttackSpeed { get => weaponData.AttackSpeed; set => weaponData.AttackSpeed = value; }
    public float MaxAttackDistance { get => weaponData.MaxAttackDistance; set => weaponData.MaxAttackDistance = value; }
    public float ReturnSpeed { get => weaponData.ReturnSpeed; set => weaponData.ReturnSpeed = value; }

    public FaceMouse faceMouse;
    public AttackState CurrentState = AttackState.Idle;
    public Vector2 StartPosition = Vector2.zero;
    public Transform MoveTargetTransform;
    [SerializeField] protected Collider2D damageCollider;

    private InputAction InputAction;
    private Vector2 returnTarget;

    [System.Serializable]
    [MemoryPackable]
    public partial class ColdWeapon_SaveData
    {
        [Header("武器属性设置")]
        public GameValue_float Damage = new GameValue_float(10f);
        public float AttackSpeed = 10f, MaxAttackDistance = 10f, ReturnSpeed = 10f;
    }

    public override void Awake()
    {
        if (_Data.ID == "") _Data.ID = "ColdWeapon";
    }

    public override void Load()
    {
        Data.ReadData(ref weaponData);

        if (item.Owner != null)
        {
            faceMouse = item.Owner.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
            var controller = item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();
            InputAction = controller._inputActions.Win10.LeftClick;
            InputAction.started += OnInputActionStarted;
            InputAction.canceled += OnInputActionCanceled;
        }
        else
        {
            faceMouse = item.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
        }

        MoveTargetTransform = (MoveTargetTransform == null && item.Owner != null)
            ? item.transform
            : transform.parent;

        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider == null)
                Debug.LogWarning("[Mod_ColdWeapon] 没有找到Collider2D，请在编辑器里赋值！");
        }
    }

    public override void Action(float deltaTime)
    {
        switch (CurrentState)
        {
            case AttackState.Attacking:
                PerformStab(AttackSpeed, MaxAttackDistance, deltaTime);
                break;

            case AttackState.Returning:
                UpdateReturning(deltaTime);
                break;
        }
    }

    #region 攻击与输入
    public void OnInputActionStarted(InputAction.CallbackContext context)
    {
        if (context.started)
            StartCoroutine(DelayedCheckUIThenAttack());
    }

    private IEnumerator DelayedCheckUIThenAttack()
    {
        yield return null; // 延迟到下一帧

        if (!IsPointerOverUI())
            StartAttack();
    }

    private void OnInputActionCanceled(InputAction.CallbackContext context) => StopAttack();

    [Button]
    public virtual void StartAttack()
    {
        if (CurrentState != AttackState.Idle) return;
        CurrentState = AttackState.Attacking;
        StartPosition = MoveTargetTransform.localPosition;
        damageCollider.enabled = true;
    }

    public virtual void StopAttack()
    {
        if (CurrentState != AttackState.Attacking) return;
        StartReturningToStartPosition(StartPosition, ReturnSpeed);
        damageCollider.enabled = false;
    }

    [Button]
    public virtual void CancelAttack()
    {
        if (CurrentState != AttackState.Attacking) return;
        StartReturningToStartPosition(StartPosition, ReturnSpeed);
        damageCollider.enabled = false;
        CurrentState = AttackState.Idle;
    }
    #endregion

    #region 攻击逻辑
    // 用于存储轨迹点的列表
    private List<Vector2> trajectoryPoints = new List<Vector2>();
    // 轨迹点的最大数量，控制轨迹长度
    public int maxTrajectoryPoints = 50;

    /// <summary>
    /// 执行刺击动作，控制物品（如武器）向目标点移动
    /// 根据距离和目标位置，采用圆弧移动或线性移动两种方式
    /// </summary>
    /// <param name="speed">移动速度</param>
    /// <param name="maxDistance">最大移动距离限制</param>
    /// <param name="deltaTime">帧时间增量，用于平滑移动</param>
    public void PerformStab(float speed, float maxDistance, float deltaTime)
    {
        // 记录当前位置到轨迹列表
        RecordTrajectoryPoint();

        // 确定起始位置：优先使用物品父物体位置，若无父物体则使用当前物体的父物体位置
        Vector2 startPosition = (item.transform.parent != null)
            ? item.transform.parent.position
            : transform.parent.position;

        // 获取目标焦点（如鼠标指向位置）
        Vector2 focusPoint = faceMouse.Data.FocusPoint;
        // 获取物品当前位置
        Vector2 swordPos = item.transform.position;
        // 计算物品相对于起始位置的向量
        Vector2 swordRel = swordPos - startPosition;
        // 计算物品到起始位置的距离
        float swordDist = swordRel.magnitude;

        // 计算从起始位置到目标焦点的方向向量
        Vector2 directionVector = focusPoint - startPosition;
        // 计算起始位置到目标焦点的直线距离
        float mouseDist = directionVector.magnitude;

        // 距离阈值：用于判断是否接近最大距离限制
        float threshold = 0.05f;

        // 条件：物品已接近最大距离（在阈值范围内），且目标焦点超出最大距离
        // 此时采用圆弧移动（保持在最大距离限制的圆上移动）
        if (swordDist >= maxDistance - threshold && mouseDist > maxDistance)
        {
            // 计算物品当前位置相对起始点的角度（弧度）
            float currentAngle = Mathf.Atan2(swordRel.y, swordRel.x);
            // 计算目标焦点在最大距离限制圆上的投影点
            Vector2 targetOnCircle = startPosition + directionVector.normalized * maxDistance;
            // 计算投影点相对起始点的向量
            Vector2 targetRel = targetOnCircle - startPosition;
            // 计算投影点相对起始点的角度（弧度）
            float targetAngle = Mathf.Atan2(targetRel.y, targetRel.x);

            // 计算当前角度与目标角度的差值（转换为角度后计算差值，再转回弧度）
            float angleDiff = Mathf.DeltaAngle(currentAngle * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            // 计算本次帧可移动的最大角度（基于速度、时间和半径）
            float maxAngleDelta = speed * deltaTime / maxDistance;

            // 确定实际移动角度：若角度差小于最大可移动角度，则直接移动到目标角度；否则按最大角度移动
            float moveAngle = Mathf.Abs(angleDiff) < maxAngleDelta
                ? angleDiff
                : Mathf.Sign(angleDiff) * maxAngleDelta;

            // 计算新角度和新位置（沿圆周移动后的位置）
            float newAngle = currentAngle + moveAngle;
            Vector2 newPos = startPosition + new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle)) * maxDistance;

            // 更新物品位置
            item.transform.position = newPos;
        }
        else
        {
            // 线性移动：当物品未接近最大距离，或目标焦点在最大距离范围内时使用
            // 计算目标点：若焦点超出最大距离则取最大距离处的点，否则直接使用焦点
            Vector2 targetPoint;
            if (mouseDist > maxDistance)
            {
                targetPoint = startPosition + directionVector.normalized * maxDistance;
            }
            else
            {
                targetPoint = focusPoint;
            }

            // 获取物品当前位置
            Vector2 currentPosition = item.transform.position;
            // 计算朝向目标点的移动方向
            Vector2 moveDir = (targetPoint - currentPosition).normalized;
            // 计算本帧可移动的距离
            float moveDist = speed * deltaTime;
            // 计算当前位置到目标点的剩余距离
            float remainingDist = Vector2.Distance(currentPosition, targetPoint);

            // 更新物品位置：若可移动距离大于等于剩余距离则直接到达目标点，否则按可移动距离移动
            if (moveDist >= remainingDist)
            {
                item.transform.position = targetPoint;
            }
            else
            {
                item.transform.position = currentPosition + moveDir * moveDist;
            }
        }
    }

    /// <summary>
    /// 记录当前武器位置到轨迹列表
    /// </summary>
    private void RecordTrajectoryPoint()
    {
        if (item != null)
        {
            // 添加当前位置到轨迹列表
            trajectoryPoints.Add(item.transform.position);

            // 如果轨迹点数量超过最大值，移除最早的点
            if (trajectoryPoints.Count > maxTrajectoryPoints)
            {
                trajectoryPoints.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 绘制Gizmos，显示轨迹和辅助线
    /// </summary>
    private void OnDrawGizmos()
    {
        // 绘制轨迹线
        DrawTrajectoryGizmos();

        // 如果物品存在，绘制辅助线
        if (item != null && faceMouse != null)
        {
            DrawHelperGizmos();
        }
    }

    /// <summary>
    /// 绘制武器移动轨迹
    /// </summary>
    private void DrawTrajectoryGizmos()
    {
        if (trajectoryPoints.Count < 2)
            return;

        // 设置轨迹线颜色为淡蓝色
        Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.8f);

        // 绘制连续的轨迹线
        for (int i = 0; i < trajectoryPoints.Count - 1; i++)
        {
            Gizmos.DrawLine(trajectoryPoints[i], trajectoryPoints[i + 1]);
        }

        // 绘制轨迹点，使轨迹更明显
        Gizmos.color = new Color(0.8f, 0.8f, 1f, 0.6f);
        foreach (var point in trajectoryPoints)
        {
            Gizmos.DrawSphere(point, 0.02f);
        }
    }

    /// <summary>
    /// 绘制辅助线，如最大范围、目标点等
    /// </summary>
    private void DrawHelperGizmos()
    {
        // 确定起始位置
        Vector2 startPosition = (item.transform.parent != null)
            ? item.transform.parent.position
            : transform.parent.position;

        // 获取目标焦点
        Vector2 focusPoint = faceMouse.Data.FocusPoint;

        // 绘制从起始位置到目标焦点的线（黄色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(startPosition, focusPoint);

        // 绘制最大距离范围（半透明绿色）
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(startPosition, GetComponent<Mod_ColdWeapon>().weaponData.MaxAttackDistance); // 替换为实际的最大距离获取方式

        // 绘制当前武器位置到目标点的线（青色）
        Vector2 targetPoint = CalculateTargetPoint(startPosition, focusPoint, GetComponent<Mod_ColdWeapon>().weaponData.MaxAttackDistance); // 替换为实际的最大距离获取方式
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(item.transform.position, targetPoint);

        // 绘制目标点标记（红色小球）
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(targetPoint, 0.05f);
    }

    /// <summary>
    /// 计算武器的目标点（与PerformStab方法中的逻辑一致）
    /// </summary>
    private Vector2 CalculateTargetPoint(Vector2 startPosition, Vector2 focusPoint, float maxDistance)
    {
        Vector2 directionVector = focusPoint - startPosition;
        float mouseDist = directionVector.magnitude;

        if (mouseDist > maxDistance)
        {
            return startPosition + directionVector.normalized * maxDistance;
        }
        else
        {
            return focusPoint;
        }
    }

    // 清空轨迹（可以在需要时调用，如武器收回时）
    public void ClearTrajectory()
    {
        trajectoryPoints.Clear();
    }
    private void UpdateReturning(float deltaTime)
    {
        Vector2 currentPos = (Vector2)MoveTargetTransform.localPosition;
        Vector2 toTarget = returnTarget - currentPos;
        float sqrDistance = toTarget.sqrMagnitude;

        float threshold = 0.0001f;
        Vector2 move = toTarget.normalized * ReturnSpeed * deltaTime;

        if (sqrDistance <= threshold || move.sqrMagnitude >= sqrDistance)
        {
            MoveTargetTransform.localPosition = returnTarget;
            CurrentState = AttackState.Idle;
        }
        else
        {
            MoveTargetTransform.localPosition = currentPos + move;
        }
    }
    #endregion

    #region 工具函数
    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
#if UNITY_STANDALONE || UNITY_EDITOR
        return EventSystem.current.IsPointerOverGameObject();
#else
        return false;
#endif
    }

    public void StartReturningToStartPosition(Vector2 startTarget, float backSpeed)
    {
        CurrentState = AttackState.Returning;
        returnTarget = startTarget;
        ReturnSpeed = backSpeed;
    }
    #endregion

    #region 伤害判定
    public void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;

        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var belongItem = item?.Owner;
        var belongTeam = belongItem?.GetComponent<ITeam>();

        // 避免打到友军
        if (beAttackTeam != null && belongTeam != null &&
            belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
            return;

        // 造成伤害
        receiver.TakeDamage(Damage.Value, belongItem == null ? item : belongItem);

        // 生成攻击特效
        if (AttackEffect != null)
        {
            Vector2 hitPoint = other.ClosestPoint(transform.position);
            StartCoroutine(SpawnEffectWithDirection(hitPoint));
        }
    }
    [SerializeField] private int sampleFrames = 2; // 采样帧数，可以在 Inspector 里手动调整

    private IEnumerator SpawnEffectWithDirection(Vector2 hitPoint)
    {
        // 记录第一个点
        Vector2 startPos = transform.position;

        // 多帧采样
        for (int i = 0; i < sampleFrames; i++)
            yield return null;

        // 记录最后一个点
        Vector2 endPos = transform.position;

        // 求平均方向
        Vector2 dir = (endPos - startPos).normalized;

        // 如果几乎没动，给一个默认方向（比如右）
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector2.right;

        // 基于 prefab 自带的 rotation 做修正，而不是覆盖
        Quaternion baseRotation = AttackEffect.transform.rotation;
        Quaternion dirRotation = Quaternion.FromToRotation(Vector2.right, dir);

        // 把方向旋转叠加到 prefab 默认旋转上
        Quaternion finalRotation = dirRotation * baseRotation;

        // 生成特效
        var effect = Instantiate(AttackEffect, hitPoint, finalRotation);

        Destroy(effect, 0.3f);
    }


    #endregion

    public override void Save()
    {
        Data.WriteData(weaponData);
        if (item.Owner != null)
        {
            InputAction.started -= OnInputActionStarted;
            InputAction.canceled -= OnInputActionCanceled;
        }
    }
}

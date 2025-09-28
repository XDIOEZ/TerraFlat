using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using MemoryPack;
using Sirenix.OdinInspector;
using static AttackTrigger;
using AYellowpaper.SerializedCollections;

public partial class Mod_ColdWeapon : Module ,IDamageSender
{
    #region 序列化字段与数据
    [Header("攻击特效")]
    public List<GameEffect> AttackEffects = new List<GameEffect>();

    public Ex_ModData_MemoryPackable Data;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public SerializedDictionary<DamageTag, float> Weakness = new SerializedDictionary<DamageTag, float>();
    [SerializeField] public ColdWeapon_SaveData weaponData = new ColdWeapon_SaveData();

    [System.Serializable]
    [MemoryPackable]
    public partial class ColdWeapon_SaveData
    {
        [Header("武器属性设置")]
        public GameValue_float Damage = new GameValue_float(10f);
        public float AttackSpeed = 10f;
        public float MaxAttackDistance = 10f;
        public float ReturnSpeed = 10f;

        [Header("安全距离设置")]
        [Tooltip("x: 造成伤害的最近距离, y: 造成伤害的最大距离")]
        public Vector2 SafetyDistance = new Vector2(0.5f, 100f);
    }
    #endregion

    #region 属性
    public GameValue_float Damage { get => weaponData.Damage; set => weaponData.Damage = value; }
    public float AttackSpeed { get => weaponData.AttackSpeed; set => weaponData.AttackSpeed = value; }
    public float MaxAttackDistance { get => weaponData.MaxAttackDistance; set => weaponData.MaxAttackDistance = value; }
    public float ReturnSpeed { get => weaponData.ReturnSpeed; set => weaponData.ReturnSpeed = value; }
    public Vector2 SafetyDistance { get => weaponData.SafetyDistance; set => weaponData.SafetyDistance = value; }
    Item IDamageSender.attacker { get => item; set => item = value; }
    SerializedDictionary<DamageTag, float> IDamageSender.Weakness { get => Weakness; set => Weakness = value; }
    #endregion

    #region 运行时字段
    public FaceMouse faceMouse;
    public AttackState CurrentState = AttackState.Idle;
    public Vector2 StartPosition = Vector2.zero;
    public Transform MoveTargetTransform;

    [SerializeField] public Collider2D damageCollider;

    private InputAction InputAction;
    private Vector2 returnTarget;

    // 用于轨迹显示与调试
    private List<Vector2> trajectoryPoints = new List<Vector2>();
    public int maxTrajectoryPoints = 50;
    #endregion

    #region Unity 生命周期
    public override void Awake()
    {
        if (_Data.ID == "") 
            _Data.ID = ModText.ColdWeapon;
    }

    public override void Load()
    {
        Data.ReadData(ref weaponData);

        if (item.Owner != null)
        {
            faceMouse = item.Owner.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
            var controller = item.Owner.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();
            if (controller != null)
            {
                InputAction = controller._inputActions.Win10.LeftClick;
                InputAction.started += OnInputActionStarted;
                InputAction.canceled += OnInputActionCanceled;
            }
        }
        else
        {
            faceMouse = item.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
        }

        // 保持原始逻辑：若 MoveTargetTransform 为空并且有 Owner，则使用 item.transform；否则使用 transform.parent
        MoveTargetTransform = (MoveTargetTransform == null && item.Owner != null)
            ? item.transform
            : transform.parent;

        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider == null)
                Debug.LogWarning("[Mod_ColdWeapon] 没有找到Collider2D，请在编辑器里赋值！");
        }
        damageCollider.enabled = false;
    }

    public override void Save()
    {
        Data.WriteData(weaponData);

        if (item.Owner != null && InputAction != null)
        {
            InputAction.started -= OnInputActionStarted;
            InputAction.canceled -= OnInputActionCanceled;
        }
    }
    #endregion

    #region 主更新/动作
    public override void ModUpdate(float deltaTime)
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

        // 总是检查安全距离，因为玩家可能在移动
        CheckSafetyDistance();
    }
    #endregion

    #region 输入与攻击控制
    public void OnInputActionStarted(InputAction.CallbackContext context)
    {
        if (context.started)
            StartCoroutine(DelayedCheckUIThenAttack());
    }

    private IEnumerator DelayedCheckUIThenAttack()
    {
        // 延迟到下一帧以确保 EventSystem 状态正确
        yield return null;

        if (!IsPointerOverUI())
            StartAttack();
    }

    private void OnInputActionCanceled(InputAction.CallbackContext context) => StopAttack();

    [Button("开始攻击")]
    public virtual void StartAttack()
    {
        if (CurrentState != AttackState.Idle) return;
        CurrentState = AttackState.Attacking;
        
        // 初始禁用伤害碰撞器，只有在有效距离范围内才启用
        if (damageCollider != null) 
            damageCollider.enabled = false;
    }

    public virtual void StopAttack()
    {
        if (CurrentState != AttackState.Attacking) return;
        StartReturningToStartPosition(StartPosition, ReturnSpeed);
        
        // 停止攻击时禁用伤害碰撞器
        if (damageCollider != null) 
            damageCollider.enabled = false;
    }

    [Button("取消攻击")]
    public virtual void CancelAttack()
    {
        if (CurrentState != AttackState.Attacking) return;
        StartReturningToStartPosition(StartPosition, ReturnSpeed);
        
        // 取消攻击时禁用伤害碰撞器
        if (damageCollider != null) 
            damageCollider.enabled = false;
        
        CurrentState = AttackState.Idle;
    }
    #endregion

    #region 攻击实现（移动与轨迹）
    /// <summary>
    /// 执行刺击动作，根据是否达到最大距离选择圆弧或线性移动。
    /// </summary>
    public void PerformStab(float speed, float maxDistance, float deltaTime)
    {
        RecordTrajectoryPoint();

        // 起始位置：尽量使用父 transform 的世界位置
        Vector2 startPosition = (item.transform.parent != null) ? (Vector2)item.transform.parent.position : (Vector2)transform.parent.position;

        // 焦点（如鼠标）及当前武器位置
        Vector2 focusPoint = faceMouse != null ? faceMouse.Data.FocusPoint : startPosition;
        Vector2 swordPos = item.transform.position;
        Vector2 swordRel = swordPos - startPosition;
        float swordDist = swordRel.magnitude;

        Vector2 directionVector = focusPoint - startPosition;
        float mouseDist = directionVector.magnitude;

        const float threshold = 0.05f;

        // 当武器几乎在最大半径上并且焦点在圆外时，用圆周移动
        if (swordDist >= maxDistance - threshold && mouseDist > maxDistance)
        {
            float currentAngle = Mathf.Atan2(swordRel.y, swordRel.x);
            Vector2 targetOnCircle = startPosition + directionVector.normalized * maxDistance;
            Vector2 targetRel = targetOnCircle - startPosition;
            float targetAngle = Mathf.Atan2(targetRel.y, targetRel.x);

            float angleDiff = Mathf.DeltaAngle(currentAngle * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float maxAngleDelta = speed * deltaTime / maxDistance;

            float moveAngle = Mathf.Abs(angleDiff) < maxAngleDelta ? angleDiff : Mathf.Sign(angleDiff) * maxAngleDelta;

            float newAngle = currentAngle + moveAngle;
            Vector2 newPos = startPosition + new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle)) * maxDistance;

            item.transform.position = newPos;
        }
        else
        {
            Vector2 targetPoint = mouseDist > maxDistance ? startPosition + directionVector.normalized * maxDistance : focusPoint;

            Vector2 currentPosition = item.transform.position;
            Vector2 toTarget = targetPoint - currentPosition;
            float moveDist = speed * deltaTime;
            float remainingDist = toTarget.magnitude;

            if (moveDist >= remainingDist)
            {
                item.transform.position = targetPoint;
            }
            else
            {
                item.transform.position = currentPosition + toTarget.normalized * moveDist;
            }
        }
    }

    /// <summary>
    /// 检查是否在有效伤害距离范围内，如果在范围内则启用伤害碰撞器
    /// </summary>
    private void CheckSafetyDistance()
    {
        // 如果没有伤害碰撞器，直接返回
        if (damageCollider == null) return;

        Vector2 currentPosition = item.transform.position;
        
        // 获取父对象（玩家）的位置作为起始位置
        Vector2 startPosition = Vector2.zero;
        if (item.transform.parent != null)
        {
            startPosition = item.transform.parent.position;
        }
        else
        {
            startPosition = transform.parent.position;
        }

        float distanceFromStart = Vector2.Distance(currentPosition, startPosition);

        // 只有在攻击状态且在有效伤害距离范围内时才启用伤害碰撞器
        if (CurrentState == AttackState.Attacking && 
            distanceFromStart >= SafetyDistance.x && 
            distanceFromStart <= SafetyDistance.y)
        {
            if (!damageCollider.enabled)
            {
                damageCollider.enabled = true;
            }
        }
        // 如果不在攻击状态或不在有效伤害距离范围内，则禁用伤害碰撞器
        else if (damageCollider.enabled)
        {
            damageCollider.enabled = false;
        }
    }

    private void RecordTrajectoryPoint()
    {
        if (item == null) return;

        trajectoryPoints.Add(item.transform.position);
        if (trajectoryPoints.Count > maxTrajectoryPoints)
            trajectoryPoints.RemoveAt(0);
    }

    public void ClearTrajectory() => trajectoryPoints.Clear();
    #endregion

    #region 返回与移动辅助
    private void UpdateReturning(float deltaTime)
    {
        if (MoveTargetTransform == null) 
        { 
            CurrentState = AttackState.Idle; 
            return; 
        }

        Vector2 currentPos = (Vector2)MoveTargetTransform.localPosition;
        Vector2 toTarget = returnTarget - currentPos;
        float sqrDistance = toTarget.sqrMagnitude;

        const float threshold = 0.0001f;
        Vector2 move = toTarget.normalized * ReturnSpeed * deltaTime;

        if (sqrDistance <= threshold || move.sqrMagnitude >= sqrDistance)
        {
            MoveTargetTransform.localPosition = returnTarget;
            CurrentState = AttackState.Idle;
            
            // 确保返回到位后禁用伤害碰撞器
            if (damageCollider != null) 
                damageCollider.enabled = false;
        }
        else
        {
            MoveTargetTransform.localPosition = currentPos + move;
            
            // 在返回过程中始终禁用伤害碰撞器
            if (damageCollider != null && damageCollider.enabled)
            {
                damageCollider.enabled = false;
            }
        }
    }

    public void StartReturningToStartPosition(Vector2 startTarget, float backSpeed)
    {
        CurrentState = AttackState.Returning;
        returnTarget = startTarget;
        ReturnSpeed = backSpeed;
        
        // 开始返回时立即禁用伤害碰撞器
        if (damageCollider != null && damageCollider.enabled)
        {
            damageCollider.enabled = false;
        }
    }
    #endregion

    #region 伤害处理
    public void OnTriggerEnter2D(Collider2D other)
    {
        // 只有在伤害碰撞器启用时才处理碰撞
        if (damageCollider == null || !damageCollider.enabled) return;
        
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;

        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var belongItem = item?.Owner;
        var belongTeam = belongItem?.GetComponent<ITeam>();

        // 避免打到友军
        if (beAttackTeam != null && belongTeam != null && belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
            return;

        float acDamage =receiver.Hurt(this);
        // 造成伤害

        // 生成攻击特效
        if (AttackEffects != null && AttackEffects.Count > 0)
        {
            Vector2 hitPoint = other.ClosestPoint(transform.position);
            SpawnEffect(hitPoint, acDamage);
        }
    }

private void SpawnEffect(Vector2 hitPoint, float damage)
{
    // 同时生成所有特效
    foreach (GameEffect effectPrefab in AttackEffects)
    {
        if (effectPrefab != null)
        {
            var effect = Instantiate(effectPrefab);
            effect.transform.position = new Vector3(hitPoint.x, hitPoint.y, 0f);
            // 传递武器transform和伤害值
            effect.Effect(transform, damage);
        }
    }
}
    #endregion

    #region Gizmos 与调试绘制
    private void OnDrawGizmos()
    {
        DrawTrajectoryGizmos();

        if (item != null && faceMouse != null)
            DrawHelperGizmos();
    }

    private void DrawTrajectoryGizmos()
    {
        if (trajectoryPoints == null || trajectoryPoints.Count < 2) return;

        Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.8f);
        for (int i = 0; i < trajectoryPoints.Count - 1; i++)
            Gizmos.DrawLine(trajectoryPoints[i], trajectoryPoints[i + 1]);

        Gizmos.color = new Color(0.8f, 0.8f, 1f, 0.6f);
        foreach (var point in trajectoryPoints)
            Gizmos.DrawSphere(point, 0.02f);
    }

    private void DrawHelperGizmos()
    {
        Vector2 startPosition = (item != null && item.transform.parent != null) ? (Vector2)item.transform.parent.position : (Vector2)transform.parent.position;
        Vector2 focusPoint = faceMouse != null ? faceMouse.Data.FocusPoint : startPosition;

        // 起点 -> 焦点
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(startPosition, focusPoint);

        float drawMax = weaponData != null ? weaponData.MaxAttackDistance : 0f;

        // 最大范围
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(startPosition, drawMax);

        // 安全距离范围
        if (weaponData != null)
        {
            // 内圈（最近伤害距离）
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(startPosition, weaponData.SafetyDistance.x);
            
            // 外圈（最大伤害距离）
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(startPosition, weaponData.SafetyDistance.y);
        }

        Vector2 targetPoint = CalculateTargetPoint(startPosition, focusPoint, drawMax);
        Gizmos.color = Color.cyan;
        if (item != null) Gizmos.DrawLine(item.transform.position, targetPoint);

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(targetPoint, 0.05f);
    }

    private Vector2 CalculateTargetPoint(Vector2 startPosition, Vector2 focusPoint, float maxDistance)
    {
        Vector2 directionVector = focusPoint - startPosition;
        float mouseDist = directionVector.magnitude;

        return mouseDist > maxDistance ? startPosition + directionVector.normalized * maxDistance : focusPoint;
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
    #endregion
}
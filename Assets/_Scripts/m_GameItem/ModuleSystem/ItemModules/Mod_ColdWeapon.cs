using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine.EventSystems;
using static AttackTrigger;

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

        if (item.BelongItem != null)
        {
            faceMouse = item.BelongItem.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
            var controller = item.BelongItem.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();
            InputAction = controller._inputActions.Win10.LeftClick;
            InputAction.started += OnInputActionStarted;
            InputAction.canceled += OnInputActionCanceled;
        }
        else
        {
            faceMouse = item.itemMods.GetMod_ByID(ModText.FaceMouse) as FaceMouse;
        }

        MoveTargetTransform = (MoveTargetTransform == null && item.BelongItem != null)
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
    public void PerformStab(float speed, float maxDistance, float deltaTime)
    {
        Vector2 startPosition = (item.transform.parent != null)
            ? item.transform.parent.position
            : transform.parent.position;

        Vector2 focusPoint = faceMouse.Data.FocusPoint;
        Vector2 swordPos = item.transform.position;
        Vector2 swordRel = swordPos - startPosition;
        float swordDist = swordRel.magnitude;

        Vector2 directionVector = focusPoint - startPosition;
        float mouseDist = directionVector.magnitude;

        float threshold = 0.05f;

        if (swordDist >= maxDistance - threshold && mouseDist > maxDistance)
        {
            // 沿圆弧移动
            float currentAngle = Mathf.Atan2(swordRel.y, swordRel.x);
            Vector2 targetOnCircle = startPosition + directionVector.normalized * maxDistance;
            Vector2 targetRel = targetOnCircle - startPosition;
            float targetAngle = Mathf.Atan2(targetRel.y, targetRel.x);

            float angleDiff = Mathf.DeltaAngle(currentAngle * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float maxAngleDelta = speed * deltaTime / maxDistance;

            float moveAngle = Mathf.Abs(angleDiff) < maxAngleDelta
                ? angleDiff
                : Mathf.Sign(angleDiff) * maxAngleDelta;

            float newAngle = currentAngle + moveAngle;
            Vector2 newPos = startPosition + new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle)) * maxDistance;

            item.transform.position = newPos;
        }
        else
        {
            // 线性移动
            Vector2 targetPoint = mouseDist > maxDistance
                ? startPosition + directionVector.normalized * maxDistance
                : focusPoint;

            Vector2 currentPosition = item.transform.position;
            Vector2 moveDir = (targetPoint - currentPosition).normalized;
            float moveDist = speed * deltaTime;
            float remainingDist = Vector2.Distance(currentPosition, targetPoint);

            item.transform.position = moveDist >= remainingDist
                ? targetPoint
                : currentPosition + moveDir * moveDist;
        }
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
        var belongItem = item?.BelongItem;
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
            // 获取碰撞点（尽量接近击中位置）
            Vector2 hitPoint = other.ClosestPoint(transform.position);

            // 随机旋转（绕 Z 轴）
            float randomAngle = Random.Range(0f, 360f);
            Quaternion randomRotation = Quaternion.Euler(0, 0, randomAngle);

            var effect = Instantiate(AttackEffect, hitPoint, randomRotation);
            Destroy(effect, 0.3f); // 0.3 秒后销毁
        }

    }
    #endregion

    public override void Save()
    {
        Data.WriteData(weaponData);
        if (InputAction != null)
        {
            InputAction.started -= OnInputActionStarted;
            InputAction.canceled -= OnInputActionCanceled;
        }
    }
}

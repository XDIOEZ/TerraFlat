using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static AttackTrigger;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine.EventSystems;

public partial class Mod_ColdWeapon : Module
{
    public Ex_ModData_MemoryPackable Data;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }

    [SerializeField] public ColdWeapon_SaveData saveData = new ColdWeapon_SaveData();

    public GameValue_float Damage { get => saveData.Damage; set => saveData.Damage = value; }
    public float AttackSpeed { get => saveData.AttackSpeed; set => saveData.AttackSpeed = value; }
    public float MaxAttackDistance { get => saveData.MaxAttackDistance; set => saveData.MaxAttackDistance = value; }
    public float ReturnSpeed { get => saveData.ReturnSpeed; set => saveData.ReturnSpeed = value; }

    public FaceMouse faceMouse;
    public AttackState CurrentState = AttackState.Idle;
    public Vector2 StartPosition = Vector2.zero;
    public Transform MoveTargetTransform;
    [SerializeField] protected Collider2D damageCollider;

    public Transform controllerItem;
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
        Data.ReadData(ref saveData);

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

        if (MoveTargetTransform == null&& item.BelongItem!= null) //不等于空 表示有东西拿着 
            MoveTargetTransform = item.transform;
        else
            MoveTargetTransform = transform.parent;
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

                PerformStab(StartPosition, AttackSpeed, MaxAttackDistance, deltaTime);

                break;

            case AttackState.Returning:
                Vector2 currentPos = (Vector2)MoveTargetTransform.localPosition;
                Vector2 toTarget = returnTarget - currentPos;
                float sqrDistance = toTarget.sqrMagnitude;

                float threshold = 0.0001f;

                // 如果已经非常接近目标，或下一帧会超过目标，就直接设置到目标点
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
                break;

        }
    }

    public void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out DamageReceiver receiver)) return;

        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var item = GetComponentInParent<Item>();
        var belongItem = item?.BelongItem;
        var belongTeam = belongItem?.GetComponent<ITeam>();

        if (beAttackTeam != null && belongTeam != null &&
            belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
            return;

        if (belongItem == null)
            receiver.TakeDamage(Damage.Value, item);
        else
            receiver.TakeDamage(Damage.Value, belongItem);
    }

    public void OnInputActionStarted(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            StartCoroutine(DelayedCheckUIThenAttack());
        }
    }

    private IEnumerator DelayedCheckUIThenAttack()
    {
        yield return null; // 延迟到下一帧

        if (!IsPointerOverUI())
        {
            StartAttack();
        }
    }


    bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

#if UNITY_STANDALONE || UNITY_EDITOR
        return EventSystem.current.IsPointerOverGameObject(); // 鼠标
        /*#elif UNITY_ANDROID || UNITY_IOS
                if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0)
                {
                    var touch = Touchscreen.current.touches[0];
                    return EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue());
                }*/
#endif
        /*  return false;*/
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
    public void PerformStab(Vector2 unusedStartPosition, float speed, float maxDistance, float deltaTime)
    {
        Vector2 startPosition;
        if (item.transform.parent != null)
        {
            startPosition = item.transform.parent.position;
        }
        else
        {
            startPosition = transform.parent.position;
        }

            Vector2 focusPoint = faceMouse.Data.FocusPoint;

        Vector2 swordPos = item.transform.position;
        Vector2 swordRel = swordPos - startPosition;
        float swordDist = swordRel.magnitude;

        Vector2 directionVector = focusPoint - startPosition;
        float mouseDist = directionVector.magnitude;

        float threshold = 0.05f; // 距离阈值，判断剑是否接近圆弧边界

        if (swordDist >= maxDistance - threshold && mouseDist > maxDistance)
        {
            // 剑已经接近圆弧边界且鼠标超出攻击范围，使用圆弧移动

            // 当前剑角度
            float currentAngle = Mathf.Atan2(swordRel.y, swordRel.x);

            // 目标角度（鼠标投影到圆弧上）
            Vector2 targetOnCircle = startPosition + directionVector.normalized * maxDistance;
            Vector2 targetRel = targetOnCircle - startPosition;
            float targetAngle = Mathf.Atan2(targetRel.y, targetRel.x);

            // 角度差（弧度）
            float angleDiff = Mathf.DeltaAngle(currentAngle * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            // 最大角速度（rad/s）
            float maxAngleDelta = speed * deltaTime / maxDistance;

            // 限制移动角度，避免超调
            float moveAngle = Mathf.Abs(angleDiff) < maxAngleDelta ? angleDiff : Mathf.Sign(angleDiff) * maxAngleDelta;

            float newAngle = currentAngle + moveAngle;

            // 新剑位置
            Vector2 newPos = startPosition + new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle)) * maxDistance;

            item.transform.position = newPos;
        }
        else
        {
            // 线性移动，剑尚未到达圆弧边界或鼠标未超出范围
            Vector2 targetPoint = mouseDist > maxDistance
                                  ? startPosition + directionVector.normalized * maxDistance
                                  : focusPoint;

            Vector2 currentPosition = item.transform.position;
            Vector2 moveDir = (targetPoint - currentPosition).normalized;
            float moveDist = speed * deltaTime;
            float remainingDist = Vector2.Distance(currentPosition, targetPoint);

            if (moveDist >= remainingDist)
                item.transform.position = targetPoint;
            else
                item.transform.position = currentPosition + moveDir * moveDist;
        }
    }




    public void StartReturningToStartPosition(Vector2 startTarget, float backSpeed)
    {
        CurrentState = AttackState.Returning;
        returnTarget = startTarget;
        ReturnSpeed = backSpeed;
    }

    public override void Save()
    {
        Data.WriteData(saveData);
        if (InputAction != null)
        {
            InputAction.started -= OnInputActionStarted;
            InputAction.canceled -= OnInputActionCanceled;
        }
    }
}

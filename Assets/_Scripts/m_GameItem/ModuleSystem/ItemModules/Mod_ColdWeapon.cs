using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static AttackTrigger;
using MemoryPack;

public partial class Mod_ColdWeapon : Module
{
    public Ex_ModData_MemoryPackable Data;
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }

    // 新增的SaveData字段
    [SerializeField]
    public ColdWeapon_SaveData saveData = new ColdWeapon_SaveData();

    // 将需要保存的字段改为属性，引用saveData中的字段
    public GameValue_float Damage
    {
        get => saveData.Damage;
        set => saveData.Damage = value;
    }

    public float AttackSpeed
    {
        get => saveData.AttackSpeed;
        set => saveData.AttackSpeed = value;
    }

    public float MaxAttackDistance
    {
        get => saveData.MaxAttackDistance;
        set => saveData.MaxAttackDistance = value;
    }

    public float ReturnSpeed
    {
        get => saveData.ReturnSpeed;
        set => saveData.ReturnSpeed = value;
    }

    public FaceMouse faceMouse;
    public AttackState CurrentState = AttackState.Idle;
    public Coroutine returnCoroutine;
    public Vector2 StartPosition = Vector2.zero;

    public Transform MoveTargetTransform;

    [SerializeField] private Collider2D damageCollider; // 统一成Collider2D，支持任意2D碰撞体

    private InputAction InputAction;

    // 保存数据类
    [System.Serializable]
    [MemoryPackable]
    public partial class ColdWeapon_SaveData
    {
        [Header("武器属性设置")]
        public GameValue_float Damage = new GameValue_float(10f);
        public float AttackSpeed = 10f;
        public float MaxAttackDistance = 10f;
        public float ReturnSpeed = 10f;

        // 可根据需要添加更多字段
    }

    public override void Awake()
    {
        if (_Data.Name == "")
        {
            _Data.Name = "ColdWeapon";
        }
    }

    public override void Load()
    {
        // 从Data加载保存的数据到saveData
        Data.ReadData(ref saveData);

        if (item.BelongItem != null)
            faceMouse = item.BelongItem.Mods[ModText.FaceMouse] as FaceMouse;

        var controller = item.BelongItem.Mods[ModText.Controller].GetComponent<PlayerController>();
        InputAction = controller._inputActions.Win10.LeftClick;

        InputAction.started += OnInputActionStarted;
        InputAction.canceled += OnInputActionCanceled;

        if (MoveTargetTransform == null)
            MoveTargetTransform = item.transform;

        if (damageCollider == null)
        {
            damageCollider = GetComponent<Collider2D>();
            if (damageCollider == null)
            {
                Debug.LogWarning("[Mod_ColdWeapon] 没有找到Collider2D，请在编辑器里赋值！");
            }
        }

        if (damageCollider != null)
            damageCollider.enabled = false; // 默认关闭
    }

    public void Update()
    {
        if (CurrentState == AttackState.Attacking && InputAction != null && InputAction.IsPressed())
        {
            OnAttackStay();
        }

        if (isReturning)
        {
            float distanceToMoveBack = ReturnSpeed * Time.deltaTime;
            Vector2 currentPos = (Vector2)MoveTargetTransform.localPosition;
            Vector2 directionBack = (returnTarget - currentPos).normalized;

            float dist = Vector2.Distance(currentPos, returnTarget);
            if (dist <= 0.1f)
            {
                MoveTargetTransform.localPosition = returnTarget;
                isReturning = false;
                CurrentState = AttackState.Idle;

                if (damageCollider != null)
                    damageCollider.enabled = false; // 回归结束时关闭碰撞体
            }
            else
            {
                MoveTargetTransform.localPosition = currentPos + directionBack * distanceToMoveBack;
            }
        }
    }

    public void OnTriggerEnter2D(Collider2D other)
    {
        // 先尝试自己物体上获取 DamageReceiver
        if (!other.TryGetComponent<DamageReceiver>(out var receiver))
        {
            if (receiver == null) return;
        }

        var beAttackTeam = other.GetComponentInParent<ITeam>();
        var item = GetComponentInParent<Item>();
        var belongItem = item != null ? item.BelongItem : null;
        var belongTeam = belongItem != null ? belongItem.GetComponent<ITeam>() : null;

        if (beAttackTeam != null && belongTeam != null &&
            belongTeam.CheckRelation(beAttackTeam.TeamID) == RelationType.Ally)
        {
            return;
        }

        receiver.TakeDamage(Damage.Value);
    }

    // 用字段记录是否处于"回归初始位置"的状态和参数
    private bool isReturning = false;
    private Vector2 returnTarget;

    private void OnInputActionStarted(InputAction.CallbackContext context)
    {
        OnAttackStart();
    }

    private void OnInputActionCanceled(InputAction.CallbackContext context)
    {
        OnAttackStop();
    }

    public void OnAttackStart()
    {
        if (CurrentState == AttackState.Idle)
        {
            CurrentState = AttackState.Attacking;
            StartPosition = MoveTargetTransform.localPosition;

            if (damageCollider != null)
                damageCollider.enabled = true; // 激活碰撞体
        }
    }

    public void OnAttackStay()
    {
        if (CurrentState == AttackState.Attacking)
        {
            PerformStab(StartPosition, AttackSpeed, MaxAttackDistance);
        }
    }

    public void OnAttackStop()
    {
        if (CurrentState == AttackState.Attacking)
        {
            StartReturningToStartPosition(StartPosition, ReturnSpeed);

            if (damageCollider != null)
                damageCollider.enabled = false; // 关闭碰撞体
        }
    }

    public void PerformStab(Vector2 startTarget, float speed, float maxDistance)
    {
        Vector2 mouseTargetLocal = MoveTargetTransform.parent != null ?
            (Vector2)MoveTargetTransform.parent.InverseTransformPoint(faceMouse.Data.TargetPosition) :
            (Vector2)faceMouse.Data.TargetPosition;

        Vector2 currentLocalPos = (Vector2)MoveTargetTransform.localPosition;

        Vector2 toTarget = mouseTargetLocal - startTarget;
        float targetDistance = toTarget.magnitude;

        if (targetDistance > maxDistance)
        {
            mouseTargetLocal = startTarget + toTarget.normalized * maxDistance;
        }

        Vector2 direction = (mouseTargetLocal - currentLocalPos).normalized;
        float distanceToMove = speed * Time.deltaTime;
        Vector2 newPosition = currentLocalPos + direction * distanceToMove;

        float remainingDistance = Vector2.Distance(currentLocalPos, mouseTargetLocal);
        if (remainingDistance < 0.1f)
        {
            float finalDistance = Vector2.Distance(startTarget, mouseTargetLocal);
            newPosition = finalDistance > maxDistance ?
                startTarget + (mouseTargetLocal - startTarget).normalized * maxDistance :
                mouseTargetLocal;
        }
        else
        {
            float currentDistance = Vector2.Distance(startTarget, newPosition);
            if (currentDistance > maxDistance)
            {
                newPosition = startTarget + (newPosition - startTarget).normalized * maxDistance;
            }
        }

        MoveTargetTransform.localPosition = newPosition;

        Debug.DrawLine(
            MoveTargetTransform.parent.TransformPoint(startTarget),
            MoveTargetTransform.parent.TransformPoint(newPosition),
            Color.red
        );
    }

    public void StartReturningToStartPosition(Vector2 startTarget, float backSpeed)
    {
        isReturning = true;
        returnTarget = startTarget;
        ReturnSpeed = backSpeed; // 注意这里应该是设置saveData中的ReturnSpeed

        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
            returnCoroutine = null;
        }
    }

    public override void Save()
    {
        // 保存saveData到Data
        Data.WriteData(saveData);

        if (InputAction != null)
        {
            InputAction.started -= OnInputActionStarted;
            InputAction.canceled -= OnInputActionCanceled;
        }
    }
}
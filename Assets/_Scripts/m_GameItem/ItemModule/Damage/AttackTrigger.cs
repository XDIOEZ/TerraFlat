using DG.Tweening;
using NaughtyAttributes;
using System.Collections;
using UltEvents;
using UnityEditor;
using UnityEngine;

public class AttackTrigger : Module
{
    #region 参数
    // 事件--属性
    public UltEvent OnStartAttack  = new UltEvent();
    public UltEvent OnStayAttack = new UltEvent();
    public UltEvent OnEndAttack  = new UltEvent();

    public Item Weapon;
    public IAttackState Attacker;

    private Coroutine hitPauseCoroutine;

    public GameValue_float RotationSpeed;

    bool HitStop = false;
    public bool ChangeHand = false;

    TurnBody TrunBody;


    #region 攻击行为的状态参数
    public Vector2 StartPosition; // 保存初始位置
    public Vector3 MouseTarget;
    public bool CanAttack;
    public AttackState CurrentState = AttackState.Idle; // 当前状态
    public Coroutine returnCoroutine;
    public IColdWeapon WeaponData;

    public Ex_ModData ModData;
    public override ModuleData _Data { get => ModData; set => ModData = value as Ex_ModData; }
    #endregion

    #endregion

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Attacker;
        }
 
    }

    public override void Load()
    {
         TrunBody = item.itemMods.GetMod_ByID(ModText.TrunBody) as TurnBody;
        TrunBody.OnTrun += ToOtherDirection;
    }

    [SerializeField] private float xOffset = 0.5f;
    public void ToOtherDirection(Vector2 direction)
    {
        if (ChangeHand == false)
        {
            return;
        }
        float sign = Mathf.Sign(direction.x);

        // 目标 x 位置：根据左右方向决定偏移值
        float targetX = xOffset * sign;

        // 平滑移动武器的本地 x 坐标
        Vector3 currentLocalPos = transform.localPosition;
        Vector3 targetLocalPos = new Vector3(targetX, currentLocalPos.y, currentLocalPos.z);

        // 动画移动（0.15秒，缓出）
        transform.DOLocalMoveX(targetLocalPos.x, 0.15f).SetEase(Ease.OutSine);
    }



    public override void ModUpdate(float deltaTime)
    {
    }
    public void SetWeapon(Item _weapon)
    {
        Weapon = _weapon;
        Attacker = _weapon.GetComponent<IAttackState>();
        WeaponData = _weapon.GetComponent<IColdWeapon>();
        if (_weapon.GetComponentInChildren<IDamageSender>()!= null)
        _weapon.GetComponentInChildren<IDamageSender>().OnDamage += AttackStop;
    }

    void AttackStop(float damage)
    {
        if (damage > 1)
        {
            if (hitPauseCoroutine != null)
            {
                StopCoroutine(hitPauseCoroutine); // 先停止旧的
            }
            hitPauseCoroutine = StartCoroutine(HitPauseTransform());
        }

    }

    IEnumerator HitPauseTransform()
    {
        if (Weapon == null) yield break;
        float rotationSpeed = RotationSpeed.Value;
        HitStop = true;
        RotationSpeed.MultiplicativeModifier = 0;

        yield return new WaitForSeconds(0.15f);

        HitStop = false;
        RotationSpeed.MultiplicativeModifier = 1;
    }



    #region (设置是否可以攻击, 触发攻击)外部接口

    public void SetCanAttack(float Stamina)
    {
        if (Stamina >= 20)
        {
            CanAttack = true;
        }
        else if (Stamina <= 0)
        {
            CanAttack = false;
        }
    }

    public void TriggerAttack(KeyState keyState, Vector3 Target)
    {
        if (Weapon == null || Attacker == null)
        {
            Attacker = null;
            WeaponData = null;
            return;
        }

        if (keyState == KeyState.Start)
        {
            if (CanAttack && CurrentState == AttackState.Idle)
            {
                MouseTarget = Target; // 同步鼠标目标位置

                Attacker?.StartAttack();
                StartAttack();
                OnStartAttack?.Invoke(); // 触发攻击开始事件
            }
        }
        else if (keyState == KeyState.End || CanAttack == false)
        {
            if (CurrentState == AttackState.Attacking)
            {
                MouseTarget = Target; // 同步鼠标目标位置
                if (Attacker != null)
                {
                    Attacker.EndAttack();
                }
                StopAttack();
                OnEndAttack?.Invoke(); // 触发攻击结束事件
            }
        }
        else if (keyState == KeyState.Hold)
        {
            if (CanAttack && CurrentState == AttackState.Attacking)
            {
                MouseTarget = Target; // 同步鼠标目标位置
                if (Attacker != null)
                {
                    Attacker.UpdateAttack();
                }
                StayTriggerAttack();
                OnStayAttack?.Invoke(); // 触发攻击持续事件
            }
        }
    }

    public void SetWeaponData(IColdWeapon Item_)
    {
        WeaponData = Item_;
    }

    public void RemoveWeaponData(Item Item_)
    {
        WeaponData = null;
    }

    public void SetAttacker(IAttackState attacker_)
    {
        Attacker = attacker_;
    }

    public void RemoveAttacker(IAttackState attacker_)
    {
        Attacker = null;
    }

    #endregion

    #region (刺击、返回、检查攻击状态)私有方法

    public void PerformStab(Vector2 startTarget, float speed, float maxDistance)
    {
        if (HitStop)
        {
            speed = 0;
        }
        // 坐标系转换：如果有父对象，则将鼠标目标转换为本地坐标
        Vector2 mouseTargetLocal = transform.parent != null ?
            (Vector2)transform.parent.InverseTransformPoint(MouseTarget) :
            (Vector2)MouseTarget;

        Vector2 currentLocalPos = (Vector2)transform.localPosition;

        // 计算从起始点到目标点的向量及其长度
        Vector2 toTarget = mouseTargetLocal - startTarget;
        float targetDistance = toTarget.magnitude;

        // 若目标超出最大范围，则先对目标进行限制
        if (targetDistance > maxDistance)
        {
            mouseTargetLocal = startTarget + toTarget.normalized * maxDistance;
        }

        // 计算移动方向
        Vector2 direction = (mouseTargetLocal - currentLocalPos).normalized;
        // 本帧移动的距离
        float distanceToMove = speed * Time.deltaTime;
        // 计算新的位置
        Vector2 newPosition = currentLocalPos + direction * distanceToMove;

        // 判断剩余距离，若过近则直接设置为目标位置（并确保不超出最大范围）
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
            // 常规移动时检查是否超过最大范围
            float currentDistance = Vector2.Distance(startTarget, newPosition);
            if (currentDistance > maxDistance)
            {
                newPosition = startTarget + (newPosition - startTarget).normalized * maxDistance;
            }
        }

        // 更新本地位置
        transform.localPosition = newPosition;

        // 调试绘制，从起始点到当前位置的连线（转换为世界坐标）
        Debug.DrawLine(
            transform.parent.TransformPoint(startTarget),
            transform.parent.TransformPoint(newPosition),
            Color.red
        );
    }

    private IEnumerator ReturnToStartPositionCoroutine(Vector2 startTarget, float backSpeed)
    {

        while (Vector2.Distance(startTarget, transform.localPosition) >= 0.1f)
        {
            float distanceToMoveBack = backSpeed * Time.deltaTime;
            Vector2 directionBack = (startTarget - (Vector2)transform.localPosition).normalized;
            transform.localPosition = (Vector2)transform.localPosition + directionBack * distanceToMoveBack;
            yield return null;
        }

        transform.localPosition = startTarget;
        CurrentState = AttackState.Idle;
    }

    public void StartReturningToStartPosition(Vector2 startTarget, float backSpeed)
    {
        if (returnCoroutine != null)
        {
            StopCoroutine(returnCoroutine);
        }
        returnCoroutine = StartCoroutine(ReturnToStartPositionCoroutine(startTarget, backSpeed));
    }

    #endregion

    #region 武器攻击状态的切换
    public void StartAttack()
    {
        CurrentState = AttackState.Attacking;
        StartPosition = transform.localPosition;
    }

    public void StayTriggerAttack()
    {
        // 直接使用EffectiveAttackSpeed和EffectiveMaxAttackDistance
        PerformStab(StartPosition, WeaponData.AttackSpeed, WeaponData.MaxAttackDistance);
    }

    public void StopAttack()
    {

#if UNITY_EDITOR
        if (!gameObject.activeInHierarchy) return;
#endif
        CurrentState = AttackState.Returning;
        StartReturningToStartPosition(StartPosition, WeaponData.ReturnSpeed);
    }

    public override void Save()
    {
        //throw new System.NotImplementedException();
        //在此处清除Dowten的使用
        DOTween.Kill(transform);
    }
    #endregion


    public enum AttackState
    {
        Idle,
        Attacking,
        Returning
    }
}

public interface IRotationSpeed
{
    float RotationSpeed { get; set; }
}
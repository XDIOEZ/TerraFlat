using NaughtyAttributes;
using System.Collections;
using UltEvents;
using UnityEditor;
using UnityEngine;

public class AttackTrigger : MonoBehaviour, ITriggerAttack ,IRotationSpeed
{
    public float DefaultRotationSpeed = 360f;
    #region 事件
    // 事件--委托
    public UltEvent onStartAttack;
    public UltEvent onStayAttack;
    public UltEvent onEndAttack;
    // 事件--属性
    public UltEvent OnStartAttack { get=> onStartAttack; set=> onStartAttack = value; }
    public UltEvent OnStayAttack { get=> onStayAttack; set=> onStayAttack = value; }
    public UltEvent OnEndAttack { get=> onEndAttack; set=> onEndAttack = value; }
    #endregion

    #region 当前使用的武器对象
    [ShowNonSerializedField]
    GameObject weaponGameObject;

    public GameObject Weapon_GameObject
    {
        get
        {
            return weaponGameObject;
        }
        set
        {
            weaponGameObject = value;
        }
    
    } // 武器对象

    public void SetWeapon(GameObject _weapon)
    {
        Weapon_GameObject = _weapon;
        Attacker = weaponGameObject.GetComponent<IAttackState>();
        WeaponData = weaponGameObject.GetComponent<IColdWeapon>();
        if (_weapon.GetComponentInChildren<IDamageSender>()!= null)
        _weapon.GetComponentInChildren<IDamageSender>().OnDamage += karou;
    }
    bool HitStop = false;
    void karou(float damage)
    {
        if(damage>1)
        StartCoroutine(HitPauseTransform());
    }

    IEnumerator HitPauseTransform()
    {
        if (Weapon_GameObject == null) yield break;
        float rotationSpeed = RotationSpeed;
        HitStop = true;
        RotationSpeed= 0;

        yield return new WaitForSeconds(0.15f);

        HitStop = false;  
        RotationSpeed = rotationSpeed;
    }




    #endregion

    #region 挂接的武器数据和行为接口

    // 挂接的攻击行为接口
    [ShowNonSerializedField]
    private IAttackState attacker;
 
    public IAttackState Attacker
    {
        get
        {
            return attacker;
        }
        set
        {
            attacker = value;
        }
    }
    [ShowNonSerializedField]
    // 挂接的武器数据
    private IColdWeapon weapon;
    public IColdWeapon WeaponData
    {
        get
        {
            return weapon;
        }

        set
        {
            weapon = value;
        }   
    }

    public float RotationSpeed
    {
        get
        {
            if (WeaponData == null)
            {
                return DefaultRotationSpeed;
            }
          return  WeaponData.SpinSpeed;
        }

        set
        {
            WeaponData.SpinSpeed = value;
        }
    }

    #endregion

    #region 攻击行为的状态参数
    public Vector2 StartPosition; // 保存初始位置
    public Vector3 MouseTarget;
    public bool CanAttack;
    public AttackState CurrentState = AttackState.Idle; // 当前状态
    private Coroutine returnCoroutine;
    #endregion

    #region (注册攻击
    private void Start()
    {
        StaminaManager staminaManager = transform.parent.GetComponentInChildren<StaminaManager>();
        if (staminaManager != null)
        {
            // 使用EffectiveStaminaCost保证武器数据更新时精力消耗值也跟着更新
            OnStartAttack += () => staminaManager.StartReduceStamina(WeaponData.EnergyCostSpeed, "AttackTrigger");
            OnEndAttack += () => staminaManager.StopReduceStamina("AttackTrigger");
            staminaManager.OnStaminaChanged += SetCanAttack; // 注册精力值变化事件
        }
    }
    #endregion

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
        if (Weapon_GameObject == null || Attacker == null)
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

    public void StopTriggerAttack()
    {
        throw new System.NotImplementedException();
    }

    public void StartTriggerAttack()
    {
        throw new System.NotImplementedException();
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
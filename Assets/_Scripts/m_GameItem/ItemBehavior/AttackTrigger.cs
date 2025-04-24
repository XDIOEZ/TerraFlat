using NaughtyAttributes;
using System.Collections;
using UltEvents;
using UnityEditor;
using UnityEngine;

public class AttackTrigger : MonoBehaviour, ITriggerAttack ,IRotationSpeed
{
    public float DefaultRotationSpeed = 360f;
    #region �¼�
    // �¼�--ί��
    public UltEvent onStartAttack;
    public UltEvent onStayAttack;
    public UltEvent onEndAttack;
    // �¼�--����
    public UltEvent OnStartAttack { get=> onStartAttack; set=> onStartAttack = value; }
    public UltEvent OnStayAttack { get=> onStayAttack; set=> onStayAttack = value; }
    public UltEvent OnEndAttack { get=> onEndAttack; set=> onEndAttack = value; }
    #endregion

    #region ��ǰʹ�õ���������
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
    
    } // ��������

    public void SetWeapon(GameObject _weapon)
    {
        Weapon_GameObject = _weapon;
        Attacker = weaponGameObject.GetComponent<IAttacker>();
        WeaponData = weaponGameObject.GetComponent<IColdWeapon>();
    }
    #endregion

    #region �ҽӵ��������ݺ���Ϊ�ӿ�

    // �ҽӵĹ�����Ϊ�ӿ�
    [ShowNonSerializedField]
    private IAttacker attacker;
 
    public IAttacker Attacker
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
    // �ҽӵ���������
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

    #region ������Ϊ��״̬����
    public Vector2 StartPosition; // �����ʼλ��
    public Vector3 MouseTarget;
    public bool CanAttack;
    public AttackState CurrentState = AttackState.Idle; // ��ǰ״̬
    private Coroutine returnCoroutine;
    #endregion

    #region (ע�ṥ��
    private void Start()
    {
        StaminaManager staminaManager = transform.parent.GetComponentInChildren<StaminaManager>();
        if (staminaManager != null)
        {
            // ʹ��EffectiveStaminaCost��֤�������ݸ���ʱ��������ֵҲ���Ÿ���
            OnStartAttack += () => staminaManager.StartReduceStamina(WeaponData.EnergyCostSpeed, "AttackTrigger");
            OnEndAttack += () => staminaManager.StopReduceStamina("AttackTrigger");
            staminaManager.OnStaminaChanged += SetCanAttack; // ע�ᾫ��ֵ�仯�¼�
        }
    }
    #endregion

    #region (�����Ƿ���Թ���, ��������)�ⲿ�ӿ�

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
                MouseTarget = Target; // ͬ�����Ŀ��λ��

                Attacker?.AttackStart();
                StartAttack();
                OnStartAttack?.Invoke(); // ����������ʼ�¼�
            }
        }
        else if (keyState == KeyState.End || CanAttack == false)
        {
            if (CurrentState == AttackState.Attacking)
            {
                MouseTarget = Target; // ͬ�����Ŀ��λ��
                if (Attacker != null)
                {
                    Attacker.AttackEnd();
                }
                StopAttack();
                OnEndAttack?.Invoke(); // �������������¼�
            }
        }
        else if (keyState == KeyState.Hold)
        {
            if (CanAttack && CurrentState == AttackState.Attacking)
            {
                MouseTarget = Target; // ͬ�����Ŀ��λ��
                if (Attacker != null)
                {
                    Attacker.AttackUpdate();
                }
                StayAttack();
                OnStayAttack?.Invoke(); // �������������¼�
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

    public void SetAttacker(IAttacker attacker_)
    {
        Attacker = attacker_;
    }

    public void RemoveAttacker(IAttacker attacker_)
    {
        Attacker = null;
    }

    #endregion

    #region (�̻������ء���鹥��״̬)˽�з���

    public void PerformStab(Vector2 startTarget, float speed, float maxDistance)
    {
        // ����ϵת��������и����������Ŀ��ת��Ϊ��������
        Vector2 mouseTargetLocal = transform.parent != null ?
            (Vector2)transform.parent.InverseTransformPoint(MouseTarget) :
            (Vector2)MouseTarget;

        Vector2 currentLocalPos = (Vector2)transform.localPosition;

        // �������ʼ�㵽Ŀ�����������䳤��
        Vector2 toTarget = mouseTargetLocal - startTarget;
        float targetDistance = toTarget.magnitude;

        // ��Ŀ�곬�����Χ�����ȶ�Ŀ���������
        if (targetDistance > maxDistance)
        {
            mouseTargetLocal = startTarget + toTarget.normalized * maxDistance;
        }

        // �����ƶ�����
        Vector2 direction = (mouseTargetLocal - currentLocalPos).normalized;
        // ��֡�ƶ��ľ���
        float distanceToMove = speed * Time.deltaTime;
        // �����µ�λ��
        Vector2 newPosition = currentLocalPos + direction * distanceToMove;

        // �ж�ʣ����룬��������ֱ������ΪĿ��λ�ã���ȷ�����������Χ��
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
            // �����ƶ�ʱ����Ƿ񳬹����Χ
            float currentDistance = Vector2.Distance(startTarget, newPosition);
            if (currentDistance > maxDistance)
            {
                newPosition = startTarget + (newPosition - startTarget).normalized * maxDistance;
            }
        }

        // ���±���λ��
        transform.localPosition = newPosition;

        // ���Ի��ƣ�����ʼ�㵽��ǰλ�õ����ߣ�ת��Ϊ�������꣩
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

    #region ��������״̬���л�
    public void StartAttack()
    {
        CurrentState = AttackState.Attacking;
        StartPosition = transform.localPosition;
    }

    public void StayAttack()
    {
        // ֱ��ʹ��EffectiveAttackSpeed��EffectiveMaxAttackDistance
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
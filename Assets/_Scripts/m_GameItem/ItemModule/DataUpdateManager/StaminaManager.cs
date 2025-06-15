using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// ��������ϵͳ�����ڹ�����Ҿ���ֵ�Ļָ������ģ�֧�ֶ���Դ���������¼�����
/// </summary>
public class StaminaManager : MonoBehaviour,IChangeStamina
{
    #region �����ֶ�

    // �����������ʵļ��ϣ�֧�ֶ������ͬʱӰ��
    [ShowNonSerializedField]
    public Dictionary<string, float> staminaReductionRates = new Dictionary<string, float>();

    // �����ָ����ʵļ��ϣ�֧�ֶ����Դ�Ļָ�����
    [ShowNonSerializedField]
    public Dictionary<string, float> staminaRecoveryRates = new Dictionary<string, float>();

    // ��ǰ�Ƿ��ھ���ֵΪ 0 ��״̬
    private bool isAtZero = false;

    // ��ǰ�ָܻ�����
    public float allRecoverySpeed = 0f;

    // ��ǰ����������
    public float allReduceSpeed = 0f;

    // ��������ʱ�������¼������ݼ���ֵ����Դ����
    public UltEvent<float, string> onStaminaReduce = new UltEvent<float, string>();

    // �����ָ�ʱ�������¼������ݻָ�ֵ����Դ����
    public UltEvent<float, string> onStaminaRecovery = new UltEvent<float, string>();

    // �����״κľ��������¼�
    public UltEvent OnEnterZeroStamina;

    // ��������Ϊ 0 ʱ�������¼���ÿ֡��
    public UltEvent OnStayZeroStamina;

    // ������ 0 �ָ�ʱ�������¼�
    public UltEvent OnExitZeroStamina;

    // ���������仯ʱ�������¼������ݱ仯ֵ��
    public UltEvent<float> OnStaminaChanged;

    // ���о������ݵĶ���ӿڣ����� Player��
    private IStamina _stamina;

    #endregion

    #region ����

    public UltEvent<float, string> OnStaminaReduce { get => onStaminaReduce; set => onStaminaReduce = value; }
    public UltEvent<float, string> OnStaminaRecovery { get => onStaminaRecovery; set => onStaminaRecovery = value; }

    // ��ǰ����ֵ���Զ���װ�����Ʒ�Χ���¼�������
    public float CurrentStamina
    {
        get => CurrentStamina1;
        set
        {
            float clamped = ClampStamina(value);
            CurrentStamina1 = clamped;

            // �����仯�¼�
            OnStaminaChanged?.Invoke(clamped);

            // ����Ƿ����/�˳��㾫��״̬
            if (clamped <= 0)
            {
                if (!isAtZero)
                {
                    OnEnterZeroStamina.Invoke();
                    isAtZero = true;
                }
            }
            else
            {
                if (isAtZero)
                {
                    OnExitZeroStamina.Invoke();
                    isAtZero = false;
                }
            }
        }
    }

    public float MaxStamina
    {
        get => MaxStamina1;
        set => MaxStamina1 = value;
    }

    // �Ƿ�Ϊ�㾫��״̬
    public bool IsAtZero
    {
        get => isAtZero;
        set => isAtZero = value;
    }

    // ��ȡ�����ð󶨵��������ݽӿڣ��Զ��Ӹ������ң�
    public IStamina Stamina
    {
        get
        {
            if (_stamina == null)
            {
                _stamina = GetComponentInParent<IStamina>();
            }
            return _stamina;
        }
        set => _stamina = value;
    }

    // �������ֵ����
    public float MaxStamina1
    {
        get => Stamina.MaxStamina;
        set => Stamina.MaxStamina = value;
    }

    // ������ǰֵ����
    public float CurrentStamina1
    {
        get => Stamina.Stamina;
        set => Stamina.Stamina = value;
    }

    #endregion

    #region Unity ��������


    private void OnEnable()
    {
        // ע�ᾫ���仯���¼�������
        OnStaminaReduce -= StartReduceStamina;
        OnStaminaReduce += StartReduceStamina;

        OnStaminaRecovery -= StartRecoverStamina;
        OnStaminaRecovery += StartRecoverStamina;

        // ע�ᾫ��״̬�¼�
        OnEnterZeroStamina += () => { Debug.Log("��ɫ����ֵ�ľ�����ʼ���ľ�������"); };
        OnStayZeroStamina += () => { Debug.Log("��ɫ����ֵ�ľ����������ľ�������"); };
        OnExitZeroStamina += () => { Debug.Log("��ɫ����ֵ��ʼ�ָ���"); };
    }

    public void Start()
    {
        //���Ĭ�ϻָ���ֵ
        StartRecoverStamina(Stamina.StaminaRecoverySpeed, "Ĭ�ϻָ�");
    }

    private void OnDisable()
    {
        // ȡ���¼����ģ�����״̬
        if (onStaminaReduce != null) onStaminaReduce -= StartReduceStamina;
        if (onStaminaRecovery != null) onStaminaRecovery -= StartRecoverStamina;

        staminaReductionRates.Clear();
        staminaRecoveryRates.Clear();
        allReduceSpeed = 0f;
        allRecoverySpeed = 0f;
    }

    private void FixedUpdate()
    {
        // ���������ָܻ� - ������ �����¾���ֵ
        float valueSpeed = (allRecoverySpeed - allReduceSpeed) * Time.fixedDeltaTime;
        CurrentStamina += valueSpeed;

        // �������ľ������������ľ��¼�
        if (CurrentStamina <= 0 && isAtZero)
        {
            OnStayZeroStamina?.Invoke();
        }
    }

    #endregion

    #region ��������

    [Button("��ʼ��������")]
    public void StartReduceStamina(float reductionSpeed, string reductionName)
    {
        if (string.IsNullOrEmpty(reductionName))
        {
            reductionName = Time.time.ToString(); // ʹ��ʱ�����ΪĬ������
        }

        // �����ھ����ͣ��ȿ۳��ٸ��£���ֹ�ۼ����
        if (staminaReductionRates.ContainsKey(reductionName))
        {
            allReduceSpeed -= staminaReductionRates[reductionName];
        }

        staminaReductionRates[reductionName] = reductionSpeed;
        allReduceSpeed += reductionSpeed;
    }

    [Button("ֹͣ��������")]
    public void StopReduceStamina(string reductionType)
    {
        if (!staminaReductionRates.ContainsKey(reductionType))
        {
            Debug.Log("�ֵ��в����ڸü�ֵ�ԣ�");
            return;
        }

        allReduceSpeed -= staminaReductionRates[reductionType];
        staminaReductionRates.Remove(reductionType);
    }

    [Button("��ʼ�����ָ�")]
    public void StartRecoverStamina(float recoverySpeed, string recoveryType)
    {
        if (staminaRecoveryRates.ContainsKey(recoveryType))
        {
            allRecoverySpeed -= staminaRecoveryRates[recoveryType];
        }

        staminaRecoveryRates[recoveryType] = recoverySpeed;
        allRecoverySpeed += recoverySpeed;
    }

    [Button("ֹͣ�����ָ�")]
    public void StopRecoverStamina(string recoveryType)
    {
        if (!staminaRecoveryRates.ContainsKey(recoveryType))
        {
            Debug.Log("�ָ����Ͳ����ڣ�");
            return;
        }

        allRecoverySpeed -= staminaRecoveryRates[recoveryType];
        staminaRecoveryRates.Remove(recoveryType);
    }

    #endregion

    #region ˽�з���

    // ���ƾ���ֵ��Χ
    private float ClampStamina(float value)
    {
        return Mathf.Clamp(value, 0f, MaxStamina);
    }

    #endregion
}

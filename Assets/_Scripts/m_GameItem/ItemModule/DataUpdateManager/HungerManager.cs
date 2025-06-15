using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// ����ֵ����ϵͳ�����ڹ����ɫ����ֵ�Ļָ������ģ�֧�ֶ���Դ���������¼�����
/// 
/// �ù����������� IHunger �ӿڣ����д洢�� Foods ���ݣ�������ǰ Food �����ֵ MaxFood����
/// �Լ� EatingSpeed����ΪĬ�ϻָ����ʣ���
/// </summary>
public class HungerManager : MonoBehaviour, IChangeHungry
{
    #region �����ֶ�

    // �����������ʵļ��ϣ�֧�ֶ����ԴͬʱӰ��
    [ShowNonSerializedField]
    public Dictionary<string, float> hungerReductionRates = new Dictionary<string, float>();

    // �����ָ����ʵļ��ϣ�֧�ֶ����Դ�Ļָ�����
    [ShowNonSerializedField]
    public Dictionary<string, float> hungerRecoveryRates = new Dictionary<string, float>();

    // ��ǰ�Ƿ��ڼ���ֵΪ 0 ��״̬
    private bool isAtZero = false;

    // ��ǰ�ָܻ�����
    public float allRecoverySpeed = 0f;

    // ��ǰ����������
    public float allReduceSpeed = 0f;

    // ��������ʱ�������¼������ݼ���ֵ����Դ����
    public UltEvent<float, string> onHungerReduce = new UltEvent<float, string>();

    // �����ָ�ʱ�������¼������ݻָ�ֵ����Դ����
    public UltEvent<float, string> onHungerRecovery = new UltEvent<float, string>();

    // �����״κľ��������¼�
    public UltEvent OnEnterZeroHunger;

    // ��������Ϊ 0 ʱ�������¼���ÿ֡��
    public UltEvent OnStayZeroHunger;

    // ������ 0 �ָ�ʱ�������¼�
    public UltEvent OnExitZeroHunger;

    // ����ֵ�����仯ʱ�������¼������ݱ仯ֵ��
    public UltEvent<float> OnHungerChanged;

    // ���м������ݵĶ���ӿڣ����� Player�������� Foods �洢��ǰ������ֵ
    private IHunger _hunger;

    #endregion

    #region ����

    public UltEvent<float, string> OnHungerReduce { get => onHungerReduce; set => onHungerReduce = value; }
    public UltEvent<float, string> OnHungerRecovery { get => onHungerRecovery; set => onHungerRecovery = value; }

    /// <summary>
    /// ��ǰ����ֵ��ͨ�� IHunger �ӿڵ� Foods.Food ��ȡ�������Ʒ�Χ�봥��״̬�仯�¼���
    /// </summary>
    public float CurrentHunger
    {
        get => CurrentHunger1;
        set
        {
            float clamped = ClampHunger(value);
            CurrentHunger1 = clamped;
            OnHungerChanged?.Invoke(clamped);

            // ����Ƿ������˳������ľ�״̬
            if (clamped <= 0)
            {
                if (!isAtZero)
                {
                    OnEnterZeroHunger?.Invoke();
                    isAtZero = true;
                }
            }
            else
            {
                if (isAtZero)
                {
                    OnExitZeroHunger?.Invoke();
                    isAtZero = false;
                }
            }
        }
    }

    /// <summary>
    /// �������ֵ����ͨ�� IHunger �ӿڵ� Foods.MaxFood ��ȡ������
    /// </summary>
    public float MaxHunger
    {
        get => MaxHunger1;
        set => MaxHunger1 = value;
    }

    /// <summary>
    /// �Ƿ��ڼ���ֵ�ľ�״̬
    /// </summary>
    public bool IsAtZero
    {
        get => isAtZero;
        set => isAtZero = value;
    }

    /// <summary>
    /// ��ȡ�����ð󶨵ļ������ݽӿڣ��Զ��Ӹ������ң�
    /// </summary>
    public IHunger Hunger
    {
        get
        {
            if (_hunger == null)
            {
                _hunger = GetComponentInParent<IHunger>();
            }
            return _hunger;
        }
        set => _hunger = value;
    }

    /// <summary>
    /// �������ֵ����ͨ�� IHunger �� Foods.MaxFood ��ȡ/���ã�
    /// </summary>
    public float MaxHunger1
    {
        get => Hunger.Foods.MaxFood;
        set => Hunger.Foods.MaxFood = value;
    }

    /// <summary>
    /// ������ǰֵ����ͨ�� IHunger �� Foods.Food ��ȡ/���ã�
    /// </summary>
    public float CurrentHunger1
    {
        get => Hunger.Foods.Food;
        set => Hunger.Foods.Food = value;
    }

    #endregion

    #region Unity ��������

    private void OnEnable()
    {
        // ע�ἢ���仯���¼�������
        onHungerReduce -= StartReduceHunger;
        onHungerReduce += StartReduceHunger;

        onHungerRecovery -= StartRecoverHunger;
        onHungerRecovery += StartRecoverHunger;

        // ע�ἢ��״̬��ص��¼��ص�
     //   OnEnterZeroHunger += () => { Debug.Log("��ɫ����ֵ�ľ���"); };
      //  OnStayZeroHunger += () => { Debug.Log("��ɫ���������У�"); };
      //  OnExitZeroHunger += () => { Debug.Log("��ɫ����ֵ�õ��ָ���"); };
    }

    private void Start()
    {
        // ���Ĭ�ϻָ���ֵ��������� EatingSpeed ��ʾĬ�ϻָ����ʣ�������Ҫ������
        StartReduceHunger(Hunger.Foods.MaxFood*0.02f, "Ĭ�ϻָ�");
    }

    private void OnDisable()
    {
        if (onHungerReduce != null) onHungerReduce -= StartReduceHunger;
        if (onHungerRecovery != null) onHungerRecovery -= StartRecoverHunger;

        hungerReductionRates.Clear();
        hungerRecoveryRates.Clear();
        allReduceSpeed = 0f;
        allRecoverySpeed = 0f;
    }

    private void FixedUpdate()
    {
        // ���������ָܻ����������������ʲ�ֵ���¼���ֵ
        float valueSpeed = (allRecoverySpeed - allReduceSpeed) * Time.fixedDeltaTime;
        CurrentHunger += valueSpeed;

        // ����ֵ�ľ�ʱ�����������ľ�״̬�¼�
        if (CurrentHunger <= 0 && isAtZero)
        {
            OnStayZeroHunger?.Invoke();
        }
    }

    #endregion

    #region ��������

    [Button("��ʼ��������")]
    public void StartReduceHunger(float reductionSpeed, string reductionName)
    {
        if (string.IsNullOrEmpty(reductionName))
        {
            reductionName = Time.time.ToString(); // ʹ��ʱ�����ΪĬ�����ͱ�ʶ
        }

        // ���Ѵ�����ͬ���ͣ������Ƴ�ԭ�����ʣ���ֹ�ۼ����
        if (hungerReductionRates.ContainsKey(reductionName))
        {
            allReduceSpeed -= hungerReductionRates[reductionName];
        }

        hungerReductionRates[reductionName] = reductionSpeed;
        allReduceSpeed += reductionSpeed;
    }

    [Button("ֹͣ��������")]
    public void StopReduceHunger(string reductionType)
    {
        if (!hungerReductionRates.ContainsKey(reductionType))
        {
            Debug.Log("�ֵ��в����ڸü����������ͣ�");
            return;
        }

        allReduceSpeed -= hungerReductionRates[reductionType];
        hungerReductionRates.Remove(reductionType);
    }

    [Button("��ʼ�����ָ�")]
    public void StartRecoverHunger(float recoverySpeed, string recoveryType)
    {
        if (hungerRecoveryRates.ContainsKey(recoveryType))
        {
            allRecoverySpeed -= hungerRecoveryRates[recoveryType];
        }

        hungerRecoveryRates[recoveryType] = recoverySpeed;
        allRecoverySpeed += recoverySpeed;
    }

    [Button("ֹͣ�����ָ�")]
    public void StopRecoverHunger(string recoveryType)
    {
        if (!hungerRecoveryRates.ContainsKey(recoveryType))
        {
            Debug.Log("�ֵ��в����ڸü����ָ����ͣ�");
            return;
        }

        allRecoverySpeed -= hungerRecoveryRates[recoveryType];
        hungerRecoveryRates.Remove(recoveryType);
    }

    #endregion

    #region ˽�з���

    // ���Ƽ���ֵ�� 0 �� MaxHunger ֮��
    private float ClampHunger(float value)
    {
        return Mathf.Clamp(value, 0f, MaxHunger);
    }

    #endregion
}

using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mover �����ڴ�����Ϸ������ƶ��߼���
/// </summary>
public class Mover : Organ, IFunction_Move
{
    #region �ֶ�
    [Header("�ƶ�����")]
    /// <summary>
    /// �ٶ���Դ���洢�ٶ������Ϣ
    /// </summary>
    [Tooltip("�ٶ���Դ")]
    private ISpeed speedSource;

    /// <summary>
    /// �ٶ�˥�����ʣ�����������ٵĿ���
    /// </summary>
    [Tooltip("�ٶ�˥������")]
    public float slowDownSpeed = 5f;

    /// <summary>
    /// ֹͣʱ���ٶȣ����ٶ�С�ڸ�ֵʱ����ֹͣ�ƶ�
    /// </summary>
    [Tooltip("ֹͣʱ���ٶ�")]
    public float endSpeed = 0.1f;

    /// <summary>
    /// �ϴ��ƶ��ķ������ڼ�¼�ƶ�����ı仯
    /// </summary>
    private Vector2 _lastDirection = Vector2.zero;

    /// <summary>
    /// ����Э�̣����ڿ�������ļ��ٹ���
    /// </summary>
    private Coroutine _slowDownCoroutine;

    /// <summary>
    /// ��ʶ�����Ƿ������ƶ�
    /// </summary>
    [Tooltip("�Ƿ������ƶ�")]
    public bool IsMoving;

    /// <summary>
    /// �ƶ������¼���������ֹͣ�ƶ�ʱ����
    /// </summary>
    [Tooltip("�ƶ������¼�")]
    public UltEvent OnMoveEnd;

    /// <summary>
    /// �ƶ������¼����������ƶ������г�������
    /// </summary>
    [Tooltip("�ƶ������¼�")]
    public UltEvent OnMoveStay;

    /// <summary>
    /// �ƶ���ʼ�¼��������忪ʼ�ƶ�ʱ����
    /// </summary>
    [Tooltip("�ƶ���ʼ�¼�")]
    public UltEvent OnMoveStart;

    /// <summary>
    /// �ٶȱ仯�ֵ䣬�洢�����ٶȱ仯����Ϣ
    /// </summary>
    [ShowNonSerializedField]
    [Tooltip("�ٶȱ仯�ֵ�")]
    public Dictionary<(string, ValueChangeType, float), float> SpeedChangeDict = new Dictionary<(string, ValueChangeType, float), float>();

    /// <summary>
    /// 2D������������ڿ�������������ƶ�
    /// </summary>
    private Rigidbody2D _rb;
    #endregion

    #region ����
    /// <summary>
    /// ��ȡ������ Rigidbody2D �������δ��ֵ��ͨ�����߷������ҡ�
    /// </summary>
    public Rigidbody2D Rb
    {
        get => _rb ??= XDTool.GetComponentInChildrenAndParent<Rigidbody2D>(gameObject);
        private set => _rb = value;
    }

    /// <summary>
    /// ��ȡ�����õ�ǰ���ƶ��ٶȡ�
    /// </summary>
    public float Speed
    {
        get => Rb ? Rb.linearVelocity.magnitude : 0f;
        set => Rb.linearVelocity = Rb.linearVelocity.normalized * value;
    }

    /// <summary>
    /// ��ȡ�������ƶ��ٶȣ��ᴥ���ٶ����¼��㡣
    /// </summary>
    public float MoveSpeed
    {
        get
        {
            return SpeedSource.Speed;
        }
        set
        {
            SpeedSource.Speed = value;
        }
    }

    /// <summary>
    /// ��ȡ�ٶ���Դ����δ��ʼ����Ӹ������в���
    /// </summary>
    [ShowNativeProperty]
    public ISpeed SpeedSource
    {
        get
        {
            if (speedSource == null)
            {
                speedSource = transform.parent.GetComponentInParent<ISpeed>();
            }
            return speedSource;
        }
    }

    public IChangeStamina changeStamina { get; set; }

    /// <summary>
    /// ��ȡ������Ĭ���ƶ��ٶ�
    /// </summary>
    public float DefaultMoveSpeed { get => SpeedSource.DefaultSpeed; set => SpeedSource.DefaultSpeed = value; }
    #endregion

    #region Unity ����
    /// <summary>
    /// ��ʼ������������ Rigidbody2D ��������ӳ�ʼ�ٶȱ仯��
    /// </summary>
    private void Start()
    {
       
        if (Rb == null)
        {
            Rb = GetComponentInParent<Rigidbody2D>();
        }

        changeStamina = transform.parent.GetComponentInChildren<IChangeStamina>();

        OnMoveStart += () => { changeStamina.StartReduceStamina(20, "�ƶ�"); };
        OnMoveEnd += () => { changeStamina.StopReduceStamina("�ƶ�"); };
      //  AddSpeedChange(("����", ValueChangeType.Add, 0), +DefaultMoveSpeed);
    }

    /// <summary>
    /// ��������ʱ�� Rigidbody2D �����ÿա�
    /// </summary>
    private void OnDestroy()
    {
        _rb = null;
    }
    #endregion

    #region ��������
    /// <summary>
    /// �����ƶ��������ٶȡ�
    /// </summary>
    /// <param name="direction">�ƶ�����</param>
    public void Move(Vector2 direction)
    {
        if (direction == Vector2.zero)
        {
            Rb.linearVelocity = Vector2.zero;
            if (IsMoving)
            {
                IsMoving = false;
                OnMoveEnd?.Invoke();
            }
            return;
        }

        if (IsMoving == false)
        {
            IsMoving = true;
            OnMoveStart?.Invoke();
        }

        if (direction != _lastDirection)
        {
            direction.Normalize();
            _lastDirection = direction;
        }

        Vector2 adjustedVelocity = direction * MoveSpeed;
        Rb.linearVelocity = adjustedVelocity;
    }

    /// <summary>
    /// ���ٶȱ仯�ֵ�������һ���ٶȱ仯������¼����ƶ��ٶȡ�
    /// </summary>
    /// <param name="Sign_Type_Priority">������ʶ���仯���ͺ����ȼ���Ԫ��</param>
    /// <param name="Value">�仯��ֵ</param>
    public void AddSpeedChange((string, ValueChangeType, float) Sign_Type_Priority, float Value)
    {
        // �ȼ���Ƿ��Ѿ�����
        if (SpeedChangeDict.ContainsKey(Sign_Type_Priority))
        {
            //Debug.LogWarning("�Ѿ�������ͬ�ĸı����ͣ�");
            return;
        }
        if (Sign_Type_Priority.Item2 == ValueChangeType.Add)
        {
            SpeedChangeDict[Sign_Type_Priority] = Value;
        }
        if (Sign_Type_Priority.Item2 == ValueChangeType.Multiply)
        {
            SpeedChangeDict[Sign_Type_Priority] = Value;
        }
        RecalculateMoveSpeed();
    }

    /// <summary>
    /// ���ٶȱ仯�ֵ����Ƴ�һ���ٶȱ仯������¼����ƶ��ٶȡ�
    /// </summary>
    /// <param name="Sign_Type_Priority">������ʶ���仯���ͺ����ȼ���Ԫ��</param>
    public void RemoveSpeedChange((string, ValueChangeType, float) Sign_Type_Priority)
    {
        if (SpeedChangeDict.ContainsKey(Sign_Type_Priority))
        {
            SpeedChangeDict.Remove(Sign_Type_Priority);
        }
        RecalculateMoveSpeed();
    }
    #endregion

    #region ˽�д���
    /// <summary>
    /// �����ٶȱ仯�ֵ����¼����ƶ��ٶȡ�
    /// </summary>
    private void RecalculateMoveSpeed()
    {
        // ���Ĭ���ٶ��Ƿ���Ч
        if (DefaultMoveSpeed <= 0)
        {
            Debug.Log(DefaultMoveSpeed + " ����Ч��Ĭ���ٶ�");
            return;
        }

        // �����ٶ�
        MoveSpeed = DefaultMoveSpeed;

        // ʹ���б��洢�ֵ��е�Ԫ�أ����������ȼ�����
        var changeList = new List<(string, ValueChangeType, float, float)>();

        // ���ֵ�����ת��Ϊ�б����������ȼ���Ϣ��Item3��
        foreach (var item in SpeedChangeDict)
        {
            changeList.Add((item.Key.Item1, item.Key.Item2, item.Key.Item3, item.Value));
        }

        // �������ȼ���Item3�������б������ȼ�С���ȴ���
        changeList.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        // ��������˳��ִ�мӷ��ͳ˷�
        foreach (var item in changeList)
        {
            var changeType = item.Item2;
            var value = item.Item4;

            // �����ӷ��仯
            if (changeType == ValueChangeType.Add)
            {
                if (value < 0)
                {
                    Debug.Log(value + " ����Ч�ļӷ��仯ֵ");
                    return;
                }
                MoveSpeed += value;
            }
            // �����˷��仯
            else if (changeType == ValueChangeType.Multiply)
            {
                if (value <= 0)
                {
                    Debug.Log(value + " ����Ч�ĳ˷��仯ֵ");
                    return;
                }
                MoveSpeed *= value;
            }
        }

       // Debug.Log("��ǰ�ٶ�: " + MoveSpeed);
    }

    /// <summary>
    /// ��������Э�̡�
    /// </summary>
    private void StartSlowDownRoutine()
    {
        if (_slowDownCoroutine != null)
        {
            StopCoroutine(_slowDownCoroutine);
        }
        _slowDownCoroutine = StartCoroutine(SlowDownRoutine());
    }

    /// <summary>
    /// �����߼���
    /// </summary>
    private IEnumerator SlowDownRoutine()
    {
        while (Rb.linearVelocity.magnitude > endSpeed)
        {
            Rb.linearVelocity = Vector2.Lerp(Rb.linearVelocity, Vector2.zero, slowDownSpeed * Time.deltaTime);
            yield return null;
        }
        Rb.linearVelocity = Vector2.zero;
    }
    #endregion
}

/// <summary>
/// �ٶȱ仯����ö�٣������ӷ��ͳ˷���
/// </summary>
public enum ValueChangeType
{
    Add,
    Multiply,
}
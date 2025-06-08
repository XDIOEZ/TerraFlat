using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// Mover 用于处理游戏对象的移动逻辑
/// </summary>
public class Mover : Organ, IFunction_Move
{
    #region 字段

    [Header("移动设置")]
    [Tooltip("速度源")]
    private ISpeed speedSource;

    [Tooltip("减速速率")]
    public float slowDownSpeed = 5f;

    [Tooltip("停止时的最小速度，小于该值则停止移动")]
    public float endSpeed = 0.1f;

    private Vector2 _lastDirection = Vector2.zero;
    private Coroutine _slowDownCoroutine;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    [Tooltip("移动结束事件")]
    public UltEvent OnMoveEnd;

    [Tooltip("移动持续事件")]
    public UltEvent OnMoveStay;

    [Tooltip("移动开始事件")]
    public UltEvent OnMoveStart;

    [ShowNonSerializedField]
    [Tooltip("速度变化字典")]
    public Dictionary<(string, ValueChangeType, float), float> SpeedChangeDict = new();

    private Rigidbody2D _rb;

    #endregion

    #region 属性

    public Rigidbody2D Rb
    {
        get => _rb ??= XDTool.GetComponentInChildrenAndParent<Rigidbody2D>(gameObject);
        private set => _rb = value;
    }

    public float Speed
    {
        get => Rb ? Rb.velocity.magnitude : 0f;
        set => Rb.velocity = Rb.velocity.normalized * value;
    }

    public float MoveSpeed
    {
        get => SpeedSource.Speed;
        set => SpeedSource.Speed = value;
    }

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

    public float DefaultMoveSpeed
    {
        get => SpeedSource.MaxSpeed;
        set => SpeedSource.MaxSpeed = value;
    }

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        if (Rb == null)
        {
            Rb = GetComponentInParent<Rigidbody2D>();
        }

        changeStamina = transform.parent.GetComponentInChildren<IChangeStamina>();

        OnMoveStart += () => { changeStamina.StartReduceStamina(20, "移动"); };
        OnMoveEnd += () => { changeStamina.StopReduceStamina("移动"); };
    }

    private void OnDestroy()
    {
        _rb = null;
    }

    #endregion

    #region 公共方法

    public void Move(Vector2 direction)
    {
        if (direction == Vector2.zero)
        {
            Rb.velocity = Vector2.zero;
            if (IsMoving)
            {
                IsMoving = false;
                OnMoveEnd?.Invoke();
            }
            return;
        }

        if (!IsMoving)
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
        Rb.velocity = adjustedVelocity;
    }

    public void AddSpeedChange((string, ValueChangeType, float) key, float value)
    {
        if (SpeedChangeDict.ContainsKey(key))
        {
            return;
        }

        if (key.Item2 == ValueChangeType.Add || key.Item2 == ValueChangeType.Multiply)
        {
            SpeedChangeDict[key] = value;
        }

        RecalculateMoveSpeed();
    }

    public void RemoveSpeedChange((string, ValueChangeType, float) key)
    {
        if (SpeedChangeDict.ContainsKey(key))
        {
            SpeedChangeDict.Remove(key);
        }
        RecalculateMoveSpeed();
    }

    #endregion

    #region 私有方法

    private void RecalculateMoveSpeed()
    {
        if (DefaultMoveSpeed <= 0)
        {
            Debug.LogWarning(DefaultMoveSpeed + " 是无效的默认速度");
            return;
        }

        MoveSpeed = DefaultMoveSpeed;

        var changeList = new List<(string, ValueChangeType, float, float)>();

        foreach (var item in SpeedChangeDict)
        {
            changeList.Add((item.Key.Item1, item.Key.Item2, item.Key.Item3, item.Value));
        }

        changeList.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        foreach (var item in changeList)
        {
            var type = item.Item2;
            var value = item.Item4;

            if (type == ValueChangeType.Add)
            {
                if (value < 0)
                {
                    Debug.LogWarning(value + " 是无效的加法变化值");
                    continue;
                }
                MoveSpeed += value;
            }
            else if (type == ValueChangeType.Multiply)
            {
                if (value <= 0)
                {
                    Debug.LogWarning(value + " 是无效的乘法变化值");
                    continue;
                }
                MoveSpeed *= value;
            }
        }
    }

    private void StartSlowDownRoutine()
    {
        if (_slowDownCoroutine != null)
        {
            StopCoroutine(_slowDownCoroutine);
        }
        _slowDownCoroutine = StartCoroutine(SlowDownRoutine());
    }

    private IEnumerator SlowDownRoutine()
    {
        while (Rb.velocity.magnitude > endSpeed)
        {
            Rb.velocity = Vector2.Lerp(Rb.velocity, Vector2.zero, slowDownSpeed * Time.deltaTime);
            yield return null;
        }
        Rb.velocity = Vector2.zero;
    }

    public override void StartWork()
    {
        Move(SpeedSource.MoveTargetPosition);
    }

    public override void UpdateWork()
    {
        throw new System.NotImplementedException();
    }

    public override void StopWork()
    {
        throw new System.NotImplementedException();
    }

    #endregion
}

/// <summary>
/// 速度变化类型枚举：加法或乘法
/// </summary>
public enum ValueChangeType
{
    Add,
    Multiply,
}

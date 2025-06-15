using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// 饥饿值管理系统：用于管理角色饥饿值的恢复与消耗，支持多来源、多类型事件订阅
/// 
/// 该管理器依赖于 IHunger 接口，其中存储了 Foods 数据（包含当前 Food 与最大值 MaxFood），
/// 以及 EatingSpeed（作为默认恢复速率）。
/// </summary>
public class HungerManager : MonoBehaviour, IChangeHungry
{
    #region 公开字段

    // 饥饿消耗速率的集合，支持多个来源同时影响
    [ShowNonSerializedField]
    public Dictionary<string, float> hungerReductionRates = new Dictionary<string, float>();

    // 饥饿恢复速率的集合，支持多个来源的恢复类型
    [ShowNonSerializedField]
    public Dictionary<string, float> hungerRecoveryRates = new Dictionary<string, float>();

    // 当前是否处于饥饿值为 0 的状态
    private bool isAtZero = false;

    // 当前总恢复速率
    public float allRecoverySpeed = 0f;

    // 当前总消耗速率
    public float allReduceSpeed = 0f;

    // 饥饿减少时触发的事件（传递减少值与来源名）
    public UltEvent<float, string> onHungerReduce = new UltEvent<float, string>();

    // 饥饿恢复时触发的事件（传递恢复值与来源名）
    public UltEvent<float, string> onHungerRecovery = new UltEvent<float, string>();

    // 饥饿首次耗尽触发的事件
    public UltEvent OnEnterZeroHunger;

    // 饥饿持续为 0 时触发的事件（每帧）
    public UltEvent OnStayZeroHunger;

    // 饥饿从 0 恢复时触发的事件
    public UltEvent OnExitZeroHunger;

    // 饥饿值发生变化时触发的事件（传递变化值）
    public UltEvent<float> OnHungerChanged;

    // 持有饥饿数据的对象接口（例如 Player），其中 Foods 存储当前饥饿数值
    private IHunger _hunger;

    #endregion

    #region 属性

    public UltEvent<float, string> OnHungerReduce { get => onHungerReduce; set => onHungerReduce = value; }
    public UltEvent<float, string> OnHungerRecovery { get => onHungerRecovery; set => onHungerRecovery = value; }

    /// <summary>
    /// 当前饥饿值（通过 IHunger 接口的 Foods.Food 获取，并限制范围与触发状态变化事件）
    /// </summary>
    public float CurrentHunger
    {
        get => CurrentHunger1;
        set
        {
            float clamped = ClampHunger(value);
            CurrentHunger1 = clamped;
            OnHungerChanged?.Invoke(clamped);

            // 检测是否进入或退出饥饿耗尽状态
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
    /// 饥饿最大值代理，通过 IHunger 接口的 Foods.MaxFood 获取与设置
    /// </summary>
    public float MaxHunger
    {
        get => MaxHunger1;
        set => MaxHunger1 = value;
    }

    /// <summary>
    /// 是否处于饥饿值耗尽状态
    /// </summary>
    public bool IsAtZero
    {
        get => isAtZero;
        set => isAtZero = value;
    }

    /// <summary>
    /// 获取或设置绑定的饥饿数据接口（自动从父级查找）
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
    /// 饥饿最大值代理（通过 IHunger 的 Foods.MaxFood 获取/设置）
    /// </summary>
    public float MaxHunger1
    {
        get => Hunger.Foods.MaxFood;
        set => Hunger.Foods.MaxFood = value;
    }

    /// <summary>
    /// 饥饿当前值代理（通过 IHunger 的 Foods.Food 获取/设置）
    /// </summary>
    public float CurrentHunger1
    {
        get => Hunger.Foods.Food;
        set => Hunger.Foods.Food = value;
    }

    #endregion

    #region Unity 生命周期

    private void OnEnable()
    {
        // 注册饥饿变化的事件处理方法
        onHungerReduce -= StartReduceHunger;
        onHungerReduce += StartReduceHunger;

        onHungerRecovery -= StartRecoverHunger;
        onHungerRecovery += StartRecoverHunger;

        // 注册饥饿状态相关的事件回调
     //   OnEnterZeroHunger += () => { Debug.Log("角色饥饿值耗尽！"); };
      //  OnStayZeroHunger += () => { Debug.Log("角色持续饥饿中！"); };
      //  OnExitZeroHunger += () => { Debug.Log("角色饥饿值得到恢复！"); };
    }

    private void Start()
    {
        // 添加默认恢复数值：这里假设 EatingSpeed 表示默认恢复速率（根据需要调整）
        StartReduceHunger(Hunger.Foods.MaxFood*0.02f, "默认恢复");
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
        // 持续根据总恢复速率与总消耗速率差值更新饥饿值
        float valueSpeed = (allRecoverySpeed - allReduceSpeed) * Time.fixedDeltaTime;
        CurrentHunger += valueSpeed;

        // 饥饿值耗尽时，触发持续耗尽状态事件
        if (CurrentHunger <= 0 && isAtZero)
        {
            OnStayZeroHunger?.Invoke();
        }
    }

    #endregion

    #region 公共方法

    [Button("开始饥饿消耗")]
    public void StartReduceHunger(float reductionSpeed, string reductionName)
    {
        if (string.IsNullOrEmpty(reductionName))
        {
            reductionName = Time.time.ToString(); // 使用时间戳作为默认类型标识
        }

        // 若已存在相同类型，则先移除原有速率，防止累计误差
        if (hungerReductionRates.ContainsKey(reductionName))
        {
            allReduceSpeed -= hungerReductionRates[reductionName];
        }

        hungerReductionRates[reductionName] = reductionSpeed;
        allReduceSpeed += reductionSpeed;
    }

    [Button("停止饥饿消耗")]
    public void StopReduceHunger(string reductionType)
    {
        if (!hungerReductionRates.ContainsKey(reductionType))
        {
            Debug.Log("字典中不存在该饥饿消耗类型！");
            return;
        }

        allReduceSpeed -= hungerReductionRates[reductionType];
        hungerReductionRates.Remove(reductionType);
    }

    [Button("开始饥饿恢复")]
    public void StartRecoverHunger(float recoverySpeed, string recoveryType)
    {
        if (hungerRecoveryRates.ContainsKey(recoveryType))
        {
            allRecoverySpeed -= hungerRecoveryRates[recoveryType];
        }

        hungerRecoveryRates[recoveryType] = recoverySpeed;
        allRecoverySpeed += recoverySpeed;
    }

    [Button("停止饥饿恢复")]
    public void StopRecoverHunger(string recoveryType)
    {
        if (!hungerRecoveryRates.ContainsKey(recoveryType))
        {
            Debug.Log("字典中不存在该饥饿恢复类型！");
            return;
        }

        allRecoverySpeed -= hungerRecoveryRates[recoveryType];
        hungerRecoveryRates.Remove(recoveryType);
    }

    #endregion

    #region 私有方法

    // 限制饥饿值在 0 至 MaxHunger 之间
    private float ClampHunger(float value)
    {
        return Mathf.Clamp(value, 0f, MaxHunger);
    }

    #endregion
}

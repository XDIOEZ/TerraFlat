using NaughtyAttributes;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// 精力管理系统：用于管理玩家精力值的恢复与消耗，支持多来源、多类型事件订阅
/// </summary>
public class StaminaManager : MonoBehaviour,IChangeStamina
{
    #region 公开字段

    // 精力消耗速率的集合，支持多个类型同时影响
    [ShowNonSerializedField]
    public Dictionary<string, float> staminaReductionRates = new Dictionary<string, float>();

    // 精力恢复速率的集合，支持多个来源的恢复类型
    [ShowNonSerializedField]
    public Dictionary<string, float> staminaRecoveryRates = new Dictionary<string, float>();

    // 当前是否处于精力值为 0 的状态
    private bool isAtZero = false;

    // 当前总恢复速率
    public float allRecoverySpeed = 0f;

    // 当前总消耗速率
    public float allReduceSpeed = 0f;

    // 精力减少时触发的事件（传递减少值与来源名）
    public UltEvent<float, string> onStaminaReduce = new UltEvent<float, string>();

    // 精力恢复时触发的事件（传递恢复值与来源名）
    public UltEvent<float, string> onStaminaRecovery = new UltEvent<float, string>();

    // 精力首次耗尽触发的事件
    public UltEvent OnEnterZeroStamina;

    // 精力持续为 0 时触发的事件（每帧）
    public UltEvent OnStayZeroStamina;

    // 精力从 0 恢复时触发的事件
    public UltEvent OnExitZeroStamina;

    // 精力发生变化时触发的事件（传递变化值）
    public UltEvent<float> OnStaminaChanged;

    // 持有精力数据的对象接口（例如 Player）
    private IStamina _stamina;

    #endregion

    #region 属性

    public UltEvent<float, string> OnStaminaReduce { get => onStaminaReduce; set => onStaminaReduce = value; }
    public UltEvent<float, string> OnStaminaRecovery { get => onStaminaRecovery; set => onStaminaRecovery = value; }

    // 当前精力值（自动封装了限制范围与事件触发）
    public float CurrentStamina
    {
        get => CurrentStamina1;
        set
        {
            float clamped = ClampStamina(value);
            CurrentStamina1 = clamped;

            // 触发变化事件
            OnStaminaChanged?.Invoke(clamped);

            // 检测是否进入/退出零精力状态
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

    // 是否为零精力状态
    public bool IsAtZero
    {
        get => isAtZero;
        set => isAtZero = value;
    }

    // 获取或设置绑定的能量数据接口（自动从父级查找）
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

    // 精力最大值代理
    public float MaxStamina1
    {
        get => Stamina.MaxStamina;
        set => Stamina.MaxStamina = value;
    }

    // 精力当前值代理
    public float CurrentStamina1
    {
        get => Stamina.Stamina;
        set => Stamina.Stamina = value;
    }

    #endregion

    #region Unity 生命周期


    private void OnEnable()
    {
        // 注册精力变化的事件处理方法
        OnStaminaReduce -= StartReduceStamina;
        OnStaminaReduce += StartReduceStamina;

        OnStaminaRecovery -= StartRecoverStamina;
        OnStaminaRecovery += StartRecoverStamina;

        // 注册精力状态事件
        OnEnterZeroStamina += () => { Debug.Log("角色耐力值耗尽！开始消耗精力上限"); };
        OnStayZeroStamina += () => { Debug.Log("角色耐力值耗尽！持续消耗精力上限"); };
        OnExitZeroStamina += () => { Debug.Log("角色耐力值开始恢复！"); };
    }

    public void Start()
    {
        //添加默认恢复数值
        StartRecoverStamina(Stamina.StaminaRecoverySpeed, "默认恢复");
    }

    private void OnDisable()
    {
        // 取消事件订阅，清理状态
        if (onStaminaReduce != null) onStaminaReduce -= StartReduceStamina;
        if (onStaminaRecovery != null) onStaminaRecovery -= StartRecoverStamina;

        staminaReductionRates.Clear();
        staminaRecoveryRates.Clear();
        allReduceSpeed = 0f;
        allRecoverySpeed = 0f;
    }

    private void FixedUpdate()
    {
        // 持续根据总恢复 - 总消耗 来更新精力值
        float valueSpeed = (allRecoverySpeed - allReduceSpeed) * Time.fixedDeltaTime;
        CurrentStamina += valueSpeed;

        // 若精力耗尽，触发持续耗尽事件
        if (CurrentStamina <= 0 && isAtZero)
        {
            OnStayZeroStamina?.Invoke();
        }
    }

    #endregion

    #region 公共方法

    [Button("开始耐力消耗")]
    public void StartReduceStamina(float reductionSpeed, string reductionName)
    {
        if (string.IsNullOrEmpty(reductionName))
        {
            reductionName = Time.time.ToString(); // 使用时间戳作为默认类型
        }

        // 若存在旧类型，先扣除再更新，防止累计误差
        if (staminaReductionRates.ContainsKey(reductionName))
        {
            allReduceSpeed -= staminaReductionRates[reductionName];
        }

        staminaReductionRates[reductionName] = reductionSpeed;
        allReduceSpeed += reductionSpeed;
    }

    [Button("停止耐力消耗")]
    public void StopReduceStamina(string reductionType)
    {
        if (!staminaReductionRates.ContainsKey(reductionType))
        {
            Debug.Log("字典中不存在该键值对！");
            return;
        }

        allReduceSpeed -= staminaReductionRates[reductionType];
        staminaReductionRates.Remove(reductionType);
    }

    [Button("开始耐力恢复")]
    public void StartRecoverStamina(float recoverySpeed, string recoveryType)
    {
        if (staminaRecoveryRates.ContainsKey(recoveryType))
        {
            allRecoverySpeed -= staminaRecoveryRates[recoveryType];
        }

        staminaRecoveryRates[recoveryType] = recoverySpeed;
        allRecoverySpeed += recoverySpeed;
    }

    [Button("停止耐力恢复")]
    public void StopRecoverStamina(string recoveryType)
    {
        if (!staminaRecoveryRates.ContainsKey(recoveryType))
        {
            Debug.Log("恢复类型不存在！");
            return;
        }

        allRecoverySpeed -= staminaRecoveryRates[recoveryType];
        staminaRecoveryRates.Remove(recoveryType);
    }

    #endregion

    #region 私有方法

    // 限制精力值范围
    private float ClampStamina(float value)
    {
        return Mathf.Clamp(value, 0f, MaxStamina);
    }

    #endregion
}

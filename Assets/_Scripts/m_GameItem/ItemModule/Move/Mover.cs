using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mover 用于处理游戏对象的移动逻辑
/// </summary>
public partial class Mover : Module, IMove
{
    #region 保存数据类
    [System.Serializable]
    [MemoryPack.MemoryPackable]
    public partial class Mover_SaveData
    {
        [Header("移动设置")]
        public GameValue_float Speed = new(10f);
        public float slowDownSpeed = 5f;
        public float endSpeed = 0.1f;

        [Header("跑步设置")]
        public float runStaminaRate = 2f;
        public float runSpeedRate = 2f;
        public bool isRunning = false;
        public float RunStaminaThreshold = 2f; // 🆕 当体力低于该值时，不能奔跑
    }

    public List<Vector2> MemoryPath_Forbidden = new List<Vector2>();     // 路径点列表

    public bool IsLock = false;

    public float RunStaminaThreshold
    {
        get => Data.RunStaminaThreshold;
        set => Data.RunStaminaThreshold = value;
    }


    #endregion

    #region 字段

    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField]
    public Mover_SaveData Data = new Mover_SaveData();
    public bool hightReaction = false;

    // 将需要保存的字段改为属性，引用saveData中的字段
    public GameValue_float Speed
    {
        get => Data.Speed;
        set => Data.Speed = value;
    }

    public float slowDownSpeed
    {
        get => Data.slowDownSpeed;
        set => Data.slowDownSpeed = value;
    }

    public float endSpeed
    {
        get => Data.endSpeed;
        set => Data.endSpeed = value;
    }

    [Tooltip("移动目标")]
    public Vector2 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    [Tooltip("移动结束事件")]
    public UltEvent OnMoveEnd;

    [Tooltip("移动持续事件")]
    public UltEvent OnMoveStay;

    [Tooltip("移动开始事件")]
    public UltEvent OnMoveStart;

    private InputAction moveAction;

    public Mod_Stamina stamina;

    //每秒精力消耗速度
    public GameValue_float staminaConsumeSpeed = new(1);

    public bool IsRunning
    {
        get => Data.isRunning;
        private set => Data.isRunning = value;
    }

    public float RunStaminaRate
    {
        get => Data.runStaminaRate;
        set => Data.runStaminaRate = value;
    }

    public float RunSpeedRate
    {
        get => Data.runSpeedRate;
        set => Data.runSpeedRate = value;
    }

    public void SetRunState(bool isRun)
    {
        if (IsRunning == isRun) return;

        // 🛑 体力不足时禁止跑步
        if (isRun && stamina != null && stamina.CurrentValue < RunStaminaThreshold)
        {
            Debug.Log("体力太低，无法奔跑");
            return;
        }


        if (isRun)
        {
            staminaConsumeSpeed.MultiplicativeModifier *= RunStaminaRate;
            Speed.MultiplicativeModifier *= RunSpeedRate;
        }
        else
        {
            staminaConsumeSpeed.MultiplicativeModifier /= RunStaminaRate;
            Speed.MultiplicativeModifier /= RunSpeedRate;
        }

        IsRunning = isRun;
    }



    #endregion

    #region 属性

    protected Rigidbody2D Rb;

    #endregion

    #region 数据管理
    public Ex_ModData_MemoryPackable ModDataMemoryPack = new Ex_ModData_MemoryPackable();

    public override ModuleData _Data { get => ModDataMemoryPack; set => ModDataMemoryPack = (Ex_ModData_MemoryPackable)value; }
    #endregion

    #region Unity 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Mover;
        }
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref Data);

        Rb = GetComponentInParent<Rigidbody2D>();

        // 加载控制器模块
        if (item.itemMods.GetMod_ByID(ModText.Controller)!= null)
        {
            var controller = item.itemMods.GetMod_ByID(ModText.Controller).GetComponent<PlayerController>();
            moveAction = controller._inputActions.Win10.Move_Player;

            controller._inputActions.Win10.Shift.started += _ => SetRunState(true);
            controller._inputActions.Win10.Shift.canceled += _ => SetRunState(false);
        }
        else
        {
            Debug.LogWarning("没有找到控制器"+item.itemData.GameName);
        }

        // 加载体力模块
        if (item.itemMods.GetMod_ByID(ModText.Stamina)!=null)
        {
            stamina = item.itemMods.GetMod_ByID(ModText.Stamina).GetComponent<Mod_Stamina>();
        }
    }

    public void Update()
    {
        if(hightReaction == true)
        Action(Time.deltaTime); // 调用Action( deltaTime)
    }

    public override void Action(float deltaTime)
    {
        if (moveAction == null) return;

            Vector2 input = moveAction.ReadValue<Vector2>();

        if (input.sqrMagnitude > 0.001f)
        {
            Vector2 target = Rb.position + input.normalized;

            Move(target, deltaTime);

            OnMoveStay?.Invoke();

            if (stamina != null)
            {
                stamina.CurrentValue -= deltaTime * staminaConsumeSpeed.Value;

                // 🛑 自动中断奔跑
                if (IsRunning && stamina.CurrentValue < RunStaminaThreshold)
                {
                    SetRunState(false);
                    Debug.Log("体力不足，自动停止奔跑");
                }

            }
        }
        else
        {
            Move(Rb.position, deltaTime); // 停止移动
        }
    }

    #endregion

    #region 公共方法
    public virtual void Move(Vector2 targetPosition, float deltaTime)
    {
        bool isZero = Vector2.Distance(transform.position, targetPosition) < endSpeed;

        // 移动起止状态切换
        if (IsMoving != !isZero)
        {
            IsMoving = !isZero;
            if (IsMoving)
                OnMoveStart?.Invoke();
            else
                OnMoveEnd?.Invoke();
        }

        if (isZero)
            return;

        // 计算移动方向和距离
        Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
        Vector2 movement = direction * Speed.Value * deltaTime;


        item.transform.position += (Vector3)movement;
        
    }


    #endregion

    #region 私有方法

    public override void Save()
    {
         ModDataMemoryPack.WriteData(Data);
         Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }

    #endregion
}
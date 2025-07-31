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

    public float RunStaminaThreshold
    {
        get => saveData.RunStaminaThreshold;
        set => saveData.RunStaminaThreshold = value;
    }


    #endregion

    #region 字段

    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField]
    public Mover_SaveData saveData = new Mover_SaveData();
    public bool hightReaction = false;

    // 将需要保存的字段改为属性，引用saveData中的字段
    public GameValue_float Speed
    {
        get => saveData.Speed;
        set => saveData.Speed = value;
    }

    public float slowDownSpeed
    {
        get => saveData.slowDownSpeed;
        set => saveData.slowDownSpeed = value;
    }

    public float endSpeed
    {
        get => saveData.endSpeed;
        set => saveData.endSpeed = value;
    }

    [Tooltip("移动目标")]
    public Vector3 TargetPosition;

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
        get => saveData.isRunning;
        private set => saveData.isRunning = value;
    }

    public float RunStaminaRate
    {
        get => saveData.runStaminaRate;
        set => saveData.runStaminaRate = value;
    }

    public float RunSpeedRate
    {
        get => saveData.runSpeedRate;
        set => saveData.runSpeedRate = value;
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
        if (_Data.Name == "")
        {
            _Data.Name = ModText.Mover;
        }
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref saveData);

        Rb = GetComponentInParent<Rigidbody2D>();

        if (item.Mods.ContainsKey(ModText.Controller))
        {
            var controller = item.Mods[ModText.Controller].GetComponent<PlayerController>();
            moveAction = controller._inputActions.Win10.Move_Player;
        }

        if (item.Mods.ContainsKey(ModText.Stamina))
        {
            stamina = item.Mods[ModText.Stamina].GetComponent<Mod_Stamina>();
        }

        if (item.Mods.ContainsKey(ModText.Controller))
        {
            var controller = item.Mods[ModText.Controller].GetComponent<PlayerController>();
            controller._inputActions.Win10.Shift.started += _ => SetRunState(true);
            controller._inputActions.Win10.Shift.canceled += _ => SetRunState(false);
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
            Move(target);
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
            Move(Rb.position); // 停止移动
        }
    }

    #endregion

    #region 公共方法
    public virtual void Move(Vector2 TargetPosition)
    {
        bool isZero = Vector2.Distance(Rb.position, TargetPosition) < endSpeed;

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
        {
            Rb.velocity = Vector2.zero;
            return;
        }

        // 计算最终速度
        Rb.velocity = (TargetPosition - Rb.position).normalized * Speed.Value;
    }

    #endregion

    #region 私有方法

    public override void Save()
    {
         ModDataMemoryPack.WriteData(saveData);
        item.Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }

    #endregion
}
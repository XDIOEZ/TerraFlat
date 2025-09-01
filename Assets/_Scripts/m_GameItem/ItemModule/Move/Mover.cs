using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Mover —— 处理游戏对象的移动逻辑
/// </summary>
public partial class Mover : Module
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
        public float RunStaminaThreshold = 2f; // 体力低于该值时，不能奔跑

    }
    #endregion

    #region 字段
    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField] public Mover_SaveData Data = new();

    public List<Vector2> MemoryPath_Forbidden = new();  // 禁止路径点
    public bool IsLock = false;
    public bool hightReaction = false;

    [Tooltip("移动目标")]
    public Vector2 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    private InputAction moveAction;
    public Rigidbody2D Rb;

    public Mod_Stamina stamina;                         // 体力模块
    public GameValue_float staminaConsumeSpeed = new(1); // 每秒精力消耗速度

    public Ex_ModData_MemoryPackable ModDataMemoryPack = new();
    public Mod_AnimationController animationController;

    [Header("移动事件")]
    public UltEvent OnMoveStart;
    public UltEvent OnMoveEnd;

    #endregion

    #region 属性
    public override ModuleData _Data
    {
        get => ModDataMemoryPack;
        set => ModDataMemoryPack = (Ex_ModData_MemoryPackable)value;
    }

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

    public bool IsRunning
    {
        get => Data.isRunning;
        set => Data.isRunning = value;
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

    public float RunStaminaThreshold
    {
        get => Data.RunStaminaThreshold;
        set => Data.RunStaminaThreshold = value;
    }
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
        LoadMod<PlayerController>(item, ModText.Controller, controller =>
        {
            // 回调里才赋值 moveAction
            moveAction = controller._inputActions.Win10.Move_Player;
            controller._inputActions.Win10.Shift.started += _ => SetRunState(true);
            controller._inputActions.Win10.Shift.canceled += _ => SetRunState(false);
        });

        // 加载体力模块
        stamina = LoadMod<Mod_Stamina>(item, ModText.Stamina);
        animationController = LoadMod<Mod_AnimationController>(item, ModText.Animation, controller =>
        {
            OnMoveStart += () => controller.SetBool(AnimationText.Move, true);
            OnMoveEnd += () => controller.SetBool(AnimationText.Move, false);
        });
    }



    private bool _wasMoving = false;

    public override void Action(float deltaTime)
    {
        if (moveAction == null) return;

        Vector2 input = moveAction.ReadValue<Vector2>();
        bool isCurrentlyMoving = input.sqrMagnitude > 0.001f;

        // 移动开始/结束事件触发
        if (!_wasMoving && isCurrentlyMoving)
        {
            OnMoveStart?.Invoke();
        }
        else if (_wasMoving && !isCurrentlyMoving)
        {
            OnMoveEnd?.Invoke();
        }
        _wasMoving = isCurrentlyMoving;

        if (isCurrentlyMoving)
        {
            Vector2 target = Rb.position + input.normalized;
            Move(target, deltaTime);

            if (stamina != null)
            {
                stamina.CurrentValue -= deltaTime * staminaConsumeSpeed.Value;

                // 自动中断奔跑
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
    public void SetRunState(bool isRun)
    {
        if (IsRunning == isRun) return;

        // 体力不足时禁止跑步
        if (isRun && stamina != null && stamina.CurrentValue < RunStaminaThreshold)
        {
            Debug.Log("体力太低，无法奔跑");
            animationController.SetBool(AnimationText.Run, false);
            return;
        }

        if (isRun)
        {
            staminaConsumeSpeed.MultiplicativeModifier *= RunStaminaRate;
            Speed.MultiplicativeModifier *= RunSpeedRate;
            animationController.SetBool(AnimationText.Run, true);
        }
        else
        {
            staminaConsumeSpeed.MultiplicativeModifier /= RunStaminaRate;
            Speed.MultiplicativeModifier /= RunSpeedRate;
            animationController.SetBool(AnimationText.Run, false);
        }

        IsRunning = isRun;
    }

    public virtual void Move(Vector2 targetPosition, float deltaTime)
    {
        bool isZero = Vector2.Distance(transform.position, targetPosition) < endSpeed;
        if (isZero) return;

        Vector2 direction = (targetPosition - (Vector2)transform.position).normalized;
        Vector2 movement = direction * Speed.Value * deltaTime;

        item.transform.position += (Vector3)movement;
    }
    #endregion

    #region 数据存取
    public override void Save()
    {
        ModDataMemoryPack.WriteData(Data);
        Item_Data.ModuleDataDic[_Data.Name] = _Data;

        OnMoveStart.Clear();
        OnMoveEnd.Clear();
    }
    #endregion
}

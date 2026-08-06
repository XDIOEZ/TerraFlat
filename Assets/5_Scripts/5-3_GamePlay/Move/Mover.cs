using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;

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

        [Header("精力消耗设置")]
        [Tooltip("移动时每秒精力消耗")]
        public float moveStaminaConsume = 1f;

        [Tooltip("奔跑时每秒精力消耗（独立值，不再参考移动消耗）")]
        public float runStaminaConsume = 2f;

        [Header("跑步设置")]
        public float runSpeedRate = 2f;
        public bool isRunning = false;
        public float RunStaminaThreshold = 2f; // 体力低于该值时，不能奔跑

    }
    #endregion

    #region 字段
    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField] public Mover_SaveData Data = new();

    [Header("奔跑输入设置")]
    [SerializeField]
    [Min(0.05f)]
    [Tooltip("Shift 按住不超过该时长时视为短按，并切换常驻奔跑/移动。")]
    private float runToggleTapThreshold = 0.25f;

    public List<Vector2> MemoryPath_Forbidden = new();  // 禁止路径点
    public bool IsLock = false;
    public bool hightReaction = false;

    [Tooltip("移动目标")]
    public Vector2 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    private InputAction moveAction;
    private InputAction runAction;
    private bool runToggleEnabled;
    public Rigidbody2D rb;

    public Mod_Stamina stamina;                         // 体力模块

    public Ex_ModData_MemoryPackable ModDataMemoryPack = new();
    public Mod_AnimatorController animationController;

    // 饥饿相关：移动/奔跑时通过 Buff 加快 Food 模块的消耗
    [Header("饥饿消耗设置")]
    [Tooltip("移动时附加的 JSON Buff ID")]
    public string moveHungerBuffId = "饥饿1.6";

    [Tooltip("奔跑时附加的 JSON Buff ID")]
    public string runHungerBuffId = "饥饿2.0";

    private BuffManager buffManager;
    private bool moveHungerBuffActive = false;
    private bool runHungerBuffActive = false;

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
        get => Data.runStaminaConsume;
        set => Data.runStaminaConsume = value;
    }

    public float MoveStaminaConsume
    {
        get => Data.moveStaminaConsume;
        set => Data.moveStaminaConsume = value;
    }

    public float RunStaminaConsume
    {
        get => Data.runStaminaConsume;
        set => Data.runStaminaConsume = value;
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

    public float RunToggleTapThreshold => Mathf.Max(0.05f, runToggleTapThreshold);
    public bool IsRunToggleEnabled => runToggleEnabled;
    #endregion

    #region Unity 生命周期
    public virtual void OnValidate()
    {
        _Data.ID = ModText.Mover;
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref Data);

        rb = GetComponentInParent<Rigidbody2D>();

        // 加载 Buff 管理器（可能不存在，需容错）
        item.itemMods.GetMod_ByID(ModText.BuffManager, out buffManager);

        // 加载控制器模块
        LoadMod<GameController>(item, ModText.Controller, controller =>
        {
            // 回调里才赋值 moveAction
            moveAction = controller._inputActions.Win10.Move_Player;
            BindRunAction(controller._inputActions.Win10.Shift);
        });

        // 加载体力模块
        stamina = LoadMod<Mod_Stamina>(item, ModText.Stamina);
        animationController = item.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);

        if (animationController != null)
        {
            OnMoveStart += () => animationController.SetBool(AnimationText.Move, true);
            OnMoveEnd += () => animationController.SetBool(AnimationText.Move, false);
        }
    }



    private bool _wasMoving = false;

    public override void ModUpdate(float deltaTime)
    {
        if (moveAction == null) return;

        if (item != null)
        {
            GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked)
            {
                Move(rb.position, deltaTime);
                if (IsRunning)
                {
                    SetRunState(false);
                }
                return;
            }
        }

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

        // 基于移动/奔跑状态，给拥有 Food+BuffManager 的对象挂/卸饥饿 Buff
        HandleHungerBuffs(isCurrentlyMoving, IsRunning);

        if (isCurrentlyMoving)
        {
            Vector2 target = rb.position + input.normalized;
            Move(target, deltaTime);

            if (stamina != null)
            {
                float consumePerSecond = IsRunning ? RunStaminaConsume : MoveStaminaConsume;
                stamina.AddStamina(-deltaTime * consumePerSecond);

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
            Move(rb.position, deltaTime); // 停止移动
        }
    }

    #endregion

    #region 公共方法
    public void HandleRunInputPressed()
    {
        SetRunState(true);
    }

    public void HandleRunInputReleased(double heldDuration)
    {
        bool isShortPress = heldDuration <= RunToggleTapThreshold;
        if (!isShortPress)
        {
            runToggleEnabled = false;
            SetRunState(false);
            return;
        }

        bool shouldKeepRunning = !runToggleEnabled;
        SetRunState(shouldKeepRunning);
        runToggleEnabled = shouldKeepRunning && IsRunning;
    }

    public void SetRunState(bool isRun)
    {
        if (!isRun)
            runToggleEnabled = false;

        if (item != null)
        {
            GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked && isRun)
            {
                return;
            }
        }

        if (IsRunning == isRun) return;
        IsRunning = isRun;
        // 体力不足时禁止跑步
        if (isRun && stamina != null && stamina.CurrentValue < RunStaminaThreshold)
        {
            Debug.Log("体力太低，无法奔跑");
            if (animationController != null) animationController.SetBool(AnimationText.Run, false);
            IsRunning = false;
            return;
        }

        if (isRun)
        {
            Speed.MultiplicativeModifier *= RunSpeedRate;
            if (animationController != null) animationController.SetBool(AnimationText.Run, true);
        }
        else
        {
            Speed.MultiplicativeModifier /= RunSpeedRate;
            if (animationController != null) animationController.SetBool(AnimationText.Run, false);
        }


    }

    public virtual void Move(Vector2 targetPosition, float deltaTime)
    {
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行 Move！");
            return;
        }

        float arriveThreshold = 0.1f;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(rb.position, targetPosition);
        if (delta.magnitude < arriveThreshold)
        {
            rb.velocity = Vector2.zero;
            return;
        }

        Vector2 direction = delta.normalized;
        rb.velocity = direction * Speed.Value;
    }

    #endregion

    #region 数据存取
    public override void Save()
    {
        var saveData = new Mover_SaveData
        {
            Speed = new GameValue_float(Data.Speed.BaseValue)
            {
                BaseAdditive = Data.Speed.BaseAdditive,
                // 运行时加成由装备/Buff重建，不写入持久化，避免读档后重复叠加或错减
                AdditiveModifier = 0f,
                MultiplicativeModifier = 1f,
                FinalAdditive = Data.Speed.FinalAdditive
            },
            slowDownSpeed = Data.slowDownSpeed,
            endSpeed = Data.endSpeed,
            moveStaminaConsume = Data.moveStaminaConsume,
            runStaminaConsume = Data.runStaminaConsume,
            runSpeedRate = Data.runSpeedRate,
            isRunning = false,
            RunStaminaThreshold = Data.RunStaminaThreshold
        };

        ModDataMemoryPack.WriteData(saveData);
        Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }
    public void OnDestroy()
    {
        UnbindRunAction();
        OnMoveStart?.Clear();
        OnMoveEnd?.Clear();

        // 模块被销毁时，确保清除与移动/奔跑相关的饥饿状态 Buff
        ClearHungerBuffs();
    }
    #endregion

    #region 奔跑输入

    private void BindRunAction(InputAction action)
    {
        UnbindRunAction();
        runAction = action;
        if (runAction == null)
            return;

        runAction.started += OnRunActionStarted;
        runAction.canceled += OnRunActionCanceled;
    }

    private void UnbindRunAction()
    {
        if (runAction == null)
            return;

        runAction.started -= OnRunActionStarted;
        runAction.canceled -= OnRunActionCanceled;
        runAction = null;
    }

    private void OnRunActionStarted(InputAction.CallbackContext context)
    {
        HandleRunInputPressed();
    }

    private void OnRunActionCanceled(InputAction.CallbackContext context)
    {
        HandleRunInputReleased(context.duration);
    }

    #endregion

    #region 饥饿 Buff 逻辑

    /// <summary>
    /// 根据当前移动/奔跑状态，动态添加或移除饥饿相关 Buff。
    /// </summary>
    private void HandleHungerBuffs(bool isMoving, bool isRunning)
    {
        if (buffManager == null)
        {
            // 该物体没有 BuffManager，直接退出（例如纯移动物体）
            return;
        }

        // 1. 处理移动饥饿 Buff
        if (isMoving)
        {
            TryAddHungerBuff(moveHungerBuffId, ref moveHungerBuffActive);
        }
        else
        {
            TryRemoveHungerBuff(moveHungerBuffId, ref moveHungerBuffActive);
        }

        // 2. 处理奔跑附加饥饿 Buff（只在移动且奔跑时生效）
        if (isMoving && isRunning)
        {
            TryAddHungerBuff(runHungerBuffId, ref runHungerBuffActive);
        }
        else
        {
            TryRemoveHungerBuff(runHungerBuffId, ref runHungerBuffActive);
        }
    }

    private void TryAddHungerBuff(string buffId, ref bool stateFlag)
    {
        if (stateFlag) return;
        if (buffManager == null) return;
        if (string.IsNullOrWhiteSpace(buffId)) return;

        stateFlag = buffManager.AddBuff(buffId);
    }

    private void TryRemoveHungerBuff(string buffId, ref bool stateFlag)
    {
        if (buffManager == null) return;
        if (string.IsNullOrWhiteSpace(buffId)) return;
        if (!stateFlag && !buffManager.HasBuff(buffId)) return;

        buffManager.RemoveBuff(buffId);
        stateFlag = false;
    }

    #endregion

    /// <summary>
    /// 强制清理当前可能仍然挂在身上的移动/奔跑饥饿 Buff。
    /// 在模块销毁或需要重置状态时调用。
    /// </summary>
    private void ClearHungerBuffs()
    {
        if (buffManager == null) return;

        // 不直接操作字典，而是复用已有的移除逻辑和状态位
        TryRemoveHungerBuff(moveHungerBuffId, ref moveHungerBuffActive);
        TryRemoveHungerBuff(runHungerBuffId, ref runHungerBuffActive);
    }
}

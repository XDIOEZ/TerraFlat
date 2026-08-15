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
        [Tooltip("松开输入时的最低减速度，防止低速拖尾过长")]
        public float slowDownSpeed = 5f;
        public float endSpeed = 0.1f;

        [Header("精力消耗设置")]
        [Tooltip("移动时每秒精力消耗")]
        public float moveStaminaConsume = 1f;

        [Tooltip("奔跑时每秒精力消耗（独立值，不再参考移动消耗）")]
        public float runStaminaConsume = 2f;

        [Header("跑步设置")]
        public float runSpeedRate = 1.5f;
        [Tooltip("是否保持奔跑模式；随玩家存档序列化保存")]
        public bool isRunning = false;
        public float RunStaminaThreshold = 2f; // 体力低于该值时，不能奔跑

    }
    #endregion

    #region 字段
    [Header("移动设置")]
    [Tooltip("速度源")]
    [SerializeField] public Mover_SaveData Data = new();

    [Header("速度过渡")]
    [Tooltip("走路、奔跑或转向时，速度达到新目标所需的时间（秒）")]
    [Min(0.01f)] public float speedTransitionDuration = 0.24f;

    [Tooltip("松开移动输入后停止所需的时间（秒），保留极短惯性")]
    [Min(0.01f)] public float stopTransitionDuration = 0.07f;

    public List<Vector2> MemoryPath_Forbidden = new();  // 禁止路径点
    public bool IsLock = false;
    public bool hightReaction = false;

    [Tooltip("移动目标")]
    public Vector2 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    private InputAction moveAction;
    private InputAction holdRunAction;
    private InputAction toggleRunAction;
    public Rigidbody2D rb;

    // 输入判定、到达判定与过渡时间的稳定下限。
    private const float InputMoveThresholdSqr = 0.001f;
    private const float ArriveThreshold = 0.1f;
    private const float MinimumTransitionDuration = 0.01f;

    public Mod_Stamina stamina;                         // 体力模块

    public Ex_ModData_MemoryPackable ModDataMemoryPack = new();
    public Mod_AnimatorController animationController;

    [Header("移动饥饿动作")]
    [Tooltip("移动模块自己的饥饿消耗配置；它不是 Buff，不会被清 Buff 道具移除。")]
    public MovementHungerActionDefinition hungerAction = new();

    private MovementHungerActionInstance hungerActionInstance;

    [Header("移动事件")]
    public UltEvent OnMoveStart;
    public UltEvent OnMoveEnd;

    /// <summary>奔跑状态真实变化时通知 HUD；体力不足等自动停止路径也会同步表现。</summary>
    public event System.Action<bool> RunStateChanged;

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

    #endregion

    #region Unity 生命周期
    public virtual void OnValidate()
    {
        _Data.ID = ModText.Mover;
        speedTransitionDuration = Mathf.Max(MinimumTransitionDuration, speedTransitionDuration);
        stopTransitionDuration = Mathf.Max(MinimumTransitionDuration, stopTransitionDuration);
        hungerAction?.ClampValues();
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref Data);
        bool persistedRunState = Data.isRunning;
        Data.isRunning = false;

        rb = GetComponentInParent<Rigidbody2D>();

        hungerAction ??= new MovementHungerActionDefinition();
        hungerActionInstance = hungerAction.CreateInstance(item);

        // 动物不含玩家输入与体力模块，按可选依赖安静解析。
        GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller != null)
        {
            moveAction = controller._inputActions.Win10.Move_Player;
            BindRunActions(
                controller._inputActions.Win10.Shift,
                controller._inputActions.Win10.ToggleRun);
        }

        // 加载体力模块
        stamina = item.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        animationController = item.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);

        if (animationController != null)
        {
            OnMoveStart += () => animationController.SetBool(AnimationText.Move, true);
            OnMoveEnd += () => animationController.SetBool(AnimationText.Move, false);
            animationController.SetBool(AnimationText.Run, false);
        }

        // 先恢复基础数据，再通过统一入口重建奔跑倍率与动画状态。
        if (persistedRunState)
            SetRunState(true);
    }



    private bool _wasMoving = false;

    public override void ModUpdate(float deltaTime)
    {
        if (moveAction == null)
        {
            hungerActionInstance?.SetMovementState(false, false);
            return;
        }
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行移动更新！");
            return;
        }

        if (item != null)
        {
            GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked)
            {
                StopImmediately();
                // 输入锁定只停止位移，奔跑开关作为存档状态保留，解锁后继续沿用。
                hungerActionInstance?.SetMovementState(false, false);
                return;
            }
        }

        Vector2 input = moveAction.ReadValue<Vector2>();
        bool isCurrentlyMoving = input.sqrMagnitude > InputMoveThresholdSqr;

        if (isCurrentlyMoving)
        {
            MoveByInput(input, deltaTime);

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
            MoveByInput(Vector2.zero, deltaTime); // 停止移动
        }

        // 每帧只更新动作实例状态；实际营养扣除仍由 Mod_Food 的统一 Tick 完成。
        hungerActionInstance?.SetMovementState(isCurrentlyMoving, IsRunning);
    }

    #endregion

    #region 公共方法
    public void HandleHoldRunInputPressed()
    {
        // 长按奔跑键按下后进入奔跑，保持输入期间持续奔跑。
        SetRunState(true);
    }

    public void HandleHoldRunInputReleased()
    {
        // 长按奔跑键松开后恢复普通移动。
        SetRunState(false);
    }

    public void HandleToggleRunInputPressed()
    {
        // 每次按下切换键，都在奔跑与普通移动之间切换。
        SetRunState(!IsRunning);
    }

    public void SetRunState(bool isRun)
    {
        if (item != null)
        {
            GameController controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked && isRun)
            {
                return;
            }
        }

        // 体力不足时禁止跑步
        if (isRun && stamina != null && stamina.CurrentValue < RunStaminaThreshold)
        {
            Debug.Log("体力太低，无法奔跑");
            if (animationController != null) animationController.SetBool(AnimationText.Run, false);
            return;
        }

        if (IsRunning == isRun) return;
        IsRunning = isRun;

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

        RunStateChanged?.Invoke(IsRunning);
    }

    public virtual void Move(Vector2 targetPosition, float deltaTime)
    {
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行 Move！");
            return;
        }

        Vector2 delta = WorldTopologyRuntime.ShortestDelta(rb.position, targetPosition);
        Vector2 targetVelocity = delta.sqrMagnitude < ArriveThreshold * ArriveThreshold
            ? Vector2.zero
            : delta.normalized * Speed.Value;
        rb.velocity = CalculateSmoothedVelocity(targetVelocity, deltaTime);
        UpdateMovementState();
    }

    /// <summary>按二维输入幅度驱动玩家移动；满幅达到当前最大速度，轻推时按比例降速。</summary>
    public void MoveByInput(Vector2 input, float deltaTime)
    {
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行输入移动！");
            return;
        }

        Vector2 clampedInput = Vector2.ClampMagnitude(input, 1f);
        Vector2 targetVelocity = clampedInput.sqrMagnitude > InputMoveThresholdSqr
            ? clampedInput * Speed.Value
            : Vector2.zero;
        rb.velocity = CalculateSmoothedVelocity(targetVelocity, deltaTime);
        UpdateMovementState();
    }

    /// <summary>将当前物理速度平滑靠近输入所要求的目标速度。</summary>
    private Vector2 CalculateSmoothedVelocity(Vector2 targetVelocity, float deltaTime)
    {
        bool isStopping = targetVelocity.sqrMagnitude <= InputMoveThresholdSqr;
        float transitionDuration = isStopping
            ? stopTransitionDuration
            : speedTransitionDuration;
        float referenceSpeed = Mathf.Max(
            Mathf.Max(rb.velocity.magnitude, targetVelocity.magnitude),
            Mathf.Max(0f, Speed.Value));
        float minimumChangeRate = isStopping ? Mathf.Max(0f, slowDownSpeed) : 0f;
        float speedChangeRate = Mathf.Max(
            minimumChangeRate,
            referenceSpeed / Mathf.Max(MinimumTransitionDuration, transitionDuration));
        Vector2 nextVelocity = Vector2.MoveTowards(
            rb.velocity,
            targetVelocity,
            speedChangeRate * Mathf.Max(0f, deltaTime));

        float stopThreshold = Mathf.Max(0.001f, endSpeed);
        return isStopping && nextVelocity.sqrMagnitude <= stopThreshold * stopThreshold
            ? Vector2.zero
            : nextVelocity;
    }

    /// <summary>输入锁定时立即停止，避免模态界面打开后角色继续滑行。</summary>
    private void StopImmediately()
    {
        if (rb == null)
            return;

        rb.velocity = Vector2.zero;
        UpdateMovementState();
    }

    /// <summary>根据实际速度同步移动事件与动画状态。</summary>
    private void UpdateMovementState()
    {
        float stopThreshold = Mathf.Max(0.001f, endSpeed);
        bool isActuallyMoving = rb != null &&
                                rb.velocity.sqrMagnitude > stopThreshold * stopThreshold;
        IsMoving = isActuallyMoving;

        if (!_wasMoving && isActuallyMoving)
            OnMoveStart?.Invoke();
        else if (_wasMoving && !isActuallyMoving)
            OnMoveEnd?.Invoke();

        _wasMoving = isActuallyMoving;
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
            isRunning = Data.isRunning,
            RunStaminaThreshold = Data.RunStaminaThreshold
        };

        ModDataMemoryPack.WriteData(saveData);
        Item_Data.ModuleDataDic[_Data.Name] = _Data;
    }
    public void OnDestroy()
    {
        UnbindRunActions();
        OnMoveStart?.Clear();
        OnMoveEnd?.Clear();
        RunStateChanged = null;

        hungerActionInstance?.Dispose();
        hungerActionInstance = null;
    }
    #endregion

    #region 奔跑输入

    private void BindRunActions(InputAction holdAction, InputAction toggleAction)
    {
        UnbindRunActions();
        holdRunAction = holdAction;
        toggleRunAction = toggleAction;

        if (holdRunAction != null)
        {
            holdRunAction.started += OnHoldRunActionStarted;
            holdRunAction.canceled += OnHoldRunActionCanceled;
        }

        if (toggleRunAction != null)
            toggleRunAction.performed += OnToggleRunActionPerformed;
    }

    private void UnbindRunActions()
    {
        if (holdRunAction != null)
        {
            holdRunAction.started -= OnHoldRunActionStarted;
            holdRunAction.canceled -= OnHoldRunActionCanceled;
            holdRunAction = null;
        }

        if (toggleRunAction != null)
        {
            toggleRunAction.performed -= OnToggleRunActionPerformed;
            toggleRunAction = null;
        }
    }

    private void OnHoldRunActionStarted(InputAction.CallbackContext context)
    {
        HandleHoldRunInputPressed();
    }

    private void OnHoldRunActionCanceled(InputAction.CallbackContext context)
    {
        HandleHoldRunInputReleased();
    }

    private void OnToggleRunActionPerformed(InputAction.CallbackContext context)
    {
        HandleToggleRunInputPressed();
    }

    #endregion

}

/// <summary>
/// 移动模块持有的饥饿动作配置模板。
/// 普通移动默认使用 1.6 倍营养消耗，奔跑在此基础上再乘 2 倍，保持原有玩法数值，
/// 但配置不再依赖 Buff JSON，因此清理 Buff 不会破坏移动饥饿规则。
/// </summary>
[System.Serializable]
public sealed class MovementHungerActionDefinition
{
    [Tooltip("是否启用移动/奔跑饥饿动作；AI 移动模块默认关闭。")]
    public bool enabled = false;

    [Min(0f)]
    [Tooltip("普通移动时的营养消耗倍率。")]
    public float moveNutritionConsumeMultiplier = 1.6f;

    [Min(0f)]
    [Tooltip("奔跑相对普通移动额外使用的营养消耗倍率。")]
    public float runNutritionConsumeMultiplier = 2f;

    /// <summary>校正运行时和 Inspector 可能写入的非法配置。</summary>
    public void ClampValues()
    {
        moveNutritionConsumeMultiplier = Mathf.Max(0f, moveNutritionConsumeMultiplier);
        runNutritionConsumeMultiplier = Mathf.Max(0f, runNutritionConsumeMultiplier);
    }

    /// <summary>按当前移动状态计算最终营养消耗倍率。</summary>
    public float ResolveMultiplier(bool isMoving, bool isRunning)
    {
        if (!enabled || !isMoving)
            return 1f;

        float multiplier = Mathf.Max(0f, moveNutritionConsumeMultiplier);
        if (isRunning)
            multiplier *= Mathf.Max(0f, runNutritionConsumeMultiplier);
        return multiplier;
    }

    /// <summary>从配置模板创建角色独享的运行实例。</summary>
    public MovementHungerActionInstance CreateInstance(Item actor)
    {
        Mod_Food food = actor?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
        return new MovementHungerActionInstance(this, food);
    }
}

/// <summary>
/// 单个角色持有的移动饥饿动作实例。
/// 只维护移动状态并把倍率交给 Mod_Food，实际扣除营养仍由 Food 模块统一执行，
/// 因此不会与基础饥饿、难度倍率、动物维持状态或其他非移动规则重复扣除。
/// </summary>
public sealed class MovementHungerActionInstance
{
    private readonly MovementHungerActionDefinition definition;
    private readonly Mod_Food food;
    private bool isMoving;
    private bool isRunning;

    public MovementHungerActionInstance(
        MovementHungerActionDefinition definition,
        Mod_Food food)
    {
        this.definition = definition;
        this.food = food;
    }

    /// <summary>当前动作是否正在为移动状态提供额外规则。</summary>
    public bool IsActive => definition != null && definition.enabled && isMoving && food != null;

    /// <summary>刷新移动/奔跑状态，并立即应用当前配置倍率。</summary>
    public void SetMovementState(bool moving, bool running)
    {
        isMoving = moving;
        isRunning = moving && running;
        ApplyMultiplier();
    }

    /// <summary>模块销毁或角色回收时还原 Food，避免对象池复用残留倍率。</summary>
    public void Dispose()
    {
        isMoving = false;
        isRunning = false;
        ApplyMultiplier();
    }

    private void ApplyMultiplier()
    {
        if (food == null)
            return;

        definition?.ClampValues();
        float multiplier = definition?.ResolveMultiplier(isMoving, isRunning) ?? 1f;
        food.SetMovementNutritionConsumeMultiplier(multiplier);
    }
}

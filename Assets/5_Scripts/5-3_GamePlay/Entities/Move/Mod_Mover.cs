using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UltEvents;

/// <summary>
/// Mod_Mover —— 处理游戏对象的移动逻辑
/// </summary>
public partial class Mod_Mover : Module
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

    [Header("移动表面响应")]
    [Tooltip("当前表面对加速度的倍率；冰面降低该值即可产生打滑。")]
    [Min(0.01f)] public float surfaceAccelerationMultiplier = 1f;

    [Tooltip("当前表面对减速度的倍率；冰面降低该值即可保留惯性。")]
    [Min(0.01f)] public float surfaceDecelerationMultiplier = 1f;

    public List<Vector2> MemoryPath_Forbidden = new();  // 禁止路径点
    public bool IsLock = false;
    public bool hightReaction = false;

    [Tooltip("移动目标")]
    public Vector2 TargetPosition;

    [Tooltip("是否正在移动")]
    public bool IsMoving;

    [Min(0.01f)] public float pushContactRadius = 0.2f; // 玩法推动占地，不依赖物理碰撞体。
    public Vector2 DrivenVelocity { get; private set; } // 只有主动移动贡献驱动行走动画。
    public Vector2 ExternalVelocity { get; private set; } // 水流/承载等被动速度贡献。
    public Vector2 RequestedMoveInput { get; private set; } // 当前真实输入，推动来源失效时可立即撤销。

    private InputAction moveAction;
    private InputAction holdRunAction;
    private InputAction toggleRunAction;
    private bool holdRunInputActive; // 长按奔跑输入的语义状态；载具等玩法只消费这里，不重复轮询物理按键。
    private Mod_GameController inputController;
    private readonly List<IWaterCurrentExposure> waterCurrentExposures = new(); // 离地模块控制表层水流接触。
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
    /// <summary>实体实际进入新的 1×1 世界单位格时通知依赖位置变化的系统。</summary>
    public event System.Action<Vector2Int> WorldUnitChanged;

    private Vector2Int _lastWorldUnit;
    private bool _hasTrackedWorldUnit;

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

    /// <summary>设置当前移动表面对加速度和减速度的影响。</summary>
    public void SetSurfaceMovementResponse(float accelerationMultiplier, float decelerationMultiplier)
    {
        surfaceAccelerationMultiplier = Mathf.Max(0.01f, accelerationMultiplier);
        surfaceDecelerationMultiplier = Mathf.Max(0.01f, decelerationMultiplier);
    }

    #endregion

    #region Unity 生命周期
    public virtual void OnValidate()
    {
        _Data.ID = ModText.Mod_Mover;
        speedTransitionDuration = Mathf.Max(MinimumTransitionDuration, speedTransitionDuration);
        stopTransitionDuration = Mathf.Max(MinimumTransitionDuration, stopTransitionDuration);
        surfaceAccelerationMultiplier = Mathf.Max(0.01f, surfaceAccelerationMultiplier);
        surfaceDecelerationMultiplier = Mathf.Max(0.01f, surfaceDecelerationMultiplier);
        hungerAction?.ClampValues();
    }

    public override void Load()
    {
        ModDataMemoryPack.ReadData(ref Data);
        bool persistedRunState = Data.isRunning;
        Data.isRunning = false;

        rb = ItemPhysicsProjection2D.EnsureMovable(item);
        DrivenVelocity = ExternalVelocity = RequestedMoveInput = Vector2.zero;
        _wasMoving = IsMoving = false;
        ResetWorldUnitTracking();
        waterCurrentExposures.Clear();
        foreach (Module module in item.itemMods.Mods.Values)
            if (module is IWaterCurrentExposure exposure)
                waterCurrentExposures.Add(exposure);

        hungerAction ??= new MovementHungerActionDefinition();
        hungerActionInstance = hungerAction.CreateInstance(item);

        // 动物不含玩家输入与体力模块，按可选依赖安静解析。
        Mod_GameController controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller != null)
        {
            inputController = controller;
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

        InitializeWetClothesAudio();

        // 先恢复基础数据，再通过统一入口重建奔跑倍率与动画状态。
        if (persistedRunState)
            SetRunState(true);
    }



    private bool _wasMoving = false;

    /// <summary>物理接触由 Rigidbody2D 结算，这里只检测实际到达的世界单位格。</summary>
    private void FixedUpdate()
    {
        if (rb == null)
            return;

        if (WorldUnitChanged == null)
            return;

        Vector2 position = WorldTopologyRuntime.NormalizePosition(rb.position);
        var currentWorldUnit = new Vector2Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y));
        if (!_hasTrackedWorldUnit)
        {
            _lastWorldUnit = currentWorldUnit;
            _hasTrackedWorldUnit = true;
            return;
        }

        if (currentWorldUnit == _lastWorldUnit)
            return;

        _lastWorldUnit = currentWorldUnit;
        WorldUnitChanged?.Invoke(currentWorldUnit);
    }

    /// <summary>重置世界单位格基线，避免加载或重建时把初始位置误判为移动事件。</summary>
    private void ResetWorldUnitTracking()
    {
        if (rb == null)
        {
            _hasTrackedWorldUnit = false;
            return;
        }

        Vector2 position = WorldTopologyRuntime.NormalizePosition(rb.position);
        _lastWorldUnit = new Vector2Int(
            Mathf.FloorToInt(position.x),
            Mathf.FloorToInt(position.y));
        _hasTrackedWorldUnit = true;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (UpdateCarrierMotion(deltaTime)) return;
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
            Mod_GameController controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            if (controller != null && controller.IsGameplayInputLocked)
            {
                StopImmediately();
                // 输入锁定只停止位移，奔跑开关作为存档状态保留，解锁后继续沿用。
                hungerActionInstance?.SetMovementState(false, false);
                return;
            }
        }

        Vector2 input = inputController != null
            ? inputController.ReadMoveInput(moveAction)
            : moveAction.ReadValue<Vector2>();
        input = BodyTraumaBuffEffects.TransformMoveInput(item, input, Time.time);
        bool isCurrentlyMoving = input.sqrMagnitude > InputMoveThresholdSqr;

        if (isCurrentlyMoving)
        {
            MoveByInput(input, deltaTime);

            if (stamina != null)
            {
                float consumePerSecond = IsRunning ? RunStaminaConsume : MoveStaminaConsume;
                string sourceId = IsRunning
                    ? StaminaConsumptionSources.MovementRun
                    : StaminaConsumptionSources.MovementWalk;
                stamina.ConsumeStaminaPerSecond(sourceId, consumePerSecond, deltaTime);

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

    /// <summary>设置可持久化的奔跑模式；输入锁只限制玩家输入，不阻止读档或跨维度恢复该状态。</summary>
    public void SetRunState(bool isRun)
    {
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
        if (CarrierSource != null) return;
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行 Move！");
            return;
        }

        Vector2 delta = WorldTopologyRuntime.ShortestDelta(rb.position, targetPosition);
        float moveSpeed = Speed.Value * ResolveBuildingMoveSpeedMultiplier();
        Vector2 targetVelocity = delta.sqrMagnitude < ArriveThreshold * ArriveThreshold
            ? Vector2.zero
            : delta.normalized * moveSpeed;
        DrivenVelocity = SmoothSurfaceVelocity(DrivenVelocity, targetVelocity, deltaTime);
        ExternalVelocity = ResolveWaterCurrentVelocity();
        rb.velocity = DrivenVelocity + ExternalVelocity;
        UpdateMovementState();
    }

    /// <summary>按二维输入幅度驱动移动；水流是额外的表层漂移，不计作主动奔跑或体力消耗。</summary>
    public void MoveByInput(Vector2 input, float deltaTime)
    {
        if (CarrierSource != null) return;
        if (rb == null)
        {
            Debug.LogError($"{name}: Rigidbody2D 为空，无法执行输入移动！");
            return;
        }

        Vector2 clampedInput = Vector2.ClampMagnitude(input, 1f);
        RequestedMoveInput = clampedInput;
        float moveSpeed = Speed.Value * ResolveBuildingMoveSpeedMultiplier();
        Vector2 targetVelocity = clampedInput.sqrMagnitude > InputMoveThresholdSqr
            ? clampedInput * moveSpeed
            : Vector2.zero;
        // 主动速度独立缓动，不能把上帧水流当作下帧主动移动的初速度。
        DrivenVelocity = SmoothSurfaceVelocity(DrivenVelocity, targetVelocity, deltaTime);
        ExternalVelocity = ResolveWaterCurrentVelocity();
        rb.velocity = DrivenVelocity + ExternalVelocity;
        UpdateMovementState();
    }

    /// <summary>导航移动与水流分别结算，动物静止时也会被推动而不会播放行走动画。</summary>
    public void ApplyNavigationVelocity(Vector2 targetVelocity, float deltaTime)
    {
        if (CarrierSource != null) return;
        if (rb == null)
            throw new System.InvalidOperationException($"{name}: 导航移动缺少 Rigidbody2D。");
        if (deltaTime <= 0f)
        {
            rb.velocity = Vector2.zero;
            DrivenVelocity = ExternalVelocity = Vector2.zero;
            UpdateMovementState();
            return;
        }

        DrivenVelocity = SmoothSurfaceVelocity(DrivenVelocity, targetVelocity, deltaTime);
        ExternalVelocity = ResolveWaterCurrentVelocity();
        rb.velocity = DrivenVelocity + ExternalVelocity;
        UpdateMovementState();
    }

    /// <summary>统一读取有效地表流向与 JSON 推动速度，平台和静水不推动角色。</summary>
    public Vector2 ResolveWaterCurrentVelocity()
    {
        foreach (IWaterCurrentExposure exposure in waterCurrentExposures)
            if (!exposure.ReceivesWaterCurrent)
                return Vector2.zero;
        return item != null && rb != null
            ? WorldMotionSystem.SampleWaterVelocity(rb.position,
                WaterCurrentPushConfigService.ResolvePushSpeed(item))
            : Vector2.zero;
    }

    /// <summary>玩家站在可通行建筑格时读取地块惩罚；动物和其它 Mod_Mover 不受玩家铺线规则影响。</summary>
    private float ResolveBuildingMoveSpeedMultiplier()
    {
        return item is Player && rb != null
            ? BuildingOccupancyRegistry.GetPlayerMoveSpeedMultiplier(rb.position)
            : 1f;
    }

    /// <summary>将实际速度按当前移动表面的响应平滑到目标速度。</summary>
    public Vector2 SmoothSurfaceVelocity(Vector2 currentVelocity, Vector2 targetVelocity, float deltaTime)
    {
        bool isStopping = targetVelocity.sqrMagnitude <= InputMoveThresholdSqr;
        float transitionDuration = isStopping
            ? stopTransitionDuration
            : speedTransitionDuration;
        float referenceSpeed = Mathf.Max(
            Mathf.Max(currentVelocity.magnitude, targetVelocity.magnitude),
            Mathf.Max(0f, Speed.Value));
        float minimumChangeRate = isStopping ? Mathf.Max(0f, slowDownSpeed) : 0f;
        float speedChangeRate = Mathf.Max(
            minimumChangeRate,
            referenceSpeed / Mathf.Max(MinimumTransitionDuration, transitionDuration));
        float surfaceMultiplier = isStopping
            ? surfaceDecelerationMultiplier
            : surfaceAccelerationMultiplier;
        Vector2 nextVelocity = Vector2.MoveTowards(
            currentVelocity,
            targetVelocity,
            speedChangeRate * surfaceMultiplier * Mathf.Max(0f, deltaTime));

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
        DrivenVelocity = ExternalVelocity = RequestedMoveInput = Vector2.zero;
        UpdateMovementState();
    }

    /// <summary>仅主动移动播放行走动画；海水漂移和船的承载不冒充角色自身迈步。</summary>
    private void UpdateMovementState()
    {
        float stopThreshold = Mathf.Max(0.001f, endSpeed);
        bool isActuallyMoving = CarrierSource == null && rb != null &&
                                DrivenVelocity.sqrMagnitude > stopThreshold * stopThreshold;
        IsMoving = isActuallyMoving;

        if (_wasMoving != isActuallyMoving)
            RefreshWetClothesAudio();

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
        SaveCarrierSafePosition();
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
        ReleaseCarrierLease();
        UnbindRunActions();
        DisposeWetClothesAudio();
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
        holdRunInputActive = false;

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
        holdRunInputActive = false;
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
        // 即使按下瞬间正被 UI 锁住，也记录真实长按状态；玩法解锁后可继续正确识别仍按住的 Shift。
        holdRunInputActive = true;
        if (inputController != null && !inputController.IsGameplayInputAllowed(context))
            return;

        HandleHoldRunInputPressed();
    }

    private void OnHoldRunActionCanceled(InputAction.CallbackContext context)
    {
        holdRunInputActive = false;
        if (inputController != null && !inputController.IsGameplayInputAllowed(context))
            return;

        HandleHoldRunInputReleased();
    }

    private void OnToggleRunActionPerformed(InputAction.CallbackContext context)
    {
        if (inputController != null && !inputController.IsGameplayInputAllowed(context))
            return;

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

    [Min(0f)]
    [Tooltip("奔跑时水分消耗倍率。")]
    public float runWaterConsumeMultiplier = 0.25f;

    /// <summary>校正运行时和 Inspector 可能写入的非法配置。</summary>
    public void ClampValues()
    {
        moveNutritionConsumeMultiplier = Mathf.Max(0f, moveNutritionConsumeMultiplier);
        runNutritionConsumeMultiplier = Mathf.Max(0f, runNutritionConsumeMultiplier);
        runWaterConsumeMultiplier = Mathf.Max(0f, runWaterConsumeMultiplier);
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

    public float ResolveWaterMultiplier(bool isMoving, bool isRunning)
    {
        if (!enabled || !isMoving || !isRunning)
            return 1f;

        return Mathf.Max(0f, runWaterConsumeMultiplier);
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
        float waterMultiplier = definition?.ResolveWaterMultiplier(isMoving, isRunning) ?? 1f;
        food.SetMovementNutritionConsumeMultiplier(multiplier);
        food.SetMovementWaterConsumeMultiplier(waterMultiplier);
    }
}

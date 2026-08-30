using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 管理员相关输入与时间控制逻辑的独立 Mono 脚本。
/// 挂在与 Player 相同的 GameObject 上，通过引用 Player 来操作玩家数据。
/// </summary>
public class PlayerAdminController : Module
{
    #region 常量与字段

    private const string AdminName = "管理员";
    public const float MinAdminMoveSpeedMultiplier = 0.1f;
    public const float MaxAdminMoveSpeedMultiplier = 100f;
    private static readonly float[] AdminMoveSpeedPresets = { 1f, 2f, 3f, 5f };

    public static bool TeleportToMouseShortcutEnabled { get; private set; } = true;

    /// <summary>本次运行中管理员无敌开关；默认关闭，不写入玩家存档。</summary>
    public static bool AdminInvincibilityEnabled { get; private set; } = false;

    [Header("核心引用")]
    [Tooltip("要控制的玩家组件")]
    public Player player;

    [Tooltip("玩家特质模块")]
    public Mod_PlayerTraits playerTraits;

    [Tooltip("玩家快捷栏，用于操作手持物品")]
    public Inventory_HotBar hotbar;

    [Tooltip("生命模块（管理员模式维持不死）")]
    public DamageReceiver damageReceiver;

    [Tooltip("理智模块（管理员模式维持不死）")]
    public Mod_San sanMod;

    [Tooltip("食物模块（管理员模式维持不饿）")]
    public Mod_Food foodMod;

    public Mod_Cam adminCamera;
    public Mod_ChunkLoader chunkLoader;

    [Header("时间控制")]
    [Tooltip("时间流逝速度")]
    public float timeScale = 1.0f;

    [Tooltip("每次调整时间速度的增量")]
    public float timeScaleStep = 0.5f;

    [Tooltip("最小时间速度")]
    public float minTimeScale = 0.1f;

    [Tooltip("最大时间速度")]
    public float maxTimeScale = 10.0f;

    [Header("时间提示GUI")]
    [Tooltip("时间提示显示时长（秒）")]
    public float timeScaleHintDuration = 1.0f;

    // 时间提示内部变量
    private string timeScaleHintText = string.Empty;
    private float timeScaleHintTimer = 0f;
    private bool showTimeScaleHint = false;
    private float initialUnityTimeScale = 1.0f;
    private bool adminRuntimeSettingsApplied = false;
    private GameController gameController;
    private Mover playerMover;
    private Mover adminMoveSpeedTarget;
    private DamageReceiver boundDamageReceiver;
    private float appliedAdminMoveSpeedMultiplier = 1f;

    public float AdminMoveSpeedMultiplier { get; private set; } = 1f;

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    #endregion

    #region 生命周期

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeSettings()
    {
        TeleportToMouseShortcutEnabled = true;
        AdminInvincibilityEnabled = false;
    }

    private void Start()
    {
        // 记录初始时间缩放，以便还原
        initialUnityTimeScale = Time.timeScale;
    }

    public override void Load()
    {
        // 尝试自动获取 Player 引用
        if (player == null) player = GetComponentInParent<Player>();
        if (player == null)
        {
            Debug.LogError($"[PlayerAdminController] 初始化失败：未找到 Player 组件！GameObject: {name}");
            return;
        }

        if (gameController == null)
            gameController = GetComponentInParent<GameController>();

        // 尝试获取模块引用
        if (player.itemMods != null)
        {
            if (playerTraits == null)
                playerTraits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);

            if (hotbar == null)
            {
                var hotbarMod = player.itemMods.GetMod_ByID(ModText.Hotbar);
                hotbar = hotbarMod?.GetComponent<Inventory_HotBar>();
            }

            if (damageReceiver == null)
                damageReceiver = player.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);

            if (sanMod == null)
                sanMod = player.itemMods.GetMod_ByID<Mod_San>(Mod_San.ModuleId);

            if (foodMod == null)
                foodMod = player.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);

            if (adminCamera == null)
                adminCamera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);

            if (chunkLoader == null)
                chunkLoader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);

            if (playerMover == null)
                playerMover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);
        }

        BindAdminDamageReceiver();
    }

    private void OnDestroy()
    {
        UnbindAdminDamageReceiver();
        Time.timeScale = initialUnityTimeScale;
    }

    private void Update()
    {
        // 基础检查：确保 Player 数据存在
        if (player?.Data == null) return;

        UpdateTimeScaleHint();

        if (gameController == null)
            gameController = GetComponentInParent<GameController>();
        if (gameController != null && gameController.IsGameplayInputLocked)
            return;

        Keyboard keyboard = Keyboard.current;

        // F1：切换管理员权限
        if (keyboard?.f1Key.wasPressedThisFrame == true)
        {
            Debug.Log("F1键被按下，切换管理员身份");
            player.Data.Name_User = AdminName;
        }

        // 非管理员不执行后续逻辑
        if (!IsAdmin()) return;

        ApplyAdminRuntimeSettings();
        KeepAdminAlive();
        HandleAdminInput(keyboard);
        HandleTimeScaleControl(keyboard);
    }

    private void OnGUI()
    {
        if (showTimeScaleHint && !string.IsNullOrEmpty(timeScaleHintText))
        {
            DrawTimeScaleHint();
        }
    }

    public override void Save() { }

    #endregion

    #region 管理员核心逻辑

    private bool IsAdmin()
    {
        return player?.Data?.Name_User == AdminName;
    }

    /// <summary>当前玩家是否拥有管理员权限。</summary>
    public bool IsAdministrator => IsAdmin();

    /// <summary>管理员权限与无敌开关同时开启时，玩家才受无敌保护。</summary>
    public bool IsAdminInvincibilityEnabled => IsAdministrator && AdminInvincibilityEnabled;

    /// <summary>为当前本地玩家开启管理员权限，兼容既有 F1 行为。</summary>
    public bool TryEnableAdministrator()
    {
        ResolveAdminSurvivalReferences();
        if (player?.Data == null)
        {
            return false;
        }

        player.Data.Name_User = AdminName;
        return true;
    }

    /// <summary>仅管理员可切换无敌；重新开启时立即恢复生存状态。</summary>
    public bool TrySetAdminInvincibilityEnabled(bool enabled)
    {
        if (!IsAdministrator)
        {
            return false;
        }

        AdminInvincibilityEnabled = enabled;
        if (!enabled)
        {
            return true;
        }

        ResolveAdminSurvivalReferences();
        RestoreAdminVitalStats();
        ResumeDyingStateForAdminInvincibility();
        return true;
    }

    /// <summary>切换当前管理员玩家的无敌状态。</summary>
    public bool TryToggleAdminInvincibility(out bool enabled)
    {
        enabled = AdminInvincibilityEnabled;
        if (!IsAdministrator)
        {
            return false;
        }

        enabled = !AdminInvincibilityEnabled;
        return TrySetAdminInvincibilityEnabled(enabled);
    }

    private void HandleAdminInput(Keyboard keyboard)
    {
        if (keyboard == null)
            return;

        // Ctrl + T：传送到鼠标位置，裸 T 留给聊天框
        if (TeleportToMouseShortcutEnabled &&
            keyboard.tKey.wasPressedThisFrame &&
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            playerTraits?.TeleportToMousePosition();
        }

        // F2：初始化创造模式背包
        if (keyboard.f2Key.wasPressedThisFrame)
        {
            playerTraits?.InitializeCreativeInventoryForAdmin();
        }

        // F5：给予背包全部物品 (999)
        if (keyboard.f5Key.wasPressedThisFrame)
        {
            AddAmountToAllBagItems(999f);
        }

        if (keyboard.f8Key.wasPressedThisFrame)
        {
            IncreaseAdminChunkLoadDistance();
        }
    }

    public static bool ToggleTeleportToMouseShortcut()
    {
        TeleportToMouseShortcutEnabled = !TeleportToMouseShortcutEnabled;
        return TeleportToMouseShortcutEnabled;
    }

    private void ApplyAdminRuntimeSettings()
    {
        if (adminRuntimeSettingsApplied)
            return;

        ResolveAdminRuntimeReferences();

        if (adminCamera != null)
        {
            adminCamera.EnableUnlimitedView();
        }

        adminRuntimeSettingsApplied = adminCamera != null;
    }

    private void ResolveAdminRuntimeReferences()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        if (adminCamera == null)
        {
            if (player?.itemMods != null && player.itemMods.ContainsKey_ID(ModText.Camera))
                adminCamera = player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera);

            if (adminCamera == null)
                adminCamera = GetComponentInParent<Mod_Cam>();
        }

        if (chunkLoader == null)
        {
            if (player?.itemMods != null && player.itemMods.ContainsKey_ID(ModText.ChunkLoader))
                chunkLoader = player.itemMods.GetMod_ByID<Mod_ChunkLoader>(ModText.ChunkLoader);

            if (chunkLoader == null)
                chunkLoader = GetComponentInParent<Mod_ChunkLoader>();
        }

        if (playerMover == null)
        {
            if (player?.itemMods != null && player.itemMods.ContainsKey_ID(ModText.Mover))
                playerMover = player.itemMods.GetMod_ByID<Mover>(ModText.Mover);

            if (playerMover == null)
                playerMover = GetComponentInChildren<Mover>(true);
        }
    }

    /// <summary>懒获取无敌恢复所需的生存模块，并保持伤害事件绑定有效。</summary>
    private void ResolveAdminSurvivalReferences()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        if (player?.itemMods == null)
            return;

        if (damageReceiver == null)
            damageReceiver = player.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp);

        if (sanMod == null)
            sanMod = player.itemMods.GetMod_ByID<Mod_San>(Mod_San.ModuleId);

        if (foodMod == null)
            foodMod = player.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);

        BindAdminDamageReceiver();
    }

    public bool TryCycleAdminMoveSpeedMultiplier(out float selectedMultiplier)
    {
        int nextIndex = 0;
        for (int i = 0; i < AdminMoveSpeedPresets.Length; i++)
        {
            if (!Mathf.Approximately(AdminMoveSpeedMultiplier, AdminMoveSpeedPresets[i]))
                continue;

            nextIndex = (i + 1) % AdminMoveSpeedPresets.Length;
            break;
        }

        selectedMultiplier = AdminMoveSpeedPresets[nextIndex];
        if (TrySetAdminMoveSpeedMultiplier(selectedMultiplier, out selectedMultiplier))
            return true;

        selectedMultiplier = AdminMoveSpeedMultiplier;
        return false;
    }

    public bool TrySetAdminMoveSpeedMultiplier(float multiplier, out float appliedMultiplier)
    {
        appliedMultiplier = AdminMoveSpeedMultiplier;
        if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
        {
            Debug.LogWarning("[Admin] Player move speed multiplier must be a finite number.");
            return false;
        }

        ResolveAdminRuntimeReferences();
        if (playerMover?.Speed == null)
        {
            Debug.LogWarning("[Admin] Player move speed adjustment failed: Mover not found.");
            return false;
        }

        multiplier = Mathf.Clamp(
            multiplier,
            MinAdminMoveSpeedMultiplier,
            MaxAdminMoveSpeedMultiplier);
        if (adminMoveSpeedTarget != playerMover)
        {
            adminMoveSpeedTarget = playerMover;
            appliedAdminMoveSpeedMultiplier = 1f;
        }

        float previousMultiplier = Mathf.Max(0.01f, appliedAdminMoveSpeedMultiplier);
        playerMover.Speed.MultiplicativeModifier =
            playerMover.Speed.MultiplicativeModifier / previousMultiplier * multiplier;
        appliedAdminMoveSpeedMultiplier = multiplier;
        AdminMoveSpeedMultiplier = multiplier;
        appliedMultiplier = multiplier;
        Debug.Log($"[Admin] Player move speed multiplier set to {multiplier:0.##}x.");
        return true;
    }

    private void IncreaseAdminChunkLoadDistance()
    {
        ResolveAdminRuntimeReferences();

        if (chunkLoader == null)
        {
            Debug.LogWarning("[Admin] Increase chunk load distance failed: Mod_ChunkLoader not found.");
            return;
        }

        int currentDistance = chunkLoader.IncreaseLoadDistanceForAdmin(1);
        Debug.Log($"[Admin] Chunk load distance increased to {currentDistance}.");
    }

    private void KeepAdminAlive()
    {
        if (!IsAdminInvincibilityEnabled)
            return;

        ResolveAdminSurvivalReferences();
        RestoreAdminVitalStats();
    }

    /// <summary>无敌开启时把生命、理智与饥饿恢复到满值。</summary>
    private void RestoreAdminVitalStats()
    {
        if (damageReceiver != null && damageReceiver.MaxHp > 0f && damageReceiver.Hp < damageReceiver.MaxHp)
        {
            damageReceiver.Hp = damageReceiver.MaxHp;
        }

        if (sanMod != null && sanMod.MaxValue > 0f && sanMod.CurrentValue < sanMod.MaxValue)
        {
            sanMod.CurrentValue = sanMod.MaxValue;
        }

        KeepAdminNotHungry();
    }

    /// <summary>伤害结算后立刻回满生命，避免致死伤害进入玩家濒死流程。</summary>
    private void HandleAdminDamageReceived(DamageReceiverDamageInfo damageInfo)
    {
        _ = damageInfo;
        if (!IsAdminInvincibilityEnabled)
            return;

        RestoreAdminVitalStats();
    }

    /// <summary>绑定生命模块事件，生命模块替换时自动解除旧监听。</summary>
    private void BindAdminDamageReceiver()
    {
        if (boundDamageReceiver == damageReceiver)
            return;

        UnbindAdminDamageReceiver();
        boundDamageReceiver = damageReceiver;
        if (boundDamageReceiver != null)
            boundDamageReceiver.OnDamageReceived += HandleAdminDamageReceived;
    }

    /// <summary>解除生命模块事件，避免场景重建后遗留监听。</summary>
    private void UnbindAdminDamageReceiver()
    {
        if (boundDamageReceiver != null)
            boundDamageReceiver.OnDamageReceived -= HandleAdminDamageReceived;

        boundDamageReceiver = null;
    }

    /// <summary>重新开启无敌时，若玩家已处于濒死则立即恢复操作。</summary>
    private void ResumeDyingStateForAdminInvincibility()
    {
        Mod_PlayerDeathState deathState = player?.itemMods?
            .GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
        deathState?.TryResumeFromAdminInvincibility();
    }

    private void KeepAdminNotHungry()
    {
        if (foodMod == null)
            foodMod = player?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);

        if (foodMod == null)
            return;

        foodMod.Data.nutrition.Carbohydrates = foodMod.Data.nutrition.Max_Carbohydrates;
        foodMod.Data.nutrition.Fat = foodMod.Data.nutrition.Max_Fat;
        foodMod.Data.nutrition.Protein = foodMod.Data.nutrition.Max_Protein;
        foodMod.Data.nutrition.Water = foodMod.Data.nutrition.Max_Water;
    }

    #endregion

    #region 物品操作逻辑

    /// <summary>
    /// 给玩家当前手持物品增加指定数量
    /// </summary>
    private void AddAmountToCurrentHandItem(float amount)
    {
        // 懒加载/容错获取
        if (hotbar == null)
            hotbar = player?.GetComponentInChildren<Inventory_HotBar>();

        if (hotbar == null)
        {
            Debug.LogError("[Admin] 操作失败：找不到 Inventory_HotBar 组件");
            return;
        }

        var slot = hotbar.CurrentSelectItemSlot;
        if (slot?.itemData == null)
        {
            Debug.LogWarning("[Admin] 操作无效：当前手中没有物品");
            return;
        }

        slot.itemData.Stack.Amount += amount;
        hotbar.RefreshUI(hotbar.CurrentIndex);

        Debug.Log($"[Admin] 手持物品 {slot.itemData.IDName} 增加 {amount}，当前: {slot.itemData.Stack.Amount}");
    }

    /// <summary>
    /// 为玩家背包中的所有物品增加指定数量
    /// </summary>
    private void AddAmountToAllBagItems(float amount)
    {
        // 链式获取，减少嵌套
        var bagMod = player?.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        
        // 检查关键路径
        if (bagMod?.inventory?.Data?.itemSlots == null)
        {
            // 如果 player 存在但没找到背包，这通常是配置错误
            if (player != null) Debug.LogError("[Admin] 操作失败：找不到背包数据 (Inventory/Data/Slots)");
            return;
        }

        int changedCount = 0;
        foreach (var slot in bagMod.inventory.Data.itemSlots)
        {
            if (slot?.itemData != null)
            {
                slot.itemData.AddAmount(amount);
                changedCount++;
            }
        }

        bagMod.inventory.RefreshUI();
        Debug.Log($"[Admin] 背包中 {changedCount} 个物品各增加数量 {amount}");
    }

    #endregion

    #region 时间控制系统

    private void HandleTimeScaleControl(Keyboard keyboard)
    {
        if (keyboard == null)
            return;

        bool timeScaleChanged = false;

        // 加速
        if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
        {
            timeScaleChanged = TryUpdateTimeScale(timeScaleStep);
        }

        // 减速
        if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
        {
            timeScaleChanged = TryUpdateTimeScale(-timeScaleStep);
        }

        // 重置
        if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
        {
            ResetTimeScale();
            timeScaleChanged = true;
        }

        if (timeScaleChanged)
        {
            ShowTimeScaleHint();
        }
    }

    private void ResetTimeScale()
    {
        timeScale = 1.0f;
        Time.timeScale = timeScale;
        timeScaleHintText = "时间速度已重置为正常速度";
        Debug.Log(timeScaleHintText);
    }

    private bool TryUpdateTimeScale(float delta)
    {
        float newScale = Mathf.Clamp(timeScale + delta, minTimeScale, maxTimeScale);
        
        // 如果变化极小，忽略
        if (Mathf.Approximately(newScale, timeScale)) return false;

        timeScale = newScale;
        Time.timeScale = timeScale;
        timeScaleHintText = $"时间速度: {timeScale:F1}x";
        Debug.Log($"时间速度调整为: {timeScale:F1}x");
        return true;
    }

    private void ShowTimeScaleHint()
    {
        showTimeScaleHint = true;
        timeScaleHintTimer = timeScaleHintDuration;
    }

    private void UpdateTimeScaleHint()
    {
        if (!showTimeScaleHint) return;

        timeScaleHintTimer -= Time.unscaledDeltaTime;
        if (timeScaleHintTimer <= 0)
        {
            showTimeScaleHint = false;
        }
    }

    private void DrawTimeScaleHint()
    {
        float alpha = Mathf.Clamp01(timeScaleHintTimer / timeScaleHintDuration);
        
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(1, 1, 1, alpha);
        
        // 显示在屏幕上方 1/4 处
        Rect position = new Rect(0, Screen.height * 0.25f, Screen.width, 50);
        GUI.Label(position, timeScaleHintText, style);
    }

    #endregion
}

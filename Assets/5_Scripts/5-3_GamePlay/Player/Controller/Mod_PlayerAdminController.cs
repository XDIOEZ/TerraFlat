using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家管理员输入、时间和快捷栏适配；权限由独立通用模块提供。
/// </summary>
public class Mod_PlayerAdminController : Module
{
    #region 常量与字段

    public const float MinAdminMoveSpeedMultiplier = 0.1f;
    public const float MaxAdminMoveSpeedMultiplier = 100f;
    private static readonly float[] AdminMoveSpeedPresets = { 1f, 2f, 3f, 5f };

    public static bool TeleportToMouseShortcutEnabled { get; private set; } = true;

    [Header("核心引用")]
    [Tooltip("要控制的玩家组件")]
    public Player player;

    [Tooltip("玩家特质模块")]
    public Mod_PlayerTraits playerTraits;

    [Tooltip("玩家快捷栏，用于操作手持物品")]
    public Mod_HotBar hotbar;

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
    private bool timeScaleHintIsReset;
    private float timeScaleHintTimer = 0f;
    private bool showTimeScaleHint = false;
    private float initialUnityTimeScale = 1.0f;
    private Mod_GameController gameController;
    private Mod_Mover playerMover;
    private Mod_Mover adminMoveSpeedTarget;
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
    }

    private void Start()
    {
        // 记录初始时间缩放，以便还原
        initialUnityTimeScale = Time.timeScale;
    }

    protected override void OnLoad()
    {
        // 尝试自动获取 Player 引用
        if (player == null) player = GetComponentInParent<Player>();
        if (player == null)
        {
            Debug.LogError($"[Mod_PlayerAdminController] 初始化失败：未找到 Player 组件！GameObject: {name}");
            return;
        }

        if (gameController == null)
            gameController = GetComponentInParent<Mod_GameController>();

        // 尝试获取模块引用
        if (player.itemMods != null)
        {
            if (playerTraits == null)
                playerTraits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);

            if (hotbar == null)
            {
                var hotbarMod = player.itemMods.GetMod_ByID(ModText.Hotbar);
                hotbar = hotbarMod?.GetComponent<Mod_HotBar>();
            }

            if (playerMover == null)
                playerMover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        }
    }

    private void OnDestroy()
    {
        Time.timeScale = initialUnityTimeScale;
    }

    private void Update()
    {
        // 基础检查：确保 Player 数据存在
        if (player?.Data == null) return;

        UpdateTimeScaleHint();

        if (gameController == null)
            gameController = GetComponentInParent<Mod_GameController>();

        Keyboard keyboard = Keyboard.current;

        // F2 允许穿过当前玩家主背包的输入锁，其它模态面板和世界加载仍阻止该快捷键。
        if (keyboard?.f2Key.wasPressedThisFrame == true && CanOpenCreativeInventoryShortcut())
            OpenCreativeInventoryShortcut();

        if (gameController != null && gameController.IsGameplayInputLocked)
            return;

        // Ctrl+F1：启用管理员权限；裸 F1 由 UIManager 用于宣传片录制模式。
        if (keyboard?.f1Key.wasPressedThisFrame == true &&
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            Debug.Log("Ctrl+F1 被按下，启用管理员权限");
            TryEnableAdministrator();
        }

        // 非管理员不执行后续逻辑
        if (!IsAdministrator) return;

        HandleTimeScaleControl(keyboard);
    }

    private void OnGUI()
    {
        if (showTimeScaleHint)
            DrawTimeScaleHint();
    }

    protected override void OnSave() { }

    #endregion

    #region 管理员核心逻辑

    /// <summary>权限由角色持有的通用管理员模块提供。</summary>
    public bool IsAdministrator => Mod_Administrator.GetLoadedAdministratorModule(item ?? player) != null;
    public bool IsAdminInvincibilityEnabled => Mod_Administrator.GetLoadedAdministratorModule(item ?? player)?.IsInvincibilityEnabled == true;

    /// <summary>Ctrl+F1 与 GM 共用正式动态模块装配，不修改角色名字。</summary>
    public bool TryEnableAdministrator()
    {
        ResolveAdminRuntimeReferences();
        if (player == null) return false;
        return Mod_Administrator.EnsureAdministratorModule(player) != null;
    }

    public bool TrySetAdminInvincibilityEnabled(bool enabled)
    {
        Mod_Administrator administrator = Mod_Administrator.GetLoadedAdministratorModule(item ?? player);
        if (administrator == null) return false;
        administrator.SetInvincibilityEnabled(enabled);
        if (enabled)
            player?.itemMods?.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId)
                ?.TryResumeFromAdminInvincibility();
        return true;
    }

    public bool TryToggleAdminInvincibility(out bool enabled)
    {
        enabled = !IsAdminInvincibilityEnabled;
        return TrySetAdminInvincibilityEnabled(enabled);
    }

    /// <summary>F2 只遵守统一玩法输入锁；主背包本身不再制造输入锁。</summary>
    private bool CanOpenCreativeInventoryShortcut()
    {
        return gameController == null || !gameController.IsGameplayInputLocked;
    }

    /// <summary>F2 一次完成创造背包初始化，并确保玩家主背包面板处于打开状态。</summary>
    private void OpenCreativeInventoryShortcut()
    {
        if (player == null || !player.IsLocalProfile)
            return;

        if (playerTraits == null && player.itemMods != null)
            playerTraits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        if (playerTraits == null)
        {
            Debug.LogWarning("[Mod_PlayerAdminController] F2 创造背包失败：未找到玩家特质模块。");
            return;
        }

        playerTraits.InitializeCreativeInventoryForAdmin();

        Mod_Inventory bagMod = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        Inventory bag = bagMod?.inventory;
        if (bag == null)
        {
            Debug.LogWarning("[Mod_PlayerAdminController] F2 创造背包已初始化，但未找到玩家主背包面板。");
            return;
        }

        // SwitchUI 会负责首次创建、发布背包打开事件并在下一帧置顶；已打开时不重复 Toggle，避免按 F2 把面板关掉。
        if (bag.basePanel == null || !bag.basePanel.IsOpen())
            bag.SwitchUI();
    }

    public static bool ToggleTeleportToMouseShortcut()
    {
        TeleportToMouseShortcutEnabled = !TeleportToMouseShortcutEnabled;
        return TeleportToMouseShortcutEnabled;
    }

    private void ResolveAdminRuntimeReferences()
    {
        if (player == null)
            player = GetComponentInParent<Player>();

        if (playerMover == null)
        {
            if (player?.itemMods != null && player.itemMods.ContainsKey_ID(ModText.Mod_Mover))
                playerMover = player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);

            if (playerMover == null)
                playerMover = GetComponentInChildren<Mod_Mover>(true);
        }
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
            Debug.LogWarning("[Admin] Player move speed adjustment failed: Mod_Mover not found.");
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

    #endregion

    #region 物品操作逻辑

    /// <summary>
    /// 给玩家当前手持物品增加指定数量
    /// </summary>
    private void AddAmountToCurrentHandItem(float amount)
    {
        // 懒加载/容错获取
        if (hotbar == null)
            hotbar = player?.GetComponentInChildren<Mod_HotBar>();

        if (hotbar == null)
        {
            Debug.LogError("[Admin] 操作失败：找不到 Mod_HotBar 组件");
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
        timeScaleHintIsReset = true;
        Debug.Log("时间速度已重置为正常速度");
    }

    /// <summary>按当前值增减时间流速，供管理员快捷键和旧命令调用。</summary>
    private bool TryUpdateTimeScale(float delta)
    {
        return TrySetTimeScale(timeScale + delta);
    }

    /// <summary>将时间流速设为指定倍率，并限制在当前管理员配置范围内。</summary>
    public bool TrySetTimeScale(float requestedScale)
    {
        if (float.IsNaN(requestedScale) || float.IsInfinity(requestedScale))
            return false;

        float newScale = Mathf.Clamp(requestedScale, minTimeScale, maxTimeScale);

        // 如果变化极小，忽略
        if (Mathf.Approximately(newScale, timeScale)) return false;

        timeScale = newScale;
        Time.timeScale = timeScale;
        timeScaleHintIsReset = false;
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
        string hint = timeScaleHintIsReset
            ? FlatWorldLocalizationService.GetUiText("时间速度已重置为正常速度")
            : FlatWorldLocalizationService.GetUiFormat("时间速度：{0:F1}×", timeScale);
        GUI.Label(position, hint, style);
    }

    #endregion
}

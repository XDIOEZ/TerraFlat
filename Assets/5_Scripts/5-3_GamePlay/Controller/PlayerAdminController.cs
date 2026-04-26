using UnityEngine;

/// <summary>
/// 管理员相关输入与时间控制逻辑的独立 Mono 脚本。
/// 挂在与 Player 相同的 GameObject 上，通过引用 Player 来操作玩家数据。
/// </summary>
public class PlayerAdminController : Module
{
    #region 常量与字段

    private const string AdminName = "管理员";

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

    public Ex_ModData_MemoryPackable ModSaveData;
    public override ModuleData _Data { get { return ModSaveData; } set { ModSaveData = (Ex_ModData_MemoryPackable)value; } }

    #endregion

    #region 生命周期

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

        // F1：切换管理员权限
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("F1键被按下，切换管理员身份");
            player.Data.Name_User = AdminName;
        }

        // 非管理员不执行后续逻辑
        if (!IsAdmin()) return;

        KeepAdminAlive();
        HandleAdminInput();
        HandleTimeScaleControl();
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

    private void HandleAdminInput()
    {
        // T：传送到鼠标位置
        if (Input.GetKeyDown(KeyCode.T))
        {
            playerTraits?.TeleportToMousePosition();
        }

        // F2：初始化创造模式背包
        if (Input.GetKeyDown(KeyCode.F2))
        {
            playerTraits?.InitializeCreativeInventoryForAdmin();
        }

        // F4：给予手持物品 (9999)
        if (Input.GetKeyDown(KeyCode.F4))
        {
            AddAmountToCurrentHandItem(9999f);
        }

        // F5：给予背包全部物品 (999)
        if (Input.GetKeyDown(KeyCode.F5))
        {
            AddAmountToAllBagItems(999f);
        }
    }

    private void KeepAdminAlive()
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

    private void HandleTimeScaleControl()
    {
        bool timeScaleChanged = false;

        // 加速
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            timeScaleChanged = TryUpdateTimeScale(timeScaleStep);
        }

        // 减速
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            timeScaleChanged = TryUpdateTimeScale(-timeScaleStep);
        }

        // 重置
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
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

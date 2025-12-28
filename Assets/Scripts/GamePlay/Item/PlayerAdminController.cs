using UnityEngine;

/// <summary>
/// 管理员相关输入与时间控制逻辑的独立 Mono 脚本。
/// 挂在与 Player 相同的 GameObject 上，通过引用 Player 来操作玩家数据。
/// </summary>
public class PlayerAdminController : MonoBehaviour
{
    private const string AdminName = "管理员";

    [Tooltip("要控制的玩家组件")]
    public Player player;

    [Tooltip("玩家快捷栏，用于操作手持物品")]
    public Inventory_HotBar hotbar;

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

    // 时间提示相关变量
    private string timeScaleHintText = string.Empty;
    private float timeScaleHintTimer = 0f;
    private bool showTimeScaleHint = false;
    private float initialUnityTimeScale = 1.0f;

    private void Awake()
    {
        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (hotbar == null && player != null)
        {
            hotbar = player.GetComponentInChildren<Inventory_HotBar>();
        }
    }

    private void Start()
    {
        initialUnityTimeScale = Time.timeScale;
    }

    private void Update()
    {
        if (player == null || player.Data == null)
            return;

        UpdateTimeScaleHint();

        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("F1键被按下，切换管理员");
            player.Data.Name_User = AdminName;
        }

        if (!IsAdmin())
            return;

        // T：传送到鼠标位置
        if (Input.GetKeyDown(KeyCode.T))
        {
            player.TeleportToMousePosition();
        }

        // F2：初始化创造模式背包
        if (Input.GetKeyDown(KeyCode.F2))
        {
            player.InitializeCreativeInventoryForAdmin();
        }

        // F3：当前手持物品数量 +9999
        if (Input.GetKeyDown(KeyCode.F4))
        {
            AddAmountToCurrentHandItem(9999f);
        }

        // 控制时间流逝速度
        HandleTimeScaleControl();
    }

    private void OnDestroy()
    {
        Time.timeScale = initialUnityTimeScale;
    }

    #region 管理员判断

    private bool IsAdmin()
    {
        return player != null && player.Data != null && player.Data.Name_User == AdminName;
    }

    #endregion

    #region 时间控制 & 提示

    private void HandleTimeScaleControl()
    {
        bool timeScaleChanged = false;

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            timeScaleChanged = TryUpdateTimeScale(timeScaleStep);
        }

        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            timeScaleChanged = TryUpdateTimeScale(-timeScaleStep);
        }

        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            timeScale = 1.0f;
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = "时间速度已重置为正常速度";
            Debug.Log("时间速度已重置为正常速度");
        }

        if (timeScaleChanged)
        {
            ShowTimeScaleHint();
        }
    }

    private void ShowTimeScaleHint()
    {
        showTimeScaleHint = true;
        timeScaleHintTimer = timeScaleHintDuration;
    }

    private bool TryUpdateTimeScale(float delta)
    {
        float newScale = Mathf.Clamp(timeScale + delta, minTimeScale, maxTimeScale);
        if (Mathf.Approximately(newScale, timeScale))
        {
            return false;
        }

        timeScale = newScale;
        Time.timeScale = timeScale;
        timeScaleHintText = $"时间速度: {timeScale}x";
        Debug.Log($"时间速度调整为: {timeScale}x");
        return true;
    }

    private void UpdateTimeScaleHint()
    {
        if (showTimeScaleHint)
        {
            timeScaleHintTimer -= Time.unscaledDeltaTime;
            if (timeScaleHintTimer <= 0)
            {
                showTimeScaleHint = false;
                timeScaleHintTimer = 0;
            }
        }
    }

    private void OnGUI()
    {
        if (showTimeScaleHint && !string.IsNullOrEmpty(timeScaleHintText))
        {
            float alpha = Mathf.Clamp01(timeScaleHintTimer / timeScaleHintDuration);

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(1, 1, 1, alpha);

            Rect position = new Rect(0, Screen.height * 0.25f, Screen.width, 50);

            GUI.Label(position, timeScaleHintText, style);
        }
    }

    #endregion

    #region 给予物品数量

    /// <summary>
    /// 给玩家当前手持物品增加指定数量（管理员用）
    /// </summary>
    private void AddAmountToCurrentHandItem(float amount)
    {
        if (hotbar == null)
        {
            if (player != null)
            {
                hotbar = player.GetComponentInChildren<Inventory_HotBar>();
            }

            if (hotbar == null)
            {
                Debug.LogWarning("PlayerAdminController: 未找到 Inventory_HotBar，无法为手持物品增加数量");
                return;
            }
        }

        var slot = hotbar.CurrentSelectItemSlot;
        if (slot == null || slot.itemData == null)
        {
            Debug.LogWarning("PlayerAdminController: 当前没有手持物品，无法增加数量");
            return;
        }

        slot.itemData.Stack.Amount += amount;
        hotbar.RefreshUI(hotbar.CurrentIndex);

        Debug.Log($"管理员为手持物品 {slot.itemData.IDName} 增加 {amount} 数量，当前数量：{slot.itemData.Stack.Amount}");
    }

    #endregion
}
using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// 玩家类，继承自Item并实现多种接口
/// </summary>
public class Player : Item
{
    private const string AdminName = "管理员";

    #region 字段与属性

    [Tooltip("玩家数据")]
    public Data_Player Data;

    [Tooltip("视角值")]
    public float PovValue
    {
        get => Data.PlayerPov;
        set => Data.PlayerPov = value;
    }

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
    private string timeScaleHintText = "";
    private float timeScaleHintTimer = 0f;
    private bool showTimeScaleHint = false;
    private float initialUnityTimeScale = 1.0f;

    public override ItemData itemData
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
        }
    }

    #endregion

    #region 事件系统

    #endregion

    #region 生命周期
    public override void Start()
    {
        base.Start();
        initialUnityTimeScale = Time.timeScale;
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public override void Load()
    {
        if (itemData == null)
        {
            Debug.LogWarning("Player.Load() called but itemData is null");
            return;
        }

        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        base.Load();
    }

    new void Update()
    {
        base.Update();

        UpdateTimeScaleHint();

        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("F1键被按下，切换管理员");
            Data.Name_User = AdminName;
        }

        // 只有管理员可以控制时间
        if (IsAdmin())
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                TeleportToMousePosition();
            }
            
            if (Input.GetKeyDown(KeyCode.F2))
            {
                GameRes.Instance.InventoryInitGet("创造模式", out Inventoryinit inventoryInit);
                if (inventoryInit != null)
                {
                   base.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag).inventory.TryInitializeItems(inventoryInit);
                }
            }
            
            // 控制时间流逝速度
            HandleTimeScaleControl();

        }
    }

    public new void OnDestroy()
    {
        base.OnDestroy();
        Time.timeScale = initialUnityTimeScale;
    }
    #endregion

    #region 公共方法
    [Button("克隆测试")]
    public void CloneTest()
    {
        this.itemData = this.itemData.DeepClone();
        Debug.Log("克隆成功");
    }

    /// <summary>
    /// 玩家死亡处理
    /// </summary>
    public void Death()
    {
        Application.Quit();
        Application.OpenURL("https://space.bilibili.com/353520649");
    }

    [Button]
    public void FixTimeScale()
    {
        timeScale = 1.0f;
        Time.timeScale = timeScale;
    }
    
    /// <summary>
    /// 将玩家传送到鼠标世界坐标位置
    /// </summary>
    public void TeleportToMousePosition()
    {
        if (Camera.main == null)
        {
            Debug.LogWarning("TeleportToMousePosition() failed: main camera not found");
            return;
        }

        // 获取鼠标在屏幕上的位置
        Vector3 mouseScreenPosition = Input.mousePosition;
        
        // 将屏幕坐标转换为世界坐标
        // z轴设置为0，因为这是2D游戏
        Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(
            new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, 0));
        
        // 保持z轴为0（2D游戏）
        mouseWorldPosition.z = 0;
        
        // 设置玩家位置到鼠标世界坐标
        transform.position = mouseWorldPosition;
        
        Debug.Log($"玩家已传送到位置: {mouseWorldPosition}");
    }

    #endregion

    #region 时间控制
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

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(1, 1, 1, alpha);
            style.alignment = TextAnchor.MiddleCenter;

            Rect position = new Rect(0, Screen.height * 0.25f, Screen.width, 50);

            GUI.Label(position, timeScaleHintText, style);
        }
    }
    #endregion

    #region 工具方法
    private bool IsAdmin()
    {
        return Data != null && Data.Name_User == AdminName;
    }
    #endregion
}
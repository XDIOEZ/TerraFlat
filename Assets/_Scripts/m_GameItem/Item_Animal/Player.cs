using Force.DeepCloner;
using JetBrains.Annotations;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// 玩家类，继承自Item并实现多种接口
/// </summary>
public class Player : Item
{
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
    public float timeScale = 1.0f; // 默认值改为1.0（正常速度）
    
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

    #endregion
    
    public override ItemData itemData
    {
        get => Data;
        set
        {
            Data = value as Data_Player;
        }
    }

    #region 事件系统

    #endregion

    #region 生命周期
    public override void Start()
    {
        ItemMgr.Instance.Player_DIC[Data.Name_User] = this;
        base.Start();
        
        // 确保游戏开始时时间流逝速度为正常速度
        timeScale = 1.0f;
        Time.timeScale = timeScale;
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    public override void Load()
    {
        // 检测玩家名称是否为管理员 
        if (Data.Name_User == "管理员")
        {
            // 遍历子对象获取Mod_Inventory
            Mod_Inventory[] mod_Inventory = GetComponentsInChildren<Mod_Inventory>();
            foreach (var inventory in mod_Inventory)
            {
                inventory.Data.InventoryInitName = "创造模式";
            }
        }
        
        transform.position = itemData.transform.position;
        transform.rotation = itemData.transform.rotation;
        transform.localScale = itemData.transform.scale;
        base.Load();
    }
    
    // 在Update方法中添加按键检测示例
    new void Update()
    {
        base.Update();
        
        // 更新时间提示显示
        UpdateTimeScaleHint();
        
        // 只有管理员可以控制时间
        if (Data.Name_User == "管理员")
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                TeleportToMousePosition();
            }
            
            // 控制时间流逝速度
            HandleTimeScaleControl();
        }
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
    
    /// <summary>
    /// 将玩家传送到鼠标世界坐标位置
    /// </summary>
    public void TeleportToMousePosition()
    {
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
    
    /// <summary>
    /// 处理时间流逝速度控制
    /// </summary>
    private void HandleTimeScaleControl()
    {
        bool timeScaleChanged = false;
        
        // 加速时间流逝 (按下+号)
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            timeScale = Mathf.Clamp(timeScale + timeScaleStep, minTimeScale, maxTimeScale);
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = $"时间速度: {timeScale}x";
            Debug.Log($"时间速度增加到: {timeScale}x");
        }
        
        // 减缓时间流逝 (按下-号)
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            timeScale = Mathf.Clamp(timeScale - timeScaleStep, minTimeScale, maxTimeScale);
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = $"时间速度: {timeScale}x";
            Debug.Log($"时间速度减少到: {timeScale}x");
        }
        
        // 重置时间速度 (按下0键)
        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            timeScale = 1.0f;
            Time.timeScale = timeScale;
            timeScaleChanged = true;
            timeScaleHintText = "时间速度已重置为正常速度";
            Debug.Log("时间速度已重置为正常速度");
        }
        
        // 如果时间速度有变化，显示提示
        if (timeScaleChanged)
        {
            ShowTimeScaleHint();
        }
    }
    
    /// <summary>
    /// 显示时间速度提示
    /// </summary>
    private void ShowTimeScaleHint()
    {
        showTimeScaleHint = true;
        timeScaleHintTimer = timeScaleHintDuration;
    }
    
    /// <summary>
    /// 更新时间提示显示
    /// </summary>
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
    
    /// <summary>
    /// 绘制时间提示GUI
    /// </summary>
    private void OnGUI()
    {
        if (showTimeScaleHint && !string.IsNullOrEmpty(timeScaleHintText))
        {
            // 计算透明度（逐渐消失效果）
            float alpha = Mathf.Clamp01(timeScaleHintTimer / timeScaleHintDuration);
            
            // 设置GUI样式
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = new Color(1, 1, 1, alpha); // 白色文字，带透明度
            style.alignment = TextAnchor.MiddleCenter;
            
            // 设置GUI位置（屏幕中央偏上）
            Rect position = new Rect(0, Screen.height * 0.25f, Screen.width, 50);
            
            // 绘制提示文字
            GUI.Label(position, timeScaleHintText, style);
        }
    }
    #endregion
}
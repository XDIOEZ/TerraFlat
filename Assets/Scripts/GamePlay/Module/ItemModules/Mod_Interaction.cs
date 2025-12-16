using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UltEvents;
using UnityEngine;

/// <summary>
/// 交互模块（Module）
/// - 负责处理与物品的交互逻辑
/// - 遵循 IInteract 接口
/// </summary>
public class Mod_Interaction : Module
{
    #region 属性和字段

    [Header("模块数据")]
    [Tooltip("交互模块的扩展数据")]
    public Ex_ModData modData;

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    [Header("交互状态")]
    [Tooltip("当前正在交互的物品")]
    public Item CurrentInteractItem;

    [ShowInInspector]
    public UltEvent<Item> OnAction_Start = new();
    public UltEvent<Item> OnAction_Update= new();
    public UltEvent<Item> OnAction_Stop = new();

    #endregion

    #region 生命周期方法

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.Interact;

    }

    public override void Awake()
    {
        base.Awake(); // 调用基类方法

        if (_Data != null)
            _Data.ID = ModText.Interact;

    }

    public override void Load()
    {
    }

    public override void Save()
    {
    }

    #endregion

    #region 交互方法

    /// <summary>
    /// 开始交互
    /// </summary>
    /// <param name="interacter">交互者</param>
    public void Interact_Start(IInteractor interacter = null)
    {
        // 检查物品是否可拾取 → 可拾取则禁止交互
        if (item.itemData.Stack.CanBePickedUp == true)
        {
            Debug.Log($"物品 {item.name} 可拾取，已禁止交互");
            return;
        }
        // 标记交互物品
        CurrentInteractItem = interacter.Item;
        OnAction_Start.Invoke(interacter.Item);
    }

    /// <summary>
    /// 更新交互过程
    /// </summary>
    /// <param name="interacter">交互者</param>
    public void Interact_Update(IInteractor interacter = null)
    {
    }

    /// <summary>
    /// 取消交互
    /// </summary>
    /// <param name="interacter">交互者</param>
    public void Interact_Cancel(IInteractor interacter = null)
    {
        // 清除交互物品
        CurrentInteractItem = null;

        // 触发取消事件
        OnAction_Stop?.Invoke(interacter?.Item);
    }

    #endregion

    #region 调试方法

    #endregion

    #region 枚举定义

    /// <summary>
    /// 日志级别
    /// </summary>
    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    #endregion
}

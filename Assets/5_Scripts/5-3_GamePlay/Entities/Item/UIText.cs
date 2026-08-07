using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 存放 UI 静态文本/按钮名，避免硬编码
/// </summary>
public static class UIText
{
    #region SettingPanelButtons
    // 返回主菜单相关按钮名候选
    public static readonly string[] ExitButtons = new[] { "保存并回到主界面按钮" }; // 返回主菜单按钮组

    // 保存游戏相关按钮名候选
    public static readonly string[] SaveButtons = new[] { "保存游戏"}; // 保存相关按钮组

    // 退出/关闭游戏相关按钮名候选
    public static readonly string[] CloseButtons = new[] { "保存并退出游戏按钮"}; // 退出游戏按钮组
    #endregion
}
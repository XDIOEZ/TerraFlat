/// <summary>
/// 集中声明玩家实体 UI 的稳定节点名契约；运行时绑定与 Editor Prefab 构建器共用同一名称，
/// 避免保存、返回主界面和返回桌面的行为因文案调整而脱节。
/// </summary>
public static class UIText
{
    #region 设置会话页

    public const string SaveButton = "保存";
    public const string ReturnToMainMenuButton = "返回游戏主界面";
    public const string ReturnToDesktopButton = "返回桌面";

    #endregion
}

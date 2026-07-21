// AI-Context: 双脚本 UI 新游戏视图契约；只声明输入框/按钮名称，存档创建逻辑归 GameManager。

/// <summary>
/// 双脚本 UI 的新世界创建视图，只声明控件命名契约。
/// 创建世界、数据校验与输入监听仍由 GameManager 负责。
/// </summary>
public sealed class NewGamePanelView : BasePanel
{
    public const string PanelKey = "NewGame";
    public const string StartButtonKey = "开始新游戏";
    public const string BackButtonKey = "返回上一个界面";
    public const string PlayerNameInputKey = "新增玩家名称输入框";
    public const string SaveNameInputKey = "新增存档名称输入框";
    public const string RadiusInputKey = "星球半径输入框";
    public const string NoiseInputKey = "噪声缩放输入框";

    public override void Init()
    {
        PanelName = PanelKey;
        base.Init();
    }
}

// AI-Context: 双脚本 UI 主界面视图契约；只声明控件名与收集入口，流程逻辑保持在 GameManager。

/// <summary>
/// 双脚本 UI 的主界面视图脚本。
/// 仅定义主界面的控件名称契约与 BasePanel 收集入口；
/// 按钮业务仍由 GameManager / NetworkModeUIController 负责。
/// </summary>
public sealed class MainMenuPanelView : BasePanel
{
    public const string PanelKey = "UI_Hello";
    public const string ContinueButtonKey = "选择存档";
    public const string NewGameButtonKey = "新游戏";
    public const string MultiplayerButtonKey = "联机模式";

    public override void Init()
    {
        PanelName = PanelKey;
        base.Init();
    }
}

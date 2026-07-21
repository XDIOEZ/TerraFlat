// AI-Context: 双脚本 UI 存档选择面板视图契约；存档加载/删除由外部控制器负责。

using UnityEngine.UI;

/// <summary>
/// 双脚本 UI 的存档选择视图：只声明控件契约与面板级交互。
/// 存档读取、角色选择和进入世界仍由 GameManager / SaveDataManager_UI 负责。
/// </summary>
public sealed class GameSavePanelView : BasePanel
{
    public const string PanelKey = "UI_GameSaveManager";
    public const string StartButtonKey = "开始游戏按钮";
    public const string LoadButtonKey = "加载存档按钮";
    public const string BackButtonKey = "返回按钮";
    public const string PlayerInputKey = "选择或新增玩家名称输入框";
    public const string SelectedSaveTextKey = "选中的存档名称";

    public override void Init()
    {
        PanelName = PanelKey;
        base.Init();

        Button backButton = GetButton(BackButtonKey);
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(Close);
            backButton.onClick.AddListener(Close);
        }
    }
}

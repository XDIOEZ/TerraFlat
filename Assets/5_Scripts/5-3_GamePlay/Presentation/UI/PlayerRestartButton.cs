using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置面板的保存重启按钮适配器：复用会话页危险按钮的视觉样式，
/// 运行时插入“重启”入口，并交给 GameManager 保存后重启整个游戏。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerRestartButton : MonoBehaviour
{
    #region 节点命名契约

    public const string ButtonName = "重启按钮";
    private const string SourceButtonName = UIText.ReturnToDesktopButton;

    #endregion

    #region 运行时状态

    private Button button;
    private Item playerItem;

    #endregion

    #region 初始化

    /// <summary>确保设置面板存在重启按钮，并绑定当前玩家。</summary>
    public static PlayerRestartButton Ensure(
        Transform settingsRoot,
        Item player)
    {
        if (settingsRoot == null)
        {
            return null;
        }

        Button restartButton = FindButton(settingsRoot, ButtonName);
        if (restartButton == null)
        {
            Button sourceButton = FindButton(settingsRoot, SourceButtonName);
            if (sourceButton == null)
            {
                Debug.LogError(
                    $"[PlayerRestartButton] 设置面板缺少样式源按钮“{SourceButtonName}”。",
                    settingsRoot);
                return null;
            }

            Transform parent = sourceButton.transform.parent != null
                ? sourceButton.transform.parent
                : settingsRoot;
            GameObject buttonObject = Object.Instantiate(
                sourceButton.gameObject,
                parent,
                false);
            buttonObject.name = ButtonName;
            restartButton = buttonObject.GetComponent<Button>();
        }

        if (restartButton == null)
        {
            Debug.LogError("[PlayerRestartButton] 重启按钮对象缺少 Button 组件。", settingsRoot);
            return null;
        }

        SetButtonLabel(restartButton);
        PlayerRestartButton adapter = restartButton.GetComponent<PlayerRestartButton>();
        if (adapter == null)
        {
            adapter = restartButton.gameObject.AddComponent<PlayerRestartButton>();
        }

        adapter.Bind(player);
        return adapter;
    }

    private void Bind(Item player)
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        playerItem = player;
        if (button == null)
        {
            return;
        }

        // 清除从桌面按钮克隆的事件，重启入口只执行自己的保存流程。
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(OnClicked);
        button.interactable = playerItem != null && GameManager.SupportsProcessRestart;

        if (playerItem == null)
        {
            Debug.LogWarning("[PlayerRestartButton] 未找到当前玩家，重启按钮已禁用。", this);
        }
    }

    #endregion

    #region 点击处理

    /// <summary>把按钮点击转交给保存重启流程。</summary>
    private void OnClicked()
    {
        GameManager.Instance.SaveAndRestart(playerItem);
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClicked);
        }
    }

    #endregion

    #region 辅助方法

    private static void SetButtonLabel(Button restartButton)
    {
        string text = GameManager.SupportsProcessRestart ? "重启" : "重启（不支持）";
        TMP_Text[] labels = restartButton.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0 && labels[0] != null)
        {
            TMP_Text label = labels[0];
            LocalizedTextBinder binder = label.GetComponent<LocalizedTextBinder>();
            if (binder == null)
            {
                binder = label.gameObject.AddComponent<LocalizedTextBinder>();
            }

            // 重启按钮由“返回桌面”按钮克隆而来，必须同步改写继承的本地化绑定，
            // 否则刷新语言或 UI 组件时会再次被旧 key 覆盖成“返回桌面”。
            binder.Configure(
                FlatWorldLocalizationService.UiTable,
                FlatWorldLocalizationService.GetUiTextKey(text),
                text);
        }
    }

    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == buttonName)
            {
                return buttons[index];
            }
        }

        return null;
    }

    #endregion
}

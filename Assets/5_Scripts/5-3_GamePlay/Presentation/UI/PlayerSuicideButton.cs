using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置面板的玩家自杀按钮适配器：复用会话页危险按钮的视觉样式，
/// 运行时插入“自杀”入口，并把点击事件交给 Mod_PlayerDeathState 统一处理。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSuicideButton : MonoBehaviour
{
    #region 节点命名契约

    public const string ButtonName = "自杀按钮";
    private const string SourceButtonName = UIText.ReturnToDesktopButton;

    #endregion

    #region 运行时状态

    private Button button;
    private Mod_PlayerDeathState playerDeathState;

    #endregion

    #region 初始化

    /// <summary>确保设置面板存在自杀按钮，并绑定当前玩家死亡模块。</summary>
    public static PlayerSuicideButton Ensure(
        Transform settingsRoot,
        Mod_PlayerDeathState deathState)
    {
        if (settingsRoot == null)
        {
            return null;
        }

        Button suicideButton = FindButton(settingsRoot, ButtonName);
        if (suicideButton == null)
        {
            Button sourceButton = FindButton(settingsRoot, SourceButtonName);
            if (sourceButton == null)
            {
                Debug.LogError(
                    $"[PlayerSuicideButton] 设置面板缺少样式源按钮“{SourceButtonName}”。",
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
            suicideButton = buttonObject.GetComponent<Button>();
        }

        if (suicideButton == null)
        {
            Debug.LogError("[PlayerSuicideButton] 自杀按钮对象缺少 Button 组件。", settingsRoot);
            return null;
        }

        SetButtonLabel(suicideButton);
        PlayerSuicideButton adapter = suicideButton.GetComponent<PlayerSuicideButton>();
        if (adapter == null)
        {
            adapter = suicideButton.gameObject.AddComponent<PlayerSuicideButton>();
        }

        adapter.Bind(deathState);
        return adapter;
    }

    private void Bind(Mod_PlayerDeathState deathState)
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        playerDeathState = deathState;
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(OnClicked);
        button.onClick.AddListener(OnClicked);
        button.interactable = playerDeathState != null;

        if (playerDeathState == null)
        {
            Debug.LogWarning("[PlayerSuicideButton] 未找到玩家死亡模块，自杀按钮已禁用。", this);
        }
    }

    #endregion

    #region 点击处理

    /// <summary>把按钮点击转交给玩家死亡状态模块。</summary>
    private void OnClicked()
    {
        playerDeathState?.ForceSuicide();
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

    private static void SetButtonLabel(Button suicideButton)
    {
        TMP_Text[] labels = suicideButton.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0 && labels[0] != null)
        {
            labels[0].text = FlatWorldLocalizationService.GetUiText("自杀");
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

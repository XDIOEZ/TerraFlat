// AI-Context: 存档列表项的纯视图绑定；只显示数据和转发选择，不读写存档文件。

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 存档与角色动态条目的视觉状态；数据选中和导航焦点可同时保留。
/// </summary>
public sealed class GameSaveItemView : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    #region 视觉参数

    private static readonly Color NormalColor = new Color(0.045f, 0.075f, 0.095f, 1f);
    private static readonly Color SelectedColor = new Color(0.075f, 0.235f, 0.225f, 1f);
    private static readonly Color FocusedColor = new Color(0.105f, 0.335f, 0.305f, 1f);
    private static readonly Color NormalTextColor = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color SelectedTextColor = new Color(0.62f, 0.92f, 0.83f, 1f);
    private static readonly Color FocusedTextColor = new Color(1f, 0.96f, 0.84f, 1f);
    private static readonly Color SelectedAccentColor = new Color(0.26f, 0.61f, 0.57f, 1f);
    private static readonly Color FocusedAccentColor = new Color(0.95f, 0.64f, 0.32f, 1f);

    #endregion

    #region 引用与状态

    public Image Background;
    public Image SelectionAccent;
    public TextMeshProUGUI Label;

    private bool dataSelected;
    private bool navigationFocused;

    #endregion

    #region 生命周期

    private void Awake()
    {
        if (Background == null)
            Background = GetComponent<Image>();
        if (Label == null)
            Label = GetComponentInChildren<TextMeshProUGUI>(true);

        RefreshVisual();
    }

    private void OnEnable()
    {
        navigationFocused = EventSystem.current != null &&
                            EventSystem.current.currentSelectedGameObject == gameObject;
        RefreshVisual();
    }

    private void OnDisable()
    {
        navigationFocused = false;
    }

    #endregion

    #region 选择状态

    /// <summary>
    /// 设置业务层已确认的存档/角色选择，不会因手柄焦点离开而丢失。
    /// </summary>
    public void SetSelected(bool selected)
    {
        dataSelected = selected;
        RefreshVisual();
    }

    /// <summary>模式切换时同时清除业务选中和导航焦点视觉。</summary>
    public void ClearSelectionVisual()
    {
        dataSelected = false;
        navigationFocused = false;
        RefreshVisual();
    }

    /// <summary>
    /// 仅键盘/手柄导航焦点显示高对比效果。
    /// 鼠标按下也会让 Button 成为 EventSystem 当前对象，但拖动 ScrollRect 时不能伪装成业务选中。
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        navigationFocused = !(eventData is PointerEventData);
        RefreshVisual();
    }

    /// <summary>焦点离开时仅移除焦点态，保留已确认的业务选择。</summary>
    public void OnDeselect(BaseEventData eventData)
    {
        navigationFocused = false;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        bool focused = navigationFocused;
        bool highlighted = dataSelected || focused;
        if (Background != null)
            Background.color = focused ? FocusedColor : dataSelected ? SelectedColor : NormalColor;
        if (SelectionAccent != null)
        {
            SelectionAccent.enabled = highlighted;
            SelectionAccent.color = focused ? FocusedAccentColor : SelectedAccentColor;
        }
        if (Label != null)
            Label.color = focused ? FocusedTextColor : dataSelected ? SelectedTextColor : NormalTextColor;
    }

    #endregion
}

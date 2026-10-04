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
            Background.color = focused
                ? FlatWorldUITheme.SaveItemFocused
                : dataSelected ? FlatWorldUITheme.SaveItemSelected : FlatWorldUITheme.SaveItemNormal;
        if (SelectionAccent != null)
        {
            SelectionAccent.enabled = highlighted;
            SelectionAccent.color = focused
                ? FlatWorldUITheme.SaveItemAccentFocused
                : FlatWorldUITheme.SaveItemAccentSelected;
        }
        if (Label != null)
            Label.color = focused
                ? FlatWorldUITheme.SaveItemTextFocused
                : dataSelected ? FlatWorldUITheme.SaveItemTextSelected : FlatWorldUITheme.SaveItemTextNormal;
    }

    #endregion
}

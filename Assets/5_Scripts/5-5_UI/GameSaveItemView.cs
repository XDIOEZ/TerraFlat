// AI-Context: 存档列表项的纯视图绑定；只显示数据和转发选择，不读写存档文件。

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档与角色动态条目的视觉状态。
/// </summary>
public sealed class GameSaveItemView : MonoBehaviour
{
    private static readonly Color NormalColor = new Color(0.045f, 0.075f, 0.095f, 1f);
    private static readonly Color SelectedColor = new Color(0.075f, 0.235f, 0.225f, 1f);
    private static readonly Color NormalTextColor = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color SelectedTextColor = new Color(0.62f, 0.92f, 0.83f, 1f);

    public Image Background;
    public Image SelectionAccent;
    public TextMeshProUGUI Label;

    private void Awake()
    {
        if (Background == null)
            Background = GetComponent<Image>();
        if (Label == null)
            Label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    public void SetSelected(bool selected)
    {
        if (Background != null)
            Background.color = selected ? SelectedColor : NormalColor;
        if (SelectionAccent != null)
            SelectionAccent.enabled = selected;
        if (Label != null)
            Label.color = selected ? SelectedTextColor : NormalTextColor;
    }
}

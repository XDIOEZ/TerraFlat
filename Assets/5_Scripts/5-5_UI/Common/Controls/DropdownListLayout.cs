using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>展开前统一下拉模板的行高和可见行数，条目生成与滚动仍由原生下拉控件负责。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ScrollRect))]
public sealed class DropdownListLayout : MonoBehaviour
{
    #region 下拉模板布局

    public const int MaxVisibleItems = 5;

    public static void EnsureFor(RectTransform template)
    {
        if (template != null && template.GetComponent<DropdownListLayout>() == null)
            template.gameObject.AddComponent<DropdownListLayout>();
    }

    private void OnEnable() => PrepareForOpen();

    public void PrepareForOpen()
    {
        // 原生 Show 在克隆前激活源模板，此时使用最新选项数量准备几何，副本直接继承。
        TMP_Dropdown tmpDropdown = GetComponentInParent<TMP_Dropdown>(true);
        if (tmpDropdown != null && tmpDropdown.template == transform)
        {
            PrepareTemplate(tmpDropdown.options.Count);
            return;
        }

        Dropdown dropdown = GetComponentInParent<Dropdown>(true);
        if (dropdown != null && dropdown.template == transform)
            PrepareTemplate(dropdown.options.Count);
    }

    private void PrepareTemplate(int optionCount)
    {
        ScrollRect scroll = GetComponent<ScrollRect>();
        RectTransform content = scroll.content;
        if (content == null)
            return;

        Toggle item = content.GetComponentInChildren<Toggle>(true);
        if (item == null || !(item.transform is RectTransform itemRect))
            return;

        float rowHeight = LayoutUtility.GetPreferredHeight(itemRect);
        if (rowHeight <= 0f)
            rowHeight = itemRect.rect.height;
        if (rowHeight <= 0f)
            return;

        // 原生 Dropdown 自己排布条目和 Content，关闭第二个布局写入方，避免隐藏模板占位或裁掉末行。
        if (content.TryGetComponent(out LayoutGroup layout))
            layout.enabled = false;
        if (content.TryGetComponent(out ContentSizeFitter fitter))
            fitter.enabled = false;

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, rowHeight);
        content.anchoredPosition = Vector2.zero;

        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = Vector2.one;
        itemRect.pivot = new Vector2(0.5f, itemRect.pivot.y);
        itemRect.sizeDelta = new Vector2(0f, rowHeight);
        itemRect.anchoredPosition = new Vector2(0f, -rowHeight * (1f - itemRect.pivot.y));

        RectTransform viewport = scroll.viewport;
        if (viewport != null && viewport != transform)
        {
            viewport.anchorMin = new Vector2(viewport.anchorMin.x, 0f);
            viewport.anchorMax = new Vector2(viewport.anchorMax.x, 1f);
            viewport.offsetMin = new Vector2(viewport.offsetMin.x, 0f);
            viewport.offsetMax = new Vector2(viewport.offsetMax.x, 0f);
        }

        ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Clamp(optionCount, 0, MaxVisibleItems) * rowHeight);
    }

    #endregion
}

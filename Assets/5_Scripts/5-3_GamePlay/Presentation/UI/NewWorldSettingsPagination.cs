using UnityEngine;
using UnityEngine.UI;

/// <summary>世界参数按基础与高级分页，切页只改显示并保留填写值。</summary>
public sealed class NewWorldSettingsPagination : MonoBehaviour
{
    #region 页面与按钮
    [SerializeField] private GameObject basicPage;
    [SerializeField] private GameObject advancedPage;
    [SerializeField] private Button basicButton;
    [SerializeField] private Button advancedButton;

    public void Configure(GameObject basic, GameObject advanced, Button basicTab, Button advancedTab)
    {
        basicPage = basic;
        advancedPage = advanced;
        basicButton = basicTab;
        advancedButton = advancedTab;
        ShowBasic();
    }

    private void OnEnable()
    {
        basicButton?.onClick.AddListener(ShowBasic);
        advancedButton?.onClick.AddListener(ShowAdvanced);
    }

    private void OnDisable()
    {
        basicButton?.onClick.RemoveListener(ShowBasic);
        advancedButton?.onClick.RemoveListener(ShowAdvanced);
    }
    #endregion

    #region 分页显示
    public void ShowBasic() => Show(false);
    public void ShowAdvanced() => Show(true);

    private void Show(bool advanced)
    {
        basicPage?.SetActive(!advanced);
        advancedPage?.SetActive(advanced);
        basicButton?.GetComponent<ReusableUITabVisual>()?.SetSelected(!advanced);
        advancedButton?.GetComponent<ReusableUITabVisual>()?.SetSelected(advanced);
        GetComponentInParent<BasePanel>()?.RefreshGamepadNavigationState();
        if (transform is RectTransform rect)
            LayoutRebuilder.MarkLayoutForRebuild(rect);
    }
    #endregion
}

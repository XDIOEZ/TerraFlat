using UnityEngine;

public class UI_FollowMouse : MonoBehaviour
{
    #region 层级与交互契约

    // 手持物必须高于快捷栏的模态层（1000），但仍低于加载/调试等全局覆盖层。
    private const int HeldItemSortingOrder = 1001;

    [SerializeField, Min(0)]
    [Tooltip("跟随指针的手持物显示层，必须高于快捷栏模态层。")]
    private int sortingOrder = HeldItemSortingOrder;

    private Canvas canvas;
    private CanvasGroup canvasGroup;

    #endregion

    [Tooltip("是否启用跟随鼠标功能")]
    public bool followMouse = true;

    [Tooltip("鼠标位置与 UI 元素的偏移量")]
    public Vector3 offset = Vector3.zero;

    public RectTransform rectTransform;

    private void Awake()
    {
        EnsurePresentationLayer();
    }

    private void OnEnable()
    {
        EnsurePresentationLayer();
    }

    private void Start()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponentInChildren<RectTransform>();
        }
    }

    private void Update()
    {
        if (followMouse && rectTransform != null)
        {
            FollowMousePosition();
        }

        // BasePanel.Open 会恢复 CanvasGroup 的交互状态；手持物只是跟随指针的视觉层，不能挡住目标槽位射线。
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void FollowMousePosition()
    {
        // 获取鼠标在屏幕中的位置
        Vector3 mousePosition = Input.mousePosition;

        // 将鼠标位置转换为世界坐标，并加上偏移
        rectTransform.position = mousePosition + offset;
    }

    public void EnableFollowMouse(bool enable)
    {
        followMouse = enable;
    }

    /// <summary>统一手持物 Canvas 排序，并缓存其非交互显示属性。</summary>
    private void EnsurePresentationLayer()
    {
        canvas ??= GetComponent<Canvas>();
        canvasGroup ??= GetComponent<CanvasGroup>();

        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Max(HeldItemSortingOrder, sortingOrder);
        }

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}

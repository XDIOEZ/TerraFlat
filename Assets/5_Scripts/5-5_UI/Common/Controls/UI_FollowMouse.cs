using UnityEngine;
using UnityEngine.InputSystem;

public class UI_FollowMouse : MonoBehaviour
{
    #region 层级与交互契约

    [SerializeField, Min(0)]
    [Tooltip("跟随指针的手持物显示层，必须高于所有游戏 UI。")]
    private int sortingOrder = UIManager.HeldItemSortingOrder;

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
        Pointer pointer = Pointer.current;
        if (pointer == null)
            return;

        // 手持物视觉跟随 Input System 当前指针，同时支持鼠标与触屏。
        Vector2 pointerPosition = pointer.position.ReadValue();
        rectTransform.position = new Vector3(pointerPosition.x, pointerPosition.y, 0f) + offset;
    }

    public void EnableFollowMouse(bool enable)
    {
        followMouse = enable;
    }

    /// <summary>统一手持物 Canvas 到全局最顶层，并缓存其非交互显示属性。</summary>
    private void EnsurePresentationLayer()
    {
        canvas ??= GetComponent<Canvas>();
        canvasGroup ??= GetComponent<CanvasGroup>();

        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Max(UIManager.HeldItemSortingOrder, sortingOrder);
        }

        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
}

// AI-Context: FlatWorld 通用 UI 微交互；仅负责按钮悬停/按压的轻量缩放反馈，不查找业务节点、不绑定点击事件。

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 为普通操作按钮提供克制的悬停与按压反馈。
/// 槽位按钮和可拖拽内容不应挂载本组件，避免干扰背包交互。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class FlatWorldUIFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    [SerializeField, Range(1f, 1.08f)] private float hoverScale = 1.018f;
    [SerializeField, Range(0.92f, 1f)] private float pressedScale = 0.975f;
    [SerializeField, Range(4f, 30f)] private float response = 18f;

    private Selectable selectable;
    private bool hovered;
    private bool pressed;
    private bool selected;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        transform.localScale = Vector3.one;
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        selected = false;
        transform.localScale = Vector3.one;
    }

    private void Update()
    {
        bool canInteract = selectable == null || selectable.IsInteractable();
        float target = !canInteract ? 1f : pressed ? pressedScale : (hovered || selected) ? hoverScale : 1f;
        float t = 1f - Mathf.Exp(-response * Time.unscaledDeltaTime);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, t);
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;
    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
    }

    public void OnPointerDown(PointerEventData eventData) => pressed = true;
    public void OnPointerUp(PointerEventData eventData) => pressed = false;
    public void OnSelect(BaseEventData eventData) => selected = true;
    public void OnDeselect(BaseEventData eventData) => selected = false;
}

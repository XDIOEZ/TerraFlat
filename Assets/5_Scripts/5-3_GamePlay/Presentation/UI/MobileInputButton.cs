using FlatWorld.Mobile;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>把正式 HUD 的按住/抬起转换为手机虚拟设备按钮，并用 pointerId 防止多指串键。</summary>
[DisallowMultipleComponent]
public sealed class MobileInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private MobileVirtualButton button;
    private int pointerId = int.MinValue;

    public void Configure(MobileVirtualButton virtualButton)
    {
        button = virtualButton;
        Release();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (pointerId != int.MinValue || eventData == null)
            return;

        pointerId = eventData.pointerId;
        MobileInputRuntime.SetButton(button, true);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null || pointerId != eventData.pointerId)
            return;

        Release();
        eventData.Use();
    }

    private void OnDisable()
    {
        Release();
    }

    public void Release()
    {
        bool wasOwned = pointerId != int.MinValue;
        pointerId = int.MinValue;
        if (wasOwned)
            MobileInputRuntime.SetButton(button, false);
    }
}

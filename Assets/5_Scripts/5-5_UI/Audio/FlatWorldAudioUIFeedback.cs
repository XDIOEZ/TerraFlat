// AI-Context: 通用 UI 声音入口；通过稳定 AudioCue ID 播放，不让按钮依赖具体 AudioClip。

using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class FlatWorldAudioUIFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerClickHandler,
    ISubmitHandler
{
    [SerializeField] private string clickCueId = AudioEventIds.UiClick;
    [SerializeField] private string hoverCueId = AudioEventIds.UiHover;
    [SerializeField] private bool playHover;

    private Selectable selectable;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (playHover && CanPlay())
            AudioService.Instance.Play(hoverCueId);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && CanPlay())
            AudioService.Instance.Play(clickCueId);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (CanPlay())
            AudioService.Instance.Play(clickCueId);
    }

    private bool CanPlay()
    {
        if (selectable == null)
            selectable = GetComponent<Selectable>();

        return selectable == null || selectable.IsInteractable();
    }

    public static void EnsureFor(Transform root)
    {
        if (root == null || !Application.isPlaying)
            return;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        EnsureFor(buttons);
    }

    /// <summary>复用面板按钮快照补齐音频反馈，避免重复扫描层级。</summary>
    public static void EnsureFor(IReadOnlyList<Button> buttons)
    {
        if (buttons == null || !Application.isPlaying)
            return;

        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button != null && button.GetComponent<FlatWorldAudioUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldAudioUIFeedback>();
        }
    }
}

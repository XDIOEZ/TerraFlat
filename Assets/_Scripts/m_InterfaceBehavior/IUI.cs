using UnityEngine;

public interface IUI
{
    Canvas Canvas { get; set; }
    void ShowUI()
    {
        Canvas.enabled = true;
    }

    void HideUI()
    {
        Canvas.enabled = false;
    }

    void SwitchUI()
    {
        Canvas.enabled = !Canvas.enabled;
    }
}
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonInfoData : MonoBehaviour, IPointerClickHandler
{
    public string Name;
    public string Path;

    public Image SelectImage;

    public void Start()
    {
        GetComponentInChildren<TextMeshProUGUI>().text = Name;
    }
    // 当物体被点击时调用
    public void OnPointerClick(PointerEventData eventData)
    {
        // 右键点击
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnContextMenu(eventData.position);
        }
    }

    // 右键菜单调用
    public void OnContextMenu(Vector2 point)
    {
        SaveMenuRightMenuUI.Instance.OpenUI(point);
        SaveMenuRightMenuUI.Instance.SelectInfo = this;
    }
}

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
    // �����屻���ʱ����
    public void OnPointerClick(PointerEventData eventData)
    {
        // �Ҽ����
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnContextMenu(eventData.position);
        }
    }

    // �Ҽ��˵�����
    public void OnContextMenu(Vector2 point)
    {
        SaveMenuRightMenuUI.Instance.OpenUI(point);
        SaveMenuRightMenuUI.Instance.SelectInfo = this;
    }
}

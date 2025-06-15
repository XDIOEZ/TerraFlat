using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class SaveInfo : MonoBehaviour, IPointerClickHandler
{
    public string saveName;
    public string savePath;

    public void Start()
    {
        GetComponentInChildren<TextMeshProUGUI>().text = saveName;
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
        SaveMenuRightMenuUI.Instance.UsingSaveInfo = this;
    }
}

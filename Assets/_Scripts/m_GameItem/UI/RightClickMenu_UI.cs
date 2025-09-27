using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RightClickMenu_UI : MonoBehaviour
{
    public ItemSlot itemSlot;
    public ItemSlot_UI itemSlotUI;
    public BasePanel basePanel;
    Item SlotOwner;

    public void Init(ItemSlot_UI _itemSlotUI,Item _SlotOwner)
    {
        itemSlotUI = _itemSlotUI;
        itemSlot = _itemSlotUI.Data;
        basePanel = GetComponent<BasePanel>();
        basePanel.CollectUIComponents();
        SlotOwner = _SlotOwner;

        basePanel.GetButton("�������").onClick.AddListener(DestroyPanel);
        basePanel.GetButton("ʹ����Ʒ").onClick.AddListener(UseItem);
        basePanel.GetButton("�鿴��Ʒ��Ϣ").onClick.AddListener(ShowItemInfo);
    }

    public void UseItem()
    {
        Item item = GameRes.Instance.InstantiatePrefab(itemSlot.itemData.IDName).GetComponent<Item>();
        item.itemData = itemSlot.itemData;
        item.Load();
        item.Owner = SlotOwner;
        item.Act();
        item.DestroySelf();
    }
        public void DestroyPanel() 
        {
            Destroy(basePanel.gameObject);
        } 

public void ShowItemInfo()
{
    GameObject itemInfoPanel = GameRes.Instance.InstantiatePrefab("��Ʒ��Ϣ���");

    BasePanel itemInfoPanelBasePanel = itemInfoPanel.GetComponent<BasePanel>();
    itemInfoPanelBasePanel.CollectUIComponents();
    itemInfoPanelBasePanel.GetText("��Ϣ").text = itemSlot.itemData.ToString();

    foreach (var moduleData in itemSlot.itemData.ModuleDataDic.Values)
    {
        itemInfoPanelBasePanel.GetText("��Ϣ").text += "\n" + moduleData.ToString();
    }
    itemInfoPanel.transform.SetParent(basePanel.transform);
        
        // ����Ϣ����ƶ�����Ļ�м�
        itemInfoPanelBasePanel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        itemInfoPanelBasePanel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        itemInfoPanelBasePanel.rectTransform.anchoredPosition = Vector2.zero;
}

}


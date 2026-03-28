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

    public void Init(ItemSlot_UI _itemSlotUI, ItemSlot _itemSlot, Item _SlotOwner)
    {
        itemSlotUI = _itemSlotUI;
        itemSlot = _itemSlot;
        basePanel = GetComponent<BasePanel>();
        basePanel.CollectUIComponents();
        SlotOwner = _SlotOwner;

        basePanel.GetButton("销毁面板").onClick.AddListener(DestroyPanel);
        basePanel.GetButton("使用物品").onClick.AddListener(UseItem);
        basePanel.GetButton("查看物品信息").onClick.AddListener(ShowItemInfo);
    }

    public void UseItem()
    {
        if (itemSlot == null || itemSlot.itemData == null)
        {
            Debug.LogError("[RightClickMenu_UI.UseItem] itemSlot 或 itemData 为空");
            return;
        }

        Item item = ItemMgr.Instance.InstantiateItem(itemSlot.itemData);
        item.Load();
        item.Owner = SlotOwner;
        item.Act();
        ItemMgr.Instance.DespawnItem(item);
    }
        public void DestroyPanel() 
        {
            Destroy(basePanel.gameObject);
        } 

public void ShowItemInfo()
{
    if (itemSlot == null || itemSlot.itemData == null)
    {
        Debug.LogError("[RightClickMenu_UI.ShowItemInfo] itemSlot 或 itemData 为空");
        return;
    }

    GameObject itemInfoPanel = GameRes.Instance.InstantiatePrefab("物品信息面板");

    BasePanel itemInfoPanelBasePanel = itemInfoPanel.GetComponent<BasePanel>();
    itemInfoPanelBasePanel.CollectUIComponents();
    itemInfoPanelBasePanel.GetText("信息").text = itemSlot.itemData.ToString();

    foreach (var moduleData in itemSlot.itemData.ModuleDataDic.Values)
    {
        itemInfoPanelBasePanel.GetText("信息").text += "\n" + moduleData.ToString();
    }
    itemInfoPanel.transform.SetParent(basePanel.transform);
        
        // 将信息面板移动到屏幕中间
        itemInfoPanelBasePanel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        itemInfoPanelBasePanel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        itemInfoPanelBasePanel.rectTransform.anchoredPosition = Vector2.zero;
}

}


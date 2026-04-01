using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using TMPro;
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
        if (basePanel == null)
        {
            Debug.LogError("[RightClickMenu_UI.Init] 缺少 BasePanel 组件");
            return;
        }

        basePanel.CollectUIComponents();
        SlotOwner = _SlotOwner;

        Button destroyButton = basePanel.GetButton("销毁面板");
        if (destroyButton != null)
            destroyButton.onClick.AddListener(DestroyPanel);

        Button useButton = basePanel.GetButton("使用物品");
        if (useButton != null)
            useButton.onClick.AddListener(UseItem);

        Button showInfoButton = basePanel.GetButton("查看物品信息");
        if (showInfoButton != null)
            showInfoButton.onClick.AddListener(ShowItemInfo);
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
    if (itemInfoPanelBasePanel == null)
    {
        Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 物品信息面板缺少 BasePanel 组件");
        return;
    }

    itemInfoPanelBasePanel.CollectUIComponents();
    TextMeshProUGUI infoText = itemInfoPanelBasePanel.GetText("信息");
    if (infoText == null)
    {
        Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 未找到文本组件: 信息");
        return;
    }

    infoText.text = itemSlot.itemData.ToString();

    foreach (var moduleData in itemSlot.itemData.ModuleDataDic.Values)
    {
        infoText.text += "\n" + moduleData.ToString();
    }
    itemInfoPanel.transform.SetParent(basePanel.transform);
    itemInfoPanel.transform.localScale = Vector3.one;

    RectTransform infoRect = itemInfoPanelBasePanel.rectTransform;
    if (infoRect == null)
    {
        infoRect = itemInfoPanelBasePanel.GetComponent<RectTransform>();
        itemInfoPanelBasePanel.rectTransform = infoRect;
    }

    if (infoRect == null)
    {
        Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 物品信息面板缺少 RectTransform");
        return;
    }
        
        // 将信息面板移动到屏幕中间
        infoRect.anchorMin = new Vector2(0.5f, 0.5f);
        infoRect.anchorMax = new Vector2(0.5f, 0.5f);
        infoRect.anchoredPosition = Vector2.zero;
}

}


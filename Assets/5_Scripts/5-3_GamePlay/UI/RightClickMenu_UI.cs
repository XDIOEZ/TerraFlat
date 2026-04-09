using NaughtyAttributes;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右键菜单交互面板：负责使用物品、查看详情和关闭菜单。
/// </summary>
public class RightClickMenu_UI : MonoBehaviour
{
    public ItemSlot itemSlot; // 当前右键选中的数据槽位
    public ItemSlot_UI itemSlotUI; // 当前右键选中的UI槽位
    public BasePanel basePanel; // 右键菜单面板
    Item SlotOwner; // 槽位所属物品（通常为容器或玩家）

    /// <summary>
    /// 初始化右键菜单并绑定按钮事件。
    /// </summary>
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

    /// <summary>
    /// 临时实例化并执行物品 Act 行为，然后立即回收实例。
    /// </summary>
    public void UseItem()
    {
        if (itemSlot == null || itemSlot.itemData == null)
        {
            Debug.LogError("[RightClickMenu_UI.UseItem] itemSlot 或 itemData 为空");
            return;
        }

        // 右键“使用”只触发行为，不直接改动场景中的持久实例。
        Item item = ItemMgr.Instance.InstantiateItem(itemSlot.itemData);
        item.Load();
        item.Owner = SlotOwner;
        item.Act();
        ItemMgr.Instance.DespawnItem(item);
    }

    /// <summary>
    /// 关闭并销毁右键菜单面板。
    /// </summary>
    public void DestroyPanel()
    {
        Destroy(basePanel.gameObject);
    }

    /// <summary>
    /// 打开“物品信息面板”，展示基础信息、模块信息和腐败信息。
    /// </summary>
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

        infoText.text = BuildItemInfoText(itemSlot.itemData);
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

    /// <summary>
    /// 构建“详细介绍”文本：基础信息 + 模块信息 + 食物腐败信息。
    /// </summary>
    private string BuildItemInfoText(ItemData itemData)
    {
        var sb = new StringBuilder();
        sb.Append(itemData.ToString());

        foreach (var moduleData in itemData.ModuleDataDic.Values)
        {
            sb.Append("\n").Append(moduleData.ToString());
        }

        string spoilageText = BuildFoodSpoilageText(itemData);
        if (!string.IsNullOrEmpty(spoilageText))
        {
            sb.Append(spoilageText);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 读取当前食物腐败状态并格式化为可读文本。
    /// </summary>
    private string BuildFoodSpoilageText(ItemData itemData)
    {
        if (itemData == null || itemData.Tags == null || !itemData.Tags.ContainsTag("Food"))
        {
            return string.Empty;
        }

        if (!TryGetFoodModuleData(itemData, out Ex_ModData_MemoryPackable foodModuleData))
        {
            return "\n\n[食物腐败]\n未找到食物模块数据";
        }

        Food foodData = null;
        foodModuleData.ReadData(ref foodData);
        if (foodData == null)
        {
            return "\n\n[食物腐败]\n食物模块数据为空";
        }

        float intervalSeconds = Mathf.Max(1f, foodData.SpoilageIntervalSeconds);
        float elapsedSeconds = Mathf.Max(0f, foodData.SpoilageElapsedSeconds);
        float progress01 = Mathf.Clamp01(elapsedSeconds / intervalSeconds);
        float remainSeconds = Mathf.Max(0f, intervalSeconds - elapsedSeconds);
        string enableText = foodData.EnableSpoilage ? "启用" : "关闭";
        string targetItemID = string.IsNullOrWhiteSpace(foodData.SpoilageTargetItemID) ? "未配置" : foodData.SpoilageTargetItemID;

        return $"\n\n[食物腐败]" +
               $"\n状态：{enableText}" +
               $"\n进度：{progress01 * 100f:F1}%" +
               $"\n累计：{elapsedSeconds:F1}s / {intervalSeconds:F1}s" +
               $"\n剩余：{remainSeconds:F1}s" +
               $"\n腐败目标：{targetItemID}";
    }

    /// <summary>
    /// 从物品模块字典中提取 Food 的序列化模块数据。
    /// </summary>
    private bool TryGetFoodModuleData(ItemData itemData, out Ex_ModData_MemoryPackable foodModuleData)
    {
        foodModuleData = null;
        if (itemData == null || itemData.ModuleDataDic == null)
        {
            return false;
        }

        foreach (var moduleData in itemData.ModuleDataDic.Values)
        {
            if (moduleData == null || moduleData.ID != ModText.Food)
            {
                continue;
            }

            foodModuleData = moduleData as Ex_ModData_MemoryPackable;
            return foodModuleData != null;
        }

        return false;
    }

}


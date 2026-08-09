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
    private GameController gameController;

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
        ResolveInputController();

        Button destroyButton = basePanel.GetButton("销毁面板");
        if (destroyButton != null)
            destroyButton.onClick.AddListener(DestroyPanel);

        Button useButton = basePanel.GetButton("使用物品");
        if (useButton != null)
            useButton.onClick.AddListener(UseItem);

        Button showInfoButton = basePanel.GetButton("查看物品信息");
        if (showInfoButton != null)
            showInfoButton.onClick.AddListener(ShowItemInfo);

        basePanel.PrepareForGamepadNavigation("使用物品", true, true);
        basePanel.Opened += AcquireGameplayInputLock;
        basePanel.Closed += ReleaseGameplayInputLock;
        if (basePanel.IsOpen())
            AcquireGameplayInputLock();
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
        if (basePanel != null)
            basePanel.Destroy();
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

        if (GameRes.Instance == null)
        {
            Debug.LogError("[RightClickMenu_UI.ShowItemInfo] GameRes.Instance 为空");
            return;
        }

        GameObject itemInfoPanel = GameRes.Instance.InstantiatePrefab("物品信息面板");
        if (itemInfoPanel == null)
        {
            Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 物品信息面板预制体实例化失败");
            return;
        }

        BasePanel itemInfoPanelBasePanel = itemInfoPanel.GetComponent<BasePanel>();
        if (itemInfoPanelBasePanel == null)
        {
            Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 物品信息面板缺少 BasePanel 组件");
            return;
        }

        itemInfoPanelBasePanel.Init();
        TextMeshProUGUI infoText = itemInfoPanelBasePanel.GetText("信息");
        if (infoText == null)
        {
            Debug.LogError("[RightClickMenu_UI.ShowItemInfo] 未找到文本组件: 信息");
            return;
        }

        infoText.text = BuildItemInfoText(itemSlot.itemData);
        itemInfoPanel.transform.SetParent(basePanel.transform);
        itemInfoPanel.transform.localScale = Vector3.one;
        itemInfoPanelBasePanel.PrepareForGamepadNavigation("销毁", true, true);
        itemInfoPanelBasePanel.Open();

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

    #region 输入锁与引用

    private void ResolveInputController()
    {
        gameController = SlotOwner?.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        gameController ??= SlotOwner?.GetComponent<GameController>();
    }

    private void AcquireGameplayInputLock()
    {
        gameController?.AcquireGameplayInputLock(this);
    }

    private void ReleaseGameplayInputLock()
    {
        gameController?.ReleaseGameplayInputLock(this);
    }

    private void OnDestroy()
    {
        if (basePanel != null)
        {
            basePanel.Opened -= AcquireGameplayInputLock;
            basePanel.Closed -= ReleaseGameplayInputLock;
        }

        ReleaseGameplayInputLock();
    }

    #endregion

    /// <summary>
    /// 构建“详细介绍”文本：基础信息 + 模块信息 + 食物腐败信息。
    /// </summary>
    private string BuildItemInfoText(ItemData itemData)
    {
        var sb = new StringBuilder();
        sb.Append(itemData.ToString());

        if (itemData.ModuleDataDic == null)
        {
            Debug.LogError($"[RightClickMenu_UI.BuildItemInfoText] 模块字典为空，物品={itemData.IDName}");
        }
        else
        {
            foreach (var moduleData in itemData.ModuleDataDic.Values)
            {
                if (moduleData == null)
                {
                    continue;
                }

                sb.Append("\n").Append(moduleData.ToString());
            }
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

        if (!TryGetFoodModuleData(itemData, out ModData_FoodData foodModuleData))
        {
            return "\n\n[食物腐败]\n未找到食物模块数据";
        }

        foodModuleData.ApplyToFoodData();

        float intervalSeconds = Mathf.Max(1f, foodModuleData.SpoilageIntervalSeconds);
        float elapsedSeconds = Mathf.Max(0f, foodModuleData.SpoilageElapsedSeconds);
        float progress01 = Mathf.Clamp01(elapsedSeconds / intervalSeconds);
        float remainSeconds = Mathf.Max(0f, intervalSeconds - elapsedSeconds);
        string enableText = foodModuleData.EnableSpoilage ? "启用" : "关闭";
        string targetItemID = string.IsNullOrWhiteSpace(foodModuleData.SpoilageTargetItemID) ? "未配置" : foodModuleData.SpoilageTargetItemID;

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
    private bool TryGetFoodModuleData(ItemData itemData, out ModData_FoodData foodModuleData)
    {
        foodModuleData = null;
        if (itemData == null || itemData.ModuleDataDic == null)
        {
            return false;
        }

        string moduleKey = null;
        ModuleData rawModuleData = null;
        foreach (var pair in itemData.ModuleDataDic)
        {
            ModuleData moduleData = pair.Value;
            if (moduleData == null || moduleData.ID != ModText.Food)
            {
                continue;
            }

            moduleKey = pair.Key;
            rawModuleData = moduleData;
            break;
        }

        if (rawModuleData == null)
        {
            return false;
        }

        if (rawModuleData is ModData_FoodData typedFoodData)
        {
            foodModuleData = typedFoodData;
            foodModuleData.ApplyToFoodData();
            return true;
        }

        foodModuleData = ModData_FoodData.FromModuleData(rawModuleData);
        if (foodModuleData == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(moduleKey))
        {
            itemData.ModuleDataDic[moduleKey] = foodModuleData;
        }

        return true;
    }

}


using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData inventoryModuleData = new InventoryModuleData();
    public override ModuleData _Data { get => inventoryModuleData; set => inventoryModuleData = (InventoryModuleData)value; }
    public Inventory _Inventory { get => inventory; set => inventory = value; }
    [Tooltip("容器，用于存放物品")]
    public Inventory inventory;
    public BasePanel basePanel;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Bag;
        }
    }

    public override void Load()
    {
        basePanel = GetComponent<BasePanel>();
        if (_Inventory == null)
        {
            _Inventory = GetComponent<Inventory>();
        }
        if (inventoryModuleData.Data.Count == 0)
        {
            inventoryModuleData.Data[_Data.Name] = inventory.Data;
        }
        else
        {
            inventory.Data = inventoryModuleData.Data[_Data.Name];
        }

        if(Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
        _Data = Item_Data.ModuleDataDic[_Data.Name];

        inventory.Owner = item;

        if (item.itemMods.GetMod_ByID(ModText.Hand))
        {
            inventory.DefaultTarget_Inventory =
                          item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>()._Inventory;
        }
        else
        {
            inventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
        //   Debug.Log("Mod_Inventory: " + item.name + " 没有找到Mod_Hand");
        }

        // 检测物品是否可拾取，如果可拾取则隐藏面板
        CheckAndHidePanelIfPickable();

        // 恢复面板位置 - 增强版本，添加更多检查
        if (basePanel != null && basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // 确保保存的数据是有效的
                if (inventoryModuleData.PanleRectPosition != null && 
                    IsValidVector2(inventoryModuleData.PanleRectPosition))
                {
                    draggerRectTransform.anchoredPosition = inventoryModuleData.PanleRectPosition;
                }
            }
        }

        _Inventory.Init();
    }


    [Button]
    public override void Save()
    {
        // 保存面板位置 - 增强版本，添加更多检查
        if (basePanel != null && basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // 只有在面板处于有效状态时才保存位置
                if (draggerRectTransform.anchoredPosition != null &&
                    IsValidVector2(draggerRectTransform.anchoredPosition))
                {
                    inventoryModuleData.PanleRectPosition = draggerRectTransform.anchoredPosition;
                }
            }
        }

        inventory.Save();
        Item_Data.ModuleDataDic[_Data.Name] = inventoryModuleData;
    }

    // 辅助方法：检查Vector2是否有效
    private bool IsValidVector2(Vector2 vector)
    {
        // 检查是否包含无效值
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }
    
    // TODO完成：检测物品可拾取状态并隐藏面板
    /// <summary>
    /// 检测物品是否可拾取，如果可拾取则隐藏面板
    /// </summary>
    private void CheckAndHidePanelIfPickable()
    {
        // 检查basePanel是否存在
        if (basePanel == null)
        {
            basePanel = GetComponent<BasePanel>();
            if (basePanel == null)
            {
                Debug.LogWarning("无法找到BasePanel组件，跳过面板隐藏逻辑");
                return;
            }
        }
        
        // 检查物品数据是否存在且物品可拾取
        if (item != null && item.itemData != null && item.itemData.Stack.CanBePickedUp)
        {
            // 使用BasePanel的Close方法隐藏面板
            basePanel.Close();
            Debug.Log($"物品 {item.name} 可拾取，自动隐藏背包面板");
        }
        // 如果物品不可拾取，则保持面板当前状态（不强制显示）
    }

    // 可选：提供一个公共方法用于外部调用
    /// <summary>
    /// 公共方法：根据物品可拾取状态更新面板显示
    /// </summary>
    public void UpdatePanelVisibilityBasedOnPickableState()
    {
        CheckAndHidePanelIfPickable();
    }
}

public interface IInventory
{
    Inventory _Inventory { get; set; }
}
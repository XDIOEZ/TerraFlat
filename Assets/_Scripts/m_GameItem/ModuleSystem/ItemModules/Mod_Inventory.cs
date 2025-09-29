using AYellowpaper.SerializedCollections;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    public InventoryModuleData  Data = new InventoryModuleData();
    public override ModuleData _Data { get => Data; set => Data = (InventoryModuleData)value; }
    public Inventory inventory { get => InventoryRefDic["背包"]; set => InventoryRefDic["背包"] = value; }
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get=> inventoryRefDic; set => inventoryRefDic = value; }
    [Tooltip("模块面板")]
    public BasePanel basePanel;
    public override void Awake()
    {
        if (_Data.ID == "")
        {
            _Data.ID = ModText.Bag;
        }
    }

    public void OnValidate()
    {
        if(inventoryRefDic.Count == 0)
        {
            inventoryRefDic.Add("背包", GetComponentInChildren<Inventory>());
        }
    }

    public override void Load()
{
    basePanel = GetComponent<BasePanel>();
    if (inventory == null)
    {
        inventory = GetComponent<Inventory>();
    }
    if (Data.Data.Count == 0)
    {
        Data.Data[_Data.Name] = inventory.Data;
    }
    else
    {
        inventory.Data = Data.Data[_Data.Name];
    }

    if(Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
    _Data = Item_Data.ModuleDataDic[_Data.Name];

    inventory.Owner = item;

    if (item.itemMods.GetMod_ByID(ModText.Hand))
    {
        inventory.DefaultTarget_Inventory =
                      item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
    }
    else
    {
        inventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
    }

    // 检查物品是否处于可拾取状态，如果可拾取则关闭面板
    CheckAndHidePanelIfPickable();

    // 恢复面板位置和开关状态
    if (basePanel != null)
    {
        // 恢复面板位置 - 增强版本，增加更多检查
        if (basePanel.Dragger != null)
        {
            var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
            if (draggerRectTransform != null)
            {
                // 只有当保存的位置数据有效且不为零时才应用位置
                if (Data.PanleRectPosition != null && 
                    IsValidVector2(Data.PanleRectPosition) &&
                    (Data.PanleRectPosition.x != 0 || Data.PanleRectPosition.y != 0))
                {
                    draggerRectTransform.anchoredPosition = Data.PanleRectPosition;
                }
                // 如果是初次加载且位置数据为空，则使用Prefab自带的位置，不需要调整
            }
        }
        
        // 恢复面板开关状态
        if (Data.BasePanelIsOpen)
        {
            basePanel.Open();
        }
        else
        {
            basePanel.Close();
        }
    }

    item.itemMods.GetMod_ByID(ModText.Interact, out Mod_Interaction interactable);
    if(interactable!= null)
    {
        interactable.OnAction_Start += Interact_Start;
        interactable.OnAction_Stop += Interact_Stop;
    }


        inventory.Init();


      
    }
    public void Start()
    {
        GameRes.Instance.InventoryInitGet(Data.InventoryInitName, out Inventoryinit inventoryInit);
        if (inventoryInit != null)
        {
            inventory.TryInitializeItems(inventoryInit);

        }
        //初始化刷新UI
        inventory.RefreshUI();
    }
    //玩家与此发生交互
    public void Interact_Start(Item item_)
    {
        item_.itemMods.GetMod_ByID(ModText.Hand, out Mod_Inventory handMod);
        if (handMod == null) return;
        inventory.DefaultTarget_Inventory = handMod.inventory;
        basePanel.Toggle();
    }
    //玩家结束交互
    public void Interact_Stop(Item item_)
    {
        inventory.DefaultTarget_Inventory = null;
        basePanel.Close();
    }

    [Button]
    public override void Save()
    {
        // 保存面板开关状态
        if (basePanel != null)
        {
            Data.BasePanelIsOpen = basePanel.IsOpen();
        }
        
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
                    Data.PanleRectPosition = draggerRectTransform.anchoredPosition;
                }
            }
        }

        inventory.Save();
        Item_Data.ModuleDataDic[_Data.Name] = Data;
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
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get; set; }
    
    [Tooltip("默认返回的目标Inventory")]
    public Inventory GetDefaultTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;
            
        // 返回第一个Inventory
        return InventoryRefDic.Values.First();
    }
    
    [Tooltip("随机返回一个Inventory")]
    public Inventory GetRandomTargetInventory()
    {
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
            return null;
            
        // 将值转换为数组并随机选择一个
        var inventories = InventoryRefDic.Values.ToArray();
        int randomIndex = UnityEngine.Random.Range(0, inventories.Length);
        return inventories[randomIndex];
    }
}
[Serializable]
[MemoryPackable]
public partial class InventoryModuleData : ModuleData
{
    [ShowInInspector]
    public Dictionary<string, Inventory_Data> Data = new Dictionary<string, Inventory_Data>();
    public Vector3 PanleRectPosition = Vector3.zero;//TODO 我在这里添加了一个Vector3变量，用于保存面板的位置
    public string InventoryInitName = "";
    public bool BasePanelIsOpen = true;
}
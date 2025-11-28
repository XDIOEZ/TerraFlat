using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Linq;
using UnityEngine;

public class Mod_Inventory : Module,IInventory
{
    #region 字段和属性
    public InventoryModuleData  Data = new InventoryModuleData();
    public override ModuleData _Data { get => Data; set => Data = (InventoryModuleData)value; }
    public Inventory inventory { get => InventoryRefDic.First().Value;}
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();
    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get=> inventoryRefDic; set => inventoryRefDic = value; }
    [Tooltip("模块面板")]
    public BasePanel basePanel;

    // 修改：将单个预制体字段改为与inventoryRefDic对应的序列化字典
    [Tooltip("Inventory面板预制体字典")]
    public SerializedDictionary<string, GameObject> inventoryPanelPrefabs = new();
    [Tooltip("模块面板的预制体")]
    public GameObject Prefab_BasePanel;

    #endregion

    #region 生命周期方法

    public void OnValidate()
    {
        _Data.ID = inventory.Data.Name;
    }

    public override void Load()
    {
        // 修改为使用for循环遍历inventoryRefDic中的所有inventory
        var inventoryPairs = inventoryRefDic.ToArray();
        for (int i = 0; i < inventoryPairs.Length; i++)
        {
            var kvp = inventoryPairs[i];
            string inventoryId = kvp.Key;
            Inventory currentInventory = kvp.Value;
            
            // 加载库存数据
            if (Data.Data.Count == 0)
            {
                Data.Data[inventoryId] = currentInventory.Data;
            }
            else if (Data.Data.ContainsKey(inventoryId))
            {
                currentInventory.Data = Data.Data[inventoryId];
            }
            
            // 为每个inventory创建对应的面板
            BasePanel inventoryPanel = null;
            if (inventoryPanelPrefabs.ContainsKey(inventoryId) && inventoryPanelPrefabs[inventoryId] != null)
            {
                inventoryPanel = UIManager.Instance.CreatePanelFromGameObject(inventoryPanelPrefabs[inventoryId]).GetComponentInChildren<BasePanel>();
            }
            
            // 设置面板引用
            currentInventory.basePanel = inventoryPanel;
            
            // 为第一个inventory设置默认的basePanel引用，保持向后兼容
            if (i == 0) // 使用索引判断是否为第一个元素
            {
                basePanel = inventoryPanel;
            }
            
            // 查找模块数据
            if (Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
                _Data = Item_Data.ModuleDataDic[_Data.Name];
            
            // 设置所有者
            currentInventory.Owner = item;
            
            // 设置默认目标库存
            if (item.itemMods.GetMod_ByID(ModText.Hand))
            {
                currentInventory.DefaultTarget_Inventory = 
                            item.itemMods.GetMod_ByID(ModText.Hand).GetComponent<IInventory>().GetDefaultTargetInventory();
            }
            else
            {
                currentInventory.DefaultTarget_Inventory = Inventory_Hand.PlayerHand;
            }
            
            // 初始化库存
            currentInventory.Init();
            currentInventory.BindController();
            
            // 尝试初始化物品
            GameRes.Instance.InventoryInitGet(Data.InventoryInitName, out Inventoryinit inventoryInit);
            if (inventoryInit != null)
            {
                currentInventory.TryInitializeItems(inventoryInit);
            }
            
            // 刷新UI
            currentInventory.RefreshUI();
        }
        
        // 恢复面板状态和位置 - 仅针对默认面板
        if (basePanel != null)
        {
            // 恢复面板位置
            if (basePanel.Dragger != null)
            {
                var draggerRectTransform = basePanel.Dragger.GetComponent<RectTransform>();
                if (draggerRectTransform != null)
                {
                    if (Data.PanleRectPosition != null && 
                        IsValidVector2(Data.PanleRectPosition) &&
                        (Data.PanleRectPosition.x != 0 || Data.PanleRectPosition.y != 0))
                    {
                        draggerRectTransform.anchoredPosition = Data.PanleRectPosition;
                    }
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
    }

    #endregion

    #region 交互方法
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
    #endregion

    #region 保存方法
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
    #endregion

    #region 辅助方法
    // 辅助方法：检查Vector2是否有效
    private bool IsValidVector2(Vector2 vector)
    {
        // 检查是否包含无效值
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) &&
               !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y);
    }
    
    // 检测物品是否可拾取，如果可拾取则隐藏面板
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
            Debug.Log($"物品 {item.name} 可拾取，自动隐藏默认面板");
        }
        // 如果物品不可拾取，则保持面板当前状态（不强制显示）
    }

    // 公共方法：根据物品可拾取状态更新面板显示
    public void UpdatePanelVisibilityBasedOnPickableState()
    {
        CheckAndHidePanelIfPickable();
    }
    #endregion
}

public interface IInventory
{
    #region 接口属性和方法
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
    #endregion
}
using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Mod_Inventory : Module, IInventory
{
    #region 字段和属性
    public Inventory_ModuleData Data = new Inventory_ModuleData();
    public override ModuleData _Data { get => Data; set => Data = (Inventory_ModuleData)value; }
    public Inventory inventory
    {
        get
        {
            // 优先从序列化的列表中取第一个
            if (InventoryInstances != null && InventoryInstances.Count > 0)
                return InventoryInstances[0];

            // 回退：从运行时字典中取第一个
            if (InventoryRefDic != null && InventoryRefDic.Count > 0)
                return InventoryRefDic.First().Value;

            return null;
        }
    }

    [Tooltip("Inventory序列化列表（用于在Inspector中配置）")]
    [SerializeReference]
    public List<Inventory> InventoryInstances = new();

    [System.NonSerialized]
    [Tooltip("运行时构建的Inventory引用字典")]
    public SerializedDictionary<string, Inventory> inventoryRefDic = new();

    [Tooltip("Inventory引用字典")]
    public SerializedDictionary<string, Inventory> InventoryRefDic { get => inventoryRefDic; set => inventoryRefDic = value; }

    [Tooltip("Inventory对应的BasePanel缓存字典")]
    [ReadOnly]
    public SerializedDictionary<Inventory, BasePanel> inventoryBasePanelCache = new();

    #endregion

    #region 生命周期方法

    public void OnValidate()
    {
        if (inventory != null && inventory.Data != null)
        {
            inventory.OnValidate();
        }
    }

    public override void Load()
    {
        // 先根据序列化列表构建运行时字典
        inventoryRefDic.Clear();
        if (InventoryInstances != null)
        {
            for (int i = 0; i < InventoryInstances.Count; i++)
            {
                var inv = InventoryInstances[i];
                if (inv == null)
                    continue;

                // 优先使用 Inventory_Data.Name 作为key，若为空则使用物体名
                string key = inv.Data != null && !string.IsNullOrEmpty(inv.Data.Name)
                    ? inv.Data.Name
                    : inv.Data.Name;

                if (string.IsNullOrEmpty(key))
                    continue;

                if (!inventoryRefDic.ContainsKey(key))
                    inventoryRefDic.Add(key, inv);
            }
        }

        // 修改为使用for循环遍历inventoryRefDic中的所有inventory
        var inventoryPairs = inventoryRefDic.ToArray();

        // 遍历inventoryRefDic中的所有inventory
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

            // 查找模块数据
            if (Item_Data.ModuleDataDic.ContainsKey(_Data.Name))
                _Data = Item_Data.ModuleDataDic[_Data.Name];

            // 设置所有者
            currentInventory.item = item;

            // 设置默认目标库存
            var handInventory = item.GetComponentInChildren<Mod_Hand>()?.HandInventory;
            currentInventory.DefaultTarget_Inventory = handInventory != null ? handInventory : Inventory_Hand.PlayerHand;

            // 初始化库存
            currentInventory.InitData();
            BindController();

            // 尝试初始化物品
            GameRes.Instance.InventoryInitGet(Data.InventoryInitName, out Inventoryinit inventoryInit);

            if (inventoryInit != null)
            {
                currentInventory.TryInitializeItems(inventoryInit);
            }

            // 根据保存的面板状态决定是否提前初始化UI
            // 如果面板之前是打开的，则在Load阶段预先创建，否则延迟到需要时再创建
            if (currentInventory.Data != null && currentInventory.Data.PanelIsOpen)
            {
                EnsurePanelCreated(currentInventory);
                NewMethod(currentInventory);
            }
        }
        // 获取交互模块引用
        if (item != null && item.itemMods != null)
        {
            var interactMod = item.itemMods.GetMod_ByID<Mod_InteractReciver>(ModText.Interact);
            if (interactMod != null)
            {
                interactMod.OnAction_Start += Interact_Start;
                interactMod.OnAction_Stop += Interact_Stop;
            }
        }
        else
        {
            Debug.LogWarning("Item或Item的itemMods为空，无法绑定交互事件");
        }
    }

    private void NewMethod(Inventory currentInventory)
    {
        // 根据参数决定是否打开面板
        if (currentInventory.Data.PanelIsOpen)
        {
            currentInventory.basePanel.Open();
            // 延迟1帧调用，确保面板在层级系统正确排列
            StartCoroutine(DelayedBringToFront(currentInventory.basePanel.GetComponent<RectTransform>()));
        }
        else
        {
            currentInventory.basePanel.Close();
        }
    }

    /// <summary>
    /// 确保指定Inventory的面板已创建，如果未创建则在此时创建
    /// </summary>
    /// <returns>成功创建了面板返回true，没有创建返回false</returns>
    public bool EnsurePanelCreated(Inventory targetInventory = null, bool Open = true)
    {
        Inventory currentInventory = targetInventory ?? inventory;
        // 调用 Inventory 自身的创建面板逻辑
        return currentInventory.EnsurePanelCreated();
    }

    public virtual void BindController()
    {
        GameController GameController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);

        foreach (var inventory in inventoryRefDic.Values)
            inventory.BindController(GameController);
    }

    public override void ModUpdate(float deltaTime)
    {
        foreach (var inventory in inventoryRefDic.Values)
        {
            inventory.ModUpdate(deltaTime);
        }
    }

    #endregion

    #region 交互方法
    //玩家与此发生交互
    public void Interact_Start(Item item_)
    {
        // 空检查：确保item_存在
        if (item_ == null)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] item_ 为空！");
            return;
        }

        // 空检查：确保InventoryRefDic存在
        if (InventoryRefDic == null || InventoryRefDic.Count == 0)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] InventoryRefDic 为空或未初始化！");
            return;
        }



        foreach (var kvp in InventoryInstances)
        {
            kvp.Interact_Start(item_);
        }

        var handInventory = item_.GetComponentInChildren<Mod_Hand>()?.HandInventory;
        if (handInventory == null)
        {
            Debug.LogError("[Mod_Inventory.Interact_Start] 未找到 Mod_Hand.HandInventory，无法设置默认交互库存");
            return;
        }

        // 设置所有Inventory的默认目标
        foreach (var kvp in InventoryRefDic)
        {
            var currentInventory = kvp.Value;
            currentInventory.DefaultTarget_Inventory = handInventory;
        }
    }


    //玩家结束交互
    public void Interact_Stop(Item item_)
    {
        // 清除所有Inventory的默认目标
        foreach (var kvp in InventoryRefDic)
        {
            var currentInventory = kvp.Value;
            currentInventory.DefaultTarget_Inventory = null;
        }

        // 关闭所有面板
        foreach (var kvp in inventoryBasePanelCache)
        {
            var currentPanel = kvp.Value;
            if (currentPanel != null)
            {
                currentPanel.Close();
            }
        }
    }
    #endregion

    #region 保存方法

    [Button]
    public override void Save()
    {
        // 保存面板开关状态 - 仅保存第一个面板的状态作为参考
        if (inventoryBasePanelCache.Count > 0)
        {
            var firstPanel = inventoryBasePanelCache.First().Value;
            if (firstPanel != null)
            {
                Data.BasePanelIsOpen = firstPanel.IsOpen();
            }
        }

        // 保存面板位置 - 仅保存第一个面板的位置作为参考
        // 保存每个 inventory 对应面板的位置到其 Inventory_Data.PanelPosition 中
        if (inventoryBasePanelCache.Count > 0)
        {
            foreach (var kvp in inventoryBasePanelCache)
            {
                var inv = kvp.Key;
                var panel = kvp.Value;
                if (inv != null && inv.Data != null && panel != null)
                {
                    RectTransform rt = null;
                    if (panel.Dragger != null)
                        rt = panel.Dragger.GetComponent<RectTransform>();
                    if (rt == null)
                        rt = panel.GetComponent<RectTransform>();

                    if (rt != null)
                    {
                        var anchored = rt.anchoredPosition;
                        if (IsValidVector2(anchored))
                        {
                            inv.Data.PanelPosition = new Vector3(anchored.x, anchored.y, inv.Data.PanelPosition.z);
                        }
                    }
                }
            }
        }

        // 保存所有Inventory的数据
        foreach (var kvp in InventoryRefDic)
        {
            kvp.Value.Save();
            Data.Data[kvp.Key] = kvp.Value.Data;
        }

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

    // 检测物品是否可拾取，如果可拾取则隐藏面板// 检测物品是否可拾取，如果可拾取则隐藏所有面板
    private void CheckAndHidePanelIfPickable()
    {
        // 检查物品数据是否存在且物品可拾取
        if (item != null && item.itemData != null && item.itemData.Stack.CanBePickedUp)
        {
            // 隐藏所有缓存的面板
            foreach (var kvp in inventoryBasePanelCache)
            {
                var currentPanel = kvp.Value;
                if (currentPanel != null)
                {
                    currentPanel.Close();
                    Debug.Log($"物品 {item.name} 可拾取，自动隐藏面板");
                }
            }
        }
        // 如果物品不可拾取，则保持面板当前状态（不强制显示）
    }


    // 公共方法：根据物品可拾取状态更新面板显示
    public void UpdatePanelVisibilityBasedOnPickableState()
    {
        CheckAndHidePanelIfPickable();
    }

    // 延迟一帧将面板置于最前方的协程方法
    private IEnumerator DelayedBringToFront(RectTransform rectTransform)
    {
        yield return null; // 等待一帧

        BasePanel.BringToFront(rectTransform);

    }

    public Inventory GetDefaultTargetInventory()
    {
        throw new System.NotImplementedException();
    }
    #endregion
}

public interface IInventory
{
    #region 接口属性和方法
    [Tooltip("默认返回的目标Inventory")]
    public Inventory GetDefaultTargetInventory();
    #endregion
}
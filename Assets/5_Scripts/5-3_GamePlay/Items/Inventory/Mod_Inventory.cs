using AYellowpaper.SerializedCollections;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Mod_Inventory : Module, IInventory, IInstanceUI
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

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
        Data ??= new Inventory_ModuleData();
        Data.Data ??= new Dictionary<string, Inventory_Data>();

        // 先根据序列化列表构建运行时字典
        inventoryRefDic.Clear();
        if (InventoryInstances != null)
        {
            for (int i = 0; i < InventoryInstances.Count; i++)
            {
                var inv = InventoryInstances[i];
                if (inv == null)
                    continue;

                // 没有名称的输入/输出/燃料槽也必须有稳定键，不能因此被存档系统跳过。
                string key = GetInventoryKey(inv, i);

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
            if (Data.Data.TryGetValue(inventoryId, out Inventory_Data savedInventoryData) &&
                savedInventoryData != null)
            {
                currentInventory.Data = savedInventoryData;
            }
            else
            {
                Data.Data[inventoryId] = currentInventory.Data;
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

        // 装备模块可能先于本背包模块恢复，统一在背包数据就绪后重新挂载装备扩展槽。
        Mod_Equipment equipment = item?.GetComponentsInChildren<Mod_Equipment>(true)
            .FirstOrDefault();
        equipment?.RefreshBagStorageSlots();
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
        GameController gameController = null;
        if (item?.itemMods?.ContainsKey_ID(ModText.Controller) == true)
            gameController = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        else if (item?.Owner?.itemMods?.ContainsKey_ID(ModText.Controller) == true)
            gameController = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);

        foreach (var currentInventory in inventoryRefDic.Values)
        {
            if (gameController != null)
                currentInventory.BindController(gameController);
            else
                currentInventory.UnbindController();
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        foreach (var inventory in inventoryRefDic.Values)
        {
            inventory.ModUpdate(deltaTime);
        }
    }

    #endregion

    #region IInstanceUI接口

    public void I_ShowPanel()
    {
        Inventory target = inventory;
        if (target == null)
            throw new System.InvalidOperationException("[Mod_Inventory] inventory 为空，无法打开面板");

        EnsureInventoryPanelPrefabAssigned(target);
        EnsurePanelCreated(target);
        target.basePanel.Open();
        target.SyncQuickTransferTarget(target.basePanel);
    }

    public void I_ClosePanel()
    {
        Inventory target = inventory;
        if (target == null)
            throw new System.InvalidOperationException("[Mod_Inventory] inventory 为空，无法关闭面板");

        if (target.basePanel != null)
        {
            target.basePanel.Close();
            target.SyncQuickTransferTarget(target.basePanel);
        }
    }

    public void I_TogglePanel()
    {
        Inventory target = inventory;
        if (target == null)
            throw new System.InvalidOperationException("[Mod_Inventory] inventory 为空，无法切换面板");

        EnsureInventoryPanelPrefabAssigned(target);
        target.SwitchUI();
    }

    private static void EnsureInventoryPanelPrefabAssigned(Inventory target)
    {
        if (target.InventoryPanel_Prefab != null)
            return;

        if (target.Data == null || string.IsNullOrEmpty(target.Data.UIPrefabName))
            throw new System.InvalidOperationException("[Mod_Inventory] InventoryPanel_Prefab 未设置，且 Data.UIPrefabName 为空，无法创建面板");

        target.InventoryPanel_Prefab = GameRes.Instance.GetPrefab(target.Data.UIPrefabName);
        if (target.InventoryPanel_Prefab == null)
            throw new System.InvalidOperationException($"[Mod_Inventory] 无法通过 GameRes 获取库存UI预制体: {target.Data.UIPrefabName}");
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
        Data ??= new Inventory_ModuleData();
        Data.Data ??= new Dictionary<string, Inventory_Data>();

        Mod_Equipment equipment = item?.GetComponentsInChildren<Mod_Equipment>(true)
            .FirstOrDefault();
        equipment?.PrepareBagStorageForOwnerSave();

        try
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

        // 保存所有 InventoryInstances；不能只遍历字典，否则没有名称的槽位会漏存。
        if (InventoryInstances != null)
        {
            for (int i = 0; i < InventoryInstances.Count; i++)
            {
                Inventory currentInventory = InventoryInstances[i];
                if (currentInventory == null)
                    continue;

                currentInventory.Save();
                Data.Data[GetInventoryKey(currentInventory, i)] = currentInventory.Data;
            }
        }

        Item_Data.ModuleDataDic[_Data.Name] = Data;
        }
        finally
        {
            equipment?.RestoreBagStorageAfterOwnerSave();
        }
    }
    #endregion

    #region 辅助方法
    /// <summary>生成库存存档键；优先使用配置名称，否则使用列表索引保证稳定唯一。</summary>
    private static string GetInventoryKey(Inventory targetInventory, int index)
    {
        string configuredName = targetInventory?.Data?.Name;
        return string.IsNullOrWhiteSpace(configuredName)
            ? $"Inventory_{index}"
            : configuredName;
    }

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
        var target = inventory;
        if (target == null)
            throw new System.InvalidOperationException("[Mod_Inventory] 默认目标 Inventory 为空，请检查 InventoryInstances/InventoryRefDic 配置");

        return target;
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

/// <summary>
/// 制作/加工模块的库存存档适配器。库存运行时类型属于 GamePlay，避免让 Data 程序集反向依赖玩法程序集。
/// </summary>
public static class InventoryModuleDataPersistence
{
    /// <summary>尝试读取独立库存存档，旧格式或空数据返回 null。</summary>
    public static Inventory_ModuleData TryRead(Ex_ModData_MemoryPackable source)
    {
        if (source?.BitData == null || source.BitData.Length == 0)
            return null;

        try
        {
            Inventory_ModuleData data = source.GetData<Inventory_ModuleData>();
            return data?.Data != null ? data : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把独立库存存档写回模块数据。</summary>
    public static void Write(Ex_ModData_MemoryPackable target, Inventory_ModuleData data)
    {
        if (target == null || data == null)
            return;

        data.Data ??= new Dictionary<string, Inventory_Data>();
        target.WriteData(data);
    }

    /// <summary>从指定键恢复一个库存的数据引用。</summary>
    public static bool TryRestore(
        Inventory target,
        Inventory_ModuleData source,
        string key)
    {
        if (target == null || source?.Data == null || string.IsNullOrEmpty(key))
            return false;

        if (!source.Data.TryGetValue(key, out Inventory_Data savedData) || savedData == null)
            return false;

        target.Data = savedData;
        return true;
    }

    /// <summary>把一个库存当前数据写入指定键。</summary>
    public static void Capture(
        Inventory_ModuleData target,
        string key,
        Inventory source)
    {
        if (target == null || string.IsNullOrEmpty(key) || source?.Data == null)
            return;

        target.Data[key] = source.Data;
    }
}

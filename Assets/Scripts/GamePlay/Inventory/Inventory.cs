using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
[System.Serializable]
public class Inventory : MonoBehaviour
{
    #region 字段和属性
    //物品所有者
    public Item item;
    //物品槽预制体
    GameObject ItemSlot_Prefab;
    //物品槽的父物体
    Transform ItemSlot_Parent;
    //数据
    public Inventory_Data Data;
    //UI列表
    public List<ItemSlot_UI> itemSlotUIs = new List<ItemSlot_UI>();

    [Header("默认交互Inventory")] //默认交互Inventory
    public Inventory DefaultTarget_Inventory;
    [Tooltip("外部自动注入")]
    public BasePanel basePanel;
    #endregion

    #region 生命周期

    public virtual void OnValidate()
    {
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = ModText.Bag;
    }

    public virtual void Awake()
    {

    }

    public virtual void ModUpdate(float deltaTime)
    {

    }

    public virtual void OnDestroy()
    {
        Data.Event_RefreshUI -= RefreshUI;
    }

    #endregion

    #region 初始化和同步


    [Tooltip("在Load时调用此函数进行数据初始化（仅初始化数据和逻辑，不涉及UI）")]
    public virtual void InitData()
    {
        // 初始化物品槽位数据
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            Data.itemSlots[i].Index = i;
            Data.itemSlots[i].SlotMaxVolume = 100;
        }

        // 初始化事件系统
        Data.Event_RefreshUI = new();
        Data.Event_RefreshUI.Clear();
        Data.Event_RefreshUI += RefreshUI;
    }

    /// <summary>
    /// 在UI面板创建后调用此函数进行UI初始化
    /// 该方法应在EnsurePanelCreated中调用，确保basePanel已存在
    /// </summary>
    public virtual void InitUI()
    {
        if (basePanel == null)
        {
            Debug.LogError("Prefab_BasePanel 未设置,请在Inspector中的Mod_Inventory中设置对应Inventory的面板预制体");
            return;
        }

        //TODO 获取BasePanel上的UI_Content组件作为Slot的父物体
        ItemSlot_Parent = basePanel.transform.GetComponentInChildren<UI_Content>().transform;

        if (ItemSlot_Parent == null)
        {
            Debug.LogError("ItemSlot_Parent 未设置");
            return;
        }

        // 加载Slot UI预制体
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

        // 同步槽位数量与 itemSlots 保持一致
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;

        // 删除多余槽位（从后往前删保证安全）
        for (int i = currentCount - 1; i >= targetCount; i--)
        {
            DestroyImmediate(ItemSlot_Parent.GetChild(i).gameObject);
        }

        // 创建缺少的槽位
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false);
        }

        // 重建UI列表并绑定数据
        itemSlotUIs.Clear();
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            var ui = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (ui != null)
                itemSlotUIs.Add(ui);
        }

        // 同步 UI 数据
        SyncData();

        //初始化时自动同步UI显示
        RefreshUI();
    }
    //同步UI与Data
    public void SyncData()
    {
        // 空检查：确保数据和UI列表都初始化
        if (itemSlotUIs == null || itemSlotUIs.Count == 0)
        {
            Debug.LogWarning($"[Inventory.SyncData] itemSlotUIs 为空或未初始化！InventoryName: {Data?.Name}");
            return;
        }

        if (Data == null || Data.itemSlots == null)
        {
            Debug.LogError($"[Inventory.SyncData] Data 或 Data.itemSlots 为空！");
            return;
        }

        // 检查数量匹配
        if (itemSlotUIs.Count != Data.itemSlots.Count)
        {
            Debug.LogWarning($"[Inventory.SyncData] UI槽位数({itemSlotUIs.Count}) 与 Data槽位数({Data.itemSlots.Count}) 不匹配！");
        }

        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
            ItemSlot_UI itemSlotUI = itemSlotUIs[i];

            // 检查UI是否存在
            if (itemSlotUI == null)
            {
                Debug.LogError($"[Inventory.SyncData] itemSlotUIs[{i}] 为空！");
                continue;
            }

            if (i >= Data.itemSlots.Count)
            {
                Debug.LogError($"[Inventory.SyncData] Data.itemSlots[{i}] 超出范围！Data槽位总数: {Data.itemSlots.Count}");
                continue;
            }

            // 检查 Data.itemSlots[i] 是否为 null
            if (Data.itemSlots[i] == null)
            {
                Debug.LogError($"[Inventory.SyncData] Data.itemSlots[{i}] 为空！");
                continue;
            }

            // 初始化UI槽位（替代 itemSlotUI.Data = Data.itemSlots[i]）
            itemSlotUI.InitializeSlot(i,
                index => Data.itemSlots[index],  // GetSlotDataFunc
                index =>
                {
                    if (Data.itemSlots[index] != null)
                    {
                        Data.itemSlots[index].ClearData();
                    }
                }  // ClearSlotDataAction
            );

            itemSlotUI.OnLeftClick.Clear();
            itemSlotUI._OnScroll.Clear();
            itemSlotUI.OnRightClick.Clear();

            itemSlotUI.OnLeftClick += OnLeftClick;
            itemSlotUI._OnScroll += OnScroll;
            itemSlotUI.OnRightClick += OnRightClick;

            // 修复 Belong_Inventory 的逻辑，将其设置为当前 Inventory 实例
            if (Data.itemSlots[i].onSlotDataChanged != null)
            {
                Data.itemSlots[i].onSlotDataChanged.Clear();
                Data.itemSlots[i].onSlotDataChanged += OnItemSlotChanged;
            }
            else
            {
                Debug.LogWarning($"[Inventory.SyncData] Data.itemSlots[{i}].onSlotDataChanged 为空！");
            }
        }
    }

    // 当物品槽数据发生变化时的回调
    private void OnItemSlotChanged(ItemSlot slot)
    {
        // 防守性编程：检查slot和Data是否为空
        if (slot == null || Data == null || Data.itemSlots == null)
        {
            Debug.LogWarning($"[Inventory.OnItemSlotChanged] slot、Data 或 Data.itemSlots 为空！");
            return;
        }

        // 找到对应的UI并刷新
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i] != null && Data.itemSlots[i] == slot)
            {
                RefreshUI(i);
                break;
            }
        }
    }

    #endregion

    #region 物品初始化

    /// <summary>
    /// 自动初始化容器内的物品
    /// </summary>
    public void TryInitializeItems(Inventoryinit inventoryinit)
    {
        // 使用InventoryInit的注册函数将物品注册到inventory中
        inventoryinit.InjectRandomItemsToInventory(this);
        Debug.Log($"[{Data.Name}] 容器初始化完成，注册 {inventoryinit.items.Count} 个物品");
    }

    /// <summary>
    /// 检查容器是否为空，没有任何物品
    /// </summary>
    /// <returns>如果容器为空返回true，否则返回false</returns>
    private bool IsInventoryEmpty()
    {
        foreach (var slot in Data.itemSlots)
        {
            if (slot.itemData != null)
                return false;
        }
        return true;
    }

    #endregion

    #region UI

    //TODO 基于新输入系统实现按下B键打开和关闭背包UI




    public void RefreshUI(int index)
    {
        if (index >= 0 && index < itemSlotUIs.Count)
        {
            itemSlotUIs[index].RefreshUI();
        }
    }

    public void RefreshUI()
    {
        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
            itemSlotUIs[i].RefreshUI();
        }
    }

    public virtual void Interact_Start(Item item_)
    {

    }

    #endregion

    #region 鼠标事件处理

    void OnRightClick(int index)
    {
        RightClickMenu_UI currentMenuInstance;
        currentMenuInstance = Instantiate(GameRes.Instance.GetPrefab("右键菜单").GetComponent<RightClickMenu_UI>());
        currentMenuInstance.Init(itemSlotUIs[index], item);
        currentMenuInstance.basePanel.Dragger.rectTransform.position = itemSlotUIs[index].transform.position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnScroll(int index, float direction)
    {
        if (direction > 0)
        {
            Data.TransferItemQuantity(DefaultTarget_Inventory.Data.itemSlots[0], Data.itemSlots[index], 1);
        }
        else if (direction < 0)
        {
            Data.TransferItemQuantity(Data.itemSlots[index], DefaultTarget_Inventory.Data.itemSlots[0], 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void OnLeftClick(int index)
    {
        ItemSlot slot = Data.GetItemSlot(index);

        //防御性检查：确保DefaultTarget_Inventory不为null
        if (DefaultTarget_Inventory == null)
        {
            Debug.LogWarning($"[{Data.Name}] 手部为空：DefaultTarget_Inventory未设置 ,点击了 [{index}]");
            return;
        }

        //防御性检查：确保DefaultTarget_Inventory的Data不为null
        if (DefaultTarget_Inventory.Data == null)
        {
            Debug.LogWarning($"[{Data.Name}] 手部为空：DefaultTarget_Inventory.Data未设置");
            return;
        }

        //默认为手部
        if (DefaultTarget_Inventory.Data.itemSlots.Count > index)
        {
            //额外检查：确保目标槽位存在且不为null
            if (DefaultTarget_Inventory.Data.itemSlots[index] == null)
            {
                Debug.LogWarning($"[{Data.Name}] 手部槽位 [{index}] 为空");
            }
            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[index]);
            DefaultTarget_Inventory.RefreshUI(index);
        }
        else
        {
            //额外检查：确保默认槽位存在且不为null
            if (DefaultTarget_Inventory.Data.itemSlots.Count == 0)
            {
                Debug.LogWarning($"[{Data.Name}] 手部物品槽列表为空");
                return;
            }

            if (DefaultTarget_Inventory.Data.itemSlots[0] == null)
            {
                Debug.LogWarning($"[{Data.Name}] 默认手部槽位 [0] 为空");
            }

            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[0]);
            DefaultTarget_Inventory.RefreshUI(0);
        }

        RefreshUI(index);
    }

    #endregion

    #region 编辑器功能

    [Sirenix.OdinInspector.Button]
    public void SyncSlotCount()
    {
        Data.itemSlots.Clear();
        int currentCount = ItemSlot_Parent.childCount;
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            Data.itemSlots.Add(new ItemSlot());
        }
    }

    #endregion

    #region 注入物品逻辑（从Inventory_Data移动过来）

    /// <summary>
    /// 随机顺序自动注入物品列表到容器中
    /// </summary>
    public void RandomOrderAutoInjectItemDataList(List<GameObject> prefabList, List<int> countList)
    {
        if (prefabList == null || countList == null) return;
        if (prefabList.Count != countList.Count) return;

        // --- Step1: 打乱物品顺序 ---
        List<int> itemIndices = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            itemIndices.Add(i);
        }

        for (int i = itemIndices.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            int temp = itemIndices[i];
            itemIndices[i] = itemIndices[r];
            itemIndices[r] = temp;
        }

        // --- Step2: 收集所有空槽位并打乱 ---
        List<int> emptySlots = new List<int>();
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            if (Data.itemSlots[i].itemData == null)
                emptySlots.Add(i);
        }

        for (int i = emptySlots.Count - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            int temp = emptySlots[i];
            emptySlots[i] = emptySlots[r];
            emptySlots[r] = temp;
        }

        // --- Step3: 按随机顺序把物品塞进随机槽位 ---
        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < itemIndices.Count && i < emptySlots.Count; i++)
        {
            int randomItemIndex = itemIndices[i];
            int slotIndex = emptySlots[i];

            GameObject prefab = prefabList[randomItemIndex];
            int count = countList[randomItemIndex];

            if (prefab == null || count <= 0) { failCount++; continue; }

            var itemComp = prefab.GetComponent<Item>();
            if (itemComp == null) { failCount++; continue; }

            var itemData = itemComp.Get_NewItemData();
            if (itemData == null) { failCount++; continue; }

            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            Data.SetOne_ItemData(slotIndex, itemData);
            Data.Event_RefreshUI.Invoke(slotIndex);

            successCount++;
        }

        Debug.Log($"随机注入完成：成功 {successCount}，失败 {failCount}");
    }

    /// <summary>
    /// 自动注入物品列表到容器中，智能查找空槽位或可堆叠槽位，避免覆盖已有物品
    /// </summary>
    /// <param name="prefabList">物品预制体列表</param>
    /// <param name="countList">对应物品数量列表</param>
    [Button("自动注入物品列表")]
    [LabelText("自动注入物品列表")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("数量列表")] List<int> countList)
    {
        // 参数验证
        if (prefabList == null || countList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表或数量列表不能为空");
            return;
        }

        if (prefabList.Count != countList.Count)
        {
            Debug.LogError($"自动注入失败：Prefab列表数量({prefabList.Count})与数量列表数量({countList.Count})不匹配");
            return;
        }

        if (prefabList.Count == 0)
        {
            Debug.LogWarning("自动注入失败：Prefab列表为空");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        // 遍历并自动注入每个物品
        for (int i = 0; i < prefabList.Count; i++)
        {
            GameObject prefab = prefabList[i];
            int count = countList[i];

            if (prefab == null)
            {
                Debug.LogWarning($"跳过空的Prefab（索引 {i}）");
                failCount++;
                continue;
            }

            if (count <= 0)
            {
                Debug.LogWarning($"跳过无效数量 {count} 的物品 {prefab.name}（索引 {i}）");
                failCount++;
                continue;
            }

            // 获取Prefab上的Item组件
            Item itemComponent = prefab.GetComponent<Item>();
            if (itemComponent == null)
            {
                Debug.LogError($"自动注入失败：Prefab {prefab.name} 上找不到Item组件（索引 {i}）");
                failCount++;
                continue;
            }

            // 克隆ItemData
            ItemData itemData = itemComponent.Get_NewItemData();
            if (itemData == null)
            {
                Debug.LogError($"自动注入失败：无法克隆 {prefab.name} 的ItemData（索引 {i}）");
                failCount++;
                continue;
            }

            // 设置数量
            itemData.Stack.Amount = count;
            itemData.Stack.CanBePickedUp = false;

            // 尝试添加物品
            if (Data.TryAddItem(itemData, true))
            {
                Debug.Log($"成功自动注入物品 {prefab.name} x{count}");
                successCount++;
            }
            else
            {
                Debug.LogError($"自动注入失败：容器空间不足，无法注入物品 {prefab.name} x{count}");
                failCount++;
            }
        }

        Debug.Log($"自动注入物品列表完成：成功 {successCount} 个，失败 {failCount} 个");
    }

    // 重载方法：支持统一数量
    [Button("自动注入物品列表(统一数量)")]
    [LabelText("自动注入物品列表(统一数量)")]
    public void AutoInjectItemDataList(
        [LabelText("物品预制体列表")] List<GameObject> prefabList,
        [LabelText("统一数量")][MinValue(1)] int uniformCount = 1)
    {
        if (prefabList == null)
        {
            Debug.LogError("自动注入失败：Prefab列表不能为空");
            return;
        }

        // 创建统一数量列表
        List<int> countList = new List<int>();
        for (int i = 0; i < prefabList.Count; i++)
        {
            countList.Add(uniformCount);
        }

        AutoInjectItemDataList(prefabList, countList);
    }

    #endregion

    #region 保存
    public virtual void Save()
    {

    }

    #endregion
}
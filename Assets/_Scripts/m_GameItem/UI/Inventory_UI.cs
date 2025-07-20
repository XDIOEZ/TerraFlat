using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NaughtyAttributes;
using UltEvents;

// 库存UI类，用于管理库存的用户界面
public class Inventory_UI : MonoBehaviour
{
    #region 字段
    [Tooltip("引用的背包对象")]
    public Inventory inventory;


    [Tooltip("物品插槽UI预制体")]
    public GameObject itemSlot_UI_prefab;

    [Tooltip("待传递物品槽选择器")]
    public SelectSlot TargetSendItemSlot = null;
    #endregion

/*    #region 属性
    [Tooltip("背包UI变化事件")]
    public UltEvent<int> OnUIChanged
    {
        get => inventory.Data.Event_RefreshUI;
        set => inventory.Data.Event_RefreshUI = value;
    }
    #endregion
*/
    #region Unity生命周期方法
    // 在脚本实例被加载时调用，进行一些初始化操作
    public void Awake()
    {
        // 如果未手动指定库存对象，则尝试从自身组件中获取
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
        }
    }
    #endregion

    #region 公共方法
    // 按钮点击可调用，用于实例化物品插槽的UI
/*    [Button]
    public void Instantiate_ItemSlotUI()
    {
        // 清空原有的物品插槽UI列表
        itemSlots_UI.Clear();

        //销毁所有子物体

        List<GameObject> childrenToDestroy = new List<GameObject>();

        foreach (Transform child in transform)
        {
            childrenToDestroy.Add(child.gameObject);
        }

        foreach (GameObject childObj in childrenToDestroy)
        {
           DestroyImmediate(childObj);
        }
  
        // 实例化新的物品插槽UI
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            // 实例化新的物品插槽UI预制体，并将其设置为当前对象的子对象
            GameObject itemSlot_UI_go = Instantiate(itemSlot_UI_prefab, transform);
            // 将新实例化的物品插槽UI组件添加到列表中
            ItemSlot_UI itemSlot_UI = itemSlot_UI_go.GetComponent<ItemSlot_UI>();

            itemSlots_UI.Add(itemSlot_UI);

            inventory.Data.itemSlots[i].Index = i;

            if(inventory.Data.itemSlots[i].SlotMaxVolume == 0|| inventory.Data.itemSlots[i].SlotMaxVolume == 128)
            inventory.Data.itemSlots[i].SlotMaxVolume = 100;

            itemSlot_UI.ItemSlot = inventory.Data.itemSlots[i];

            itemSlot_UI.ItemSlot.Index = i;

            if (itemSlot_UI.OnItemClick == null)
            {
                itemSlot_UI.OnItemClick = new UltEvent<int>();
            }
            // 清除旧的点击事件监听器
            itemSlot_UI.OnItemClick.Clear();
            // 添加新的点击事件监听器
            itemSlot_UI.OnItemClick += OnItemSlotClicked;

            itemSlot_UI.OnItemClick += (int index) =>
            {
                Debug.Log("点击事件");
            };

            itemSlot_UI.RefreshUI();
        }
    }*/

/*    public void AddListenersToItemSlots()
    {

        foreach (ItemSlot_UI itemSlot_UI in itemSlots_UI)
        {
            if (itemSlot_UI.OnItemClick == null)
            {
                itemSlot_UI.OnItemClick = new UltEvent<int>();
            }
            // 清除旧的点击事件监听器
            itemSlot_UI.OnItemClick.Clear();
            // 添加新的点击事件监听器
            itemSlot_UI.OnItemClick += OnItemSlotClicked;

            itemSlot_UI.OnItemClick += (int index) =>
            {
                Debug.Log("点击事件");
            };
        }
    }*/


/*
    // 初始化并刷新所有库存UI的方法
    [Button("刷新所有库存UI")]
    public void RefreshAllInventoryUI()
    {
        for (int i = 0; i < itemSlots_UI.Count; i++)
        {
            // 将UI的背后数据切换引用为真实背包数据
            UpdateDataFormInventory(i);
            // 触发UI变化事件以刷新对应索引的UI
            OnUIChanged.Invoke(i);
        }
    }

    void UpdateDataFormInventory(int i)
    {
        //如果索引超出范围，则将其设置为最后一个插槽的索引
        if (i >= inventory.Data.itemSlots.Count)
        {
            i = inventory.Data.itemSlots.Count - 1;
        }

        itemSlots_UI[i].ItemSlot = inventory.Data.itemSlots[i];
    }

    // 刷新指定索引的物品插槽UI
    public void RefreshSlotUI(int index)
    {
        UpdateDataFormInventory(index);

        // 如果索引超出范围，则将其设置为最后一个插槽的索引
        if (index >= itemSlots_UI.Count)
        {
            index = itemSlots_UI.Count - 1;
        }
        // 调用对应索引的物品插槽UI的刷新方法
        itemSlots_UI[index].RefreshUI();
    }*/

    #endregion

    #region 私有方法
    // 处理物品插槽点击事件
    private void OnItemSlotClicked(int _index_)
    {
        int LocalIndex = _index_;
        int InputIndex = _index_;

        if (TargetSendItemSlot.HandInventoryUI.inventory.Data.Name == "手部插槽")
        {
            InputIndex = 0;
        }

        // 刷新手上插槽的UI
      //  TargetSendItemSlot.HandInventoryUI.OnUIChanged.Invoke(InputIndex);
    }
    #endregion
}

/*using Force.DeepCloner;
using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

// 库存类，继承自 MonoBehaviour
[System.Serializable]
public class Inventory : MonoBehaviour
{
    #region 字段
    [Tooltip("Data From")]
    [ShowNonSerializedField]
    IInventoryData _inventoryData;
    [Tooltip("所属对象")]
    public Item _belongItem;
    [Tooltip("对应 UI 管理器")]
    public Inventory_UI _ui;
    [Tooltip("序列化保存的容器数据")]
    [ShowInInspector]
    public Inventory_Data Data;
    [Tooltip("最小堆叠容量数量")]
    public int MinStackVolume = 2;
    [Tooltip("当物品槽发生变化时触发的事件")]
    public UltEvent<int, ItemSlot> onSlotChanged = new UltEvent<int, ItemSlot>();
    [Tooltip("当背包发生变化时触发的事件")]
    public UltEvent<int> onUIChanged = new UltEvent<int>();
    #endregion

    #region 属性
    // 标识插槽是否已经全部满了
    [ShowNativeProperty]
    public bool Inventory_Slots_All_IsFull
    {
        get
        {
            foreach (var slot in Data.itemSlots)
            {
                if (!slot.IsFull)
                {
                    return false;
                }
            }
            return true;
        }
    }
    public IInventoryData Belong_Inventory
    {
        get
        {
            return _inventoryData;
        }
        set
        {
            _inventoryData = value;
        }
    }
    public Inventory_UI UI
    {
        get
        {
            if (_ui == null)
            {
                _ui = GetComponent<Inventory_UI>();
            }
            return _ui;
        }
    }
    #endregion

    #region 生命周期

    public void Awake()
    {
        onSlotChanged += ChangeItemData_Default;//注册默认交互事件
        onSlotChanged += (int index, ItemSlot itemSlot) => { onUIChanged.Invoke(index); };//注册UI变化事件                                                                          // 订阅库存UI变化事件，当库存UI变化时调用刷新UI方法
        onUIChanged += UI.RefreshSlotUI;

        if (Data.inventoryName == "")
            Data.inventoryName = gameObject.name;
    }


    #endregion

    #region 增删改
    public bool AddItem(ItemData inputItemData, int index = -1)
    {
        // 物品为空或无法添加时直接返回
        if (!CanAddTheItem(inputItemData))
        {
            // Debug.Log("无法添加物品：物品为空或背包已满");
            return false;
        }

        int stackIndex = -1; // 可堆叠的槽位索引
        int emptyIndex = -1; // 空槽位索引

        // 检查是否可以进行堆叠（仅当物品体积小于最小可堆叠体积时允许）
        if (inputItemData.Stack.Volume < MinStackVolume)
        {
            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                var slot = Data.itemSlots[i];
                if (!slot.IsFull && slot._ItemData != null &&
                    slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                    slot._ItemData.IDName == inputItemData.IDName &&
                    slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
                {
                    stackIndex = i;
                    break;
                }
            }
        }


        // 如果无法堆叠，则寻找空槽位
        if (stackIndex == -1)
        {
            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                if (Data.itemSlots[i]._ItemData == null)
                {
                    emptyIndex = i;
                    break;
                }
            }
        }

        // 进行物品添加操作
        if (stackIndex != -1)
        {
            // 堆叠物品
            //  Debug.Log("堆叠物品到槽位: " + stackIndex);
            ChangeItemDataAmount(stackIndex, inputItemData.Stack.Amount);
        }
        else if (emptyIndex != -1)
        {
            // 放入新物品
            // Debug.Log("放入物品到空槽位: " + emptyIndex);
            SetOne_ItemData(emptyIndex, inputItemData);
        }
        else
        {
            Debug.LogError("背包已满，无法添加物品");
            return false;
        }
        UI.RefreshAllInventoryUI();
        inputItemData.Stack.CanBePickedUp = false;
        return true;
    }

    public bool CanAddTheItem(ItemData inputItemData)
    {
        if (inputItemData == null)
        {
            // Debug.Log("物品为空，无法添加");
            return false;
        }

        // 如果物品体积大于等于最小可堆叠体积，则只能放入空槽位
        if (inputItemData.Stack.Volume >= MinStackVolume)
        {
            foreach (var slot in Data.itemSlots)
            {
                if (slot._ItemData == null)
                {
                    return true;
                }
            }
            Debug.Log("背包已满，无法添加大体积物品");
            return false;
        }

        // 查找可堆叠的位置
        foreach (var slot in Data.itemSlots)
        {
            if (!slot.IsFull && slot._ItemData != null &&
                slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                slot._ItemData.IDName == inputItemData.IDName &&
                slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
            {
                //  Debug.Log("找到可堆叠的槽位");
                return true;
            }
        }

        // 如果不能堆叠，则寻找空槽位
        foreach (var slot in Data.itemSlots)
        {
            if (slot._ItemData == null)
            {
                // Debug.Log("找到空槽位");
                return true;
            }
        }

        Debug.LogError("背包已满，无法添加物品");
        return false;
    }
    // 移除物品
    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        onUIChanged.Invoke(index);
        itemSlot._ItemData = null;
    }

    //添加一个新方法 用于减少特定数量的物品
    public bool RemoveItemAmount(ItemSlot itemSlot, int amount)
    {
        if (itemSlot._ItemData == null)
        {
            Debug.Log("物品槽位为空，无法减少");
            return false;
        }

        if (itemSlot._ItemData.Stack.Amount < amount)
        {
            Debug.Log("物品槽位数量不足，无法减少");
            return false;
        }

        itemSlot._ItemData.Stack.Amount -= amount;
        onUIChanged.Invoke(itemSlot.Index);
        return true;
    }
    // 默认的物品数据交换方法
    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float ChangeReate = 1;
        if (inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>() != null)
        {
            ChangeReate = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>().GetItemAmountRate;
        }
        ItemData localData = Data.itemSlots[index]._ItemData;

        ItemData inputDataHand = inputSlotHand._ItemData;

        ItemSlot localSlot = Data.itemSlots[index];





        // 两者为空
        if (inputDataHand == null && localData == null)
        {
            return;
        }

        // 手上有物体，本地无物体
        if (inputDataHand != null && localData == null)
        {
            int changeAmount = (int)Mathf.Ceil(inputDataHand.Stack.Amount * ChangeReate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // 本地有物体，手上没物体
        if (localData != null && inputDataHand == null)
        {
            int changeAmount = (int)Mathf.Ceil(localData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // 特殊交换
        if (localSlot._ItemData.Stack.Volume > MinStackVolume || localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            Debug.Log("特殊交换");
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            return;
        }

        // 物品相同
        if (inputSlotHand._ItemData.IDName == localSlot._ItemData.IDName)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // 两者不为空且物品不相同
        if (inputDataHand != null && localData != null && localData.IDName != inputDataHand.IDName)
        {
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            Debug.Log("(物品不同)交换物品槽位:" + index + " 物品:" + inputSlotHand._ItemData.IDName);
            return;
        }
    }
    // 修改物品数量
    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        int changeCount = 0;

        if (inputSlotHand._ItemData == null)
        {
            ItemData tempItemData = localSlot._ItemData.DeepClone();
            tempItemData.Stack.Amount = 1;
            inputSlotHand._ItemData = tempItemData;
            inputSlotHand._ItemData.Stack.Amount = 0;
        }

        if (localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            return false;
        }

        while (true)
        {
            localSlot._ItemData.Stack.Amount--;
            changeCount++;
            inputSlotHand._ItemData.Stack.Amount++;

            // 如果本地槽位容量已经满了，跳出循环
            if (changeCount >= count || localSlot._ItemData.Stack.Amount <= 0 || inputSlotHand._ItemData.Stack.Amount >= inputSlotHand.SlotMaxVolume)
            {
                if (localSlot._ItemData.Stack.Amount <= 0)
                {
                    localSlot.ResetData();
                }
                return true;
            }
        }
    }

    #endregion

    #region 插槽物品查询 和 设置
    // 设置单个物品
    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        Data.itemSlots[index]._ItemData = inputItemData;
    }
    //修改对应插槽的数量
    public void ChangeItemDataAmount(int index, float amount)
    {
        Data.itemSlots[index]._ItemData.Stack.Amount += amount;
    }
    // 获取指定索引的物品槽
    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= Data.itemSlots.Count)
        {
            return Data.itemSlots[0];
        }
        return Data.itemSlots[index];
    }

    public void SyncSlotBelongInventory()
    {
        foreach (var slot in Data.itemSlots)
        {
            slot.Belong_Inventory = this;
        }
    }

    // 获取指定索引的物品数据
    public ItemData GetItemData(int index)
    {
        return GetItemSlot(index)._ItemData;
    }
}*/
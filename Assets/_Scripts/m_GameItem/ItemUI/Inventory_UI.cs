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

    [Tooltip("物品插槽UI列表")]
    public List<ItemSlot_UI> itemSlots_UI = new List<ItemSlot_UI>();

    [Tooltip("物品插槽UI预制体")]
    public GameObject itemSlot_UI_prefab;

    [Tooltip("待传递物品槽选择器")]
    public SelectSlot TargetSendItemSlot = null;
    #endregion

    #region 属性
    [Tooltip("背包UI变化事件")]
    public UltEvent<int> OnUIChanged
    {
        get => inventory.onUIChanged;
        set => inventory.onUIChanged = value;
    }
    #endregion

    #region Unity生命周期方法
    // 在脚本实例被加载时调用，进行一些初始化操作
    public void Awake()
    {
        inventory.Data.inventoryName = gameObject.name;
        // 如果未手动指定库存对象，则尝试从自身组件中获取
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
        }
        // 重新实例化所有物品插槽的UI
        //InstantiateItemSlots();
    }

    // 当脚本销毁时调用，用于取消订阅事件以避免内存泄漏
    private void OnDestroy()
    {
        // 取消订阅库存UI变化事件
        OnUIChanged -= RefreshSlotUI;
    }
    #endregion

    #region 公共方法
    // 按钮点击可调用，用于实例化物品插槽的UI
    [Button]
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

            if (itemSlot_UI.onItemClick == null)
            {
                itemSlot_UI.onItemClick = new UltEvent<int>();
            }
            // 清除旧的点击事件监听器
            itemSlot_UI.onItemClick.Clear();
            // 添加新的点击事件监听器
            itemSlot_UI.onItemClick += OnItemSlotClicked;

            itemSlot_UI.onItemClick += (int index) =>
            {
                Debug.Log("点击事件");
            };

            itemSlot_UI.RefreshUI();
        }
    }

    public void AddListenersToItemSlots()
    {

        foreach (ItemSlot_UI itemSlot_UI in itemSlots_UI)
        {
            if (itemSlot_UI.onItemClick == null)
            {
                itemSlot_UI.onItemClick = new UltEvent<int>();
            }
            // 清除旧的点击事件监听器
            itemSlot_UI.onItemClick.Clear();
            // 添加新的点击事件监听器
            itemSlot_UI.onItemClick += OnItemSlotClicked;

            itemSlot_UI.onItemClick += (int index) =>
            {
                Debug.Log("点击事件");
            };
        }
    }



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
    }

    #endregion

    #region 私有方法
    // 处理物品插槽点击事件
    private void OnItemSlotClicked(int _index_)
    {
        int LocalIndex = _index_;
        int InputIndex = _index_;

        if (TargetSendItemSlot.HandInventoryUI.inventory.Data.inventoryName == "手部插槽")
        {
            InputIndex = 0;
        }

        // 触发库存的插槽变化事件
        inventory.onSlotChanged?.Invoke(LocalIndex, TargetSendItemSlot.HandInventoryUI.inventory.GetItemSlot(InputIndex));

        // 刷新手上插槽的UI
        TargetSendItemSlot.HandInventoryUI.OnUIChanged.Invoke(InputIndex);
    }
    #endregion
}

using UnityEngine;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public class Inventory : MonoBehaviour
{
    //容器所属对象
    public Item Owner;
    //插槽接口预制体
    public GameObject ItemSlot_Prefab;
    //生成接口的父对象
    public Transform ItemSlot_Parent;
    //数据
    public Inventory_Data Data;
    //UI列表
    public List<ItemSlot_UI> itemSlotUIs;
    //负责交互的Inventory
    public Inventory DefaultTarget_Inventory;

    public virtual void Awake()
    {
        // 若未命名，则赋默认名
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = gameObject.name;

        // 未设置Prefab 自动调用
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

        // 若未设置父对象，则默认为第一个子对象
        if (ItemSlot_Parent == null)
            ItemSlot_Parent = transform.GetChild(0);
    }

    //同步UI的Data
    public void SyncData()
    {
        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
           ItemSlot_UI itemSlotUI = itemSlotUIs[i];
            itemSlotUI.Data = Data.itemSlots[i];
            itemSlotUI.OnLeftClick += OnClick;
            itemSlotUI._OnScroll += OnScroll;
            itemSlotUI.Data.Belong_Inventory = this;
        }
    }

    public virtual void Init()
    {
        // 若未设置数据，则自动生成
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            Data.itemSlots[i].Index = i;
            Data.itemSlots[i].SlotMaxVolume = 100;
        }

        // 同步子对象数量与 itemSlots 数量一致
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

        // 删除多余的子对象（从后往前删除更安全）
        for (int i = currentCount - 1; i >= targetCount; i--)
        {
            DestroyImmediate(ItemSlot_Parent.GetChild(i).gameObject); // 或 Destroy() 视情况
        }

        // 添加缺少的子对象
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false); // false: 保持局部坐标
                                                                                    //  item.name = $"Slot_{i}"; // 可选：命名方便调试
        }

        // 清空旧列表，重新填充 UI 槽位列表
        itemSlotUIs.Clear();
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            var ui = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (ui != null)
                itemSlotUIs.Add(ui);
        }

        // 同步 UI 数据
        SyncData();
        // 注册刷新UI事件
        Data.Event_RefreshUI += RefreshUI;
        //初始化刷新UI
        RefreshUI();
    }

    public void RefreshUI(int index)
    {
       // print("同步UI数据"+ index);
        itemSlotUIs[index].RefreshUI();
    }
    public void RefreshUI()
    {
        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
            itemSlotUIs[i].RefreshUI();
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnScroll(int index, float direction)
    {
        if(direction > 0)
        {
            Data.TransferItemQuantity(DefaultTarget_Inventory.Data.itemSlots[0], Data.itemSlots[index], 1);
        } else if(direction < 0)
        {
            Data.TransferItemQuantity(Data.itemSlots[index], DefaultTarget_Inventory.Data.itemSlots[0], 1);
        }
       
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void OnClick(int index)
    {
        ItemSlot slot = Data.GetItemSlot(index);

        //默认为交换
        if(DefaultTarget_Inventory.Data.itemSlots.Count > index)
        {
            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[index]);


            DefaultTarget_Inventory.RefreshUI(index);
        }
        else
        {
            Data.ChangeItemData_Default(index, DefaultTarget_Inventory.Data.itemSlots[0]);


            DefaultTarget_Inventory.RefreshUI(0);

        }

        RefreshUI(index);
        
    }

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

    public void OnDestroy()
    {
       Data. Event_RefreshUI -= RefreshUI;
    }
}
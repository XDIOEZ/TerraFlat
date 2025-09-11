
using UnityEngine;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public class Inventory : MonoBehaviour
{
    //������������
    public Item Owner;
    //��۽ӿ�Ԥ����
    public GameObject ItemSlot_Prefab;
    //���ɽӿڵĸ�����
    public Transform ItemSlot_Parent;
    //����
    public Inventory_Data Data;
    //UI�б�
    public List<ItemSlot_UI> itemSlotUIs;
    //���𽻻���Inventory
    public Inventory DefaultTarget_Inventory;

    public virtual void Awake()
    {
        // ��δ��������Ĭ����
        if (string.IsNullOrEmpty(Data.Name))
            Data.Name = gameObject.name;

        // δ����Prefab �Զ�����
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

        // ��δ���ø�������Ĭ��Ϊ��һ���Ӷ���
        if (ItemSlot_Parent == null)
            ItemSlot_Parent = transform.GetChild(0);
    }

    //ͬ��UI��Data
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
        // ��δ�������ݣ����Զ�����
        for (int i = 0; i < Data.itemSlots.Count; i++)
        {
            Data.itemSlots[i].Index = i;
            Data.itemSlots[i].SlotMaxVolume = 100;
        }

        // ͬ���Ӷ��������� itemSlots ����һ��
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;
        ItemSlot_Prefab = GameRes.Instance.GetPrefab("Slot_UI");

        // ɾ��������Ӷ��󣨴Ӻ���ǰɾ������ȫ��
        for (int i = currentCount - 1; i >= targetCount; i--)
        {
            DestroyImmediate(ItemSlot_Parent.GetChild(i).gameObject); // �� Destroy() �����
        }

        // ���ȱ�ٵ��Ӷ���
        for (int i = currentCount; i < targetCount; i++)
        {
            GameObject item = Instantiate(ItemSlot_Prefab, ItemSlot_Parent, false); // false: ���־ֲ�����
                                                                                    //  item.name = $"Slot_{i}"; // ��ѡ�������������
        }

        // ��վ��б�������� UI ��λ�б�
        itemSlotUIs.Clear();
        for (int i = 0; i < ItemSlot_Parent.childCount; i++)
        {
            var ui = ItemSlot_Parent.GetChild(i).GetComponent<ItemSlot_UI>();
            if (ui != null)
                itemSlotUIs.Add(ui);
        }

        // ͬ�� UI ����
        SyncData();
        // ע��ˢ��UI�¼�
        Data.Event_RefreshUI += RefreshUI;
        //��ʼ��ˢ��UI
        RefreshUI();
    }

    public void RefreshUI(int index)
    {
       // print("ͬ��UI����"+ index);
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

        //Ĭ��Ϊ����
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
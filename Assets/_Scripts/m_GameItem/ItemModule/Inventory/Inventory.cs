
using UnityEngine;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System.Collections.Generic;

public class Inventory : MonoBehaviour
{
    public IInventoryData DataInterface;

    public Item Belong_Item;

    public GameObject ItemSlot_Prefab;

    public Transform ItemSlot_Parent;

    public Inventory_Data Data;

    public List<ItemSlot_UI> itemSlotUIs;

    public Inventory DefaultTarget_Inventory;

    public bool UI_IsShow = true;


    public void Awake()
    {
        // ��δ��������Ĭ����
        if (string.IsNullOrEmpty(Data.inventoryName))
            Data.inventoryName = gameObject.name;

        Belong_Item = GetComponentInParent<Item>();
        // ��ʼ�����ݽӿ�
        DataInterface = GetComponentInParent<IInventoryData>();

        DataInterface.Children_Inventory_GameObject[Data.inventoryName] = this;
    }
    public void Start()
    {
        if(ItemSlot_Parent == null)
            ItemSlot_Parent = transform.GetChild(0);

        // ���Ի�ȡ�������ݣ�����ֵĬ������
        if (!DataInterface.InventoryData_Dict.TryGetValue(gameObject.name, out var existingData) || existingData == null)
        {
            DataInterface.InventoryData_Dict[gameObject.name] = Data;
          //  DataInterface.Children_Inventory_GameObject[gameObject.name] = this;
            //����Data ��������
            for (int i = 0; i < Data.itemSlots.Count; i++)
            {
                Data.itemSlots[i].Index = i;
                Data.itemSlots[i].SlotMaxVolume = 100;
            }
        }
        else
        {
            Data = existingData;
        }

        

        // ͬ���Ӷ��������� itemSlots ����һ��
        int currentCount = ItemSlot_Parent.childCount;
        int targetCount = Data.itemSlots.Count;

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

        

        

        if (DefaultTarget_Inventory == null)
        {
            DefaultTarget_Inventory = DataInterface.Children_Inventory_GameObject["�ֲ����"];
        }

        Data.Event_RefreshUI += RefreshUI;
    }

    //ͬ��UI��Data
    public void SyncData()
    {
        for (int i = 0; i < itemSlotUIs.Count; i++)
        {
           ItemSlot_UI itemSlotUI = itemSlotUIs[i];
            itemSlotUI.ItemSlot = Data.itemSlots[i];
            itemSlotUI.OnItemClick += OnItemClick;
            itemSlotUI.ItemSlot.Belong_Inventory = this;
        }
    }

    public void RefreshUI(int index)
    {
        print("ͬ��UI����"+ index);
        itemSlotUIs[index].RefreshUI();
    }

    public virtual void OnItemClick(int index)
    {
        Debug.Log("�����" + index);
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

}
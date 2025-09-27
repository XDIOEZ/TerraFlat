using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NaughtyAttributes;
using UltEvents;

// ���UI�࣬���ڹ�������û�����
public class Inventory_UI : MonoBehaviour
{
    #region �ֶ�
    [Tooltip("���õı�������")]
    public Inventory inventory;


    [Tooltip("��Ʒ���UIԤ����")]
    public GameObject itemSlot_UI_prefab;

    [Tooltip("��������Ʒ��ѡ����")]
    public SelectSlot TargetSendItemSlot = null;
    #endregion

/*    #region ����
    [Tooltip("����UI�仯�¼�")]
    public UltEvent<int> OnUIChanged
    {
        get => inventory.Data.Event_RefreshUI;
        set => inventory.Data.Event_RefreshUI = value;
    }
    #endregion
*/
    #region Unity�������ڷ���
    // �ڽű�ʵ��������ʱ���ã�����һЩ��ʼ������
    public void Awake()
    {
        // ���δ�ֶ�ָ�����������Դ���������л�ȡ
        if (inventory == null)
        {
            inventory = GetComponent<Inventory>();
        }
    }
    #endregion

    #region ��������
    // ��ť����ɵ��ã�����ʵ������Ʒ��۵�UI
/*    [Button]
    public void Instantiate_ItemSlotUI()
    {
        // ���ԭ�е���Ʒ���UI�б�
        itemSlots_UI.Clear();

        //��������������

        List<GameObject> childrenToDestroy = new List<GameObject>();

        foreach (Transform child in transform)
        {
            childrenToDestroy.Add(child.gameObject);
        }

        foreach (GameObject childObj in childrenToDestroy)
        {
           DestroyImmediate(childObj);
        }
  
        // ʵ�����µ���Ʒ���UI
        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            // ʵ�����µ���Ʒ���UIԤ���壬����������Ϊ��ǰ������Ӷ���
            GameObject itemSlot_UI_go = Instantiate(itemSlot_UI_prefab, transform);
            // ����ʵ��������Ʒ���UI�����ӵ��б���
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
            // ����ɵĵ���¼�������
            itemSlot_UI.OnItemClick.Clear();
            // ����µĵ���¼�������
            itemSlot_UI.OnItemClick += OnItemSlotClicked;

            itemSlot_UI.OnItemClick += (int index) =>
            {
                Debug.Log("����¼�");
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
            // ����ɵĵ���¼�������
            itemSlot_UI.OnItemClick.Clear();
            // ����µĵ���¼�������
            itemSlot_UI.OnItemClick += OnItemSlotClicked;

            itemSlot_UI.OnItemClick += (int index) =>
            {
                Debug.Log("����¼�");
            };
        }
    }*/


/*
    // ��ʼ����ˢ�����п��UI�ķ���
    [Button("ˢ�����п��UI")]
    public void RefreshAllInventoryUI()
    {
        for (int i = 0; i < itemSlots_UI.Count; i++)
        {
            // ��UI�ı��������л�����Ϊ��ʵ��������
            UpdateDataFormInventory(i);
            // ����UI�仯�¼���ˢ�¶�Ӧ������UI
            OnUIChanged.Invoke(i);
        }
    }

    void UpdateDataFormInventory(int i)
    {
        //�������������Χ����������Ϊ���һ����۵�����
        if (i >= inventory.Data.itemSlots.Count)
        {
            i = inventory.Data.itemSlots.Count - 1;
        }

        itemSlots_UI[i].ItemSlot = inventory.Data.itemSlots[i];
    }

    // ˢ��ָ����������Ʒ���UI
    public void RefreshSlotUI(int index)
    {
        UpdateDataFormInventory(index);

        // �������������Χ����������Ϊ���һ����۵�����
        if (index >= itemSlots_UI.Count)
        {
            index = itemSlots_UI.Count - 1;
        }
        // ���ö�Ӧ��������Ʒ���UI��ˢ�·���
        itemSlots_UI[index].RefreshUI();
    }*/

    #endregion

    #region ˽�з���
    // ������Ʒ��۵���¼�
    private void OnItemSlotClicked(int _index_)
    {
        int LocalIndex = _index_;
        int InputIndex = _index_;

        if (TargetSendItemSlot.HandInventoryUI.inventory.Data.Name == "�ֲ����")
        {
            InputIndex = 0;
        }

        // ˢ�����ϲ�۵�UI
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

// ����࣬�̳��� MonoBehaviour
[System.Serializable]
public class Inventory : MonoBehaviour
{
    #region �ֶ�
    [Tooltip("Data From")]
    [ShowNonSerializedField]
    IInventoryData _inventoryData;
    [Tooltip("��������")]
    public Item _belongItem;
    [Tooltip("��Ӧ UI ������")]
    public Inventory_UI _ui;
    [Tooltip("���л��������������")]
    [ShowInInspector]
    public Inventory_Data Data;
    [Tooltip("��С�ѵ���������")]
    public int MinStackVolume = 2;
    [Tooltip("����Ʒ�۷����仯ʱ�������¼�")]
    public UltEvent<int, ItemSlot> onSlotChanged = new UltEvent<int, ItemSlot>();
    [Tooltip("�����������仯ʱ�������¼�")]
    public UltEvent<int> onUIChanged = new UltEvent<int>();
    #endregion

    #region ����
    // ��ʶ����Ƿ��Ѿ�ȫ������
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

    #region ��������

    public void Awake()
    {
        onSlotChanged += ChangeItemData_Default;//ע��Ĭ�Ͻ����¼�
        onSlotChanged += (int index, ItemSlot itemSlot) => { onUIChanged.Invoke(index); };//ע��UI�仯�¼�                                                                          // ���Ŀ��UI�仯�¼��������UI�仯ʱ����ˢ��UI����
        onUIChanged += UI.RefreshSlotUI;

        if (Data.inventoryName == "")
            Data.inventoryName = gameObject.name;
    }


    #endregion

    #region ��ɾ��
    public bool AddItem(ItemData inputItemData, int index = -1)
    {
        // ��ƷΪ�ջ��޷����ʱֱ�ӷ���
        if (!CanAddTheItem(inputItemData))
        {
            // Debug.Log("�޷������Ʒ����ƷΪ�ջ򱳰�����");
            return false;
        }

        int stackIndex = -1; // �ɶѵ��Ĳ�λ����
        int emptyIndex = -1; // �ղ�λ����

        // ����Ƿ���Խ��жѵ���������Ʒ���С����С�ɶѵ����ʱ����
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


        // ����޷��ѵ�����Ѱ�ҿղ�λ
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

        // ������Ʒ��Ӳ���
        if (stackIndex != -1)
        {
            // �ѵ���Ʒ
            //  Debug.Log("�ѵ���Ʒ����λ: " + stackIndex);
            ChangeItemDataAmount(stackIndex, inputItemData.Stack.Amount);
        }
        else if (emptyIndex != -1)
        {
            // ��������Ʒ
            // Debug.Log("������Ʒ���ղ�λ: " + emptyIndex);
            SetOne_ItemData(emptyIndex, inputItemData);
        }
        else
        {
            Debug.LogError("�����������޷������Ʒ");
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
            // Debug.Log("��ƷΪ�գ��޷����");
            return false;
        }

        // �����Ʒ������ڵ�����С�ɶѵ��������ֻ�ܷ���ղ�λ
        if (inputItemData.Stack.Volume >= MinStackVolume)
        {
            foreach (var slot in Data.itemSlots)
            {
                if (slot._ItemData == null)
                {
                    return true;
                }
            }
            Debug.Log("�����������޷���Ӵ������Ʒ");
            return false;
        }

        // ���ҿɶѵ���λ��
        foreach (var slot in Data.itemSlots)
        {
            if (!slot.IsFull && slot._ItemData != null &&
                slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData &&
                slot._ItemData.IDName == inputItemData.IDName &&
                slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
            {
                //  Debug.Log("�ҵ��ɶѵ��Ĳ�λ");
                return true;
            }
        }

        // ������ܶѵ�����Ѱ�ҿղ�λ
        foreach (var slot in Data.itemSlots)
        {
            if (slot._ItemData == null)
            {
                // Debug.Log("�ҵ��ղ�λ");
                return true;
            }
        }

        Debug.LogError("�����������޷������Ʒ");
        return false;
    }
    // �Ƴ���Ʒ
    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        onUIChanged.Invoke(index);
        itemSlot._ItemData = null;
    }

    //���һ���·��� ���ڼ����ض���������Ʒ
    public bool RemoveItemAmount(ItemSlot itemSlot, int amount)
    {
        if (itemSlot._ItemData == null)
        {
            Debug.Log("��Ʒ��λΪ�գ��޷�����");
            return false;
        }

        if (itemSlot._ItemData.Stack.Amount < amount)
        {
            Debug.Log("��Ʒ��λ�������㣬�޷�����");
            return false;
        }

        itemSlot._ItemData.Stack.Amount -= amount;
        onUIChanged.Invoke(itemSlot.Index);
        return true;
    }
    // Ĭ�ϵ���Ʒ���ݽ�������
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





        // ����Ϊ��
        if (inputDataHand == null && localData == null)
        {
            return;
        }

        // ���������壬����������
        if (inputDataHand != null && localData == null)
        {
            int changeAmount = (int)Mathf.Ceil(inputDataHand.Stack.Amount * ChangeReate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // ���������壬����û����
        if (localData != null && inputDataHand == null)
        {
            int changeAmount = (int)Mathf.Ceil(localData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // ���⽻��
        if (localSlot._ItemData.Stack.Volume > MinStackVolume || localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            Debug.Log("���⽻��");
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            return;
        }

        // ��Ʒ��ͬ
        if (inputSlotHand._ItemData.IDName == localSlot._ItemData.IDName)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // ���߲�Ϊ������Ʒ����ͬ
        if (inputDataHand != null && localData != null && localData.IDName != inputDataHand.IDName)
        {
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            Debug.Log("(��Ʒ��ͬ)������Ʒ��λ:" + index + " ��Ʒ:" + inputSlotHand._ItemData.IDName);
            return;
        }
    }
    // �޸���Ʒ����
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

            // ������ز�λ�����Ѿ����ˣ�����ѭ��
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

    #region �����Ʒ��ѯ �� ����
    // ���õ�����Ʒ
    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        Data.itemSlots[index]._ItemData = inputItemData;
    }
    //�޸Ķ�Ӧ��۵�����
    public void ChangeItemDataAmount(int index, float amount)
    {
        Data.itemSlots[index]._ItemData.Stack.Amount += amount;
    }
    // ��ȡָ����������Ʒ��
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

    // ��ȡָ����������Ʒ����
    public ItemData GetItemData(int index)
    {
        return GetItemSlot(index)._ItemData;
    }
}*/
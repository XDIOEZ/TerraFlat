using Force.DeepCloner;
using MemoryPack;
using NaughtyAttributes;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

// ����࣬�̳��� MonoBehaviour
[System.Serializable]
public class Inventory : MonoBehaviour
{
    #region �ֶ�
    [Tooltip("��Ʒ��������")]
    [ShowNonSerializedField]
    public IInventoryData belong_Item;
    [Tooltip("��Ӧ UI ������")]
    public Inventory_UI _ui;
    [Tooltip("���л��������������")]
    public Inventory_Data Data = new Inventory_Data();
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
    public IInventoryData Belong_Item
    {
        get
        {
            return belong_Item;
        }
        set
        {
            belong_Item = value;
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
                    slot._ItemData.PrefabPath == inputItemData.PrefabPath &&
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
            Debug.Log("�ѵ���Ʒ����λ: " + stackIndex);
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
                slot._ItemData.Name == inputItemData.Name &&
                slot._ItemData.Stack.CurrentVolume + inputItemData.Stack.CurrentVolume <= slot.SlotMaxVolume)
            {
                Debug.Log("�ҵ��ɶѵ��Ĳ�λ");
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
        itemSlot._ItemData = null;
        onUIChanged.Invoke(index);
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
        if (inputSlotHand._ItemData.Name == localSlot._ItemData.Name)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            onUIChanged.Invoke(index);
            return;
        }

        // ���߲�Ϊ������Ʒ����ͬ
        if (inputDataHand != null && localData != null && localData.PrefabPath != inputDataHand.PrefabPath)
        {
            localSlot.Change(inputSlotHand);
            onUIChanged.Invoke(index);
            Debug.Log("(��Ʒ��ͬ)������Ʒ��λ:" + index + " ��Ʒ:" + inputSlotHand._ItemData.PrefabPath);
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
}
    #endregion
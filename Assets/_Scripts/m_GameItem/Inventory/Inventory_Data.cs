using Force.DeepCloner;
using MemoryPack;
using System;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

[Serializable]
[MemoryPackable]
public partial class Inventory_Data
{
    // ��������
    public string inventoryName = string.Empty;
    // ������Ʒ���б�
    public List<ItemSlot> itemSlots = new List<ItemSlot>();
    // ��ǰѡ��Ĳ������
    public int selectedSlotIndex = 0;

    public bool IsFull
    {
        get
        {
            foreach (var slot in itemSlots)
            {
                if (slot._ItemData == null) 
                {
                    return false;
                }
            }
            return true;
        }
    }

    // ����Ʒ�۷����仯ʱ�������¼�
    [MemoryPackIgnore]
    public UltEvent<int, ItemSlot> onSlotChanged = new UltEvent<int, ItemSlot>();
    // ������ UI �����仯ʱ�������¼�
    [MemoryPackIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>();
    // ���캯��
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string inventoryName)
    {
        this.itemSlots = itemSlots;
        this.inventoryName = inventoryName;
    }

    // �հ׹��캯��
    public Inventory_Data() { }

    #region ��������
    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        Event_RefreshUI.Invoke(index);
        itemSlot._ItemData = null;
    }

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float ChangeReate = 1;
        if (inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>() != null)
        {
            ChangeReate = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>().GetItemAmountRate;
        }
        ItemData localData = itemSlots[index]._ItemData;

        ItemData inputDataHand = inputSlotHand._ItemData;

        ItemSlot localSlot = itemSlots[index];

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
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���������壬����û����
        if (localData != null && inputDataHand == null)
        {
            int changeAmount = (int)Mathf.Ceil(localData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���⽻��
        if (localSlot._ItemData.Stack.Volume ==1 || localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
        {
            Debug.Log("���⽻��");
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ��Ʒ��ͬ
        if (inputSlotHand._ItemData.IDName == localSlot._ItemData.IDName)
        {
            int changeAmount = (int)Mathf.Ceil(localSlot._ItemData.Stack.Amount * ChangeReate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���߲�Ϊ������Ʒ����ͬ
        if (inputDataHand != null && localData != null && localData.IDName != inputDataHand.IDName)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Debug.Log("(��Ʒ��ͬ)������Ʒ��λ:" + index + " ��Ʒ:" + inputSlotHand._ItemData.IDName);
            return;
        }
    }

    //�޸Ķ�Ӧ��۵�����
    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index]._ItemData.Stack.Amount += amount;
    }

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
                    localSlot.ClearData();
                }
                return true;
            }
        }
    }

    // ��ȡָ����������Ʒ��
    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
        {
            return itemSlots[0];
        }
        return itemSlots[index];
    }

    // ��ȡָ����������Ʒ����
    public ItemData GetItemData(int index)
    {
        return GetItemSlot(index)._ItemData;
    }

    public bool AddItem(ItemData inputItemData)
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
        if (inputItemData.Stack.Volume == 1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                var slot = itemSlots[i];
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
            for (int i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i]._ItemData == null)
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

        inputItemData.Stack.CanBePickedUp = false;
        // ������Ʒ��Ӳ���
        int updatedIndex = -1; // ��¼ʵ�ʸ��µĲ�λ����

        if (stackIndex != -1)
        {
            // �ѵ���Ʒ
            // Debug.Log("�ѵ���Ʒ����λ: " + stackIndex);
            ChangeItemDataAmount(stackIndex, inputItemData.Stack.Amount);
            updatedIndex = stackIndex;
        }
        else if (emptyIndex != -1)
        {
            // ��������Ʒ
            // Debug.Log("������Ʒ���ղ�λ: " + emptyIndex);
            SetOne_ItemData(emptyIndex, inputItemData);
            updatedIndex = emptyIndex;
        }
        else
        {
            Debug.LogError("�����������޷������Ʒ");
            return false;
        }

        inputItemData.Stack.CanBePickedUp = false;
        Event_RefreshUI.Invoke(updatedIndex); // ʹ��ʵ�ʸ��µĲ�λ����
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
        if (inputItemData.Stack.Volume > 1)
        {
            foreach (var slot in itemSlots)
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
        foreach (var slot in itemSlots)
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
        foreach (var slot in itemSlots)
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

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index]._ItemData = inputItemData;
    }
    #endregion
}

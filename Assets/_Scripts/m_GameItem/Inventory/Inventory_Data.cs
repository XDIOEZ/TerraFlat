using FastCloner.Code;
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
    //TODO ����Event
    public string Name = string.Empty;                      // ��������
    public List<ItemSlot> itemSlots = new List<ItemSlot>(); // ��Ʒ���б�
    public int Index = 0;                      // ��ǰѡ�в�λ����

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public UltEvent<int> Event_RefreshUI = new UltEvent<int>(); // UIˢ���¼�

    [FastClonerIgnore]
    public bool IsFull => itemSlots.TrueForAll(slot => slot._ItemData != null);

    // ���캯��
    [MemoryPackConstructor]
    public Inventory_Data(List<ItemSlot> itemSlots, string Name)
    {
        this.itemSlots = itemSlots;
        this.Name = Name;
    }

    #region ��۲����߼�

    public void RemoveItemAll(ItemSlot itemSlot, int index = 0)
    {
        itemSlot._ItemData = null;
        Event_RefreshUI.Invoke(index);
    }

    public void SetOne_ItemData(int index, ItemData inputItemData)
    {
        itemSlots[index]._ItemData = inputItemData;
    }

    public ItemSlot GetItemSlot(int index)
    {
        if (index < 0 || index >= itemSlots.Count)
            return itemSlots[0];
        return itemSlots[index];
    }

    public ItemData GetItemData(int index) => GetItemSlot(index)._ItemData;

    public void ChangeItemDataAmount(int index, float amount)
    {
        itemSlots[index]._ItemData.Stack.Amount += amount;
    }

    #endregion

    #region ���������߼�

    public void ChangeItemData_Default(int index, ItemSlot inputSlotHand)
    {
        float rate = 1f;
        if (inputSlotHand.Belong_Inventory == null)
        {
            return;
        }
        var handInventory = inputSlotHand.Belong_Inventory.GetComponent<Inventory_Hand>();
        if (handInventory != null)
            rate = handInventory.GetItemAmountRate;

        var localSlot = itemSlots[index];
        var localData = localSlot._ItemData;
        var inputData = inputSlotHand._ItemData;

        // ���1��������Ϊ��
        if (localData == null && inputData == null) return;

        // ���2���������壬���ؿ�
        if (inputData != null && localData == null)
        {
            int changeAmount = Mathf.CeilToInt(inputData.Stack.Amount * rate);
            ChangeItemAmount(inputSlotHand, localSlot, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���3���ֿգ�������
        if (inputData == null && localData != null)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���4�����⽻�����������ݲ�һ�£�
        if (inputData.ItemSpecialData != localData.ItemSpecialData)
        {
            localSlot.Change(inputSlotHand);
            Event_RefreshUI.Invoke(index);
            Debug.Log("���⽻��");
            return;
        }

        // ���5����Ʒ��ͬ���ѵ�����
        if (inputData.IDName == localData.IDName)
        {
            int changeAmount = Mathf.CeilToInt(localData.Stack.Amount * rate);
            ChangeItemAmount(localSlot, inputSlotHand, changeAmount);
            Event_RefreshUI.Invoke(index);
            return;
        }

        // ���6����Ʒ��ͬ��ֱ�ӽ���
        localSlot.Change(inputSlotHand);
        Event_RefreshUI.Invoke(index);
        Debug.Log($"(��Ʒ��ͬ)������Ʒ��λ:{index} ��Ʒ:{inputSlotHand._ItemData.IDName}");
    }

    public bool ChangeItemAmount(ItemSlot localSlot, ItemSlot inputSlotHand, int count)
    {
        if (inputSlotHand._ItemData == null)
        {
            var tempData = FastCloner.FastCloner.DeepClone(localSlot._ItemData);
            tempData.Stack.Amount = 0;
            inputSlotHand._ItemData = tempData;
        }

        if (localSlot._ItemData.ItemSpecialData != inputSlotHand._ItemData.ItemSpecialData)
            return false;

        int changed = 0;

        while (changed < count &&
               localSlot._ItemData.Stack.Amount > 0 &&
               inputSlotHand._ItemData.Stack.Amount < inputSlotHand.SlotMaxVolume)
        {
            localSlot._ItemData.Stack.Amount--;
            inputSlotHand._ItemData.Stack.Amount++;
            changed++;
        }

        if (localSlot._ItemData.Stack.Amount <= 0)
            localSlot.ClearData();

        return changed > 0;
    }

    #endregion

    #region �����ת���߼�

    public bool TryAddItem(ItemData inputItemData, bool doAdd = true)
    {
        if (inputItemData == null) return false;

        float unitVolume = inputItemData.Stack.Volume;
        float remainingAmount = inputItemData.Stack.Amount;
        bool addedAny = false;

        // �Ƕѵ���Ʒ���������1��
        if (unitVolume > 1)
        {
            for (int i = 0; i < itemSlots.Count; i++)
            {
                if (itemSlots[i]._ItemData == null)
                {
                    if (doAdd)
                    {
                        SetOne_ItemData(i,inputItemData);
                        Event_RefreshUI.Invoke(i);
                        inputItemData.Stack.CanBePickedUp = false;
                    }
                    return true;
                }
            }
            return false;
        }

        // �ѵ���Ʒ�����Ϊ1��
        for (int i = 0; i < itemSlots.Count && remainingAmount > 0; i++)
        {
            var slot = itemSlots[i];
            bool hasItem = slot._ItemData != null;
            bool sameItem = hasItem &&
                            slot._ItemData.IDName == inputItemData.IDName &&
                            slot._ItemData.ItemSpecialData == inputItemData.ItemSpecialData;

            if ((!hasItem && slot.IsFull) || (hasItem && (!sameItem || slot.IsFull)))
                continue;

            float currentVol = hasItem ? slot._ItemData.Stack.CurrentVolume : 0f;
            float canAdd = slot.SlotMaxVolume - currentVol;
            float toAdd = Mathf.Min(remainingAmount, canAdd);
            if (toAdd <= 0f) continue;

            if (doAdd)
            {
                if (hasItem)
                    ChangeItemDataAmount(i, toAdd);
                else
                {
                    var newItem = FastCloner.FastCloner.DeepClone(inputItemData);
                    newItem.Stack.Amount = toAdd;
                    SetOne_ItemData(i, newItem);
                }
                Event_RefreshUI.Invoke(i);
            }

            remainingAmount -= toAdd;
            addedAny = true;
        }

        if (doAdd)
        {
            inputItemData.Stack.CanBePickedUp = false;
            if (remainingAmount > 0)
                Debug.LogWarning($"��Ʒ���δ��ȫ��ɣ�ʣ�� {remainingAmount} ��δ��ӡ�");
        }

        return addedAny;
    }

    /// <summary>
    /// ��������Ʒ��֮��ת��ָ��������upToCount������Ʒ��
    /// ת���߼��������¼�飺
    /// - ������λ��Ч���Ҳ���ͬ
    /// - ��Դ��λ����Ʒ������������
    /// - ���Ŀ���λ������Ʒ��������������Դ��Ʒһ�£������������ݣ�
    /// - ����Ʒ���ɶѵ���Volume > 1�������ܺϲ�������ղ۲�����ת��
    /// - ת�ƺ��Զ����� UI ������
    /// </summary>
    public bool TransferItemQuantity(ItemSlot slotFrom, ItemSlot slotTo, int upToCount)
    {
        if (slotFrom == null || slotTo == null || slotFrom == slotTo || upToCount <= 0)
            return false;

        var dataFrom = slotFrom._ItemData;
        if (dataFrom == null || dataFrom.Stack.Amount <= 0)
            return false;

        var dataTo = slotTo._ItemData;

        // ��Ŀ���λ������Ʒ����ȷ��ID����������һ��
        if (dataTo != null &&
            (dataTo.IDName != dataFrom.IDName || dataTo.ItemSpecialData != dataFrom.ItemSpecialData))
            return false;

        // ����Ʒ���ɶѵ���Volume > 1�������ܽ��жѵ�ʽת�ƣ�ֻ��ֱ���ƶ��������ղ�
        if (dataFrom.Stack.Volume > 1)
        {
            // �ǿղ�λ���ܽ��ղ��ɶѵ���Ʒ
            if (dataTo != null)
                return false;

            // ֻ����ת��һ��
            var singleData = dataFrom;
            if (dataFrom.Stack.Amount == 1)
            {
                // ֱ�Ӱ�Ǩ���ã����� Clone������ GC��
                slotTo._ItemData = dataFrom;
                slotFrom.ClearData();
            }
            else
            {
                // ��ԭ�����и��Ƴ�һ���¶���
                var newData = dataFrom.DeepClone();
                newData.Stack.Amount = 1;
                dataFrom.Stack.Amount -= 1;
                slotTo._ItemData = newData;
            }

            slotFrom.RefreshUI();
            slotTo.RefreshUI();
            return true;
        }

        // �ѵ��߼�����
        int transferCount = Mathf.Min(upToCount, (int)dataFrom.Stack.Amount);

        // ��¡һ��ת�ƶ�������ת������
        var transferData = dataFrom.DeepClone();
        transferData.Stack.Amount = transferCount;

        // �۳���Դ��Ʒ����
        dataFrom.Stack.Amount -= transferCount;
        if (dataFrom.Stack.Amount <= 0)
            slotFrom.ClearData();

        // ���Ŀ��Ϊ�գ�ֱ�Ӹ�ֵ�������������
        if (dataTo == null)
            slotTo._ItemData = transferData;
        else
            dataTo.Stack.Amount += transferCount;

        slotFrom.RefreshUI();
        slotTo.RefreshUI();

        return true;
    }


    #endregion
}

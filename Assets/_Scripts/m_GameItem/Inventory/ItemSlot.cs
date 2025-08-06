using FastCloner.Code;
using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using UltEvents;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class ItemSlot
{
    //TODO ���ò������
    // ��ǰ����е���Ʒ����   
    [ShowInInspector]
    public ItemData itemData = null; // �ؼ��޸�

    public ItemTag CanAcceptItemType = new ItemTag();

    public float SlotMaxVolume = 100;

    public int Index = -1;


    #region  ��ʱ����

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public Inventory Belong_Inventory = null;

    [MemoryPackIgnore]
    [FastClonerIgnore]
    public bool IsFull
    {
        get
        {
            if (itemData == null)
            {
                return false;
            }
            return itemData.Stack.CurrentVolume >= SlotMaxVolume;
        }
    }
    [MemoryPackIgnore]
    public int Amount
    {
        get
        {
            if (itemData == null)
            {
                return 0;
            }
            return (int)itemData.Stack.Amount;
        }
        set
        {
            if (itemData == null)
            {
                return;
            }
            itemData.Stack.Amount = value;
        }
    }
    [MemoryPackConstructor]
    public ItemSlot(int index = -1)
    {
        Index = index;
    }

    //[MemoryPackIgnore]
    //public UltEvent onSlotDataChanged = new UltEvent();

    #endregion

    #region ��ɾ��
    /// <summary>
    /// ���������һ����Ʒ��
    /// �����Ϊ�գ���ֱ�ӷ��ø���Ʒ��
    /// �������������ͬID����Ʒ����������ѵ�������
    /// </summary>
    /// <param name="itemData">Ҫ��ӵ���Ʒ���ݡ�</param>
    public void AddItem(ItemData itemData)
    {
        if (this.itemData == null)
        {
            // ���Ϊ�գ�ֱ�ӷ�����Ʒ
            this.itemData = itemData;
        }
        else if (this.itemData.IDName == itemData.IDName)
        {
            // �����������ͬID����Ʒ�����Ӷѵ�����
            this.itemData.Stack.Amount += itemData.Stack.Amount;
        }
      //  onSlotDataChanged.Invoke();
        // ��������в�ͬID����Ʒ���򲻽����κβ���
    }

    /// <summary>
    /// ���ٲ���еĲ��ֻ�ȫ����Ʒ��
    /// ����������С�ڶѵ�����������ٶѵ���������ղ�ۡ�
    /// </summary>
    /// <param name="destroyCount">Ҫ���ٵ���Ʒ������</param>
    public void DestroyItem(float destroyCount)
    {
        if (itemData != null)
        {
            if (itemData.Stack.Amount > destroyCount)
            {
                // ��������С�ڶѵ����������ٶѵ�
                itemData.Stack.Amount -= destroyCount;
            }
            else
            {
                // �����������ڻ���ڶѵ���������ղ��
                itemData = null;
            }
        }
    }

    /// <summary>
    /// ����һ����۽�����Ʒ��
    /// ����һ�����������Ʒ��
    /// - ����ǰ���Ϊ�գ�����һ����۵���Ʒ������ǰ��ۡ�
    /// - ����ǰ���������ͬID����Ʒ���ϲ��ѵ�������
    /// </summary>
    /// <param name="ItemSlot_Input">Ҫ�����Ĳ�۶���</param>
    public void Change(ItemSlot _ItemData_Input)
    {/*
        if (_ItemData == null)
        {
            // ��ǰ���Ϊ�գ�ֱ�ӽ������۵���Ʒ������ǰ���
            _ItemData = _ItemData_Input._ItemData;
            _ItemData_Input._ItemData = null; // ���������
        }
        else if (_ItemData.ID == _ItemData_Input._ItemData.ID&&_ItemData.ID!=0&&_ItemData_Input._ItemData.ID!=0)//����Id��Ϊ0������ͬID
        {
            // �����������ͬID����Ʒ���ϲ��ѵ�����
            _ItemData.Stack.amount += _ItemData_Input._ItemData.Stack.amount;
            _ItemData_Input._ItemData = null; // ���������
        }
        else
        {*/
        // ��ǰ��۵���Ʒ�������۵���Ʒ��ͬ��������Ʒ
        ItemData temp = itemData;
        itemData = _ItemData_Input.itemData;
        _ItemData_Input.itemData = temp;
       


    }
    #endregion


    public void ClearData()
    {
        itemData =null;
    }

    public void RefreshUI()
    {
        Belong_Inventory.RefreshUI(Index);
    }
}
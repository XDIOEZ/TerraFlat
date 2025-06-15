using MemoryPack;
using NUnit.Framework.Interfaces;
using Org.BouncyCastle.Asn1.Cmp;
using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UltEvents;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;


// ʹ��[System.Serializable]����ʹ������Ա����л����Ա���Unity�༭������ʾ�ͱ༭
[MemoryPackUnion(0, typeof(Data_Creature))]//��������
[MemoryPackUnion(1, typeof(Data_Armor))]//��������
[MemoryPackUnion(2, typeof(Data_Ammo))]//��ҩ����
[MemoryPackUnion(3, typeof(Data_ColdWeapon))]//����������
[MemoryPackUnion(4, typeof(Data_GeneralItem))]//ͨ����Ʒ����
[MemoryPackUnion(5, typeof(Data_Building))]//��������
[MemoryPackUnion(6, typeof(Data_Player))]//�������
[MemoryPackUnion(8, typeof(Data_TileMap))]//��Ƭ��ͼ����
[MemoryPackUnion(51, typeof(Data_Weapon))]//��������
[MemoryPackUnion(52, typeof(Data_Worker))]//����������
[MemoryPackUnion(53, typeof(Data_Boundary))]//ǽ������
[MemoryPackUnion(54, typeof(Data_Food))]//ǽ������
[MemoryPackUnion(55, typeof(Data_Tile_Block))]//��������

[MemoryPackable]
[System.Serializable]   
public  abstract partial class ItemData
{
    [Tooltip("��Ʒ����")]
    public string IDName;

    [Tooltip("��Ʒ����")]
    public string GameName;

    [Tooltip("��Ʒ����")]
    public string Description = "ʲô��û������";

    [Tooltip("��Ʒ�;ö�")]
    public float Durability = 1;

    [Tooltip("��Ʒ��ǩ")]
    public ItemTag ItemTags;

    [Tooltip("��Ʒ�ѵ���Ϣ")]
    public ItemStack Stack;

    [Tooltip("��Ʒ����")]
    public ItemTransform _transform = new();

    [Tooltip("��Ʒ��������")]
    public string ItemSpecialData;

    [Tooltip("ȫ��Ψһ��ʶ")]
    public int Guid;


    //��дToString�����������ڿ���̨�����Ʒ��Ϣ
    public override string ToString()
    {
        string str =
            $"��Ʒ���ƣ�{IDName}\n" +
            $"��Ʒ������{Description}\n" +
            $"��Ʒ�����{Stack.Volume}\n" +
            $"��Ʒ�;öȣ�{Durability}\n" +
            $"�Ƿ��ʰȡ��{Stack.CanBePickedUp}\n" +
            $"��Ʒ��ǩ��{ItemTags}\n" +
            $"��Ʒ�ѵ���Ϣ��{Stack}\n" +
            $"��Ʒλ�ã�{_transform.Position}\n" +
            $"��Ʒ��ת��{_transform.Rotation}\n" +
            $"��Ʒ���ţ�{_transform.Scale}\n" +
            $"��Ʒ�������ݣ�{ItemSpecialData}\n" +
            $"ȫ��Ψһ��ʶ��{Guid}";
        return str;
    }

    public virtual int SyncData()
    {
        //�򿪶�Ӧ��Excel���
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        var excel = m_ExcelManager.Instance;


        //���Ҷ�Ӧ����ƷIDName
        int nameColumn = excel.FindColumn(0,ExcelIdentifyRow.IDName);


        int itemRow = excel.FindRow(nameColumn, IDName);
        int itemL = -1;

        if (itemRow == -1)
        {
            return -1;
        }

        itemL = excel.FindColumn(0, ExcelIdentifyRow.GameName);
        GameName = excel.GetCellValue(itemRow, itemL).ToString();
        // ����
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Description);
        Description = excel.GetCellValue(itemRow, itemL).ToString();

        // �;ö�
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Durability);
        Durability = Convert.ToSingle(excel.GetCellValue(itemRow, itemL));

        // ��������
        itemL = excel.FindColumn(0, ExcelIdentifyRow.ItemSpecialData);
        ItemSpecialData = excel.GetCellValue(itemRow, itemL).ToString();

        // ��Ʒ�ѵ���Ϣ
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Volume);
        Stack.Volume = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // �Ƿ��ʰȡ
        itemL = excel.FindColumn(0, ExcelIdentifyRow.CanBePickedUp);
        object canBePickedUpValue = excel.GetCellValue(itemRow, itemL);
        if (canBePickedUpValue is double || canBePickedUpValue is int)
            Stack.CanBePickedUp = Convert.ToInt32(canBePickedUpValue) != 0;
        else
            Stack.CanBePickedUp = Convert.ToBoolean(canBePickedUpValue);

        // ���ͱ�ǩ�б�
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Item_TypeTag);
        string typeTagStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_TypeTag = excel.ParseStringList(typeTagStr);

        // ���ʱ�ǩ�б�
        itemL = excel.FindColumn(0, ExcelIdentifyRow.Item_Material);
        string materialStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_Material = excel.ParseStringList(materialStr);

        Guid = 0;
        Debug.Log("��������ͬ���ɹ���");
        return itemRow;
    }
}

[MemoryPackable]
[System.Serializable]
public partial class ItemTag
{
    public List<string> Item_TypeTag = new List<string> { "None", "None" };
    public List<string> Item_Material = new List<string> { "None" };
    public override string ToString()
    {
        string str =
            $"��Ʒ���ͣ�{Item_TypeTag[0]}({Item_TypeTag[1]})," +
            $"��Ʒ���ʣ�{Item_Material[0]}";
        return str;
    }
    
}

[System.Serializable]
[MemoryPackable]
public partial class ItemTransform
{
    [Tooltip("��Ʒλ��")]
    public Vector3 Position;

    [Tooltip("��Ʒ��ת")]
    public Quaternion Rotation;

    [Tooltip("��Ʒ����")]
    public Vector3 Scale;
}


[MemoryPackable]
[System.Serializable]
public partial class ItemStack
{
    [Tooltip("��Ʒ����")]
    public float Amount = 1;//���������

    [Tooltip("��Ʒ���")]
    // ���������ͱ���Volume�����ڴ洢��Ʒ����������������ֵ
    public float Volume = 1;

    [Tooltip("�Ƿ��ʰȡ")]
    public bool CanBePickedUp = true;

    [MemoryPackIgnore]
    [Tooltip("��ǰ�����")]
    public float CurrentVolume
    {
        get
        {
            return Amount * Volume;
        }
    }


    public override string ToString()
    {
        return string.Format("��������:{0}", Amount);
    }

}







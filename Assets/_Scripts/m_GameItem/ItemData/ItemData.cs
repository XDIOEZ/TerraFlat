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
[MemoryPackUnion(0, typeof(WeaponData))]//��������
[MemoryPackUnion(1, typeof(ArmorData))]//��������
[MemoryPackUnion(2, typeof(AmmoData))]//��ҩ����
[MemoryPackUnion(3, typeof(Tree_Data))]//������
[MemoryPackUnion(4, typeof(Apple_Red_Data))]//��ƻ������
[MemoryPackUnion(5, typeof(PlayerData))]//�������
[MemoryPackUnion(6, typeof(CoalData))]//ú̿����
[MemoryPackUnion(7, typeof(Com_ItemData))]//ͨ����Ʒ����
[MemoryPackUnion(8, typeof(PickaxeToolData))]//��ʯ�乤������
[MemoryPackUnion(9, typeof(WorkerData))]//����������
[MemoryPackUnion(10, typeof(StorageData))]//�ֿ�����
[MemoryPackUnion(11, typeof(ColdWeaponData))]//�ֿ�����
[MemoryPackUnion(12, typeof(FoodData))]//ʳ������
[MemoryPackUnion(13, typeof(AnimalData))]//��������
[MemoryPackUnion(14, typeof(TileMapData))]//��������


[MemoryPackable(SerializeLayout.Explicit)]

[System.Serializable]   
public  abstract partial class ItemData
{
    [Foldout("��Ʒ����")]
    [MemoryPackOrder(0)]
    [Tooltip("��Ʒ����")]
    public string Name;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(1)]
    [Tooltip("��ƷID")]
    public int ID;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(2)]
    [Tooltip("��Ʒ����")]
    public string Description = "ʲô��û������";

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(3)]
    [Tooltip("Ԥ����·��")]
    public string PrefabPath = "";

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(5)]
    [Tooltip("��Ʒ�;ö�")]
    public float Durability = 1;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(7)]
    [Tooltip("��Ʒ��ǩ")]
    public ItemTag ItemTags;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(8)]
    [Tooltip("��Ʒ�ѵ���Ϣ")]
    public ItemStack Stack;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(12)]
    [Tooltip("��Ʒ����")]
    public ItemTransform _transform;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(13)]
    [Tooltip("��Ʒ��������")]
    public string ItemSpecialData;

    [Foldout("��Ʒ����")]
    [MemoryPackOrder(14)]
    [Tooltip("ȫ��Ψһ��ʶ")]
    public int Guid;


    //��дToString�����������ڿ���̨�����Ʒ��Ϣ
    public override string ToString()
    {
        string str =
            $"��Ʒ���ƣ�{Name}\n" +
            $"��ƷID��{ID}\n" +
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

    [Sirenix.OdinInspector.Button("ͬ������")]
    public virtual int SyncData()
    {
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        if (string.IsNullOrWhiteSpace(Name))
        {
            Debug.LogWarning("��Ʒ����Ϊ�գ��޷�ͬ������");
            return -1;
        }

        var excel = m_ExcelManager.Instance;

        int nameColumn = excel.FindColumn(0, "Name");
        int itemRow = excel.FindRow(nameColumn, Name);
        int itemL = -1;

        // ID
        itemL = excel.FindColumn(0, "ID");
        ID = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // ����
        itemL = excel.FindColumn(0, "Description");
        Description = excel.GetCellValue(itemRow, itemL).ToString();

        // �;ö�
        itemL = excel.FindColumn(0, "Durability");
        Durability = Convert.ToSingle(excel.GetCellValue(itemRow, itemL));

        // ��������
        itemL = excel.FindColumn(0, "ItemSpecialData");
        ItemSpecialData = excel.GetCellValue(itemRow, itemL).ToString();

        // ��Ʒ�ѵ���Ϣ
        itemL = excel.FindColumn(0, "Volume");
        Stack.Volume = Convert.ToInt32(excel.GetCellValue(itemRow, itemL));

        // �Ƿ��ʰȡ
        itemL = excel.FindColumn(0, "CanBePickedUp");
        object canBePickedUpValue = excel.GetCellValue(itemRow, itemL);
        if (canBePickedUpValue is double || canBePickedUpValue is int)
            Stack.CanBePickedUp = Convert.ToInt32(canBePickedUpValue) != 0;
        else
            Stack.CanBePickedUp = Convert.ToBoolean(canBePickedUpValue);

        // ���ͱ�ǩ�б�
        itemL = excel.FindColumn(0, "Item_TypeTag");
        string typeTagStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_TypeTag = excel.ParseStringList(typeTagStr);

        // ���ʱ�ǩ�б�
        itemL = excel.FindColumn(0, "Item_Material");
        string materialStr = excel.GetCellValue(itemRow, itemL).ToString();
        ItemTags.Item_Material = excel.ParseStringList(materialStr);

        Debug.Log("ͬ�����ݳɹ���");
        return itemRow;
    }



    // �°淽����ͨ��Vector3�������ñ任���ݣ�����ѡ������
    public void SetTransformValue(Vector3 position,Quaternion? rotation = null,Vector3? scale = null)
    {
        _transform.Position = position;

        // ʹ�ÿպϲ����������ԭ��ֵ
        _transform.Rotation = rotation ?? _transform.Rotation;
        _transform.Scale = scale ?? _transform.Scale;
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








using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable, MemoryPackable]
public partial class Data_Player : ItemData
{
    #region ����
    [Tooltip("Ѫ��")]
    public Hp hp = new Hp(30);

    [Tooltip("������")]
    public Defense defense = new(5, 5);
    #endregion
    #region �ٶ�
    public GameValue_float Speed = new ();
    #endregion

    #region ����
    [Tooltip("����ֵ")]
    public float stamina = 100;
    [Tooltip("��������")]
    public float staminaMax = 100;
    [Tooltip("�����ָ��ٶ�")]
    public float staminaRecoverySpeed = 1;
    #endregion

    #region ʳ��
    [Tooltip("����ֵ")]
    public Nutrition hunger = new Nutrition(100, 100);
    #endregion

    #region ���

    [ShowInInspector]
    [Tooltip("�������")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion
    [ShowInInspector]
    public Dictionary<string, UIData> UIDataDictionary = new Dictionary<string, UIData>();

   [ShowNonSerializedField]
    [Tooltip("����û���")]
    public string Name_User = "Ikun";

    public float PlayerPov = 10;
}
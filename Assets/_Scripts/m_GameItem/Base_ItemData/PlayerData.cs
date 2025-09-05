
using MemoryPack;
using NaughtyAttributes;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

[System.Serializable, MemoryPackable]
public partial class Data_Player : ItemData
{
    [Tooltip("��ǰ�������������,���ڿ�ʼ��Ϸʱ����������ĸ���ͼ�浵")]
    public string CurrentSceneName = "����";
    [Tooltip("��ʶ����Ƿ��ڷ�����,Ҳ�����Ƿ�������ɵ�ͼ")]
    public bool IsInRoom = false;
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
    /*[Tooltip("����ֵ")]
    public Nutrition hunger = new Nutrition(100, 100);*/
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
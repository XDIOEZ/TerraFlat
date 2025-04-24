
using MemoryPack;
using NaughtyAttributes;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable, MemoryPackable]
public partial class PlayerData : ItemData
{
    #region ����
    [Tooltip("Ѫ��")]
    public Hp hp = new Hp(30);

    [Tooltip("������")]
    public Defense defense = new(5, 5);
    #endregion
    #region �ٶ�
    [Tooltip("Ĭ���ٶ�")]
    public float defaultSpeed = 3;
    [Tooltip("�ٶ�")]
    public float speed = 3;
    [Tooltip("�����ٶ�")]
    public float runSpeed = 6;
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
    public Hunger_Water hunger = new Hunger_Water(100, 100);
    #endregion

    #region ���

    [ShowNonSerializedField]
    [Tooltip("�������")]
    public Dictionary<string, Inventory_Data> _inventoryData = new Dictionary<string, Inventory_Data>();
    #endregion

    [ShowNonSerializedField]
    [Tooltip("����û���")]
    public string PlayerUserName = "Ikun";
}
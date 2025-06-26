using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
[MemoryPackable]
public  partial class EntityData
{
    #region ˽���ֶ�
    [Header("ʵ������")]
    // ��ǰֵ�ֶΣ���ʼ��ΪĬ��ֵ��
    // ����
    public float stamina = 100;
    // ����
    public Hp hp  = new Hp(100);
    //������     
    public Defense defense = new Defense(1,1);
    //�ٶ�
    public float speed = 4;
    //����
    public float Power = 10;
    // ���ֵ�ֶ�
    public float maxStamina = 100;
    public Hp maxHP = new Hp(100);
    public float maxDefense = 10;
    public float maxSpeed = 8;
    public float maxPower = 10;


    public void ResetValuesToMax()
    {
        stamina = maxStamina;
        hp = maxHP;
        speed = maxSpeed;
        Power = maxPower;
    }
    #endregion
}

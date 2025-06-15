using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[MemoryPackUnion(0, typeof(GunData))]
[MemoryPackUnion(1, typeof(PickaxeToolData))]
[MemoryPackable]
[System.Serializable]
public abstract partial class Data_Weapon : ItemData
{
    #region ������ֵ����
    [Header("---������ֵ---")]
    [Tooltip("�����˺�(��ʱ����)")]
    public Damage _damage; // �����˺�
    [Tooltip("����ÿ�뾫������(���ڹ���״̬ʱÿ�����ĵľ���)")]
    public float StaminaCost = 10f; // ����ÿ�뾫������
    [Tooltip("��󹥻�����(����״̬ʱ����������ƶ�����)")]
    public float MaxAttackDistance = 1f; // ��󹥻�����
    [Tooltip("��Ʒ�����ٶ�")]
    public float AttackSpeed = 20;
    [Tooltip("��Ʒ�����ٶ�")]
    public float ReturnSpeed = 5f; // ��ǰ��Ʒ�����ٶ�
    [Tooltip("����˺����(��λ����)")]
    public float _minDamageInterval = 0.5f; // ����˺����
   


    #endregion

    //��дToString����������鿴����
    public override string ToString()
    {
        return
               "�����˺���" + _damage + "\n" +
               "����ÿ�뾫�����ģ�" + StaminaCost + "\n" +
               "��󹥻����룺" + MaxAttackDistance + "\n" +
               //   "Ĭ����󹥻�ʱ�䣺" + DefaultMaxAttackTime + "\n" +
               //  "�ɸı���󹥻�ʱ�䣺" + MaxAttackTime + "\n" +
               "�����ٶȣ�" + AttackSpeed + "\n" +
               "��Ʒ�����ٶȣ�" + ReturnSpeed + "\n";
          /*     "��ǰ����ʱ�䣺" + AttackTime + "\n" +
               "��ǰ��Ʒ����ʱ�䣺" + ItemBackTime;*/
    }
}

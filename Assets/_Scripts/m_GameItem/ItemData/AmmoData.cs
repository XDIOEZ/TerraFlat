using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[MemoryPackable]
[System.Serializable]
public partial class AmmoData:ItemData
{
    [Header("�ӵ�����")]
    public float speed;//�ٶ�
    public Damage damage;//�˺�
    public float range;//���
    public float Fired;//�Ƿ��Ѿ�����
    public float MinDamageInterval = 0.5f;//��С�˺����
}

using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class HouseData : ItemData
{
    //---�������ӵĳ�������
    public string sceneName;
    //---�����ĵ�ǰ�;ö�//ʹ��Hp��
    public Hp hp;
    //�����ķ���
    public Defense defense;
    //---�Ƿ��ڽ���״̬
    public bool isBuilding;
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Item Spawn Data", menuName = "ScriptObjects/Biome Item Spawn Data")]
public class Biome_ItemSpawn : ScriptableObject
{ //���ɵ���Ʒ������
    public string itemName;
    //������Ʒ������
    public int itemCount;
    //���ɸ���
    public float SpawnChance;


    [Tooltip("����ƫ������ȷ����ͬ��Դʹ�ò�ͬ����ͼ��")]
    public float hashOffset = 20000f;
}

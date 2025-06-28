using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Item Spawn Data", menuName = "ScriptObjects/Biome Item Spawn Data")]
public class Biome_ItemSpawn : ScriptableObject
{ //���ɵ���Ʒ������
    public string itemName = "";
    //������Ʒ������
    public int itemCount = 1;

    public float SpawnChance = 0.5f;

    public EnvironmentConditionRange environmentConditionRange;
}

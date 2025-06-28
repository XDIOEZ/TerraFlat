using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Item Spawn Data", menuName = "ScriptObjects/Biome Item Spawn Data")]
public class Biome_ItemSpawn : ScriptableObject
{ //生成的物品的名称
    public string itemName = "";
    //生成物品的数量
    public int itemCount = 1;

    public float SpawnChance = 0.5f;

    public EnvironmentConditionRange environmentConditionRange;
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Item Spawn Data", menuName = "ScriptObjects/Biome Item Spawn Data")]
public class Biome_ItemSpawn : ScriptableObject
{ //生成的物品的名称
    public string itemName;
    //生成物品的数量
    public int itemCount;
    //生成概率
    public float SpawnChance;


    [Tooltip("噪声偏移量，确保不同资源使用不同噪声图层")]
    public float hashOffset = 20000f;
}

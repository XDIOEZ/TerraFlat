using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(fileName = "Tile Spawn Data", menuName = "ScriptObjects/Biome Tile Spawn Data")]
public class BiomeTileSpawn : ScriptableObject
{
    public string TileDataName = "TileItem_";
   public EnvironmentConditionRange environmentConditionRange;
}

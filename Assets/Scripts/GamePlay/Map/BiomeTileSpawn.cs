using UnityEngine;
[CreateAssetMenu(fileName = "Tile Spawn Data", menuName = "ScriptObjects/Biome Tile Spawn Data")]
public class BiomeTileSpawn : ScriptableObject
{
    [Header("要生成的地块逻辑SO (Tile_Block)")]
    public Tile_Block TileBlock;

    public EnvironmentConditionRange environmentConditionRange;
}

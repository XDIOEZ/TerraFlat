using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StructureDefinition", menuName = "FlatWorld/Structures/Definition")]
public sealed class StructureDefinitionSO : ScriptableObject
{
    public string StructureId;
    public string DisplayName;
    public bool Enabled = true;
    public int SeedSalt;
    [Min(0f)] public float Weight = 1f;
    [Range(0f, 1f)] public float SpawnChance = 0.2f;
    [Min(8)] public int RegionSizeInTiles = 48;
    [Min(0)] public int MinDistanceFromWorldOrigin = 24;
    [Min(0)] public int ChunkEdgeMargin = 1;
    [Min(0f)] public float MaxHeightDelta = 0.2f;
    public EnvironmentConditionRange EnvironmentCondition = new();
    public List<BiomeData> AllowedBiomes = new();
    public bool AllowRotation = true;
    public bool AllowMirror = true;
    public bool ClearProceduralItemsInFootprint = true;
    public List<WeightedStructureTemplate> Templates = new();

    public bool IsEnvironmentValid(EnvironmentLayers layers, int x, int y, BiomeData resolvedBiome)
    {
        if (EnvironmentCondition != null && !EnvironmentCondition.IsMatch(layers, x, y))
            return false;
        return IsBiomeAllowed(resolvedBiome);
    }

    public bool IsEnvironmentValid(EnvironmentSample sample, BiomeData resolvedBiome)
    {
        if (EnvironmentCondition != null && !EnvironmentCondition.IsMatch(sample))
            return false;

        return IsBiomeAllowed(resolvedBiome);
    }

    public bool IsBiomeAllowed(BiomeData resolvedBiome)
    {
        if (AllowedBiomes == null || AllowedBiomes.Count == 0)
            return true;
        if (resolvedBiome == null || string.IsNullOrWhiteSpace(resolvedBiome.BiomeId))
            return false;

        for (int i = 0; i < AllowedBiomes.Count; i++)
        {
            BiomeData biome = AllowedBiomes[i];
            if (biome != null && string.Equals(biome.BiomeId, resolvedBiome.BiomeId, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void OnValidate()
    {
        RegionSizeInTiles = Mathf.Max(8, RegionSizeInTiles);
        ChunkEdgeMargin = Mathf.Max(0, ChunkEdgeMargin);
        MinDistanceFromWorldOrigin = Mathf.Max(0, MinDistanceFromWorldOrigin);
        MaxHeightDelta = Mathf.Max(0f, MaxHeightDelta);
    }
}

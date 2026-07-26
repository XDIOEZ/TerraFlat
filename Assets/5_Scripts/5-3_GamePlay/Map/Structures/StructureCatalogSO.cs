using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "StructureCatalog", menuName = "FlatWorld/Structures/Catalog")]
public sealed class StructureCatalogSO : ScriptableObject
{
    public const string DefaultResourcesPath = "Config/StructureCatalog_Default";

    public bool Enabled = true;
    [Min(1)] public int GenerationVersion = 1;
    public List<StructureDefinitionSO> Definitions = new();

    public uint CalculateContentHash()
    {
        uint hash = StructureHashUtility.Begin();
        hash = StructureHashUtility.Add(hash, GenerationVersion);
        hash = StructureHashUtility.Add(hash, Enabled);

        IEnumerable<StructureDefinitionSO> definitions = (Definitions ?? new List<StructureDefinitionSO>())
            .Where(definition => definition != null)
            .OrderBy(definition => definition.StructureId, StringComparer.Ordinal);

        foreach (StructureDefinitionSO definition in definitions)
        {
            hash = StructureHashUtility.Add(hash, definition.StructureId);
            hash = StructureHashUtility.Add(hash, definition.Enabled);
            hash = StructureHashUtility.Add(hash, definition.SeedSalt);
            hash = StructureHashUtility.Add(hash, definition.Weight);
            hash = StructureHashUtility.Add(hash, definition.SpawnChance);
            hash = StructureHashUtility.Add(hash, definition.RegionSizeInTiles);
            hash = StructureHashUtility.Add(hash, definition.MinDistanceFromWorldOrigin);
            hash = StructureHashUtility.Add(hash, definition.ChunkEdgeMargin);
            hash = StructureHashUtility.Add(hash, definition.MaxHeightDelta);
            hash = StructureHashUtility.Add(hash, definition.AllowRotation);
            hash = StructureHashUtility.Add(hash, definition.AllowMirror);
            hash = StructureHashUtility.Add(hash, definition.ClearProceduralItemsInFootprint);
            hash = AddEnvironmentHash(hash, definition.EnvironmentCondition);

            IEnumerable<string> biomeIds = (definition.AllowedBiomes ?? new List<BiomeData>())
                .Where(biome => biome != null)
                .Select(biome => string.IsNullOrEmpty(biome.BiomeName) ? biome.name : biome.BiomeName)
                .OrderBy(value => value, StringComparer.Ordinal);
            foreach (string biomeId in biomeIds)
                hash = StructureHashUtility.Add(hash, biomeId);

            IEnumerable<WeightedStructureTemplate> templates =
                (definition.Templates ?? new List<WeightedStructureTemplate>())
                .Where(entry => entry?.Template != null)
                .OrderBy(entry => entry.Template.TemplateId, StringComparer.Ordinal);
            foreach (WeightedStructureTemplate entry in templates)
            {
                hash = StructureHashUtility.Add(hash, entry.Weight);
                hash = AddTemplateHash(hash, entry.Template);
            }
        }

        return hash;
    }

    public static StructureCatalogSO LoadDefault()
    {
        return Resources.Load<StructureCatalogSO>(DefaultResourcesPath);
    }

    private static uint AddTemplateHash(uint hash, StructureTemplateSO template)
    {
        hash = StructureHashUtility.Add(hash, template.TemplateId);
        hash = StructureHashUtility.Add(hash, template.Version);
        hash = StructureHashUtility.Add(hash, template.Size.x);
        hash = StructureHashUtility.Add(hash, template.Size.y);
        hash = StructureHashUtility.Add(hash, template.Pivot.x);
        hash = StructureHashUtility.Add(hash, template.Pivot.y);

        foreach (StructureTileStamp stamp in template.TileStamps ?? new List<StructureTileStamp>())
        {
            hash = StructureHashUtility.Add(hash, stamp.LocalPosition.x);
            hash = StructureHashUtility.Add(hash, stamp.LocalPosition.y);
            hash = StructureHashUtility.Add(hash, (int)stamp.WriteMode);
            string tileId = stamp.TileBlock == null
                ? string.Empty
                : (string.IsNullOrEmpty(stamp.TileBlock.tileItemName)
                    ? stamp.TileBlock.name
                    : stamp.TileBlock.tileItemName);
            hash = StructureHashUtility.Add(hash, tileId);
        }

        foreach (StructureItemStamp stamp in template.ItemStamps ?? new List<StructureItemStamp>())
        {
            hash = StructureHashUtility.Add(hash, stamp.ItemPrefabId);
            hash = StructureHashUtility.Add(hash, stamp.LocalPosition.x);
            hash = StructureHashUtility.Add(hash, stamp.LocalPosition.y);
            hash = StructureHashUtility.Add(hash, stamp.RotationZ);
            hash = StructureHashUtility.Add(hash, stamp.Scale.x);
            hash = StructureHashUtility.Add(hash, stamp.Scale.y);
            hash = StructureHashUtility.Add(hash, stamp.Scale.z);
            hash = StructureHashUtility.Add(hash, (int)stamp.OrientationMode);
            hash = StructureHashUtility.Add(hash, stamp.Optional);
            hash = StructureHashUtility.Add(hash, stamp.SpawnChance);
            hash = StructureHashUtility.Add(hash, stamp.SeedSalt);
        }

        foreach (StructureMarkerData marker in template.Markers ?? new List<StructureMarkerData>())
        {
            hash = StructureHashUtility.Add(hash, (int)marker.Type);
            hash = StructureHashUtility.Add(hash, marker.MarkerId);
            hash = StructureHashUtility.Add(hash, marker.LocalPosition.x);
            hash = StructureHashUtility.Add(hash, marker.LocalPosition.y);
            hash = StructureHashUtility.Add(hash, marker.Size.x);
            hash = StructureHashUtility.Add(hash, marker.Size.y);
            hash = StructureHashUtility.Add(hash, marker.ContentId);
            hash = StructureHashUtility.Add(hash, marker.RotationZ);
            hash = StructureHashUtility.Add(hash, (int)marker.OrientationMode);
            hash = StructureHashUtility.Add(hash, marker.Chance);
            hash = StructureHashUtility.Add(hash, marker.SeedSalt);
        }

        return hash;
    }

    private static uint AddEnvironmentHash(uint hash, EnvironmentConditionRange range)
    {
        if (range == null)
            return StructureHashUtility.Add(hash, 0);

        hash = StructureHashUtility.Add(hash, range.TemperatureRange.x);
        hash = StructureHashUtility.Add(hash, range.TemperatureRange.y);
        hash = StructureHashUtility.Add(hash, range.HumidityRange.x);
        hash = StructureHashUtility.Add(hash, range.HumidityRange.y);
        hash = StructureHashUtility.Add(hash, range.PrecipitationRange.x);
        hash = StructureHashUtility.Add(hash, range.PrecipitationRange.y);
        hash = StructureHashUtility.Add(hash, range.SolidityRange.x);
        hash = StructureHashUtility.Add(hash, range.SolidityRange.y);
        hash = StructureHashUtility.Add(hash, range.HightRange.x);
        hash = StructureHashUtility.Add(hash, range.HightRange.y);
        return hash;
    }
}

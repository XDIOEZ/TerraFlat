using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum StructureValidationSeverity
{
    Warning,
    Error
}

public sealed class StructureValidationIssue
{
    public StructureValidationSeverity Severity;
    public string Message;
    public UnityEngine.Object Context;
}

public static class StructureTemplateValidator
{
    public static List<StructureValidationIssue> Validate(StructureAuthoringRoot root)
    {
        List<StructureValidationIssue> issues = new();
        if (root == null)
        {
            Add(issues, StructureValidationSeverity.Error, "当前Prefab Stage缺少StructureAuthoringRoot", null);
            return issues;
        }

        if (root.Definition == null)
            Add(issues, StructureValidationSeverity.Error, "未指定StructureDefinition", root);
        else if (string.IsNullOrWhiteSpace(root.Definition.StructureId))
            Add(issues, StructureValidationSeverity.Error, "StructureId为空", root.Definition);

        if (root.Template == null)
            Add(issues, StructureValidationSeverity.Error, "未指定StructureTemplate", root);
        else if (string.IsNullOrWhiteSpace(root.Template.TemplateId))
            Add(issues, StructureValidationSeverity.Error, "TemplateId为空", root.Template);

        if (root.Size.x <= 0 || root.Size.y <= 0)
            Add(issues, StructureValidationSeverity.Error, "模板Size必须大于0", root);
        if (!Contains(root, root.Pivot))
            Add(issues, StructureValidationSeverity.Error, "Pivot超出模板边界", root);

        Item[] items = root.GetComponentsInChildren<Item>(true);
        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (!IsTopLevelPlacedItem(root, item))
                continue;

            Vector2 local = root.transform.InverseTransformPoint(item.transform.position);
            if (!Contains(root, local))
                Add(issues, StructureValidationSeverity.Error, $"物件超出边界：{item.name}", item);
            if (item.itemData == null || string.IsNullOrWhiteSpace(item.itemData.IDName))
                Add(issues, StructureValidationSeverity.Error, $"物件缺少ItemData.IDName：{item.name}", item);
            if (item.name.EndsWith(Mod_Building.SummonerPrefabSuffix, StringComparison.Ordinal) ||
                item.itemData?.IDName?.EndsWith(Mod_Building.SummonerPrefabSuffix, StringComparison.Ordinal) == true)
            {
                Add(issues, StructureValidationSeverity.Error, $"遗迹不能使用建筑召唤器：{item.name}", item);
            }

            Mod_Building building = item.GetComponent<Mod_Building>();
            if (building != null && building.Data != null && building.Data.Role == BuildingRole.Summoner)
                Add(issues, StructureValidationSeverity.Error, $"建筑Role必须为PlacedBuilding：{item.name}", item);
        }

        StructureItemAuthoring[] itemMetadata =
            root.GetComponentsInChildren<StructureItemAuthoring>(true);
        for (int i = 0; i < itemMetadata.Length; i++)
        {
            StructureItemAuthoring metadata = itemMetadata[i];
            Vector2 local = root.transform.InverseTransformPoint(metadata.transform.position);
            if (!Contains(root, local))
                Add(issues, StructureValidationSeverity.Error, $"物件超出边界：{metadata.name}", metadata);
            if (string.IsNullOrWhiteSpace(metadata.ItemPrefabId))
                Add(issues, StructureValidationSeverity.Error, $"物件缺少ItemPrefabId：{metadata.name}", metadata);
            if (metadata.SourcePrefab == null &&
                StructureAuthoringPrefabUtility.FindItemPrefabById(metadata.ItemPrefabId) == null)
            {
                Add(issues, StructureValidationSeverity.Error, $"找不到物件Prefab：{metadata.ItemPrefabId}", metadata);
            }
        }

        StructureMarkerAuthoring[] markers = root.GetComponentsInChildren<StructureMarkerAuthoring>(true);
        int entranceCount = 0;
        HashSet<string> markerIds = new(StringComparer.Ordinal);
        for (int i = 0; i < markers.Length; i++)
        {
            StructureMarkerAuthoring marker = markers[i];
            Vector2 local = root.transform.InverseTransformPoint(marker.transform.position);
            if (!Contains(root, local))
                Add(issues, StructureValidationSeverity.Error, $"Marker超出边界：{marker.name}", marker);
            if (marker.Type == StructureMarkerType.Entrance)
                entranceCount++;
            if ((marker.Type == StructureMarkerType.Loot || marker.Type == StructureMarkerType.Enemy) &&
                string.IsNullOrWhiteSpace(marker.ContentId))
            {
                Add(issues, StructureValidationSeverity.Error, $"{marker.Type} Marker缺少ContentId", marker);
            }
            if (!string.IsNullOrWhiteSpace(marker.MarkerId) && !markerIds.Add(marker.MarkerId))
                Add(issues, StructureValidationSeverity.Error, $"MarkerId重复：{marker.MarkerId}", marker);
        }

        if (entranceCount == 0)
            Add(issues, StructureValidationSeverity.Warning, "建议至少设置一个Entrance Marker", root);

        ValidateTiles(root, issues);
        ValidateCatalogIds(root, issues);
        return issues;
    }

    internal static bool IsTopLevelPlacedItem(StructureAuthoringRoot root, Item item)
    {
        if (root == null || item == null)
            return false;
        Transform parent = item.transform.parent;
        while (parent != null && parent != root.transform)
        {
            if (parent.GetComponent<Item>() != null)
                return false;
            parent = parent.parent;
        }
        return parent == root.transform;
    }

    private static void ValidateTiles(StructureAuthoringRoot root, List<StructureValidationIssue> issues)
    {
        if (root.Tilemap == null)
            return;

        Dictionary<TileBase, List<Tile_Block>> tileLookup = StructureTemplateBaker.BuildTileBlockLookup();
        foreach (Vector3Int position in root.Tilemap.cellBounds.allPositionsWithin)
        {
            TileBase tile = root.Tilemap.GetTile(position);
            if (tile == null)
                continue;

            if (!tileLookup.TryGetValue(tile, out List<Tile_Block> blocks) || blocks.Count == 0)
                Add(issues, StructureValidationSeverity.Error, $"Tile找不到对应Tile_Block：{tile.name}", root.Tilemap);
            else if (blocks.Count > 1)
                Add(issues, StructureValidationSeverity.Error, $"Tile对应多个Tile_Block：{tile.name}", root.Tilemap);

            if (position.x < 0 || position.y < 0 || position.x >= root.Size.x || position.y >= root.Size.y)
                Add(issues, StructureValidationSeverity.Error, $"Tile超出边界：{position}", root.Tilemap);
        }
    }

    private static void ValidateCatalogIds(StructureAuthoringRoot root, List<StructureValidationIssue> issues)
    {
        StructureCatalogSO catalog = StructureCatalogSO.LoadDefault();
        if (catalog?.Definitions == null)
            return;

        if (root.Definition != null)
        {
            int count = catalog.Definitions.Count(definition =>
                definition != null &&
                string.Equals(definition.StructureId, root.Definition.StructureId, StringComparison.Ordinal));
            if (count > 1)
                Add(issues, StructureValidationSeverity.Error, $"Catalog中StructureId重复：{root.Definition.StructureId}", root.Definition);
        }

        if (root.Template != null)
        {
            int count = catalog.Definitions
                .Where(definition => definition?.Templates != null)
                .SelectMany(definition => definition.Templates)
                .Count(entry => entry?.Template != null &&
                    string.Equals(entry.Template.TemplateId, root.Template.TemplateId, StringComparison.Ordinal));
            if (count > 1)
                Add(issues, StructureValidationSeverity.Warning, $"TemplateId被多个定义引用：{root.Template.TemplateId}", root.Template);
        }
    }

    private static bool Contains(StructureAuthoringRoot root, Vector2 point)
    {
        return point.x >= 0f && point.y >= 0f && point.x <= root.Size.x && point.y <= root.Size.y;
    }

    private static void Add(
        List<StructureValidationIssue> issues,
        StructureValidationSeverity severity,
        string message,
        UnityEngine.Object context)
    {
        issues.Add(new StructureValidationIssue
        {
            Severity = severity,
            Message = message,
            Context = context
        });
    }
}

public static class StructureTemplateBaker
{
    public static bool Bake(StructureAuthoringRoot root, out List<StructureValidationIssue> issues)
    {
        issues = StructureTemplateValidator.Validate(root);
        if (issues.Any(issue => issue.Severity == StructureValidationSeverity.Error))
            return false;

        StructureTemplateSO template = root.Template;
        Undo.RecordObject(template, "烘焙遗迹模板");
        template.Size = root.Size;
        template.Pivot = root.Pivot;
        template.ItemStamps = BakeItems(root);
        template.Markers = BakeMarkers(root);
        template.TileStamps = BakeTiles(root);
        EditorUtility.SetDirty(template);

        if (root.Definition != null && root.Definition.Templates.All(entry => entry?.Template != template))
        {
            Undo.RecordObject(root.Definition, "关联遗迹模板");
            root.Definition.Templates.Add(new WeightedStructureTemplate
            {
                Template = template,
                Weight = 1f
            });
            EditorUtility.SetDirty(root.Definition);
        }

        AssetDatabase.SaveAssets();
        return true;
    }

    public static Dictionary<TileBase, List<Tile_Block>> BuildTileBlockLookup()
    {
        Dictionary<TileBase, List<Tile_Block>> lookup = new();
        string[] guids = AssetDatabase.FindAssets("t:Tile_Block");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Tile_Block block = AssetDatabase.LoadAssetAtPath<Tile_Block>(path);
            TileBase tile = block?.GetTileBaseAsset();
            if (tile == null)
                continue;
            if (!lookup.TryGetValue(tile, out List<Tile_Block> blocks))
            {
                blocks = new List<Tile_Block>();
                lookup[tile] = blocks;
            }
            blocks.Add(block);
        }
        return lookup;
    }

    private static List<StructureItemStamp> BakeItems(StructureAuthoringRoot root)
    {
        List<StructureItemStamp> output = new();
        HashSet<StructureItemAuthoring> bakedMetadata = new();
        Item[] items = root.GetComponentsInChildren<Item>(true);
        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (!StructureTemplateValidator.IsTopLevelPlacedItem(root, item))
                continue;

            StructureItemAuthoring metadata = item.GetComponent<StructureItemAuthoring>();
            if (metadata != null)
                bakedMetadata.Add(metadata);
            Vector3 localPosition = root.transform.InverseTransformPoint(item.transform.position);
            Quaternion localRotation = Quaternion.Inverse(root.transform.rotation) * item.transform.rotation;
            output.Add(new StructureItemStamp
            {
                ItemPrefabId = !string.IsNullOrWhiteSpace(metadata?.ItemPrefabId)
                    ? metadata.ItemPrefabId
                    : item.itemData.IDName,
                LocalPosition = new Vector2(localPosition.x, localPosition.y),
                RotationZ = localRotation.eulerAngles.z,
                Scale = item.transform.localScale,
                OrientationMode = metadata != null
                    ? metadata.OrientationMode
                    : StructureOrientationMode.KeepWorldOrientation,
                Optional = metadata != null && metadata.Optional,
                SpawnChance = metadata != null ? metadata.SpawnChance : 1f,
                SeedSalt = metadata != null ? metadata.SeedSalt : 0
            });
        }

        StructureItemAuthoring[] metadataItems =
            root.GetComponentsInChildren<StructureItemAuthoring>(true);
        for (int i = 0; i < metadataItems.Length; i++)
        {
            StructureItemAuthoring metadata = metadataItems[i];
            if (metadata == null || bakedMetadata.Contains(metadata))
                continue;

            Vector3 localPosition =
                root.transform.InverseTransformPoint(metadata.transform.position);
            Quaternion localRotation =
                Quaternion.Inverse(root.transform.rotation) * metadata.transform.rotation;
            output.Add(new StructureItemStamp
            {
                ItemPrefabId = metadata.ItemPrefabId,
                LocalPosition = new Vector2(localPosition.x, localPosition.y),
                RotationZ = localRotation.eulerAngles.z,
                Scale = metadata.transform.localScale,
                OrientationMode = metadata.OrientationMode,
                Optional = metadata.Optional,
                SpawnChance = metadata.SpawnChance,
                SeedSalt = metadata.SeedSalt
            });
        }

        return output
            .OrderBy(stamp => stamp.LocalPosition.x)
            .ThenBy(stamp => stamp.LocalPosition.y)
            .ThenBy(stamp => stamp.ItemPrefabId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<StructureMarkerData> BakeMarkers(StructureAuthoringRoot root)
    {
        StructureMarkerAuthoring[] markers = root.GetComponentsInChildren<StructureMarkerAuthoring>(true);
        List<StructureMarkerData> output = new(markers.Length);
        for (int i = 0; i < markers.Length; i++)
        {
            StructureMarkerAuthoring marker = markers[i];
            Vector3 local = root.transform.InverseTransformPoint(marker.transform.position);
            output.Add(new StructureMarkerData
            {
                Type = marker.Type,
                MarkerId = marker.MarkerId,
                LocalPosition = new Vector2(local.x, local.y),
                Size = marker.Size,
                ContentId = marker.ContentId,
                RotationZ =
                    (Quaternion.Inverse(root.transform.rotation) *
                     marker.transform.rotation).eulerAngles.z,
                OrientationMode = marker.OrientationMode,
                Chance = marker.Chance,
                SeedSalt = marker.SeedSalt
            });
        }

        return output
            .OrderBy(marker => marker.Type)
            .ThenBy(marker => marker.MarkerId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<StructureTileStamp> BakeTiles(StructureAuthoringRoot root)
    {
        List<StructureTileStamp> output = new();
        if (root.Tilemap == null)
            return output;

        Dictionary<TileBase, List<Tile_Block>> lookup = BuildTileBlockLookup();
        foreach (Vector3Int position in root.Tilemap.cellBounds.allPositionsWithin)
        {
            TileBase tile = root.Tilemap.GetTile(position);
            if (tile == null ||
                !lookup.TryGetValue(tile, out List<Tile_Block> blocks) ||
                blocks.Count != 1)
            {
                continue;
            }

            output.Add(new StructureTileStamp
            {
                LocalPosition = new Vector2Int(position.x, position.y),
                TileBlock = blocks[0],
                WriteMode = StructureTileWriteMode.ReplaceAll
            });
        }

        return output
            .OrderBy(stamp => stamp.LocalPosition.x)
            .ThenBy(stamp => stamp.LocalPosition.y)
            .ToList();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class StructureAuthoringPrefabUtility
{
    private const string AuthoringFolder =
        "Assets/Editor/FlatWorld/Structures/AuthoringAssets";

    public static GameObject OpenOrCreate(StructureDefinitionSO definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        StructureTemplateSO template = GetPrimaryTemplate(definition);
        if (template == null)
            throw new InvalidOperationException($"遗迹 {definition.StructureId} 没有可编辑模板。");

        StructureEditorWindow.EnsureFolder(AuthoringFolder);
        GameObject prefab = FindAuthoringPrefab(definition, template);
        if (prefab == null)
            prefab = CreateFromTemplate(definition, template);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        AssetDatabase.OpenAsset(prefab);
        return prefab;
    }

    public static StructureTemplateSO GetPrimaryTemplate(StructureDefinitionSO definition)
    {
        return definition?.Templates?
            .Where(entry => entry?.Template != null && entry.Weight > 0f)
            .OrderByDescending(entry => entry.Weight)
            .ThenBy(entry => entry.Template.TemplateId, StringComparer.Ordinal)
            .Select(entry => entry.Template)
            .FirstOrDefault();
    }

    public static GameObject FindItemPrefabById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        string[] likelyGuids = AssetDatabase.FindAssets($"{itemId} t:Prefab");
        GameObject match = FindMatchingItemPrefab(likelyGuids, itemId);
        if (match != null)
            return match;

        return FindMatchingItemPrefab(AssetDatabase.FindAssets("t:Prefab"), itemId);
    }

    public static string GetItemId(GameObject prefab)
    {
        if (prefab == null)
            return string.Empty;

        Item item = prefab.GetComponent<Item>() ?? prefab.GetComponentInChildren<Item>(true);
        try
        {
            return item?.itemData?.IDName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static GameObject FindAuthoringPrefab(
        StructureDefinitionSO definition,
        StructureTemplateSO template)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { AuthoringFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            StructureAuthoringRoot root = prefab?.GetComponent<StructureAuthoringRoot>();
            if (root != null && (root.Definition == definition || root.Template == template))
                return prefab;
        }

        return null;
    }

    private static GameObject CreateFromTemplate(
        StructureDefinitionSO definition,
        StructureTemplateSO template)
    {
        string safeId = string.IsNullOrWhiteSpace(definition.StructureId)
            ? definition.name
            : definition.StructureId;
        string path = AssetDatabase.GenerateUniqueAssetPath(
            $"{AuthoringFolder}/{safeId}_Authoring.prefab");

        GameObject authoring = new(safeId);
        try
        {
            StructureAuthoringRoot root = authoring.AddComponent<StructureAuthoringRoot>();
            if (root == null)
                throw new InvalidOperationException("无法添加StructureAuthoringRoot。");

            root.Definition = definition;
            root.Template = template;
            root.Size = template.Size;
            root.Pivot = template.Pivot;
            authoring.AddComponent<Grid>();

            GameObject tilemapObject = new("Tilemap", typeof(Tilemap), typeof(TilemapRenderer));
            tilemapObject.transform.SetParent(authoring.transform, false);
            root.Tilemap = tilemapObject.GetComponent<Tilemap>();

            RestoreTiles(root, template);
            RestoreItems(root, template);
            RestoreMarkers(root, template);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(authoring, path);
            if (prefab == null)
                throw new InvalidOperationException($"创建Authoring Prefab失败：{path}");

            AssetDatabase.SaveAssets();
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(authoring);
        }
    }

    private static void RestoreTiles(
        StructureAuthoringRoot root,
        StructureTemplateSO template)
    {
        if (root.Tilemap == null || template.TileStamps == null)
            return;

        for (int i = 0; i < template.TileStamps.Count; i++)
        {
            StructureTileStamp stamp = template.TileStamps[i];
            TileBase tile = stamp?.TileBlock?.GetTileBaseAsset();
            if (tile == null)
                continue;

            root.Tilemap.SetTile(
                new Vector3Int(stamp.LocalPosition.x, stamp.LocalPosition.y, 0),
                tile);
        }
    }

    private static void RestoreItems(
        StructureAuthoringRoot root,
        StructureTemplateSO template)
    {
        if (template.ItemStamps == null)
            return;

        for (int i = 0; i < template.ItemStamps.Count; i++)
        {
            StructureItemStamp stamp = template.ItemStamps[i];
            if (stamp == null || string.IsNullOrWhiteSpace(stamp.ItemPrefabId))
                continue;

            GameObject prefab = FindItemPrefabById(stamp.ItemPrefabId);
            GameObject instance = prefab != null
                ? PrefabUtility.InstantiatePrefab(prefab) as GameObject
                : new GameObject($"Missing_{stamp.ItemPrefabId}");
            if (instance == null)
                continue;

            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition =
                new Vector3(stamp.LocalPosition.x, stamp.LocalPosition.y, 0f);
            instance.transform.localRotation = Quaternion.Euler(0f, 0f, stamp.RotationZ);
            instance.transform.localScale =
                stamp.Scale == Vector3.zero ? Vector3.one : stamp.Scale;

            Item item = instance.GetComponent<Item>() ??
                        instance.GetComponentInChildren<Item>(true);
            GameObject metadataOwner = item != null ? item.gameObject : instance;
            StructureItemAuthoring metadata =
                metadataOwner.GetComponent<StructureItemAuthoring>() ??
                metadataOwner.AddComponent<StructureItemAuthoring>();
            metadata.ItemPrefabId = stamp.ItemPrefabId;
            metadata.SourcePrefab = prefab;
            metadata.OrientationMode = stamp.OrientationMode;
            metadata.Optional = stamp.Optional;
            metadata.SpawnChance = stamp.SpawnChance;
            metadata.SeedSalt = stamp.SeedSalt;
        }
    }

    private static void RestoreMarkers(
        StructureAuthoringRoot root,
        StructureTemplateSO template)
    {
        if (template.Markers == null)
            return;

        for (int i = 0; i < template.Markers.Count; i++)
        {
            StructureMarkerData source = template.Markers[i];
            if (source == null)
                continue;

            GameObject markerObject = new($"{source.Type}_{source.MarkerId}");
            markerObject.transform.SetParent(root.transform, false);
            markerObject.transform.localPosition =
                new Vector3(source.LocalPosition.x, source.LocalPosition.y, 0f);
            StructureMarkerAuthoring marker =
                markerObject.AddComponent<StructureMarkerAuthoring>();
            marker.Type = source.Type;
            marker.MarkerId = source.MarkerId;
            marker.Size = source.Size;
            marker.ContentId = source.ContentId;
            markerObject.transform.localRotation =
                Quaternion.Euler(0f, 0f, source.RotationZ);
            marker.OrientationMode = source.OrientationMode;
            marker.Chance = source.Chance;
            marker.SeedSalt = source.SeedSalt;
        }
    }

    private static GameObject FindMatchingItemPrefab(
        IReadOnlyList<string> guids,
        string itemId)
    {
        for (int i = 0; i < guids.Count; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (string.Equals(GetItemId(prefab), itemId, StringComparison.Ordinal))
                return prefab;
        }

        return null;
    }
}

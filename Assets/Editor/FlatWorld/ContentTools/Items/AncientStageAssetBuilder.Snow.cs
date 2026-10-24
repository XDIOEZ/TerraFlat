using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static partial class AncientStageAssetBuilder
{
    /// <summary>创建独立雪覆盖和 Shader 降雪资源，并装配到正式区块表现。</summary>
    private static void BuildSnow()
    {
        const string configPath = "Assets/Resources/Weather/SnowCoverConfig.asset";
        if (AssetDatabase.LoadAssetAtPath<SnowCoverConfig>(configPath) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<SnowCoverConfig>(), configPath);
        const string materialPath = "Assets/9_Shaders/Material/Snowflake.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/9_Shaders/Shader/Snowflake.shader"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        const string effectPath = "Assets/Resources/Weather/SnowEffect.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(effectPath) == null)
        {
            GameObject root = new("SnowEffect", typeof(MeshFilter), typeof(MeshRenderer), typeof(SnowEffectController));
            try
            {
                root.GetComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, effectPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        const string chunkPath = "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab";
        GameObject chunk = PrefabUtility.LoadPrefabContents(chunkPath);
        try
        {
            Transform target = chunk.transform.Find("SeasonalSnow");
            if (target == null)
            {
                GameObject layer = new("SeasonalSnow", typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(chunk.transform, false); target = layer.transform;
                var source = chunk.transform.Find("Ground").GetComponent<TilemapRenderer>();
                var renderer = layer.GetComponent<TilemapRenderer>();
                renderer.sharedMaterial = source.sharedMaterial; renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = 3;
            }
            var snow = chunk.GetComponent<ChunkSnowCoverRenderer>() ?? chunk.AddComponent<ChunkSnowCoverRenderer>();
            Transform wallTarget = chunk.transform.Find("SeasonalSnowWalls");
            if (wallTarget == null)
            {
                GameObject layer = new("SeasonalSnowWalls", typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(chunk.transform, false); wallTarget = layer.transform;
                var source = chunk.transform.Find("Ground").GetComponent<TilemapRenderer>();
                var renderer = layer.GetComponent<TilemapRenderer>();
                renderer.sharedMaterial = source.sharedMaterial; renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = 5;
            }
            SerializedObject fields = new(snow);
            fields.FindProperty("snowTile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Tile>("Assets/7_Tiles/Base/TileBase_Snow.asset");
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(chunk, chunkPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(chunk); }
    }
}

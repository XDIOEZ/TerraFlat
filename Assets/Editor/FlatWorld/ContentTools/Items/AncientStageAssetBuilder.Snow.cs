using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static partial class AncientStageAssetBuilder
{
    /// <summary>创建独立雪覆盖和雪粒子资源，并装配到正式区块表现。</summary>
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
            GameObject root = new("SnowEffect");
            try
            {
                GameObject flakes = new("Snowflakes"); flakes.transform.SetParent(root.transform, false);
                flakes.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ParticleSystem particles = flakes.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.startSpeed = 2.5f; main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.startLifetime = 12f; main.maxParticles = 1500;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new Color(0.88f, 0.94f, 1f, 0.9f);
                var emission = particles.emission; emission.rateOverTime = 100f;
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(36f, 1f, 0.1f);
                var noise = particles.noise; noise.enabled = true; noise.strength = 0.3f; noise.frequency = 0.18f;
                var renderer = flakes.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = 100;
                root.AddComponent<RainEffectController>();
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
            fields.FindProperty("tilemap").objectReferenceValue = target.GetComponent<Tilemap>();
            fields.FindProperty("wallTilemap").objectReferenceValue = wallTarget.GetComponent<Tilemap>();
            fields.FindProperty("snowTile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Tile_Block>("Assets/4_ScriptObjects/World/Tiles/Tile_Snow.asset").TileBase;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(chunk, chunkPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(chunk); }
    }
}

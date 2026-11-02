using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 草地共享纹理网格回归

    private static JObject CaptureGrassMeshBatch(JObject args)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Application.isPlaying)
            throw new InvalidOperationException("草网格规则测试只允许在编辑态隔离场景执行。");

        Scene scene = default;
        Sprite first = null, second = null;
        Texture2D atlas = null;
        Material material = null;
        ChunkTilemapRenderer ownerA = null, ownerB = null;
        try
        {
            scene = CreateGameplayTestProbeScene("[LuaGrassMeshBatch]");
            var a = new GameObject("GrassOwnerA");
            SceneManager.MoveGameObjectToScene(a, scene);
            ownerA = a.AddComponent<ChunkTilemapRenderer>();
            var b = new GameObject("GrassOwnerB");
            SceneManager.MoveGameObjectToScene(b, scene);
            ownerB = b.AddComponent<ChunkTilemapRenderer>();

            atlas = new Texture2D(8, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var colors = new Color[32];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            atlas.SetPixels(colors);
            atlas.Apply();
            first = Sprite.Create(atlas, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 32f,
                0, SpriteMeshType.FullRect);
            second = Sprite.Create(atlas, new Rect(4, 0, 4, 4), new Vector2(.5f, .5f), 32f,
                0, SpriteMeshType.FullRect);
            Material template = Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
            if (template == null) throw new InvalidOperationException("缺少草网格 Shader 测试资源。");
            material = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_GrassSwayEnabled", 1f);

            ChunkGrassMeshBatchService.Set(ownerA, 0, first, material,
                Matrix4x4.Translate(new Vector3(1f, 1f, 0f)), Color.white);
            ChunkGrassMeshBatchService.Set(ownerA, 1, second, material,
                Matrix4x4.Translate(new Vector3(2f, 1f, 0f)), Color.green);
            ChunkGrassMeshBatchService.Set(ownerB, 0, first, material,
                Matrix4x4.Translate(new Vector3(3f, 1f, 0f)), Color.white);
            ChunkGrassMeshBatchService.FlushForTests();
            var merged = ChunkGrassMeshBatchService.GetStatsForTests();
            int ownerCount = ChunkGrassMeshBatchService.GetOwnerVisualCount(ownerA);
            ChunkGrassMeshBatchService.Clear(ownerA, 0);
            ChunkGrassMeshBatchService.FlushForTests();
            var removed = ChunkGrassMeshBatchService.GetStatsForTests();

            ownerB.transform.position = new Vector3(140f, 0f, 0f);
            ChunkGrassMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkGrassMeshBatchService.FlushForTests();
            var shifted = ChunkGrassMeshBatchService.GetStatsForTests();
            ownerB.transform.position = Vector3.zero;
            ChunkGrassMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkGrassMeshBatchService.FlushForTests();
            var returned = ChunkGrassMeshBatchService.GetStatsForTests();

            ChunkGrassMeshBatchService.SetOwnerExternalProjection(ownerB,
                Matrix4x4.Translate(new Vector3(20f, 0f, 0f)), false, null);
            ChunkGrassMeshBatchService.FlushForTests();
            var projected = ChunkGrassMeshBatchService.GetStatsForTests();
            ChunkGrassMeshBatchService.ClearOwnerExternalProjection(ownerB);
            ChunkGrassMeshBatchService.FlushForTests();
            var restored = ChunkGrassMeshBatchService.GetStatsForTests();

            ChunkGrassMeshBatchService.Clear(ownerA, 1);
            ChunkGrassMeshBatchService.Clear(ownerB, 0);
            var cleared = ChunkGrassMeshBatchService.GetStatsForTests();
            return new JObject
            {
                ["mergedBatches"] = merged.Batches, ["mergedPages"] = merged.Pages,
                ["mergedVertices"] = merged.Vertices, ["nativeMaterials"] = merged.NativeMaterials,
                ["ownerVisuals"] = ownerCount,
                ["removedVertices"] = removed.Vertices, ["shiftedBatches"] = shifted.Batches,
                ["returnedBatches"] = returned.Batches,
                ["projectedBatches"] = projected.Batches, ["restoredBatches"] = restored.Batches,
                ["clearedBatches"] = cleared.Batches, ["clearedPages"] = cleared.Pages
            };
        }
        finally
        {
            if (ownerA != null) ChunkGrassMeshBatchService.UnregisterOwner(ownerA);
            if (ownerB != null) ChunkGrassMeshBatchService.UnregisterOwner(ownerB);
            if (scene.IsValid()) CloseGameplayTestProbeScene(scene);
            SharedSpriteMeshCache.ReleaseTemporarySpriteGeometryForTests(first);
            SharedSpriteMeshCache.ReleaseTemporarySpriteGeometryForTests(second);
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
#else
        return new JObject { ["unavailable"] = true };
#endif
    }

    #endregion
}

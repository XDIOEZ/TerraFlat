using System;
using System.Collections.Generic;
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

            ChunkSharedSpriteMeshBatchService.Set(ownerA, 0, first, material,
                Matrix4x4.Translate(new Vector3(1f, 1f, 0f)), Color.white);
            ChunkSharedSpriteMeshBatchService.Set(ownerA, 1, second, material,
                Matrix4x4.Translate(new Vector3(2f, 1f, 0f)), Color.green);
            ChunkSharedSpriteMeshBatchService.Set(ownerB, 0, first, material,
                Matrix4x4.Translate(new Vector3(3f, 1f, 0f)), Color.white);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var merged = ChunkSharedSpriteMeshBatchService.GetStatsForTests();
            int ownerCount = ChunkSharedSpriteMeshBatchService.GetOwnerVisualCount(ownerA);
            ChunkSharedSpriteMeshBatchService.Clear(ownerA, 0);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var removed = ChunkSharedSpriteMeshBatchService.GetStatsForTests();

            ownerB.transform.position = new Vector3(140f, 0f, 0f);
            ChunkSharedSpriteMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var shifted = ChunkSharedSpriteMeshBatchService.GetStatsForTests();
            ownerB.transform.position = Vector3.zero;
            ChunkSharedSpriteMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var returned = ChunkSharedSpriteMeshBatchService.GetStatsForTests();

            ChunkSharedSpriteMeshBatchService.SetOwnerExternalProjection(ownerB,
                Matrix4x4.Translate(new Vector3(20f, 0f, 0f)), false, null);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var projected = ChunkSharedSpriteMeshBatchService.GetStatsForTests();
            ChunkSharedSpriteMeshBatchService.ClearOwnerExternalProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var restored = ChunkSharedSpriteMeshBatchService.GetStatsForTests();

            ChunkSharedSpriteMeshBatchService.Clear(ownerA, 1);
            ChunkSharedSpriteMeshBatchService.Clear(ownerB, 0);
            var cleared = ChunkSharedSpriteMeshBatchService.GetStatsForTests();
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
            if (ownerA != null) ChunkSharedSpriteMeshBatchService.UnregisterOwner(ownerA);
            if (ownerB != null) ChunkSharedSpriteMeshBatchService.UnregisterOwner(ownerB);
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

    /// <summary>用不同 Sprite 的真实阴影几何检查共享纹理合批、顶点阴影参数与增量生命周期。</summary>
    private static JObject CaptureNaturalShadowMeshBatch(JObject args)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Application.isPlaying)
            throw new InvalidOperationException("自然物阴影网格规则只允许在编辑态隔离场景执行。");

        Scene scene = default;
        Texture2D texture = null;
        Sprite first = null, second = null;
        Material material = null;
        ChunkTilemapRenderer ownerA = null, ownerB = null;
        try
        {
            scene = CreateGameplayTestProbeScene("[LuaNaturalShadowMeshBatch]");
            var a = new GameObject("NaturalShadowOwnerA");
            SceneManager.MoveGameObjectToScene(a, scene);
            ownerA = a.AddComponent<ChunkTilemapRenderer>();
            var b = new GameObject("NaturalShadowOwnerB");
            SceneManager.MoveGameObjectToScene(b, scene);
            ownerB = b.AddComponent<ChunkTilemapRenderer>();
            texture = new Texture2D(16, 8, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            var pixels = new Color[128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            first = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 32f,
                0, SpriteMeshType.FullRect);
            second = Sprite.Create(texture, new Rect(8, 0, 8, 8), new Vector2(.5f, .5f), 32f,
                0, SpriteMeshType.FullRect);
            var template = Resources.Load<Material>("Config/WorldModel/BRG/ChunkBRG-Sprite-Lit");
            if (template == null) throw new InvalidOperationException("缺少太阳阴影网格材质模板。");
            material = new Material(template) { hideFlags = HideFlags.HideAndDontSave };

            ChunkBatchRendererGroupService.InstanceData ShadowData(Sprite sprite, float worldX, float crop)
            {
                var geometry = SharedSpriteMeshCache.GetSunShadowGeometry(sprite);
                var data = ChunkBatchRendererGroupService.InstanceData.Create(
                    Matrix4x4.Translate(new Vector3(worldX, 2f, 0f)),
                    new Vector4(0f, geometry.TexelScale, 1f, 0f), geometry.UvBounds, Color.white);
                data.FlowX = new Vector4(worldX, 2f, 1f, 3f);
                data.FlowY = new Vector4(geometry.UvToLocalY.x, geometry.UvToLocalY.y,
                    geometry.UvToLocalY.z - crop, crop > 0f ? 1f : 0f);
                data.Transform0.w = -1f;
                return data;
            }

            ChunkSharedSpriteMeshBatchService.SetNaturalShadow(ownerA, 0, first, material, ShadowData(first, 1f, 0f));
            ChunkSharedSpriteMeshBatchService.SetNaturalShadow(ownerA, 1, second, material, ShadowData(second, 2f, .2f));
            ChunkSharedSpriteMeshBatchService.SetNaturalShadow(ownerB, 0, first, material, ShadowData(first, 3f, 0f));
            ChunkSharedSpriteMeshBatchService.Set(ownerA, 100, first, material,
                Matrix4x4.Translate(new Vector3(4f, 2f, 0f)), Color.white);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var merged = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            var grassSeparate = ChunkSharedSpriteMeshBatchService.GetStatsForTests();
            ChunkSharedSpriteMeshBatchService.ClearGrass(ownerA);
            var shadowSurvivedGrassClear = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            var renderers = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                if (renderer != null && renderer.gameObject.scene == scene &&
                    renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.IsKeywordEnabled("_CHUNK_RESOURCE"))
                    renderers.Add(renderer);
            bool shaderReady = renderers.Count == 1 && renderers[0].sharedMaterial.IsKeywordEnabled("_CHUNK_NATIVE_BATCH");
            bool differentUvs = false, preservedShadowParams = false;
            if (renderers.Count == 1)
            {
                Mesh mesh = renderers[0].GetComponent<MeshFilter>().sharedMesh;
                Vector2[] uv = mesh.uv;
                Vector2[] firstUv = SharedSpriteMeshCache.GetSunShadowGeometry(first).Geometry.Uv;
                Vector2[] secondUv = SharedSpriteMeshCache.GetSunShadowGeometry(second).Geometry.Uv;
                var data0 = new List<Vector4>();
                var bounds = new List<Vector4>();
                var clip = new List<Vector4>();
                mesh.GetUVs(3, data0);
                mesh.GetUVs(4, bounds);
                mesh.GetUVs(7, clip);
                differentUvs = Array.Exists(uv, v => v == firstUv[0]) &&
                    Array.Exists(uv, v => v == secondUv[0]) && firstUv[0] != secondUv[0];
                preservedShadowParams = data0.Count == 12 && bounds.Count == 12 && clip.Count == 12;
                if (preservedShadowParams)
                {
                    for (int i = 0; i < 12; i++)
                        preservedShadowParams &= Mathf.Abs(data0[i].z - 1f) < .00001f &&
                            (bounds[i] == SharedSpriteMeshCache.GetSunShadowGeometry(first).UvBounds ||
                             bounds[i] == SharedSpriteMeshCache.GetSunShadowGeometry(second).UvBounds);
                    preservedShadowParams &= clip.Exists(v => v.w > .5f) && clip.Exists(v => v.w < .5f);
                }
            }
            ChunkSharedSpriteMeshBatchService.ClearNaturalShadow(ownerA, 0);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var removed = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            ownerB.transform.position = new Vector3(140f, 0f, 0f);
            ChunkSharedSpriteMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var shifted = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            ownerB.transform.position = Vector3.zero;
            ChunkSharedSpriteMeshBatchService.RefreshOwnerProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var returned = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            ChunkSharedSpriteMeshBatchService.SetOwnerExternalProjection(ownerB,
                Matrix4x4.Translate(new Vector3(20f, 0f, 0f)), false, null);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var projected = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            ChunkSharedSpriteMeshBatchService.ClearOwnerExternalProjection(ownerB);
            ChunkSharedSpriteMeshBatchService.FlushForTests();
            var restored = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            ChunkSharedSpriteMeshBatchService.ClearNaturalShadow(ownerA, 1);
            ChunkSharedSpriteMeshBatchService.ClearNaturalShadow(ownerB, 0);
            var cleared = ChunkSharedSpriteMeshBatchService.GetStatsForTests(
                ChunkBatchRendererGroupService.VisualLayer.NaturalShadow);
            return new JObject
            {
                ["mergedBatches"] = merged.Batches, ["mergedPages"] = merged.Pages,
                ["mergedVertices"] = merged.Vertices, ["resourceShader"] = shaderReady,
                ["grassSeparate"] = grassSeparate.Batches == 1 && grassSeparate.Pages == 1,
                ["grassClearKeepsShadow"] = shadowSurvivedGrassClear.Batches == 1 &&
                    shadowSurvivedGrassClear.Vertices == 12,
                ["differentUvs"] = differentUvs, ["shadowParams"] = preservedShadowParams,
                ["removedVertices"] = removed.Vertices, ["shiftedBatches"] = shifted.Batches,
                ["returnedBatches"] = returned.Batches, ["projectedBatches"] = projected.Batches,
                ["restoredBatches"] = restored.Batches, ["clearedBatches"] = cleared.Batches
            };
        }
        finally
        {
            if (ownerA != null) ChunkSharedSpriteMeshBatchService.UnregisterOwner(ownerA);
            if (ownerB != null) ChunkSharedSpriteMeshBatchService.UnregisterOwner(ownerB);
            if (scene.IsValid()) CloseGameplayTestProbeScene(scene);
            SharedSpriteMeshCache.ReleaseTemporarySpriteGeometryForTests(first);
            SharedSpriteMeshCache.ReleaseTemporarySpriteGeometryForTests(second);
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
#else
        return new JObject { ["unavailable"] = true };
#endif
    }

    #endregion
}

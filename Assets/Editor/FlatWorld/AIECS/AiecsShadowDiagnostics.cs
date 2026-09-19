using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>
    /// 脚底阴影的离线几何维护和确定性验证；只更新目录的非透明边界，不重导图集或修改源贴图。
    /// 批次验证覆盖 0、1、4096、4097 和 20000 个阴影以及清空、复用、销毁；报告写入 Library。
    /// </summary>
    public static class AiecsShadowDiagnostics
    {
        #region 非透明边界维护
        private const string CatalogPath = "Assets/6_Art/Generated/AIECS/生物动画目录.asset";

        /// <summary>从已生成图集补齐脚底边界，保留现有动画、图集和 GUID。</summary>
        [MenuItem("FlatWorld/AIECS/阴影 更新非透明边界")]
        public static void RefreshVisibleBounds()
        {
            AIECSCatalogAudit.RequireEditMode();
            AiecsAnimationCatalog catalog = AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(CatalogPath);
            if (catalog == null || catalog.Material == null) throw new InvalidDataException("缺少生物动画目录。");
            string texturePath = AssetDatabase.GetAssetPath(catalog.Material.mainTexture);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(texturePath))) throw new InvalidDataException("无法读取现有图集。");
                Color32[] pixels = texture.GetPixels32();
                foreach (AiecsSpriteGeometry sprite in catalog.Sprites)
                {
                    Rect uv = sprite.AtlasRect;
                    var region = new RectInt(Mathf.RoundToInt(uv.x * texture.width), Mathf.RoundToInt(uv.y * texture.height),
                        Mathf.RoundToInt(uv.width * texture.width), Mathf.RoundToInt(uv.height * texture.height));
                    sprite.VisibleRect = MeasureVisibleBounds(pixels, texture.width, region, sprite.LocalRect);
                }
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                Debug.Log($"[AIECS 阴影] 已补齐 {catalog.Sprites.Length} 张精灵的非透明边界；图集未改动。");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        /// <summary>编辑器冷路径扫描 Alpha；局部坐标保持原图 Pivot 和 PPU。</summary>
        internal static Rect MeasureVisibleBounds(Color32[] pixels, int textureWidth, RectInt region, Rect local)
        {
            int minX = region.width, minY = region.height, maxX = -1, maxY = -1;
            for (int y = 0; y < region.height; y++)
            for (int x = 0; x < region.width; x++)
            {
                if (pixels[(region.y + y) * textureWidth + region.x + x].a < 8) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            if (maxX < minX || maxY < minY) return default;
            return new Rect(local.xMin + local.width * minX / region.width,
                local.yMin + local.height * minY / region.height,
                local.width * (maxX - minX + 1) / region.width,
                local.height * (maxY - minY + 1) / region.height);
        }
        #endregion

        #region 确定性回归
        /// <summary>验证共享材质、非透明脚底、索引边界和所有权清理，不替代真实 Play 截图。</summary>
        [MenuItem("FlatWorld/AIECS/阴影 验证批次与生命周期")]
        public static void Validate()
        {
            AIECSCatalogAudit.RequireEditMode();
            var catalog = AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(CatalogPath);
            var material = Resources.Load<Material>("AIECS/ActorBlobShadow");
            Require(material != null && material.shader != null && material.shader.isSupported, "阴影材质/Shader 不可用。");
            Require(!ShaderUtil.ShaderHasError(material.shader), "阴影 Shader 编译错误。");
            Require(material.FindPass("BlobShadow") >= 0, "缺少 Universal2D 阴影 Pass。");
            int measured = 0;
            foreach (AiecsActorVisual actor in catalog.Actors)
            {
                AiecsAnimationFrame frame = actor.Clips[0].Sample(0f);
                var sprite = catalog.Sprites[frame.Sprite];
                Require(sprite.VisibleRect.width > 0 && sprite.VisibleRect.height > 0, actor.Id + " 非透明边界缺失。");
                Vector4 shape = AiecsShadowRenderer.MeasureFootprint(actor, sprite, frame);
                Require(float.IsFinite(shape.x) && float.IsFinite(shape.y) && shape.z >= 0.18f && shape.z <= 2.5f && shape.w > 0,
                    actor.Id + " 阴影尺寸无效。");
                measured++;
            }

            var pixels = new Color32[16]; pixels[5] = new Color32(255, 255, 255, 255);
            Rect visible = MeasureVisibleBounds(pixels, 4, new RectInt(0, 0, 4, 4), new Rect(-2, -2, 4, 4));
            Require(visible == new Rect(-1, -1, 1, 1), "非透明边界没有排除留白。");
            Require(Mathf.Abs(AiecsShadowRenderer.ResolveOpacity(0.4f, 0.5f, 0, 0) - 0.2f) < 0.00001f, "昼夜与主体透明度未正确组合。");
            Require(AiecsShadowRenderer.ResolveOpacity(0f, 1f, 0, 0) == 0f, "零光照仍然绘制阴影。");
            Require(AiecsShadowRenderer.ResolveOpacity(0.4f, 0f, 0, 0) == 0f, "已完全淡出的主体仍有阴影。");
            Require(AiecsShadowRenderer.ResolveOpacity(0.4f, 1f, 0.1f, 0) == 0f, "浅水状态仍有阴影。");
            Require(AiecsShadowRenderer.ResolveOpacity(0.4f, 1f, 0, 0.5f) == 0f, "水态过渡仍有阴影。");

            int initialRoots = CountRoots();
            var renderer = new AiecsShadowRenderer(SceneManager.GetActiveScene());
            var counts = new[] { 0, 1, 4096, 4097, 20000, 1, 0 };
            try
            {
                foreach (int count in counts)
                {
                    renderer.Begin();
                    for (int i = 0; i < count; i++)
                        renderer.Append(new Vector2(i % 160, i / 160), new Vector4(0, 0, 0.8f, 0.25f), false, 0.4f);
                    renderer.End();
                    Require(renderer.ShadowCount == count, "阴影数量不一致。");
                    Require(renderer.BatchCount == (count + 4095) / 4096, "批次数量不一致。");
                    var nodes = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == "AIECS 脚底阴影批次").ToArray();
                    Require(nodes.Count(g => g.GetComponent<MeshRenderer>().enabled) == renderer.BatchCount, "旧批次没有正确隐藏。");
                    foreach (var node in nodes.Where(g => g.GetComponent<MeshRenderer>().enabled))
                    {
                        var mesh = node.GetComponent<MeshFilter>().sharedMesh;
                        Require(mesh.GetVertexBufferStride(0) == 24, "顶点布局与上传结构不一致。");
                        Require(mesh.GetIndexCount(0) <= 4096 * 6, "索引超过单批容量。");
                    }
                }
                renderer.Begin(); renderer.Append(Vector2.zero, new Vector4(0, 0, 1, 0.3f), false, 0f); renderer.End();
                Require(renderer.ShadowCount == 0, "透明阴影仍然提交。");
                renderer.Hide(); Require(renderer.BatchCount == 0, "隐藏未清除批次数。");
            }
            finally { renderer.Dispose(); }
            Require(CountRoots() == initialRoots, "释放后存在残留阴影节点。");
            Directory.CreateDirectory("Library/FlatWorldGameplayMCP/Captures/BlobShadows");
            File.WriteAllText("Library/FlatWorldGameplayMCP/Captures/BlobShadows/validation.json",
                JsonConvert.SerializeObject(new { passed = true, actors = measured, counts, vertexStride = 24,
                    maxPerBatch = 4096, cleanup = true, opacityWaterAndFadeRules = true }, Formatting.Indented));
            Debug.Log($"[AIECS 阴影] 验证通过：{measured} 物种、0～20000 阴影、复用/隐藏/销毁。");
        }

        /// <summary>只数当前场景中本系统的根节点。</summary>
        private static int CountRoots() => SceneManager.GetActiveScene().GetRootGameObjects().Count(g => g.name == "AIECS 脚底阴影批次");

        /// <summary>失败时保留可定位的断言，不吞掉验证错误。</summary>
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[AIECS 阴影验证] " + message);
        }
        #endregion
    }
}

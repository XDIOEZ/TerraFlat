using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>
    /// 单图摇摆导出器：复用正式 Manifest 的继承合并、身体首图和动画模块参数。
    /// 不实例化正式 Actor、不改源贴图；真实 Sprite 三角形先解包，再生成禁止缩图的专用点采样图集。
    /// </summary>
    internal sealed class AiecsAnimationExporter
    {
        // 导出路径和发现缓存，原型资源独立于正式 Actor 目录。
        internal const string OutputRoot = "Assets/6_Art/Generated/Actors/AIECS";
        private readonly List<AddressableAssetEntry> addresses = new();
        private readonly List<Sprite> sprites = new();
        private readonly Dictionary<Sprite, int> spriteIndices = new();
        private readonly List<string> diagnostics = new();
        private readonly SortedSet<string> dependencies = new(StringComparer.Ordinal);

        #region 目录与资源发现
        /// <summary>通过项目现有 Addressables 设置发现源资源，不使用 sourcePrefab 回推运行时来源。</summary>
        internal AiecsAnimationExporter()
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                throw new InvalidDataException("项目缺少 Addressables 设置。");
            AddressableAssetSettingsDefaultObject.Settings.GetAllAssets(addresses, false);
        }

        /// <summary>生成完整目录；显式传入本工具的旧目录时保留资源 GUID 并重新导出。</summary>
        internal AiecsAnimationCatalog Export(AiecsAnimationCatalog destination = null)
        {
            string path = destination == null ? null : AssetDatabase.GetAssetPath(destination);
            if (destination != null && (Path.GetDirectoryName(path)?.Replace('\\', '/') != OutputRoot ||
                destination.Material == null ||
                !AssetDatabase.GetAssetPath(destination.Material).StartsWith("Assets/9_Shaders/Material/AIECS/", StringComparison.Ordinal) ||
                AssetDatabase.GetAssetPath(destination.Material.mainTexture) != OutputRoot + "/" + Path.GetFileNameWithoutExtension(path) + "_图集.png"))
                throw new InvalidDataException("只能原地重导本工具生成的完整 AIECS 目录，不能覆盖其它美术资源。");
            List<ItemDefinitionDto> definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions();
            var catalog = ScriptableObject.CreateInstance<AiecsAnimationCatalog>();
            catalog.name = "生物动画目录";
            try
            {
                catalog.Actors = definitions.Where(x => !x.Abstract).OrderBy(x => x.Id, StringComparer.Ordinal)
                    .Select(ExportActor).ToArray();
                if (catalog.Actors.Length == 0 || sprites.Count == 0)
                    throw new InvalidDataException("启用的 Actor 目录没有可导出的图片帧。");
                EnsureFolder(OutputRoot);
                path ??= AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/生物动画目录.asset");
                string stem = Path.GetFileNameWithoutExtension(path);
                catalog.Sprites = ExportAtlas(OutputRoot + "/" + stem + "_图集.png", out Texture2D atlas);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/9_Shaders/Shader/AiecsSpriteLit.shader");
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new InvalidDataException("AIECS Lit Shader 尚未通过编译。");
                EnsureFolder("Assets/9_Shaders/Material/AIECS");
                Material material = destination == null ? new Material(shader) { name = "生物批量光照" } : destination.Material;
                material.shader = shader;
                material.mainTexture = atlas;
                if (destination == null)
                    AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath("Assets/9_Shaders/Material/AIECS/生物批量光照.mat"));
                else
                    EditorUtility.SetDirty(material);
                catalog.Material = material;
                catalog.Diagnostics = diagnostics.ToArray();
                catalog.SourceFingerprint = BuildFingerprint();
                if (destination == null)
                    AssetDatabase.CreateAsset(catalog, path);
                else
                {
                    EditorUtility.CopySerialized(catalog, destination);
                    UnityEngine.Object.DestroyImmediate(catalog);
                    catalog = destination;
                    EditorUtility.SetDirty(catalog);
                }
                AssetDatabase.SaveAssets();
                WriteReport(catalog, OutputRoot + "/" + stem + "_导出记录.md");
                return catalog;
            }
            catch
            {
                if (!AssetDatabase.Contains(catalog)) UnityEngine.Object.DestroyImmediate(catalog);
                throw;
            }
        }

        /// <summary>按声明的稳定地址解析资源，Sprite 子资源必须精确匹配名称。</summary>
        internal T Resolve<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(address)) throw new InvalidDataException("资源地址为空。");
            int bracket = address.LastIndexOf('[');
            string root = bracket >= 0 ? address.Substring(0, bracket) : address;
            string sub = bracket >= 0 ? address.Substring(bracket + 1).TrimEnd(']') : null;
            AddressableAssetEntry entry = addresses.FirstOrDefault(x => x.address == root || x.AssetPath == root);
            if (entry == null) throw new InvalidDataException("Addressables 未登记资源：" + address);
            dependencies.Add(entry.AssetPath);
            T asset = sub == null ? AssetDatabase.LoadAssetAtPath<T>(entry.AssetPath) :
                AssetDatabase.LoadAllAssetsAtPath(entry.AssetPath).OfType<T>().SingleOrDefault(x => x.name == sub);
            if (asset == null) throw new InvalidDataException("资源类型或子资源不匹配：" + address);
            return asset;
        }

        /// <summary>逐级创建项目内资源目录，由 Unity 管理目录 GUID。</summary>
        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion

        #region 单图摇摆目录
        /// <summary>直接导出正式身体首图与状态贴图，不读取旧 Animator 时间轴。</summary>
        private AiecsActorVisual ExportActor(ItemDefinitionDto definition)
        {
            ItemVisualDefinitionDto visual = definition.Visual;
            var result = new AiecsActorVisual
            {
                Id = definition.Id, SortingLayerId = SortingLayer.NameToID(visual.SortingLayerName),
                SortingOrder = visual.SortingOrder ?? 0, Color = visual.Color ?? Color.white,
                FlipX = visual.FlipX ?? false, FlipY = visual.FlipY ?? false
            };
            var clips = new List<AiecsAnimationClip> { ExportState("Stand", Resolve<Sprite>(visual.SpriteAddress), visual) };
            if (!definition.Modules.TryGetValue("animator", out ItemModuleDefinitionDto configured))
                throw new InvalidDataException(definition.Id + " 缺少动画模块配置。");
            GameObject shell = AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
            GameObject module = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Modules/Animation/" + configured.Prefab + ".prefab");
            string moduleId = string.IsNullOrWhiteSpace(configured.Id) ? configured.Prefab : configured.Id;
            var prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, null, moduleId) as Mod_AnimatorController;
            prototype ??= ItemDefinitionCatalogLoader.FindModulePrototype(null, module, moduleId) as Mod_AnimatorController;
            if (prototype == null) throw new InvalidDataException(definition.Id + " 缺少动画模块原型。");
            dependencies.Add(definition.SourcePrefab);
            if (module != null) dependencies.Add(AssetDatabase.GetAssetPath(module));
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(prototype), result.Sway);
            if (configured.Parameters != null)
                JsonUtility.FromJsonOverwrite(configured.Parameters.ToString(), result.Sway);
            foreach (Mod_AnimatorController.SingleSpriteState state in prototype.singleSpriteStates ?? Array.Empty<Mod_AnimatorController.SingleSpriteState>())
            {
                if (state == null || string.IsNullOrWhiteSpace(state.stateName) || state.sprite == null)
                    throw new InvalidDataException(definition.Id + " 的单图状态贴图不完整。");
                clips.Add(ExportState(state.stateName, state.sprite, visual));
            }
            result.Clips = clips.ToArray();
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (AiecsAnimationClip clip in result.Clips)
            {
                AiecsAnimationFrame frame = clip.Frames[0];
                Rect rect = GetLocalRect(sprites[frame.Sprite]);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = new Vector3(corner % 2 == 0 ? rect.xMin : rect.xMax, corner < 2 ? rect.yMin : rect.yMax);
                    point.x *= result.FlipX ? -1f : 1f;
                    point.y *= result.FlipY ? -1f : 1f;
                    point = Quaternion.Euler(0, 0, frame.Rotation) * Vector3.Scale(point, frame.Scale) + frame.Position;
                    min = Mathf.Min(min, point.y); max = Mathf.Max(max, point.y);
                }
            }
            result.BodyYRange = new Vector2(min, max);
            return result;
        }

        private AiecsAnimationClip ExportState(string state, Sprite sprite, ItemVisualDefinitionDto visual)
        {
            return new AiecsAnimationClip
            {
                State = state, Duration = 1f, Loop = true, Markers = Array.Empty<AiecsAnimationMarker>(),
                Frames = new[] { new AiecsAnimationFrame { Sprite = RegisterSprite(sprite),
                    Position = visual.RendererLocalPosition ?? Vector3.zero,
                    Scale = visual.RendererLocalScale ?? Vector3.one,
                    Rotation = (visual.RendererLocalEulerAngles ?? Vector3.zero).z } }
            };
        }
        #endregion

        #region 图片解包与图集
        /// <summary>按 Sprite 对象去重，不用名字合并不同资源。</summary>
        private int RegisterSprite(Sprite sprite)
        {
            if (spriteIndices.TryGetValue(sprite, out int index)) return index;
            dependencies.Add(AssetDatabase.GetAssetPath(sprite));
            index = sprites.Count;
            sprites.Add(sprite);
            spriteIndices.Add(sprite, index);
            return index;
        }

        /// <summary>使用 Sprite 的原始矩形、Pivot 和 PPU 恢复完整局部几何。</summary>
        private static Rect GetLocalRect(Sprite sprite)
        {
            return new Rect(-sprite.pivot / sprite.pixelsPerUnit, sprite.rect.size / sprite.pixelsPerUnit);
        }

        /// <summary>用专用 Shader 解包真实网格后合图；打包发生缩图时直接拒绝交付。</summary>
        private AiecsSpriteGeometry[] ExportAtlas(string path, out Texture2D result)
        {
            var textures = new List<Texture2D>();
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/9_Shaders/Shader/AiecsFrameCopy.shader");
            var copy = new Material(shader);
            try
            {
                foreach (Sprite sprite in sprites) textures.Add(BakeSprite(sprite, copy));
                Rect[] rects = atlas.PackTextures(textures.ToArray(), 2, Mathf.Min(8192, SystemInfo.maxTextureSize), false);
                var frames = new AiecsSpriteGeometry[sprites.Count];
                for (int index = 0; index < sprites.Count; index++)
                {
                    if (Mathf.Abs(rects[index].width * atlas.width - textures[index].width) > 0.5f ||
                        Mathf.Abs(rects[index].height * atlas.height - textures[index].height) > 0.5f)
                        throw new InvalidDataException("单张图集空间不足；需要分页实现，禁止静默缩小原图。");
                    frames[index] = new AiecsSpriteGeometry { AtlasRect = rects[index],
                        LocalRect = GetLocalRect(sprites[index]),
                        VisibleRect = AiecsShadowDiagnostics.MeasureVisibleBounds(textures[index].GetPixels32(), textures[index].width,
                            new RectInt(0, 0, textures[index].width, textures[index].height), GetLocalRect(sprites[index])),
                        Source = AssetDatabase.GetAssetPath(sprites[index]) + "[" + sprites[index].name + "]" };
                }
                File.WriteAllBytes(path, atlas.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 8192;
                importer.SaveAndReimport();
                result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                return frames;
            }
            finally
            {
                foreach (Texture2D texture in textures) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        /// <summary>渲染源 Sprite 的真实 UV 和三角形，不要求源贴图 Read/Write，也不改导入设置。</summary>
        private static Texture2D BakeSprite(Sprite sprite, Material copy)
        {
            int width = Mathf.RoundToInt(sprite.rect.width);
            int height = Mathf.RoundToInt(sprite.rect.height);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var mesh = new Mesh();
            Texture2D output = null;
            GL.PushMatrix();
            try
            {
                mesh.vertices = sprite.vertices.Select(x => new Vector3(x.x, x.y, 0f)).ToArray();
                mesh.uv = sprite.uv;
                mesh.triangles = sprite.triangles.Select(x => (int)x).ToArray();
                RenderTexture.active = target;
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                GL.Clear(true, true, Color.clear);
                Rect rect = GetLocalRect(sprite);
                GL.LoadProjectionMatrix(Matrix4x4.Ortho(rect.xMin, rect.xMax, rect.yMin, rect.yMax, -1f, 1f));
                GL.modelview = Matrix4x4.identity;
                copy.mainTexture = sprite.texture;
                copy.SetPass(0);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                output.Apply();
                return output;
            }
            catch
            {
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                throw;
            }
            finally
            {
                GL.PopMatrix();
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        #endregion

        #region 交接记录
        /// <summary>将 Manifest、启用分包和资源依赖哈希纳入源指纹。</summary>
        private string BuildFingerprint()
        {
            string manifest = File.ReadAllText(ActorDefinitionCatalogLoader.BuiltInManifestPath);
            var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<ActorDefinitionManifestDto>(manifest);
            var text = new StringBuilder(manifest);
            foreach (ActorDefinitionPackageDto package in dto.Packages.Where(x => x.Enabled))
                text.Append(File.ReadAllText(ItemDefinitionCatalogLoader.ResolvePackagePath(ActorDefinitionCatalogLoader.BuiltInActorRoot, package.Path)));
            foreach (string dependency in dependencies)
                text.Append(dependency).Append(AssetDatabase.GetAssetDependencyHash(dependency));
            return Hash128.Compute(text.ToString()).ToString();
        }

        /// <summary>记录单图状态与共享图集，不把资源导出当成运行验收。</summary>
        private static void WriteReport(AiecsAnimationCatalog catalog, string path)
        {
            var report = new StringBuilder("# AIECS 单图摇摆导出记录\n\n");
            report.AppendLine("源指纹：" + catalog.SourceFingerprint);
            report.AppendLine("\n这是资源导出结果，不是 Play Mode、视觉或性能验收。\n");
            report.AppendLine("| Actor | 状态数 | 单图数 | 语义标记数 |\n| --- | --- | --- | --- |");
            foreach (AiecsActorVisual actor in catalog.Actors)
                report.AppendLine($"| {actor.Id} | {actor.Clips.Length} | {actor.Clips.Sum(x => x.Frames.Length)} | {actor.Clips.Sum(x => x.Markers.Length)} |");
            report.AppendLine($"\n共 {catalog.Sprites.Length} 张去重 Sprite；图集 {catalog.Material.mainTexture.width}×{catalog.Material.mainTexture.height}。\n");
            report.AppendLine("## 表现范围\n\n- 身体按明确状态选图，摇摆、攻击撞击与死亡翻转由 ECS 表现采样器合成。\n- 生命、伤害、移动与飞行高度由正式模拟负责，导出目录只保存表现数据。\n- 自定义法线/Mask/材质效果尚未导出，当前使用平面法线与白 Mask。\n");
            foreach (string diagnostic in catalog.Diagnostics) report.AppendLine("- " + diagnostic);
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }
        #endregion
    }
}

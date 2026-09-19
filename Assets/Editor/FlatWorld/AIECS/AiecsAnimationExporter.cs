using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>
    /// P1 动画导出器：复用正式 Manifest 的校验和继承合并，从 Addressables 解析实际控制器。
    /// 不实例化正式 Actor、不改源贴图；真实 Sprite 三角形先解包，再生成禁止缩图的专用点采样图集。
    /// </summary>
    internal sealed class AiecsAnimationExporter
    {
        // 导出路径和发现缓存，原型资源独立于正式 Actor 目录。
        internal const string OutputRoot = "Assets/6_Art/Generated/AIECS";
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

        #region 控制器与时间轴
        /// <summary>导出一个继承合并后的物种，覆盖控制器按实际映射解析。</summary>
        private AiecsActorVisual ExportActor(ItemDefinitionDto definition)
        {
            ItemVisualDefinitionDto visual = definition.Visual;
            RuntimeAnimatorController source = Resolve<RuntimeAnimatorController>(visual.AnimatorControllerAddress);
            var overrides = new Dictionary<AnimationClip, AnimationClip>();
            if (source is AnimatorOverrideController over)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                over.GetOverrides(pairs);
                foreach (var pair in pairs) overrides[pair.Key] = pair.Value != null ? pair.Value : pair.Key;
                source = over.runtimeAnimatorController;
            }
            if (!(source is AnimatorController controller))
                throw new InvalidDataException(definition.Id + " 控制器类型不受支持。");
            if (controller.layers.Length != 1)
                throw new InvalidDataException(definition.Id + " 使用多层 Animator，须先补齐混合导出。");
            var result = new AiecsActorVisual
            {
                Id = definition.Id, SortingLayerId = SortingLayer.NameToID(visual.SortingLayerName),
                SortingOrder = visual.SortingOrder ?? 0, Color = visual.Color ?? Color.white,
                FlipX = visual.FlipX ?? false, FlipY = visual.FlipY ?? false
            };
            var clips = new List<AiecsAnimationClip>();
            ExportStates(controller.layers[0].stateMachine, controller.layers[0].name, definition, overrides, clips);
            result.Clips = clips.ToArray();
            if (result.Clips.Length == 0) throw new InvalidDataException(definition.Id + " 没有可用状态。");
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            foreach (AiecsAnimationClip clip in clips)
            foreach (AiecsAnimationFrame frame in clip.Frames)
            {
                Rect rect = GetLocalRect(sprites[frame.Sprite]);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = new Vector3(corner % 2 == 0 ? rect.xMin : rect.xMax,
                        corner < 2 ? rect.yMin : rect.yMax, 0f);
                    point.x *= result.FlipX ? -1f : 1f;
                    point.y *= result.FlipY ? -1f : 1f;
                    point = Quaternion.Euler(0, 0, frame.Rotation) * Vector3.Scale(point, frame.Scale) + frame.Position;
                    min = Mathf.Min(min, point.y); max = Mathf.Max(max, point.y);
                }
            }
            result.BodyYRange = new Vector2(min, max);
            return result;
        }

        /// <summary>递归使用完整状态路径，明确拒绝尚未实现的 BlendTree 或动态状态速度。</summary>
        private void ExportStates(AnimatorStateMachine machine, string path, ItemDefinitionDto definition,
            Dictionary<AnimationClip, AnimationClip> overrides, List<AiecsAnimationClip> output)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                AnimatorState state = child.state;
                if (!(state.motion is AnimationClip clip) || state.speed <= 0f || state.speedParameterActive ||
                    state.timeParameterActive || state.mirror || state.mirrorParameterActive || state.cycleOffset != 0f || state.cycleOffsetParameterActive)
                    throw new InvalidDataException(definition.Id + "/" + state.name + " 使用未支持的运动、镜像或动态时间参数。");
                if (overrides.TryGetValue(clip, out AnimationClip replacement)) clip = replacement;
                dependencies.Add(AssetDatabase.GetAssetPath(clip));
                if (state.behaviours.Length != 0)
                    diagnostics.Add(definition.Id + "/" + state.name + "：StateMachineBehaviour 未迁移，不能转正。");
                output.Add(ExportClip(clip, path + "." + state.name, state.speed, definition));
            }
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                ExportStates(child.stateMachine, path + "." + child.stateMachine.name, definition, overrides, output);
        }

        /// <summary>保留实际 Sprite 键帧时长；有 Transform 曲线时同时按源帧率采样姿态。</summary>
        private AiecsAnimationClip ExportClip(AnimationClip clip, string state, float speed, ItemDefinitionDto definition)
        {
            EditorCurveBinding[] objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objectBindings.Length != 1 || objectBindings[0].type != typeof(SpriteRenderer) || objectBindings[0].propertyName != "m_Sprite")
                throw new InvalidDataException(definition.Id + "/" + state + " 不是单 Sprite 动作，附属物须先实现导出。");
            EditorCurveBinding spriteBinding = objectBindings[0];
            if (!string.IsNullOrEmpty(spriteBinding.path))
                throw new InvalidDataException(definition.Id + "/" + state + " 的 Sprite 不在 Animator 本节点，须先处理层级变换。");
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, spriteBinding);
            if (keys.Length == 0 || keys[0].time > 0f || clip.length <= 0f || keys.Any(x => !(x.value is Sprite)))
                throw new InvalidDataException(definition.Id + "/" + state + " 含空白帧或缺少起始图片，不能静默补图。");
            var transforms = new List<KeyValuePair<string, AnimationCurve>>();
            var markers = new List<AiecsAnimationMarker>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                string property = binding.propertyName.Replace("m_", "").ToLowerInvariant();
                if (binding.type == typeof(Transform) && binding.path == spriteBinding.path &&
                    (property.StartsWith("localposition.") || property.StartsWith("localscale.") ||
                     property == "localeuleranglesraw.z" || property == "localeulerangles.z"))
                    transforms.Add(new KeyValuePair<string, AnimationCurve>(property, curve));
                else
                {
                    string label = binding.path + "|" + binding.type.Name + "|" + binding.propertyName;
                    markers.Add(new AiecsAnimationMarker { Binding = label, Curve = curve });
                    diagnostics.Add(definition.Id + "/" + state + "：保留曲线 " + label + "，尚未接入玩法结算。");
                }
            }
            foreach (AnimationEvent evt in AnimationUtility.GetAnimationEvents(clip))
            {
                markers.Add(new AiecsAnimationMarker { Binding = "event|" + evt.functionName, Time = evt.time,
                    Payload = JsonUtility.ToJson(evt) });
                diagnostics.Add(definition.Id + "/" + state + "：保留事件 " + evt.functionName + "，尚未映射语义。");
            }
            bool loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
            var times = new SortedSet<float>(keys.Where(x => x.time < clip.length).Select(x => x.time)) { 0f };
            // 非循环动作必须保留末端键帧和末端姿态，死亡等动作才能准确停在终态。
            if (!loop) times.Add(clip.length);
            if (transforms.Count != 0)
            {
                foreach (var curve in transforms)
                    foreach (Keyframe key in curve.Value.keys)
                        if (key.time >= 0f && key.time < clip.length) times.Add(key.time);
                int samples = Mathf.CeilToInt(clip.length * clip.frameRate);
                for (int index = 1; index < samples; index++) times.Add(index / clip.frameRate);
            }
            var frames = new List<AiecsAnimationFrame>();
            int keyIndex = 0;
            foreach (float time in times)
            {
                while (keyIndex + 1 < keys.Length && keys[keyIndex + 1].time <= time) keyIndex++;
                var frame = new AiecsAnimationFrame
                {
                    Start = time / speed, Sprite = RegisterSprite((Sprite)keys[keyIndex].value),
                    Position = definition.Visual.RendererLocalPosition ?? Vector3.zero,
                    Scale = definition.Visual.RendererLocalScale ?? Vector3.one,
                    Rotation = (definition.Visual.RendererLocalEulerAngles ?? Vector3.zero).z
                };
                foreach (var transform in transforms) ApplyTransform(ref frame, transform.Key, transform.Value.Evaluate(time));
                frames.Add(frame);
            }
            return new AiecsAnimationClip { State = state, Duration = clip.length / speed,
                Loop = loop,
                Frames = frames.ToArray(), Markers = markers.ToArray() };
        }

        /// <summary>将受支持的绝对局部姿态曲线写入帧；其余曲线在导出时显式登记。</summary>
        private static void ApplyTransform(ref AiecsAnimationFrame frame, string property, float value)
        {
            int axis = property.EndsWith(".x") ? 0 : property.EndsWith(".y") ? 1 : 2;
            if (property.StartsWith("localposition.")) frame.Position[axis] = value;
            else if (property.StartsWith("localscale.")) frame.Scale[axis] = value;
            else frame.Rotation = value;
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

        /// <summary>记录实际帧数和能力缺口，防止把可显示动作当成完整生物迁移。</summary>
        private static void WriteReport(AiecsAnimationCatalog catalog, string path)
        {
            var report = new StringBuilder("# AIECS P1 动画导出记录\n\n");
            report.AppendLine("源指纹：" + catalog.SourceFingerprint);
            report.AppendLine("\n这是资源导出结果，不是 Play Mode、视觉或性能验收。\n");
            report.AppendLine("| Actor | 动作数 | 时间轴帧数 | 语义标记数 |\n| --- | --- | --- | --- |");
            foreach (AiecsActorVisual actor in catalog.Actors)
                report.AppendLine($"| {actor.Id} | {actor.Clips.Length} | {actor.Clips.Sum(x => x.Frames.Length)} | {actor.Clips.Sum(x => x.Markers.Length)} |");
            report.AppendLine($"\n共 {catalog.Sprites.Length} 张去重 Sprite；图集 {catalog.Material.mainTexture.width}×{catalog.Material.mainTexture.height}。\n");
            report.AppendLine("## 尚未迁移\n\n- Animator 转场与参数驱动由后续 ECS 决策接管，本切片仅播放明确动作。\n- 命中、生命、Buff、生态、世界水深、阴影批次、附属物和正式资源加载均未接入。\n- 自定义法线/Mask/材质效果的导出尚未实现，当前原型为平面法线与白 Mask。\n");
            foreach (string diagnostic in catalog.Diagnostics) report.AppendLine("- " + diagnostic);
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }
        #endregion
    }
}

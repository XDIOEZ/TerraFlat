using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>
    /// P0 只读盘点：复用正式 Actor 继承解析、模块定位和参数校验契约。
    /// 只加载资产，不实例化外壳、不注册 GameRes、不接触场景或存档；报告写入 Library/AIECS。
    /// </summary>
    public static class AIECSCatalogAudit
    {
        public const string ReportDirectory = "Library/AIECS";

        /// <summary>从正式目录生成可复查的已解析能力清单。</summary>
        [MenuItem("FlatWorld/AIECS/P0 生成只读能力清单")]
        public static void WriteInventory()
        {
            RequireEditMode();
            var context = new CatalogContext();
            JObject report = context.BuildInventory();
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(ReportDirectory + "/p0-inventory.json", report.ToString(Formatting.Indented));
            Debug.Log($"[AIECS P0] actors={context.Definitions.Count}, issues={((JArray)report["issues"]).Count}; {ReportDirectory}/p0-inventory.json");
        }

        /// <summary>避免开发工具在正式世界运行时改写加载器的编辑器缓存。</summary>
        internal static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("AIECS 内容盘点/导出仅允许在非 PlayMode 执行。");
        }

        /// <summary>持有一次盘点的真实目录和 Addressables 索引，不拥有运行时句柄。</summary>
        internal sealed class CatalogContext
        {
            internal readonly List<ItemDefinitionDto> Definitions;
            internal readonly List<AddressableAssetEntry> Entries;
            private readonly Dictionary<string, GameObject> prefabs = new(StringComparer.Ordinal);

            /// <summary>读取 Manifest 启用分包及 Addressables 条目，继承语义完全由正式加载器决定。</summary>
            internal CatalogContext()
            {
                Definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions()
                    .Where(value => !value.Abstract).OrderBy(value => value.Id, StringComparer.Ordinal).ToList();
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                    throw new InvalidDataException("缺少 AddressableAssetSettings。");
                Entries = new List<AddressableAssetEntry>();
                settings.GetAllAssets(Entries, false);
            }

            /// <summary>通过稳定 Address 唯一定位资产，禁止从 sourcePrefab 或文件名猜外壳/控制器。</summary>
            internal T Resolve<T>(string address) where T : UnityEngine.Object
            {
                var matches = Entries.Where(entry => string.Equals(entry.address, address?.Trim(), StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Address '{address}' 候选数={matches.Length}，必须唯一。");
                T asset = AssetDatabase.LoadAssetAtPath<T>(matches[0].AssetPath);
                return asset != null ? asset : throw new InvalidDataException($"Address '{address}' 不是 {typeof(T).Name}。");
            }

            /// <summary>解析通用 Prefab 目录中的模块，再复用正式外壳内嵌模块回退规则。</summary>
            internal Module ResolveModule(GameObject shell, string prefabId)
            {
                var candidates = new List<GameObject>();
                foreach (var entry in Entries.Where(value => value.labels.Contains("Prefab") && !value.labels.Contains(ActorDefinitionCatalogLoader.ShellAddressableLabel)))
                {
                    if (!prefabs.TryGetValue(entry.guid, out var prefab))
                    {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
                        prefabs.Add(entry.guid, prefab);
                    }
                    if (prefab != null && (entry.address == prefabId || prefab.name == prefabId))
                        candidates.Add(prefab);
                }
                if (candidates.Count > 1)
                    throw new InvalidDataException($"模块 Prefab '{prefabId}' 存在重复别名。");
                return ItemDefinitionCatalogLoader.FindModulePrototype(shell, candidates.FirstOrDefault(), prefabId);
            }

            /// <summary>输出逐定义 JSON、模块具体类型、结构组件和资源指纹；未知能力保持未迁移。</summary>
            internal JObject BuildInventory()
            {
                var actors = new JArray();
                var issues = new JArray();
                foreach (ItemDefinitionDto definition in Definitions)
                {
                    ActorDefinitionCatalogLoader.TryGetResolvedSource(definition.Id, out JObject resolved);
                    var row = new JObject { ["id"] = definition.Id, ["resolvedDefinition"] = resolved };
                    actors.Add(row);
                    try
                    {
                        GameObject shell = Resolve<GameObject>(definition.ShellAddress);
                        RuntimeAnimatorController controller = Resolve<RuntimeAnimatorController>(definition.Visual?.AnimatorControllerAddress);
                        row["shell"] = AssetRecord(shell);
                        row["controller"] = AssetRecord(controller);
                        row["hasIAIActor"] = shell.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component is IAIActor);
                        row["perceptionGeometry"] = new JArray(ActorPerceptionShapeCompiler.Compile(shell, definition.Visual?.Collider)
                            .Select(shape => new JObject
                            {
                                ["kind"] = shape.IsCircle == 0 ? "AABB" : "Circle",
                                ["center"] = new JArray(shape.Center.x, shape.Center.y),
                                ["extents"] = new JArray(shape.Extents.x, shape.Extents.y),
                                ["radius"] = shape.Radius,
                                ["runtimePhysicsQuery"] = false
                            }));
                        var modules = new JArray();
                        var matched = new HashSet<Module>();
                        foreach (var pair in definition.Modules.OrderBy(value => value.Key, StringComparer.Ordinal))
                        {
                            Module module = ResolveModule(shell, pair.Value.Prefab);
                            var item = new JObject
                            {
                                ["key"] = pair.Key, ["prefab"] = pair.Value.Prefab, ["id"] = pair.Value.Id,
                                ["enabled"] = pair.Value.Enabled ?? true,
                                ["ecsCapability"] = "未迁移；P0 只盘点，不授权生产后端切换"
                            };
                            modules.Add(item);
                            if (module == null)
                            {
                                issues.Add($"{definition.Id}/{pair.Key}: 无法解析模块 {pair.Value.Prefab}");
                                continue;
                            }
                            matched.Add(module);
                            item["type"] = module.GetType().FullName;
                            item["asset"] = AssetRecord(module);
                            item["interfaces"] = new JArray(module.GetType().GetInterfaces().Select(type => type.FullName).OrderBy(value => value));
                            try
                            {
                                ModuleJsonConfigurator.Validate(module, definition.Id, pair.Key, pair.Value.Prefab,
                                    pair.Value.Parameters?.ToString(Formatting.None));
                                item["parameterContract"] = "通过只读校验";
                            }
                            catch (Exception exception)
                            {
                                item["parameterContract"] = exception.GetBaseException().Message;
                                issues.Add($"{definition.Id}/{pair.Key}: {exception.GetBaseException().Message}");
                            }
                        }
                        row["modules"] = modules;
                        row["shellComponents"] = new JArray(shell.GetComponentsInChildren<Component>(true)
                            .Select(component => component == null ? new JObject { ["type"] = "MissingScript" } : new JObject
                            {
                                ["path"] = AnimationUtility.CalculateTransformPath(component.transform, shell.transform),
                                ["type"] = component.GetType().FullName,
                                ["inJsonModuleSet"] = component is Module module && matched.Contains(module)
                            }));
                        row["sortingGroups"] = new JArray(shell.GetComponentsInChildren<SortingGroup>(true).Select(group => new JObject
                        {
                            ["path"] = AnimationUtility.CalculateTransformPath(group.transform, shell.transform),
                            ["layerId"] = group.sortingLayerID, ["layerValue"] = SortingLayer.GetLayerValueFromID(group.sortingLayerID),
                            ["order"] = group.sortingOrder
                        }));
                        row["renderers"] = new JArray(shell.GetComponentsInChildren<Renderer>(true).Select(renderer => new JObject
                        {
                            ["path"] = AnimationUtility.CalculateTransformPath(renderer.transform, shell.transform),
                            ["type"] = renderer.GetType().Name,
                            ["materials"] = new JArray(renderer.sharedMaterials.Select(material => material == null ? "<null>" : AssetDatabase.GetAssetPath(material))),
                            ["shaders"] = new JArray(renderer.sharedMaterials.Select(material => material == null ? "<null>" : material.shader.name))
                        }));
                    }
                    catch (Exception exception)
                    {
                        issues.Add($"{definition.Id}: {exception.GetBaseException().Message}");
                    }
                }
                return new JObject
                {
                    ["unity"] = Application.unityVersion,
                    ["observedWorkstationOnly"] = new JObject { ["cpu"] = SystemInfo.processorType, ["gpu"] = SystemInfo.graphicsDeviceName, ["ramMiB"] = SystemInfo.systemMemorySize },
                    ["targetHardware"] = "未获用户确认；工作站不是目标硬件",
                    ["playModeRun"] = false, ["productionBackendChanged"] = false,
                    ["manifest"] = ActorDefinitionCatalogLoader.RelativeManifestPath,
                    ["actors"] = actors, ["issues"] = issues
                };
            }
        }

        /// <summary>记录资源 GUID、局部 ID 和依赖哈希，支持导出前后的来源核对。</summary>
        internal static JObject AssetRecord(UnityEngine.Object asset)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
            string path = AssetDatabase.GetAssetPath(asset);
            return new JObject { ["path"] = path, ["guid"] = guid, ["localId"] = localId, ["dependencyHash"] = AssetDatabase.GetAssetDependencyHash(path).ToString() };
        }
    }
}

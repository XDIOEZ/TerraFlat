using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>
    /// 复用 FlatWorld 开发菜单生成 P1 资源与独立场景，生成过程中不进入 Play Mode。
    /// 场景默认只有 48 个真实 ECS 渲染实体输入；玩家、树木、建筑是现有资源的静态显示参照。
    /// </summary>
    public static class AiecsPrototypeTools
    {
        #region 导出入口
        /// <summary>从当前正式内容导出动画、能力清单和独立原型场景；保留旧导出供对照。</summary>
        [MenuItem("FlatWorld/AIECS/P1 生成渲染原型资源和场景")]
        public static void CreateArtifacts()
        {
            AIECSCatalogAudit.RequireEditMode();
            AIECSCatalogAudit.WriteInventory();
            var exporter = new AiecsAnimationExporter();
            AiecsAnimationCatalog catalog = exporter.Export();
            string path = CreateScene(exporter, catalog);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            Debug.Log($"[AIECS P1] 已生成 {catalog.Actors.Length} 物种/{catalog.Sprites.Length} 图片的原型：{path}；未运行视觉或性能验证。");
        }

        /// <summary>重导指定的开发目录，保留场景引用及目录、图集、材质的 GUID。</summary>
        public static void ReexportCatalog(AiecsAnimationCatalog catalog)
        {
            AIECSCatalogAudit.RequireEditMode();
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            new AiecsAnimationExporter().Export(catalog);
            Debug.Log($"[AIECS P1] 已更新动画资源：{AssetDatabase.GetAssetPath(catalog)}；未运行场景。");
        }

        /// <summary>从选中资产显式执行重导，不在导入或进入场景时自动改写资源。</summary>
        [MenuItem("Assets/FlatWorld/AIECS/重新导出所选动画目录")]
        private static void ReexportSelectedCatalog()
        {
            ReexportCatalog(Selection.activeObject as AiecsAnimationCatalog);
        }

        /// <summary>只有选中 AIECS 动画目录时才开放重导入口。</summary>
        [MenuItem("Assets/FlatWorld/AIECS/重新导出所选动画目录", true)]
        private static bool CanReexportSelectedCatalog()
        {
            return Selection.activeObject is AiecsAnimationCatalog && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>在临时附加场景内生成内容，保存后关闭，仅恢复调用前活动场景。</summary>
        private static string CreateScene(AiecsAnimationExporter exporter, AiecsAnimationCatalog catalog)
        {
            AiecsAnimationExporter.EnsureFolder("Assets/3_Scenes/Development");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/3_Scenes/Development/AIECS渲染原型.unity");
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = new GameObject("AIECS 渲染原型（独立场景）");
                var host = root.AddComponent<AiecsRenderPrototype>();
                host.Catalog = catalog;
                var legacy = new GameObject("旧显示参照（无玩法组件）");
                host.LegacyRoot = legacy.transform;
                var camera = new GameObject("原型相机").AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 6f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.16f, 0.19f);
                camera.transparencySortMode = TransparencySortMode.CustomAxis;
                camera.transparencySortAxis = Vector3.up;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                host.TargetCamera = camera;

                var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
                ItemDefinitionDto tree = definitions.First(x => !x.Abstract && x.Tags != null && x.Tags.Contains("Tree") && !string.IsNullOrEmpty(x.Visual?.SpriteAddress));
                ItemDefinitionDto building = definitions.First(x => !x.Abstract && x.ShellPrefab == "BuildingBodyShell" && !string.IsNullOrEmpty(x.Visual?.SpriteAddress));
                AddItemReference(exporter, tree, legacy.transform, new Vector3(-3f, 0f, 0f));
                AddItemReference(exporter, building, legacy.transform, new Vector3(3f, 0f, 0f));
                AddPlayerReference(legacy.transform);
                AddLight("柔和全局光", Light2D.LightType.Global, Vector3.zero, Color.white, 0.3f, 0);
                AddLight("暖色局部光", Light2D.LightType.Point, new Vector3(-3f, 1f, 0f), new Color(1f, 0.65f, 0.35f), 1f, 0);
                AddLight("蓝色叠加光", Light2D.LightType.Point, new Vector3(3f, -1f, 0f), new Color(0.3f, 0.55f, 1f), 0.6f, 1);
                EditorSceneManager.SaveScene(scene, path);
                return path;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
        #endregion

        #region 原生显示参照
        /// <summary>从当前合并后的 Item JSON 建立一个静态 Sprite 参照，不创建 Item 或模块。</summary>
        private static void AddItemReference(AiecsAnimationExporter exporter, ItemDefinitionDto definition,
            Transform parent, Vector3 position)
        {
            var root = new GameObject(definition.GameName + "（" + definition.Id + " 静态参照）");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = exporter.Resolve<Sprite>(definition.Visual.SpriteAddress);
            renderer.sharedMaterial = exporter.Resolve<Material>(definition.Visual.MaterialAddress);
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            renderer.color = definition.Visual.Color ?? Color.white;
            renderer.flipX = definition.Visual.FlipX ?? false;
            renderer.flipY = definition.Visual.FlipY ?? false;
            renderer.sortingLayerName = definition.Visual.SortingLayerName ?? "Default";
            renderer.sortingOrder = definition.Visual.SortingOrder ?? 0;
            root.transform.localScale = definition.Visual.RendererLocalScale ?? Vector3.one;
            root.transform.rotation = Quaternion.Euler(definition.Visual.RendererLocalEulerAngles ?? Vector3.zero);
        }

        /// <summary>复制玩家实际静态 Sprite 层级与组内顺序，禁止把玩家玩法组件带入原型。</summary>
        private static void AddPlayerReference(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/Player/Player.prefab");
            var root = new GameObject("玩家静态显示参照");
            root.transform.SetParent(parent, false);
            var group = root.AddComponent<SortingGroup>();
            group.sortingLayerName = "Default";
            group.sortingOrder = 0;
            foreach (SpriteRenderer source in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!source.enabled || source.sprite == null) continue;
                var child = new GameObject(source.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = prefab.transform.InverseTransformPoint(source.transform.position);
                child.transform.localRotation = Quaternion.Inverse(prefab.transform.rotation) * source.transform.rotation;
                child.transform.localScale = source.transform.lossyScale;
                var target = child.AddComponent<SpriteRenderer>();
                target.sprite = source.sprite;
                target.sharedMaterial = source.sharedMaterial;
                target.color = source.color;
                target.flipX = source.flipX;
                target.flipY = source.flipY;
                target.sortingLayerID = source.sortingLayerID;
                target.sortingOrder = source.sortingOrder;
                target.spriteSortPoint = source.spriteSortPoint;
            }
            if (root.GetComponentsInChildren<SpriteRenderer>().Length == 0)
                throw new InvalidDataException("玩家 Prefab 没有可用静态 Sprite，不能用占位图片宣称完成参照。");
        }

        /// <summary>配置已有管线支持的 Light2D，启用精确法线采样以覆盖法线 Pass。</summary>
        private static void AddLight(string name, Light2D.LightType type, Vector3 position, Color color, float intensity, int blendStyle)
        {
            var light = new GameObject(name).AddComponent<Light2D>();
            light.transform.position = position;
            light.lightType = type;
            light.color = color;
            light.intensity = intensity;
            light.blendStyleIndex = blendStyle;
            if (type == Light2D.LightType.Point)
            {
                light.pointLightOuterRadius = 7f;
                var serialized = new SerializedObject(light);
                serialized.FindProperty("m_NormalMapQuality").intValue = (int)Light2D.NormalMapQuality.Accurate;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        #endregion
    }

    /// <summary>只显示原型真实统计；不把颜色批次数当作 GPU 实测 Draw Calls。</summary>
    [CustomEditor(typeof(AiecsRenderPrototype))]
    internal sealed class AiecsRenderPrototypeInspector : UnityEditor.Editor
    {
        /// <summary>显示原型参数与独立计数，规模变化由用户停用再启用原型后生效。</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var prototype = (AiecsRenderPrototype)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("独立场景 P1：轨迹不是 AI，水输入不是正式地形；计数不是性能验收。规模变化需停用再启用组件。", MessageType.Info);
            EditorGUILayout.LabelField("实际 ECS 实体", prototype.CreatedEntities.ToString());
            EditorGUILayout.LabelField("本帧位置更新", prototype.UpdatedPositions.ToString());
            EditorGUILayout.LabelField("相机内实体", prototype.VisibleEntities.ToString());
            EditorGUILayout.LabelField("本帧提交图片", prototype.SubmittedSprites.ToString());
            EditorGUILayout.LabelField("颜色批次（非 GPU 实测）", prototype.ColorBatches.ToString());
            EditorGUILayout.LabelField("顶点/索引上传字节", prototype.UploadedBytes.ToString());
            EditorGUILayout.LabelField("旧显示单元", prototype.LegacyDisplayItems.ToString());
            if (Application.isPlaying) Repaint();
        }
    }
}

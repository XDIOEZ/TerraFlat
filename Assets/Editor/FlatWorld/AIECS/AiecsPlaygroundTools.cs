using System;
using FlatWorld.AIECS.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>创建可直接 Play 的正式 AIECS 开发入口；使用 Unity 序列化保存资产，复用现有动画目录和启动场景。</summary>
    public static class AiecsPlaygroundTools
    {
        public const string ScenePath = "Assets/3_Scenes/Development/AIECS实战入口.unity";
        public const string PrefabPath = "Assets/2_Prefabs/Development/AIECS实战开发入口.prefab";
        public const string CatalogPath = "Assets/6_Art/Generated/Actors/AIECS/生物动画目录.asset";
        private const string UiCanvasName = "AIECS开发UI";

        /// <summary>用户显式打开开发入口；已有场景的未保存修改由 Unity 的标准场景保存流程处理。</summary>
        [MenuItem("FlatWorld/AIECS/打开实战开发入口")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 后打开入口场景。");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateAssets(); EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>仅创建缺失资产；不覆盖用户已经调整的场景、配置或当前打开场景。</summary>
        public static string CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译结束后的编辑模式创建入口。");
            var catalog = AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("缺少当前生物动画目录，请先使用 AIECS 动画导出入口。");
            EnsureFolder("Assets/2_Prefabs", "Development"); EnsureFolder("Assets/3_Scenes", "Development");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                CreateBasePrefab(catalog);
                RebuildUiPrefabAsset();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return ScenePath;
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                PrefabUtility.InstantiatePrefab(prefab, scene);
                var cameraObject = new GameObject("入口相机"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 6f;
                camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor; cameraObject.transform.position = new Vector3(0, 0, -10);
                var lightObject = new GameObject("入口全局光"); SceneManager.MoveGameObjectToScene(lightObject, scene);
                lightObject.AddComponent<Light2D>().lightType = Light2D.LightType.Global;
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            return ScenePath;
        }

        /// <summary>显式重建开发入口的 uGUI 子树；保留 Prefab 根和 AIECS 配置，只替换开发面板。</summary>
        [MenuItem("FlatWorld/AIECS/重建实战开发入口 uGUI Prefab")]
        public static void RebuildUiPrefabAsset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编译结束后的编辑模式重建 AIECS 开发 UI。");

            var catalog = AssetDatabase.LoadAssetAtPath<AiecsAnimationCatalog>(CatalogPath);
            if (catalog == null)
                throw new InvalidOperationException("缺少当前生物动画目录，请先使用 AIECS 动画导出入口。");

            EnsureFolder("Assets/2_Prefabs", "Development");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                CreateBasePrefab(catalog);

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                AiecsPlayground entry = root.GetComponent<AiecsPlayground>();
                if (entry == null)
                {
                    entry = root.AddComponent<AiecsPlayground>();
                    entry.Catalog = catalog;
                    entry.LoadGameStartOnPlay = true;
                }
                else if (entry.Catalog == null)
                {
                    entry.Catalog = catalog;
                }

                Transform oldUi = root.transform.Find(UiCanvasName);
                if (oldUi != null)
                    UnityEngine.Object.DestroyImmediate(oldUi.gameObject);

                BuildDevelopmentUi(root.transform, entry);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>仅在资产缺失时创建稳定的逻辑根；后续 UI 重建不会替换根对象或已有 Prefab GUID。</summary>
        private static void CreateBasePrefab(AiecsAnimationCatalog catalog)
        {
            var host = new GameObject("AIECS实战开发入口");
            try
            {
                var entry = host.AddComponent<AiecsPlayground>();
                entry.Catalog = catalog;
                entry.LoadGameStartOnPlay = true;
                PrefabUtility.SaveAsPrefabAsset(host, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>构建标准 uGUI 控件，让 GamePlayMCP 能通过 Canvas/Button/Toggle 语义树真实点击开发入口。</summary>
        private static void BuildDevelopmentUi(Transform parent, AiecsPlayground entry)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 5;

            GameObject canvasObject = new GameObject(
                UiCanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.layer = uiLayer;
            canvasObject.transform.SetParent(parent, false);

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31980;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Image panel = CreateImage("AIECS开发面板", canvasObject.transform, new Color(0.18f, 0.18f, 0.18f, 0.97f));
            // 开发面板收窄并置于 HUD 空白区，兼容很小的 Editor Game View，也不覆盖 Food/Hotbar/Quest。
            SetBottomLeft(panel.rectTransform, 220f, 250f, 860f, 260f);

            TextMeshProUGUI title = CreateText("标题", panel.transform, "AIECS 实战开发入口（临时世界）", 22f, TextAlignmentOptions.Left, FontStyles.Bold);
            SetBottomLeft(title.rectTransform, 18f, 222f, 824f, 28f);

            Button playerDuel = CreateButton("玩家对战", panel.transform, "玩家对战", 150f, 48f);
            SetBottomLeft(playerDuel.GetComponent<RectTransform>(), 18f, 166f, 150f, 48f);
            Button armies = CreateButton("两军交战", panel.transform, "两军交战（每次 +200）", 210f, 48f);
            SetBottomLeft(armies.GetComponent<RectTransform>(), 178f, 166f, 210f, 48f);
            TextMeshProUGUI armiesLabel = armies.GetComponentInChildren<TextMeshProUGUI>(true);
            Button wander = CreateButton("无目标游荡", panel.transform, "无目标游荡", 150f, 48f);
            SetBottomLeft(wander.GetComponent<RectTransform>(), 398f, 166f, 150f, 48f);
            Button flee = CreateButton("低血量逃跑", panel.transform, "低血量逃跑", 150f, 48f);
            SetBottomLeft(flee.GetComponent<RectTransform>(), 558f, 166f, 150f, 48f);
            Button clear = CreateButton("清理", panel.transform, "清理", 110f, 48f);
            SetBottomLeft(clear.GetComponent<RectTransform>(), 718f, 166f, 110f, 48f);

            TextMeshProUGUI status = CreateText("状态文本", panel.transform, entry.Status, 16f, TextAlignmentOptions.Left, FontStyles.Normal);
            status.enableWordWrapping = true;
            status.overflowMode = TextOverflowModes.Ellipsis;
            SetBottomLeft(status.rectTransform, 18f, 126f, 824f, 34f);

            Toggle participates = CreateToggle("玩家参与感知与战斗", panel.transform, "玩家参与感知与战斗");
            SetBottomLeft(participates.GetComponent<RectTransform>(), 18f, 94f, 260f, 30f);

            TextMeshProUGUI stats = CreateText("统计文本", panel.transform, "存活 0 / 目标 0 / 游荡 0 / 追击 0 / 逃跑 0 / 攻击 0", 15f, TextAlignmentOptions.Left, FontStyles.Normal);
            SetBottomLeft(stats.rectTransform, 18f, 70f, 824f, 20f);
            TextMeshProUGUI tick = CreateText("Tick文本", panel.transform, "本 Tick：感知请求 0，候选 0，LOS 0，热点格 0", 15f, TextAlignmentOptions.Left, FontStyles.Normal);
            SetBottomLeft(tick.rectTransform, 18f, 50f, 824f, 20f);
            TextMeshProUGUI cumulative = CreateText("累计文本", panel.transform, "累计：玩家受击 0（-0.0 HP），武器→ECS 命中 0，死亡 0，掉落 0", 15f, TextAlignmentOptions.Left, FontStyles.Normal);
            SetBottomLeft(cumulative.rectTransform, 18f, 30f, 824f, 20f);
            TextMeshProUGUI navigation = CreateText("导航文本", panel.transform, "共享目标 0 / Chunk 0 / 出口图 0 / 目标图 0 / 区块路线 0", 15f, TextAlignmentOptions.Left, FontStyles.Normal);
            SetBottomLeft(navigation.rectTransform, 18f, 10f, 824f, 20f);

            FlatWorldUITheme.Apply(canvasObject.transform);

            var serializedEntry = new SerializedObject(entry);
            Assign(serializedEntry, "playerDuelButton", playerDuel);
            Assign(serializedEntry, "armiesButton", armies);
            Assign(serializedEntry, "wanderButton", wander);
            Assign(serializedEntry, "fleeButton", flee);
            Assign(serializedEntry, "clearButton", clear);
            Assign(serializedEntry, "playerParticipatesToggle", participates);
            Assign(serializedEntry, "armiesButtonLabel", armiesLabel);
            Assign(serializedEntry, "statusText", status);
            Assign(serializedEntry, "statisticsText", stats);
            Assign(serializedEntry, "tickText", tick);
            Assign(serializedEntry, "cumulativeText", cumulative);
            Assign(serializedEntry, "navigationText", navigation);
            serializedEntry.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            target.layer = parent.gameObject.layer;
            target.transform.SetParent(parent, false);
            Image image = target.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Button CreateButton(string name, Transform parent, string label, float width, float height)
        {
            Image image = CreateImage(name, parent, new Color(0.34f, 0.34f, 0.34f, 0.99f));
            image.rectTransform.sizeDelta = new Vector2(width, height);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI text = CreateText("文本", image.transform, label, 16f, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(text.rectTransform, 8f, 4f);
            return button;
        }

        private static Toggle CreateToggle(string name, Transform parent, string label)
        {
            GameObject target = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            target.layer = parent.gameObject.layer;
            target.transform.SetParent(parent, false);
            Toggle toggle = target.GetComponent<Toggle>();

            Image background = CreateImage("勾选框", target.transform, new Color(0.26f, 0.26f, 0.26f, 1f));
            SetBottomLeft(background.rectTransform, 0f, 1f, 28f, 28f);
            Image checkmark = CreateImage("勾选标记", background.transform, new Color(0.84f, 0.77f, 0.42f, 1f));
            SetBottomLeft(checkmark.rectTransform, 6f, 6f, 16f, 16f);
            TextMeshProUGUI text = CreateText("文本", target.transform, label, 15f, TextAlignmentOptions.Left, FontStyles.Normal);
            SetBottomLeft(text.rectTransform, 38f, 0f, 360f, 30f);

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = true;
            return toggle;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string value,
            float size,
            TextAlignmentOptions alignment,
            FontStyles style)
        {
            GameObject target = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            target.layer = parent.gameObject.layer;
            target.transform.SetParent(parent, false);
            TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
            text.text = value;
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static void SetBottomLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, float horizontalPadding, float verticalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
            rect.localScale = Vector3.one;
        }

        private static void Assign(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"AiecsPlayground 缺少序列化字段：{propertyName}");
            property.objectReferenceValue = value;
        }

        /// <summary>通过 AssetDatabase 创建稳定目录，让 Unity 生成正确 GUID。</summary>
        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}

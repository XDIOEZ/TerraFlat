using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.Automation
{
    /// <summary>
    /// 对正式手机 HUD 做无运行时副作用的结构与横屏布局验收。覆盖 16:9、20:9、左右刘海安全区，
    /// 检查指向捕获层射线顺序、两侧摇杆、抽屉按钮及九格快捷栏的最大宽度约束。
    /// </summary>
    public static class MobileControlsLayoutValidator
    {
        private const string MobilePrefabPath =
            "Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControls.prefab";
        private const string UIRootPath = "Assets/Resources/UI/UIRoot.prefab";
        private const string PlayerPrefabPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
        private const float MobileActionRightMargin = 76f;
        private const float MobileActionBottomMargin = 54f;
        private const float MobileAttackZoneSize = 230f;
        private const float MobileActionButtonSize = 112f;
        private const float MobileActionGap = 16f;
        private const float MobileActionGroupWidth = MobileActionButtonSize * 2f + MobileActionGap;
        private const float MobileActionGroupHeight = MobileAttackZoneSize + MobileActionGap + MobileActionButtonSize;
        private const float MobileHotbarSideButtonSize = 82f;
        private const float MobileHotbarSideButtonGap = 12f;

        #region 菜单入口

        /// <summary>只读验收待办 UI：不进入 Play、不写偏好、不保存 Prefab，覆盖多分辨率与缩放乘区。</summary>
        [MenuItem("FlatWorld/Validation/Validate UI Occlusion Touch Size And Crafting")]
        public static void ValidateUiOcclusionTouchSizeAndCrafting()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在非 Play 状态执行 UI 静态诊断。");
            ValidateOcclusionContract();
            GameObject host = new GameObject("UI layout diagnostic", typeof(RectTransform));
            host.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                RectTransform bounds = host.GetComponent<RectTransform>();
                GameObject mobile = UnityEngine.Object.Instantiate(RequirePrefab(MobilePrefabPath), bounds, false);
                GameObject[] crafting = new GameObject[2];
                string[] names = { "UI_HandCraftTable", "UI_MakerTable" };
                for (int index = 0; index < names.Length; index++)
                    crafting[index] = UnityEngine.Object.Instantiate(RequirePrefab(
                        "Assets/2_Prefabs/2-1_UI/Gameplay/Crafting/" + names[index] + ".prefab"), bounds, false);

                Vector4[] screens =
                {
                    new Vector4(2560, 1440, 0, 0), new Vector4(1920, 1080, 0, 0),
                    new Vector4(1600, 900, 0, 0), new Vector4(1280, 720, 0, 0),
                    new Vector4(1024, 768, 0, 0), new Vector4(2400, 1080, 132, 48),
                    new Vector4(2400, 1080, 48, 132)
                };
                CanvasScaler scaler = RequirePrefab(UIRootPath).GetComponentInChildren<CanvasScaler>(true);
                if (scaler == null)
                    throw new InvalidOperationException("UIRoot 缺少 CanvasScaler。");
                foreach (Vector4 screen in screens)
                {
                    foreach (float uiScale in new[] { UIUserSettings.MinimumScale, 1f,
                        UIUserSettings.DefaultScale, UIUserSettings.MaximumScale })
                    {
                        float canvasScale = Mathf.Min(screen.x / scaler.referenceResolution.x,
                            screen.y / scaler.referenceResolution.y) * uiScale;
                        bounds.sizeDelta = new Vector2((screen.x - screen.z - screen.w) / canvasScale,
                            screen.y / canvasScale);
                        bounds.ForceUpdateRectTransforms();
                        string context = $"{screen.x}×{screen.y}, UI={uiScale}, insets={screen.z}/{screen.w}";
                        ValidateTouchSizeGeometry(mobile, context);
                        foreach (GameObject panel in crafting)
                            ValidateCraftingSizeGeometry(panel, context);
                    }
                }
                ValidateTouchEditorContract();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
            Debug.Log("[UI Todo] 透视三个 Shader 通道、触控尺寸和制作面板 28 组屏幕/缩放组合通过只读 Editor 诊断。");
        }

        /// <summary>核对透视共享门控以及正式设置页的序列化引用，不改变玩家偏好。</summary>
        private static void ValidateOcclusionContract()
        {
            string shader = System.IO.File.ReadAllText("Assets/9_Shaders/Shader/Sprite-Lit-Master.shader");
            if (!shader.Contains("saturate(_PlayerOcclusionEnabled) *") ||
                CountOccurrences(shader, "float playerOcclusion = ComputePlayerOcclusionMask(") != 3 ||
                CountOccurrences(shader, "*= lerp(1.0, saturate(_PlayerOcclusionAlpha), playerOcclusion);") != 3)
                throw new InvalidOperationException("透视三个 Sprite Shader 通道必须共用 Enabled 门控和 alpha 插值。");
            string bridge = System.IO.File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/PlayerOcclusionShaderGlobals.cs");
            if (!bridge.Contains("PlayerPrefs.GetInt(PreferenceKey, 0)") ||
                !bridge.Contains("if (!Enabled || localPlayer == null"))
                throw new InvalidOperationException("透视默认关闭和相机渲染门控契约不完整。");
            GameObject page = RequirePrefab("Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_VisualEffectsSettings.prefab");
            VisualEffectsSettingsPanelLauncher launcher = page.GetComponent<VisualEffectsSettingsPanelLauncher>();
            if (launcher == null)
                throw new InvalidOperationException("视觉特效设置页缺少控制器。");
            SerializedObject serialized = new SerializedObject(launcher);
            Toggle toggle = serialized.FindProperty("occlusionToggle").objectReferenceValue as Toggle;
            if (toggle == null || toggle.isOn || toggle.GetComponent<LayoutElement>().minHeight < 60f)
                throw new InvalidOperationException("请先定向重建视觉特效页；透视应默认关闭且满足触控高度。");
        }

        private static int CountOccurrences(string source, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }
            return count;
        }

        /// <summary>实际调用节点尺寸算法，验证视觉/命中矩形同源、边缘限幅和重复应用不累乘。</summary>
        private static void ValidateTouchSizeGeometry(GameObject mobile, string context)
        {
            RectTransform root = mobile.GetComponent<RectTransform>();
            foreach (MobileControlLayoutNode node in mobile.GetComponentsInChildren<MobileControlLayoutNode>(true))
            {
                if (!node.SupportsSize)
                    continue;
                Graphic hit = node.GetComponent<Graphic>();
                if (hit == null || !hit.raycastTarget || hit.rectTransform != node.LayoutTarget)
                    throw new InvalidOperationException($"{node.ControlId} 的视觉与命中矩形不一致。");
                foreach (float multiplier in new[] { 0.5f, 1f, 2f, 1f })
                {
                    node.ApplySize(root, multiplier);
                    Vector3 firstScale = node.LayoutTarget.localScale;
                    node.ApplySize(root, multiplier);
                    if (Vector3.Distance(firstScale, node.LayoutTarget.localScale) > 0.0001f)
                        throw new InvalidOperationException($"{context}: {node.ControlId} 尺寸累乘。");
                    foreach (Vector2 corner in new[] { Vector2.zero, Vector2.one,
                        new Vector2(0f, 1f), new Vector2(1f, 0f) })
                    {
                        node.ApplyNormalizedPosition(root, corner);
                        RequireContained(root, node.LayoutTarget, context + " / " + node.ControlId);
                    }
                }
            }
        }

        /// <summary>核对实际 Prefab 的全屏外壳、固定内容和只缩不放；同一实例连续调整模拟窗口变化。</summary>
        private static void ValidateCraftingSizeGeometry(GameObject panel, string context)
        {
            RectTransform outer = panel.GetComponent<RectTransform>();
            RectTransform content = panel.transform.Find("CraftingContent") as RectTransform;
            SafeAreaScaleGroup fit = panel.GetComponent<SafeAreaScaleGroup>();
            if (outer.anchorMin != Vector2.zero || outer.anchorMax != Vector2.one ||
                outer.offsetMin != Vector2.zero || outer.offsetMax != Vector2.zero ||
                content == null || content.sizeDelta != new Vector2(1344f, 756f) || fit == null)
                throw new InvalidOperationException(panel.name + " 尚未执行制作面板安全区定向适配。");
            if (panel.GetComponent<CanvasScaler>() != null || panel.GetComponent<Image>().enabled)
                throw new InvalidOperationException(panel.name + " 外壳不能缩放 Canvas 或绘制全屏背景。");
            fit.ApplyScale();
            float expected = Mathf.Min(1f, (outer.rect.width - 48f) / 1344f,
                (outer.rect.height - 48f) / 756f);
            if (Mathf.Abs(content.localScale.x - expected) > 0.0001f ||
                Mathf.Abs(content.localScale.y - expected) > 0.0001f)
                throw new InvalidOperationException(context + " 制作内容缩放不符合只缩不放规则。");
            fit.ApplyScale();
            if (Mathf.Abs(content.localScale.x - expected) > 0.0001f)
                throw new InvalidOperationException(context + " 制作内容重复应用发生累乘。");
            RequireContained(outer, content, context + " / " + panel.name);
        }

        private static void RequireContained(RectTransform root, RectTransform target, string context)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = root.InverseTransformPoint(corner);
                if (local.x < root.rect.xMin - 0.01f || local.x > root.rect.xMax + 0.01f ||
                    local.y < root.rect.yMin - 0.01f || local.y > root.rect.yMax + 0.01f)
                    throw new InvalidOperationException(context + " 超出安全区。");
            }
        }

        private static void ValidateTouchEditorContract()
        {
            GameObject editor = RequirePrefab(
                "Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControlLayoutEditor.prefab");
            foreach (string name in new[] { "缩小按钮", "放大按钮", "尺寸文本", "保存按钮", "恢复默认按钮", "取消按钮" })
                RequireNode(editor.transform, name);
            string hud = System.IO.File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerMobileControlsHUD.cs");
            int handler = hud.IndexOf("private void HandleMobileControlLayoutChanged()", StringComparison.Ordinal);
            int release = hud.IndexOf("ResetAllTouchState();", handler, StringComparison.Ordinal);
            int layout = hud.IndexOf("ApplyMobileControlLayout();", handler, StringComparison.Ordinal);
            if (release < handler || release > layout)
                throw new InvalidOperationException("布局提交必须先释放触点再应用新尺寸。");
        }

        [MenuItem("FlatWorld/Validation/Validate Mobile Controls Layout")]
        public static void Validate()
        {
            GameObject mobilePrefab = RequirePrefab(MobilePrefabPath);
            Transform gameplay = RequireNode(mobilePrefab.transform, "玩法控制层");
            Transform heldItemDrop = RequireNode(gameplay, "手持物丢弃区");
            Transform aim = RequireNode(gameplay, "普通指向区");
            Transform move = RequireNode(gameplay, "移动摇杆");
            Transform actionGroup = RequireNode(gameplay, "右侧操作组");
            Transform attack = RequireNode(actionGroup, "攻击摇杆");
            Transform interact = RequireNode(actionGroup, "交互");
            Transform use = RequireNode(actionGroup, "使用");
            Transform run = RequireNode(gameplay, "奔跑");
            Transform persistent = RequireNode(mobilePrefab.transform, "常驻控制层");
            Transform menu = RequireNode(persistent, "菜单");
            Transform hotbar = RequireNode(mobilePrefab.transform, "快捷栏锚点");
            Transform crafting = RequireNode(hotbar, "制作");
            Transform backpack = RequireNode(hotbar, "背包");
            Transform drawer = RequireNode(mobilePrefab.transform, "菜单抽屉");
            Transform zoom = RequireNode(drawer, "镜头缩放");

            if (menu.parent != persistent)
                throw new InvalidOperationException("菜单按钮必须独立于玩法控制层，模态面板打开时仍需保留返回入口。");
            if (hotbar.parent != mobilePrefab.transform)
                throw new InvalidOperationException("快捷栏锚点必须独立于玩法控制层，才能在打开背包时保持显示。");
            if (crafting.parent != hotbar || backpack.parent != hotbar)
                throw new InvalidOperationException("手机制作与背包入口必须分别固定在快捷栏锚点左右两侧。");
            LayoutElement craftingLayout = crafting.GetComponent<LayoutElement>();
            LayoutElement backpackLayout = backpack.GetComponent<LayoutElement>();
            if (craftingLayout == null || !craftingLayout.ignoreLayout ||
                backpackLayout == null || !backpackLayout.ignoreLayout)
            {
                throw new InvalidOperationException("快捷栏制作与背包入口必须忽略九格快捷栏布局。");
            }
            RectTransform craftingRect = (RectTransform)crafting;
            RectTransform backpackRect = (RectTransform)backpack;
            Vector2 leftMiddle = new Vector2(0f, 0.5f);
            Vector2 rightMiddle = new Vector2(1f, 0.5f);
            if (craftingRect.anchorMin != leftMiddle || craftingRect.anchorMax != leftMiddle ||
                craftingRect.pivot != new Vector2(1f, 0.5f) ||
                craftingRect.anchoredPosition != new Vector2(-MobileHotbarSideButtonGap, 0f) ||
                craftingRect.sizeDelta != Vector2.one * MobileHotbarSideButtonSize ||
                backpackRect.anchorMin != rightMiddle || backpackRect.anchorMax != rightMiddle ||
                backpackRect.pivot != new Vector2(0f, 0.5f) ||
                backpackRect.anchoredPosition != new Vector2(MobileHotbarSideButtonGap, 0f) ||
                backpackRect.sizeDelta != Vector2.one * MobileHotbarSideButtonSize)
            {
                throw new InvalidOperationException("快捷栏必须保持左制作、右背包，并让两个入口紧贴九格快捷栏且保持单槽尺寸。");
            }

            if (heldItemDrop.GetSiblingIndex() >= aim.GetSiblingIndex() ||
                aim.GetSiblingIndex() >= move.GetSiblingIndex() ||
                aim.GetSiblingIndex() >= actionGroup.GetSiblingIndex())
            {
                throw new InvalidOperationException("手持物丢弃面和普通指向区必须依次位于摇杆与按钮下方。");
            }

            if (attack.parent != actionGroup || interact.parent != actionGroup || use.parent != actionGroup)
                throw new InvalidOperationException("攻击摇杆、交互和使用按钮必须共用右侧操作组坐标系。");

            RequireRaycast(heldItemDrop, true);
            RequireRaycast(aim, true);
            RequireRaycast(move, true);
            RequireRaycast(attack, true);
            RequireDropSurface(heldItemDrop, true);
            RequireDropSurface(aim, false);
            RequireDropSurface(move, false);
            RequireDropSurface(attack, false);
            RequireNode(run, "状态标记").GetComponent<Image>();
            RequireNode(drawer, "抽屉按钮区").GetComponent<VerticalLayoutGroup>();
            RequireSlider(zoom);
            RequireMobileButtons(mobilePrefab.transform);
            RequireInfrastructurePrefabs();

            ValidateReferenceGeometry(2560f, 1440f, 0f, 0f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1920f, 1080f, 0f, 0f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1600f, 900f, 0f, 0f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1280f, 720f, 0f, 0f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 0f, 0f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 132f, 48f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 48f, 132f, heldItemDrop, aim, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);

            Debug.Log("[Mobile Layout] 通过：16:9、20:9、左右刘海安全区与两种横屏方向结构均满足约束。");
        }

        #endregion

        #region 结构检查

        private static GameObject RequirePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null
                ? prefab
                : throw new InvalidOperationException($"缺少 Prefab：{path}");
        }

        private static Transform RequireNode(Transform root, string name)
        {
            Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].name == name)
                    return nodes[i];
            }

            throw new InvalidOperationException($"手机 HUD 缺少节点：{name}");
        }

        private static void RequireRaycast(Transform node, bool expected)
        {
            Image image = node.GetComponent<Image>();
            if (image == null || image.raycastTarget != expected)
                throw new InvalidOperationException($"{node.name} 的射线配置不正确。");
        }

        private static void RequireMobileButtons(Transform root)
        {
            string[] required =
            {
                "交互", "使用", "奔跑", "菜单", "背包", "装备", "制作",
                "设置"
            };
            for (int i = 0; i < required.Length; i++)
            {
                Transform node = RequireNode(root, required[i]);
                if (node.GetComponent<Button>() == null)
                    throw new InvalidOperationException($"手机 HUD 节点 {required[i]} 缺少 Button。");
            }
        }

        private static void RequireDropSurface(Transform node, bool raycastOnlyWhileHoldingItem)
        {
            MobileHeldItemDropSurface surface = node.GetComponent<MobileHeldItemDropSurface>();
            if (surface == null ||
                surface.RaycastOnlyWhileHoldingItem != raycastOnlyWhileHoldingItem)
            {
                throw new InvalidOperationException($"{node.name} 的手持物丢弃触控配置不正确。");
            }
        }

        private static void RequireSlider(Transform node)
        {
            Slider slider = node.GetComponent<Slider>();
            if (slider == null || slider.minValue >= slider.maxValue ||
                slider.fillRect == null || slider.handleRect == null)
            {
                throw new InvalidOperationException("手机 HUD 的镜头缩放节点必须是有效的横向 Slider。");
            }
        }

        private static void RequireInfrastructurePrefabs()
        {
            GameObject uiRoot = RequirePrefab(UIRootPath);
            if (RequireNode(uiRoot.transform, "SafeAreaRoot").GetComponent<SafeAreaRectController>() == null)
                throw new InvalidOperationException("UIRoot/SafeAreaRoot 缺少安全区控制器。");

            GameObject player = RequirePrefab(PlayerPrefabPath);
            if (player.GetComponent<PlayerMobileControlsHUD>() == null)
                throw new InvalidOperationException("Player.prefab 尚未挂载 PlayerMobileControlsHUD。");
        }

        #endregion

        #region 参考分辨率验收

        private static void ValidateReferenceGeometry(
            float screenWidth,
            float screenHeight,
            float leftInset,
            float rightInset,
            Transform heldItemDrop,
            Transform aim,
            Transform move,
            Transform actionGroup,
            Transform attack,
            Transform interact,
            Transform use,
            Transform run,
            Transform hotbar,
            Transform drawer,
            Transform zoom)
        {
            float safeWidth = screenWidth - leftInset - rightInset;
            if (safeWidth <= 0f || screenHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(screenWidth));

            RectTransform heldItemDropRect = (RectTransform)heldItemDrop;
            RectTransform aimRect = (RectTransform)aim;
            RectTransform moveRect = (RectTransform)move;
            RectTransform actionGroupRect = (RectTransform)actionGroup;
            RectTransform attackRect = (RectTransform)attack;
            RectTransform interactRect = (RectTransform)interact;
            RectTransform useRect = (RectTransform)use;
            RectTransform runRect = (RectTransform)run;
            RectTransform hotbarRect = (RectTransform)hotbar;
            RectTransform drawerRect = (RectTransform)drawer;
            RectTransform zoomRect = (RectTransform)zoom;
            if (heldItemDropRect.anchorMin != Vector2.zero || heldItemDropRect.anchorMax != Vector2.one ||
                heldItemDropRect.offsetMin != Vector2.zero || heldItemDropRect.offsetMax != Vector2.zero)
            {
                throw new InvalidOperationException("手持物丢弃区必须覆盖玩法控制层，并保持在所有真实控件下方。");
            }
            if (aimRect.anchorMin != new Vector2(1f - UIUserSettings.DefaultRightControlZoneRatio, 0f) ||
                aimRect.anchorMax != Vector2.one ||
                moveRect.anchorMin != Vector2.zero ||
                moveRect.anchorMax != new Vector2(UIUserSettings.DefaultLeftControlZoneRatio, 1f))
            {
                throw new InvalidOperationException("移动和普通指向摇杆必须使用三段触控区的默认左右比例。");
            }
            if (actionGroupRect.anchorMin != new Vector2(1f, 0f) ||
                actionGroupRect.anchorMax != new Vector2(1f, 0f) ||
                actionGroupRect.pivot != new Vector2(1f, 0f) ||
                actionGroupRect.anchoredPosition != new Vector2(-MobileActionRightMargin, MobileActionBottomMargin) ||
                actionGroupRect.sizeDelta != new Vector2(MobileActionGroupWidth, MobileActionGroupHeight))
            {
                throw new InvalidOperationException("右侧操作组必须以统一的右下安全边距定位。");
            }
            if (actionGroupRect.sizeDelta.x + MobileActionRightMargin > safeWidth + 0.01f ||
                actionGroupRect.sizeDelta.y + MobileActionBottomMargin > screenHeight + 0.01f)
            {
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区无法容纳右侧操作组。");
            }
            if (attackRect.anchorMin != new Vector2(1f, 0f) ||
                attackRect.anchorMax != new Vector2(1f, 0f) ||
                attackRect.pivot != new Vector2(1f, 0f) ||
                attackRect.anchoredPosition != new Vector2(-(MobileActionGroupWidth - MobileAttackZoneSize) * 0.5f, 0f) ||
                attackRect.sizeDelta != new Vector2(MobileAttackZoneSize, MobileAttackZoneSize))
            {
                throw new InvalidOperationException("攻击摇杆必须在右侧操作组底部居中。");
            }
            float actionButtonY = MobileAttackZoneSize + MobileActionGap;
            if (interactRect.anchorMin != Vector2.zero || interactRect.anchorMax != Vector2.zero ||
                interactRect.pivot != Vector2.zero || interactRect.anchoredPosition != new Vector2(0f, actionButtonY) ||
                interactRect.sizeDelta != new Vector2(MobileActionButtonSize, MobileActionButtonSize) ||
                useRect.anchorMin != new Vector2(1f, 0f) || useRect.anchorMax != new Vector2(1f, 0f) ||
                useRect.pivot != new Vector2(1f, 0f) || useRect.anchoredPosition != new Vector2(0f, actionButtonY) ||
                useRect.sizeDelta != new Vector2(MobileActionButtonSize, MobileActionButtonSize))
            {
                throw new InvalidOperationException("交互和使用按钮必须在右侧操作组顶部横向对齐。");
            }
            Vector2 leftMiddle = new Vector2(0f, 0.5f);
            if (runRect.anchorMin != leftMiddle || runRect.anchorMax != leftMiddle ||
                runRect.pivot != leftMiddle || runRect.anchoredPosition.x < 48f)
            {
                throw new InvalidOperationException("奔跑按钮必须锚定在安全区左侧中部，避开左下角玩家信息。");
            }
            float runBottom = screenHeight * 0.5f + runRect.anchoredPosition.y -
                              runRect.sizeDelta.y * runRect.pivot.y;
            if (runBottom < screenHeight * 0.3f)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 中奔跑按钮过于靠近左下角。");
            float targetHotbarWidth = Mathf.Min(760f, safeWidth * 0.44f);
            float sideReserve = Mathf.Max(moveRect.sizeDelta.x, actionGroupRect.sizeDelta.x) + 68f;
            if (targetHotbarWidth + sideReserve * 2f > safeWidth + 0.01f)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区中快捷栏会与摇杆重叠。");
            if (hotbarRect.sizeDelta.x < targetHotbarWidth - 0.01f)
                throw new InvalidOperationException("快捷栏锚点不足以承载宽度上限。");
            float drawerHeight = screenHeight * (drawerRect.anchorMax.y - drawerRect.anchorMin.y) + drawerRect.sizeDelta.y;
            if (drawerRect.sizeDelta.x > safeWidth || drawerHeight > screenHeight)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区无法容纳手机抽屉。");
            ScrollRect drawerScroll = RequireNode(drawer, "菜单滚动区").GetComponent<ScrollRect>();
            if (drawerScroll == null || zoom.parent.parent != drawerScroll.content ||
                zoomRect.anchorMin != new Vector2(0f, 0f) ||
                zoomRect.anchorMax != new Vector2(1f, 0f) || zoomRect.offsetMin.y < 0f ||
                zoomRect.offsetMax.y <= zoomRect.offsetMin.y)
            {
                throw new InvalidOperationException("镜头缩放滑动条必须位于抽屉滚动列表的独立条目内，并在条目内横向拉伸。");
            }
        }

        #endregion
    }
}

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

        #region 菜单入口

        [MenuItem("FlatWorld/Validation/Validate Mobile Controls Layout")]
        public static void Validate()
        {
            GameObject mobilePrefab = RequirePrefab(MobilePrefabPath);
            Transform gameplay = RequireNode(mobilePrefab.transform, "玩法控制层");
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
            Transform drawer = RequireNode(mobilePrefab.transform, "菜单抽屉");
            Transform zoom = RequireNode(drawer, "镜头缩放");

            if (menu.parent != persistent)
                throw new InvalidOperationException("菜单按钮必须独立于玩法控制层，模态面板打开时仍需保留返回入口。");
            if (hotbar.parent != mobilePrefab.transform)
                throw new InvalidOperationException("快捷栏锚点必须独立于玩法控制层，才能在打开背包时保持显示。");

            if (aim.GetSiblingIndex() >= move.GetSiblingIndex() ||
                aim.GetSiblingIndex() >= actionGroup.GetSiblingIndex())
            {
                throw new InvalidOperationException("普通指向捕获层必须位于摇杆与按钮之前，才能只命中右侧空白区。");
            }

            if (attack.parent != actionGroup || interact.parent != actionGroup || use.parent != actionGroup)
                throw new InvalidOperationException("攻击摇杆、交互和使用按钮必须共用右侧操作组坐标系。");

            RequireRaycast(aim, true);
            RequireRaycast(move, true);
            RequireRaycast(attack, true);
            RequireNode(run, "状态标记").GetComponent<Image>();
            RequireNode(drawer, "抽屉按钮区").GetComponent<GridLayoutGroup>();
            RequireSlider(zoom);
            RequireMobileButtons(mobilePrefab.transform);
            RequireInfrastructurePrefabs();

            ValidateReferenceGeometry(2560f, 1440f, 0f, 0f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1920f, 1080f, 0f, 0f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1600f, 900f, 0f, 0f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(1280f, 720f, 0f, 0f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 0f, 0f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 132f, 48f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);
            ValidateReferenceGeometry(2400f, 1080f, 48f, 132f, move, actionGroup, attack, interact, use, run, hotbar, drawer, zoom);

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
                "交互", "使用", "奔跑", "菜单", "背包", "装备", "制作", "状态",
                "丢弃一个", "设置"
            };
            for (int i = 0; i < required.Length; i++)
            {
                Transform node = RequireNode(root, required[i]);
                if (node.GetComponent<Button>() == null)
                    throw new InvalidOperationException($"手机 HUD 节点 {required[i]} 缺少 Button。");
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

            RectTransform moveRect = (RectTransform)move;
            RectTransform actionGroupRect = (RectTransform)actionGroup;
            RectTransform attackRect = (RectTransform)attack;
            RectTransform interactRect = (RectTransform)interact;
            RectTransform useRect = (RectTransform)use;
            RectTransform runRect = (RectTransform)run;
            RectTransform hotbarRect = (RectTransform)hotbar;
            RectTransform drawerRect = (RectTransform)drawer;
            RectTransform zoomRect = (RectTransform)zoom;
            if (moveRect.anchorMin != Vector2.zero || moveRect.anchorMax != new Vector2(0.5f, 1f))
                throw new InvalidOperationException("移动摇杆必须覆盖左半屏作为浮动按下区域。");
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
            if (drawerRect.sizeDelta.x > safeWidth || drawerRect.sizeDelta.y > screenHeight)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区无法容纳手机抽屉。");
            if (zoom.parent != drawer || zoomRect.anchorMin != new Vector2(0f, 0f) ||
                zoomRect.anchorMax != new Vector2(1f, 0f) || zoomRect.offsetMin.y < 16f ||
                zoomRect.offsetMax.y <= zoomRect.offsetMin.y)
            {
                throw new InvalidOperationException("镜头缩放滑动条必须固定在手机菜单抽屉底部并横向拉伸。");
            }
        }

        #endregion
    }
}

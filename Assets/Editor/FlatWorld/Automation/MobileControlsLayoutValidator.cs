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

        #region 菜单入口

        [MenuItem("FlatWorld/Validation/Validate Mobile Controls Layout")]
        public static void Validate()
        {
            GameObject mobilePrefab = RequirePrefab(MobilePrefabPath);
            Transform gameplay = RequireNode(mobilePrefab.transform, "玩法控制层");
            Transform aim = RequireNode(gameplay, "普通指向区");
            Transform move = RequireNode(gameplay, "移动摇杆");
            Transform attack = RequireNode(gameplay, "攻击摇杆");
            Transform run = RequireNode(gameplay, "奔跑");
            Transform persistent = RequireNode(mobilePrefab.transform, "常驻控制层");
            Transform menu = RequireNode(persistent, "菜单");
            Transform hotbar = RequireNode(mobilePrefab.transform, "快捷栏锚点");
            Transform drawer = RequireNode(mobilePrefab.transform, "菜单抽屉");

            if (menu.parent != persistent)
                throw new InvalidOperationException("菜单按钮必须独立于玩法控制层，模态面板打开时仍需保留返回入口。");
            if (hotbar.parent != mobilePrefab.transform)
                throw new InvalidOperationException("快捷栏锚点必须独立于玩法控制层，才能在打开背包时保持显示。");

            if (aim.GetSiblingIndex() >= move.GetSiblingIndex() ||
                aim.GetSiblingIndex() >= attack.GetSiblingIndex())
            {
                throw new InvalidOperationException("普通指向捕获层必须位于摇杆与按钮之前，才能只命中右侧空白区。");
            }

            RequireRaycast(aim, true);
            RequireRaycast(move, true);
            RequireRaycast(attack, true);
            RequireNode(run, "状态标记").GetComponent<Image>();
            RequireNode(drawer, "抽屉按钮区").GetComponent<GridLayoutGroup>();
            RequireMobileButtons(mobilePrefab.transform);
            RequireInfrastructurePrefabs();

            ValidateReferenceGeometry(2560f, 1440f, 0f, 0f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(1920f, 1080f, 0f, 0f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(1600f, 900f, 0f, 0f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(1280f, 720f, 0f, 0f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 0f, 0f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 132f, 48f, move, attack, run, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 48f, 132f, move, attack, run, hotbar, drawer);

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
                "丢弃一个", "镜头+", "镜头-", "设置"
            };
            for (int i = 0; i < required.Length; i++)
            {
                Transform node = RequireNode(root, required[i]);
                if (node.GetComponent<Button>() == null)
                    throw new InvalidOperationException($"手机 HUD 节点 {required[i]} 缺少 Button。");
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
            Transform attack,
            Transform run,
            Transform hotbar,
            Transform drawer)
        {
            float safeWidth = screenWidth - leftInset - rightInset;
            if (safeWidth <= 0f || screenHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(screenWidth));

            RectTransform moveRect = (RectTransform)move;
            RectTransform attackRect = (RectTransform)attack;
            RectTransform runRect = (RectTransform)run;
            RectTransform hotbarRect = (RectTransform)hotbar;
            RectTransform drawerRect = (RectTransform)drawer;
            if (moveRect.anchorMin != Vector2.zero || moveRect.anchorMax != new Vector2(0.5f, 1f))
                throw new InvalidOperationException("移动摇杆必须覆盖左半屏作为浮动按下区域。");
            if (-attackRect.anchoredPosition.x < 64f || attackRect.anchoredPosition.y < 48f)
            {
                throw new InvalidOperationException("右侧攻击摇杆距离安全区边角过近。");
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
            float sideReserve = Mathf.Max(moveRect.sizeDelta.x, attackRect.sizeDelta.x) + 68f;
            if (targetHotbarWidth + sideReserve * 2f > safeWidth + 0.01f)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区中快捷栏会与摇杆重叠。");
            if (hotbarRect.sizeDelta.x < targetHotbarWidth - 0.01f)
                throw new InvalidOperationException("快捷栏锚点不足以承载宽度上限。");
            if (drawerRect.sizeDelta.x > safeWidth || drawerRect.sizeDelta.y > screenHeight)
                throw new InvalidOperationException($"{screenWidth}x{screenHeight} 安全区无法容纳手机抽屉。");
        }

        #endregion
    }
}

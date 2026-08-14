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
            Transform hotbar = RequireNode(gameplay, "快捷栏锚点");
            Transform drawer = RequireNode(mobilePrefab.transform, "菜单抽屉");

            if (aim.GetSiblingIndex() >= move.GetSiblingIndex() ||
                aim.GetSiblingIndex() >= attack.GetSiblingIndex())
            {
                throw new InvalidOperationException("普通指向捕获层必须位于摇杆与按钮之前，才能只命中右侧空白区。");
            }

            RequireRaycast(aim, true);
            RequireRaycast(move, true);
            RequireRaycast(attack, true);
            RequireNode(drawer, "抽屉按钮区").GetComponent<GridLayoutGroup>();
            RequireMobileButtons(mobilePrefab.transform);
            RequireInfrastructurePrefabs();

            ValidateReferenceGeometry(1920f, 1080f, 0f, 0f, move, attack, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 0f, 0f, move, attack, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 132f, 48f, move, attack, hotbar, drawer);
            ValidateReferenceGeometry(2400f, 1080f, 48f, 132f, move, attack, hotbar, drawer);

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
            Transform hotbar,
            Transform drawer)
        {
            float safeWidth = screenWidth - leftInset - rightInset;
            if (safeWidth <= 0f || screenHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(screenWidth));

            RectTransform moveRect = (RectTransform)move;
            RectTransform attackRect = (RectTransform)attack;
            RectTransform hotbarRect = (RectTransform)hotbar;
            RectTransform drawerRect = (RectTransform)drawer;
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

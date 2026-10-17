using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>独立无 Canvas 面板复用正式查询聚合器，不创建 UIManager 或绑定玩家控制器。</summary>
public static partial class GameplayTestSystemProbe
{
    #region 手持物世界丢弃面板资格采样

    private static JObject ServiceHeldItemWorldDropPanels(JObject args)
    {
        string mode = Text(args, "mode", "allowed");
        var root = new GameObject("[LuaHeldItemDropPanels]", typeof(RectTransform));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        int originalOrder = BasePanel.CurrentOrder;
        try
        {
            BasePanel first = CreateHeldItemDropProbePanel(root.transform, "First");
            BasePanel second = CreateHeldItemDropProbePanel(root.transform, "Second");
            bool defaultAllows = first.AllowsHeldItemWorldDrop;
            object owner = new object();
            if (mode == "destroyed_owner")
            {
                var ownerObject = new GameObject("[LuaHeldItemDropOwner]");
                ownerObject.transform.SetParent(root.transform, false);
                ownerObject.SetActive(false);
                owner = ownerObject;
            }
            object nextOwner = new object();
            object unknownOwner = new object();
            bool denied = mode == "denied" || mode == "denied_closed" ||
                          mode == "denied_disabled" || mode == "nonblocking_denied";
            first.SetGameplayInputBlocking(mode != "nonblocking_denied" && mode != "nonblocking_allowed");
            first.SetHeldItemWorldDropAllowed(!denied, mode == "no_owner" ? null : owner);
            if (mode != "empty") first.Open();
            root.SetActive(true);

            var query = new UIManager.HeldItemWorldDropPanelQuery();
            void Refresh()
            {
                query.Clear();
                query.Include(first);
                query.Include(second);
            }
            Refresh();
            var result = new JObject
            {
                ["mode"] = mode,
                ["defaultAllows"] = defaultAllows,
                ["initialCanDrop"] = query.CanDropHeldItemInWorld,
                ["initialOwnerCompatible"] = query.IsCompatibleLockOwner(owner),
                ["idempotentNotificationDelta"] = -1
            };

            switch (mode)
            {
                case "stacked":
                    second.Open();
                    break;
                case "closed":
                case "denied_closed":
                    first.Close();
                    break;
                case "disabled":
                case "denied_disabled":
                    first.enabled = false;
                    break;
                case "inactive":
                    first.gameObject.SetActive(false);
                    break;
                case "revoked":
                    first.SetHeldItemWorldDropAllowed(false, owner);
                    break;
                case "owner_changed":
                    first.SetHeldItemWorldDropAllowed(true, nextOwner);
                    break;
                case "destroyed_owner":
                    Object.DestroyImmediate((Object)owner);
                    break;
                case "destroyed_panel":
                    Object.DestroyImmediate(first.gameObject);
                    break;
                case "reopened":
                    first.Close();
                    Refresh();
                    result["closedCanDrop"] = query.CanDropHeldItemInWorld;
                    result["closedOwnerCompatible"] = query.IsCompatibleLockOwner(owner);
                    first.Open();
                    break;
                case "shared_owner":
                    second.SetHeldItemWorldDropAllowed(true, owner);
                    second.Open();
                    first.Close();
                    break;
                case "idempotent":
                    UIManager manager = UIManager.ExistingInstance;
                    int revision = manager != null ? manager.InteractionSurfaceRevision : 0;
                    first.SetHeldItemWorldDropAllowed(true, owner);
                    result["idempotentNotificationDelta"] = Application.isPlaying && manager != null
                        ? manager.InteractionSurfaceRevision - revision : -1;
                    break;
            }

            Refresh();
            result["canDrop"] = query.CanDropHeldItemInWorld;
            result["ownerCompatible"] = query.IsCompatibleLockOwner(owner);
            result["nextOwnerCompatible"] = query.IsCompatibleLockOwner(nextOwner);
            result["unknownOwnerCompatible"] = query.IsCompatibleLockOwner(unknownOwner);
            result["nullOwnerCompatible"] = query.IsCompatibleLockOwner(null);
            result["ownerCleared"] = first != null && first.HeldItemWorldDropLockOwner == null;
            return result;
        }
        finally
        {
            try { Object.DestroyImmediate(root); }
            finally { BasePanel.CurrentOrder = originalOrder; }
        }
    }

    private static BasePanel CreateHeldItemDropProbePanel(Transform parent, string name)
    {
        var panelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        panelObject.transform.SetParent(parent, false);
        BasePanel panel = panelObject.AddComponent<BasePanel>();
        panel.rectTransform = panelObject.GetComponent<RectTransform>();
        panel.canvasGroup = panelObject.GetComponent<CanvasGroup>();
        panel.InitClosed();
        return panel;
    }

    private static JObject ServiceHeldItemWorldDropPosition(JObject args)
    {
        string mode = Text(args, "mode", "inside");
        var root = new GameObject("[LuaHeldItemDropBounds]", typeof(RectTransform));
        root.SetActive(false);
        root.hideFlags = HideFlags.HideAndDontSave;
        int originalOrder = BasePanel.CurrentOrder;
        try
        {
            BasePanel panel = CreateHeldItemDropProbePanel(root.transform, "FullScreenPanel");
            panel.rectTransform.sizeDelta = new Vector2(1000f, 800f);
            panel.rectTransform.localPosition = new Vector3(500f, 400f, 0f);
            var windowObject = new GameObject("ActualWindow", typeof(RectTransform));
            windowObject.transform.SetParent(panel.transform, false);
            RectTransform window = windowObject.GetComponent<RectTransform>();
            window.sizeDelta = new Vector2(200f, 100f);
            bool fallback = mode == "fallback_inside" || mode == "fallback_outside";
            if (!fallback)
            {
                var surfaceObject = new GameObject("WindowDragSurface", typeof(RectTransform));
                surfaceObject.transform.SetParent(panel.transform, false);
                surfaceObject.AddComponent<UIWindowDragSurface>().Configure(window);
            }

            Camera camera = null;
            if (mode == "camera_inside" || mode == "camera_outside")
            {
                var cameraObject = new GameObject("BoundsCamera");
                cameraObject.transform.SetParent(root.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 500f;
                camera.transform.position = panel.transform.position + Vector3.back * 10f;
                Canvas canvas = panel.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                panel.rectTransform.sizeDelta = new Vector2(1000f, 800f);
                panel.rectTransform.localPosition = new Vector3(500f, 400f, 0f);
                panel.rectTransform.localScale = Vector3.one;
                camera.transform.position = panel.transform.position + Vector3.back * 10f;
            }

            panel.SetHeldItemWorldDropAllowed(true);
            panel.Open();
            root.SetActive(true);
            RectTransform bounds = fallback ? panel.rectTransform : window;
            Vector3 localPoint = Vector3.zero;
            if (mode == "outside" || mode == "fallback_outside" || mode == "camera_outside")
                localPoint.x = bounds.rect.xMax + 20f;
            else if (mode == "edge_inside") localPoint.x = bounds.rect.xMax - 0.5f;
            else if (mode == "edge_outside") localPoint.x = bounds.rect.xMax + 0.5f;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, bounds.TransformPoint(localPoint));
            if (mode == "nan") screenPoint.x = float.NaN;
            if (mode == "infinity") screenPoint.y = float.PositiveInfinity;
            var query = new UIManager.HeldItemWorldDropPanelQuery();
            query.Include(panel);
            return new JObject
            {
                ["mode"] = mode,
                ["canDrop"] = query.CanDropHeldItemInWorld,
                ["canDropAtPoint"] = query.CanDropAtScreenPosition(screenPoint, null),
                ["insidePanelRoot"] = mode != "nan" && mode != "infinity" &&
                    RectTransformUtility.RectangleContainsScreenPoint(panel.rectTransform, screenPoint, camera),
                ["usesActualWindow"] = !fallback
            };
        }
        finally
        {
            try { Object.DestroyImmediate(root); }
            finally { BasePanel.CurrentOrder = originalOrder; }
        }
    }

    #endregion
}

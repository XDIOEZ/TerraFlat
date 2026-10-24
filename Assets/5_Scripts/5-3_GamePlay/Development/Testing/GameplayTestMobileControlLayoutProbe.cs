#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class GameplayTestMobileControlLayoutProbe
{
    #region 手机布局独立采样

    [InitializeOnLoadMethod]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() =>
        GameplayTestSystemProbe.Register("ui_settings", "mobile_layout", Capture);

    private static JObject Capture(JObject args)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/2-1_UI/Gameplay/Mobile/UI_MobileControls.prefab");
        if (prefab == null) throw new InvalidOperationException("正式手机 HUD Prefab 缺失。");
        var rootObject = new GameObject("[LuaMobileLayoutProbe]", typeof(RectTransform));
        rootObject.SetActive(false);
        rootObject.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            RectTransform root = (RectTransform)rootObject.transform;
            root.sizeDelta = new Vector2((float)GameplayTestSystemProbe.Number(args, "width", 1920),
                (float)GameplayTestSystemProbe.Number(args, "height", 1080));
            GameObject preview = UnityEngine.Object.Instantiate(prefab, root, false);
            // 直接复用正式预览准备和控件事件，不向玩家 UI、虚拟设备或偏好写入状态。
            MobileControlLayoutEditor.PreparePreviewInput(preview);
            string controlId = GameplayTestSystemProbe.Text(args, "control", "move");
            bool floatingRuntime = GameplayTestSystemProbe.Flag(args, "floatingRuntime", false);
            MobileControlLayoutNode selected = null;
            foreach (MobileControlLayoutNode node in preview.GetComponentsInChildren<MobileControlLayoutNode>(true))
            {
                if (!floatingRuntime) node.PrepareForEditingPreview();
                if (node.ControlId == controlId) selected = node;
            }
            if (selected == null) throw new InvalidOperationException("手机布局控件缺失：" + controlId);
            if (floatingRuntime && controlId != UIUserSettings.MobileAimControlId)
                throw new ArgumentException("浮动运行时采样只使用始终浮动的普通指向摇杆。");

            RectTransform zone = selected.LayoutTarget;
            Vector2 zoneAnchorMin = zone.anchorMin;
            Vector2 zoneAnchorMax = zone.anchorMax;
            Vector2 zoneSize = zone.sizeDelta;
            Vector3 zonePosition = zone.position;
            selected.ApplySize(root, (float)GameplayTestSystemProbe.Number(args, "size", 1.5));
            rootObject.SetActive(true);
            if (!floatingRuntime) selected.SetEditing(root, true);

            RectTransform handle = (RectTransform)selected.transform;
            JObject before = Bounds(handle, root);
            var destination = new Vector2((float)GameplayTestSystemProbe.Number(args, "x", 0.43),
                (float)GameplayTestSystemProbe.Number(args, "y", 0.52));
            Vector3 desiredWorld = root.TransformPoint(new Vector3(
                Mathf.LerpUnclamped(root.rect.xMin, root.rect.xMax, destination.x),
                Mathf.LerpUnclamped(root.rect.yMin, root.rect.yMax, destination.y), 0));
            var pointer = new PointerEventData(null)
            {
                pointerId = 7,
                position = RectTransformUtility.WorldToScreenPoint(null, handle.TransformPoint(handle.rect.center))
            };
            Graphic graphic = selected.GetComponent<Graphic>();
            bool raycast = graphic != null && graphic.raycastTarget && graphic.Raycast(pointer.position, null);
            JObject afterDown = before;
            JObject afterForeign = before;
            Vector2 start = selected.CaptureNormalizedPosition(root);
            if (floatingRuntime)
            {
                selected.ApplyNormalizedPosition(root, destination);
            }
            else
            {
                ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                afterDown = Bounds(handle, root);
                ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.beginDragHandler);
                var foreign = new PointerEventData(null) { pointerId = 8, position = desiredWorld };
                ExecuteEvents.Execute(selected.gameObject, foreign, ExecuteEvents.dragHandler);
                afterForeign = Bounds(handle, root);
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, desiredWorld);
                ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.endDragHandler);
            }

            Vector2 position = selected.CaptureNormalizedPosition(root);
            bool inputsDisabled = true;
            foreach (Behaviour behaviour in preview.GetComponentsInChildren<Behaviour>(true))
                if (behaviour is Button || behaviour is FlatWorldUIFeedback ||
                    behaviour is MobileHeldItemDropSurface || behaviour is MobileVirtualJoystick ||
                    behaviour is MobileInputButton)
                    inputsDisabled &= !behaviour.enabled;
            FlatWorldUIFeedback feedback = selected.GetComponent<FlatWorldUIFeedback>();
            return new JObject
            {
                ["supportsPosition"] = selected.SupportsPosition, ["raycast"] = raycast,
                ["inputsDisabled"] = inputsDisabled, ["size"] = selected.SizeMultiplier,
                ["before"] = before, ["afterDown"] = afterDown, ["afterForeign"] = afterForeign,
                ["afterDrag"] = Bounds(handle, root), ["root"] = Bounds(root, root),
                ["startX"] = start.x, ["startY"] = start.y, ["x"] = position.x, ["y"] = position.y,
                ["zoneUnchanged"] = zone.anchorMin == zoneAnchorMin && zone.anchorMax == zoneAnchorMax &&
                    zone.sizeDelta == zoneSize && zone.position == zonePosition && zone.localScale == Vector3.one,
                ["feedbackBaseScale"] = feedback != null ? feedback.BaseScale.x : 0f
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootObject);
        }
    }

    private static JObject Bounds(RectTransform target, RectTransform root)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 min = root.InverseTransformPoint(corners[0]);
        Vector3 max = min;
        for (int index = 1; index < corners.Length; index++)
        {
            Vector3 point = root.InverseTransformPoint(corners[index]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new JObject
        {
            ["left"] = min.x, ["right"] = max.x, ["bottom"] = min.y, ["top"] = max.y,
            ["width"] = max.x - min.x, ["height"] = max.y - min.y
        };
    }

    #endregion
}
#endif

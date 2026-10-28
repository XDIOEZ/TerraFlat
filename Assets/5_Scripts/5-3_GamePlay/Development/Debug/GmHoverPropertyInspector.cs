using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.InputSystem;

// 悬停观察器独立持有查询缓冲、反射缓存和文本显示状态。
internal sealed partial class GmHoverPropertyInspector
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int HoverInspectorHitCapacity = 32;
    private const int HoverInspectorTextLimit = 60 * 1024;
    private const float HoverInspectorRefreshInterval = 0.12f;

    #region F6 悬停属性观察器

    private readonly Collider2D[] hoverInspectorHits = new Collider2D[HoverInspectorHitCapacity];
    private readonly StringBuilder hoverInspectorBuilder = new StringBuilder(8192);
    private readonly List<object> hoverInspectorObjectPath = new List<object>(8);
    private static readonly Dictionary<Type, FieldInfo[]> hoverInspectorFieldCache = new Dictionary<Type, FieldInfo[]>();
    private bool hoverInspectorEnabled;
    private GameObject hoverInspectorTarget;
    private Item hoverInspectorItem;
    private string hoverInspectorText = string.Empty;
    private string hoverInspectorTargetName = string.Empty;
    private Vector2 hoverInspectorScroll;
    private float hoverInspectorNextRefreshTime;
    private GUIStyle hoverInspectorTitleStyle;
    private GUIStyle hoverInspectorBodyStyle;
    private GUIStyle hoverInspectorHintStyle;

    #endregion

    #region F6 悬停属性观察器

    /// <summary>F6 开关悬停检查，并持续解析鼠标下最靠前的玩法实体。</summary>
    public void UpdateFromPointer()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard?.f6Key.wasPressedThisFrame == true)
        {
            hoverInspectorEnabled = !hoverInspectorEnabled;
            hoverInspectorScroll = Vector2.zero;
            hoverInspectorNextRefreshTime = 0f;
            if (!hoverInspectorEnabled)
                ClearTarget();
        }

        if (!hoverInspectorEnabled)
            return;

        Mouse mouse = Mouse.current;
        Camera camera = Camera.main;
        if (mouse == null || camera == null)
        {
            ClearTarget();
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();
        Vector3 worldPosition3 = camera.ScreenToWorldPoint(
            new Vector3(screenPosition.x, screenPosition.y, -camera.transform.position.z));
        ResolveHoverInspectorTarget((Vector2)worldPosition3, out GameObject target, out Item item);

        if (target != hoverInspectorTarget || item != hoverInspectorItem)
        {
            hoverInspectorTarget = target;
            hoverInspectorItem = item;
            hoverInspectorScroll = Vector2.zero;
            hoverInspectorNextRefreshTime = 0f;
        }

        float scrollDelta = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollDelta) > 0.01f)
            hoverInspectorScroll.y = Mathf.Max(0f, hoverInspectorScroll.y - scrollDelta * 0.35f);

        if (Time.unscaledTime < hoverInspectorNextRefreshTime)
            return;

        hoverInspectorNextRefreshTime = Time.unscaledTime + HoverInspectorRefreshInterval;
        RefreshHoverInspectorText();
    }

    /// <summary>从重叠碰撞体中优先选择 Item，并用渲染顺序处理同点重叠对象。</summary>
    private void ResolveHoverInspectorTarget(Vector2 worldPosition, out GameObject target, out Item item)
    {
        target = null;
        item = null;

        int hitCount = GameplayPhysics2D.OverlapPointNonAlloc(worldPosition, hoverInspectorHits);
        int bestScore = int.MinValue;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hoverInspectorHits[i];
            if (hit == null || !hit.enabled || !hit.gameObject.activeInHierarchy)
                continue;

            Collider2D source = ColliderSource2D.Resolve(hit);
            if (source == null)
                continue;

            Item candidateItem = GameplayPhysics2D.ResolveComponent<Item>(hit);
            GameObject candidateTarget = candidateItem != null
                ? candidateItem.gameObject
                : (source.attachedRigidbody != null ? source.attachedRigidbody.gameObject : source.gameObject);
            int score = candidateItem != null ? 1_000_000 : 0;
            score += ResolveHoverInspectorSortingScore(candidateTarget);

            if (score <= bestScore)
                continue;

            bestScore = score;
            target = candidateTarget;
            item = candidateItem;
        }

        Array.Clear(hoverInspectorHits, 0, hoverInspectorHits.Length);
    }

    /// <summary>把世界排序折成稳定分数，鼠标重叠时尽量命中视觉上最靠前的对象。</summary>
    private static int ResolveHoverInspectorSortingScore(GameObject target)
    {
        if (target == null)
            return 0;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        int best = int.MinValue;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            int layerValue = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
            int score = layerValue * 10000 + renderer.sortingOrder;
            if (score > best)
                best = score;
        }

        return best == int.MinValue ? 0 : best;
    }

    /// <summary>低频重建文本；展示 ItemData、ModuleData 与运行时玩法组件字段。</summary>
    private void RefreshHoverInspectorText()
    {
        hoverInspectorBuilder.Clear();

        if (hoverInspectorTarget == null)
        {
            hoverInspectorTargetName = string.Empty;
            hoverInspectorText = FlatWorldLocalizationService.GetUiText(
                "已开启。\n把鼠标移动到带 2D 碰撞体的游戏对象上即可查看参数。");
            return;
        }

        string displayName = hoverInspectorItem?.itemData?.GameName;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = hoverInspectorTarget.name;
        hoverInspectorTargetName = displayName;

        Transform root = hoverInspectorTarget.transform;
        hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat("对象：{0}", hoverInspectorTarget.name));
        hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat(
            "类型：{0}", hoverInspectorItem != null ? hoverInspectorItem.GetType().Name : "GameObject"));
        hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat("位置：{0}", FormatInspectorValue(root.position)));
        hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat("旋转 Z：{0:0.###}°", root.eulerAngles.z));
        hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat("缩放：{0}", FormatInspectorValue(root.lossyScale)));

        AppendUnityRuntimeSummary(hoverInspectorBuilder, hoverInspectorTarget);

        if (hoverInspectorItem?.itemData != null)
        {
            hoverInspectorBuilder.AppendLine();
            hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiText("【ItemData】"));
            AppendInspectorObject(
                hoverInspectorBuilder,
                hoverInspectorItem.itemData,
                "  ",
                depth: 0,
                maxDepth: 2);

            hoverInspectorBuilder.AppendLine();
            hoverInspectorBuilder.AppendLine(FlatWorldLocalizationService.GetUiFormat(
                "【模块】 {0}", hoverInspectorItem.Mods?.Count ?? 0));
            if (hoverInspectorItem.Mods != null)
            {
                foreach (KeyValuePair<string, Module> pair in hoverInspectorItem.Mods.OrderBy(entry => entry.Key))
                {
                    Module module = pair.Value;
                    if (module == null)
                        continue;

                    hoverInspectorBuilder.AppendLine(
                        $"  ▸ {pair.Key}  [{module.GetType().Name}]  ID={module.ResolvedModuleId ?? "-"}  Enabled={module.Enabled}");
                    if (module._Data != null)
                    {
                        AppendInspectorObject(
                            hoverInspectorBuilder,
                            module._Data,
                            "    ",
                            depth: 0,
                            maxDepth: 2);
                    }
                }
            }
        }

        AppendGameplayComponents(hoverInspectorBuilder, hoverInspectorTarget);

        if (hoverInspectorBuilder.Length > HoverInspectorTextLimit)
        {
            hoverInspectorBuilder.Length = HoverInspectorTextLimit;
            hoverInspectorBuilder.AppendLine();
            hoverInspectorBuilder.Append(FlatWorldLocalizationService.GetUiText("……参数过多，显示已在 60KB 处截断。"));
        }

        hoverInspectorText = hoverInspectorBuilder.ToString();
    }

    /// <summary>Unity 基础组件只输出少量关键运行态，避免把引擎内部字段淹没玩法参数。</summary>
    private static void AppendUnityRuntimeSummary(StringBuilder builder, GameObject target)
    {
        Rigidbody2D body = target.GetComponentInChildren<Rigidbody2D>(true);
        if (body != null)
        {
            builder.AppendLine(
                FlatWorldLocalizationService.GetUiFormat(
                    "刚体：{0}, Simulated={1}, 速度={2}, 角速度={3:0.###}",
                    body.bodyType, body.simulated, FormatInspectorValue(body.velocity), body.angularVelocity));
        }

        SpriteRenderer sprite = target.GetComponentInChildren<SpriteRenderer>(true);
        if (sprite != null)
        {
            builder.AppendLine(
                FlatWorldLocalizationService.GetUiFormat(
                    "精灵：{0}, Sorting={1}/{2}, 可见={3}",
                    sprite.sprite != null ? sprite.sprite.name : "-", sprite.sortingLayerName,
                    sprite.sortingOrder, sprite.isVisible));
        }

        Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
        if (colliders.Length > 0)
        {
            builder.AppendLine(FlatWorldLocalizationService.GetUiFormat("碰撞体：{0}", colliders.Length));
            int shown = Mathf.Min(colliders.Length, 8);
            for (int i = 0; i < shown; i++)
            {
                Collider2D collider = colliders[i];
                builder.AppendLine(
                    FlatWorldLocalizationService.GetUiFormat(
                        "  - {0}: Enabled={1}, Trigger={2}, Bounds={3}",
                        collider.GetType().Name, collider.enabled, collider.isTrigger,
                        FormatInspectorValue(collider.bounds)));
            }
            if (colliders.Length > shown)
                builder.AppendLine(FlatWorldLocalizationService.GetUiFormat("  - ……其余 {0} 个", colliders.Length - shown));
        }
    }

    /// <summary>反射游戏自有 MonoBehaviour 字段，补足蜜蜂 AI 等运行时内部参数。</summary>
    private void AppendGameplayComponents(StringBuilder builder, GameObject target)
    {
        Component[] components = target.GetComponentsInChildren<Component>(true);
        bool wroteHeader = false;
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null ||
                component is Transform ||
                component is Renderer ||
                component is Collider2D ||
                component is Rigidbody2D ||
                component is Item)
            {
                continue;
            }

            Type type = component.GetType();
            if (type.Assembly == typeof(MonoBehaviour).Assembly)
                continue;

            if (!wroteHeader)
            {
                builder.AppendLine();
                builder.AppendLine(FlatWorldLocalizationService.GetUiText("【运行时玩法组件】"));
                wroteHeader = true;
            }

            string path = BuildRelativeTransformPath(target.transform, component.transform);
            builder.AppendLine($"  ▸ {type.Name}{(string.IsNullOrEmpty(path) ? string.Empty : $"  @ {path}")}");
            AppendInspectorObject(builder, component, "    ", depth: 0, maxDepth: 1);
        }
    }

    /// <summary>按字段读取普通数据对象，不调用属性 Getter，避免调试器触发玩法副作用。</summary>
    private void AppendInspectorObject(
        StringBuilder builder,
        object value,
        string indent,
        int depth,
        int maxDepth)
    {
        if (value == null)
        {
            builder.AppendLine($"{indent}<null>");
            return;
        }

        for (int i = 0; i < hoverInspectorObjectPath.Count; i++)
        {
            if (ReferenceEquals(hoverInspectorObjectPath[i], value))
            {
                builder.AppendLine(FlatWorldLocalizationService.GetUiFormat(
                    "{0}<循环引用：{1}>", indent, value.GetType().Name));
                return;
            }
        }

        hoverInspectorObjectPath.Add(value);
        try
        {
            FieldInfo[] fields = GetHoverInspectorFields(value.GetType());
            if (fields.Length == 0)
            {
                builder.AppendLine($"{indent}{FormatInspectorValue(value)}");
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                if (builder.Length >= HoverInspectorTextLimit)
                    return;

                FieldInfo field = fields[i];
                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch (Exception exception)
                {
                    builder.AppendLine(FlatWorldLocalizationService.GetUiFormat(
                        "{0}{1}: <读取失败 {2}>", indent, field.Name, exception.GetType().Name));
                    continue;
                }

                if (ShouldExpandInspectorValue(fieldValue, depth, maxDepth))
                {
                    builder.AppendLine(FlatWorldLocalizationService.GetUiFormat("{0}{1}：", indent, field.Name));
                    AppendInspectorObject(builder, fieldValue, indent + "  ", depth + 1, maxDepth);
                }
                else
                {
                    builder.AppendLine(FlatWorldLocalizationService.GetUiFormat(
                        "{0}{1}: {2}", indent, field.Name, FormatInspectorValue(fieldValue)));
                }
            }
        }
        finally
        {
            hoverInspectorObjectPath.RemoveAt(hoverInspectorObjectPath.Count - 1);
        }
    }

    private static FieldInfo[] GetHoverInspectorFields(Type type)
    {
        if (hoverInspectorFieldCache.TryGetValue(type, out FieldInfo[] cached))
            return cached;

        List<FieldInfo> fields = new List<FieldInfo>(32);
        for (Type current = type;
             current != null && current != typeof(object) && current != typeof(MonoBehaviour) && current != typeof(Component);
             current = current.BaseType)
        {
            FieldInfo[] declared = current.GetFields(InstanceFlags | BindingFlags.DeclaredOnly);
            Array.Sort(declared, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            for (int i = 0; i < declared.Length; i++)
            {
                FieldInfo field = declared[i];
                if (field.IsStatic ||
                    field.Name.IndexOf("k__BackingField", StringComparison.Ordinal) >= 0 ||
                    typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                fields.Add(field);
            }
        }

        cached = fields.ToArray();
        hoverInspectorFieldCache[type] = cached;
        return cached;
    }

    private static bool ShouldExpandInspectorValue(object value, int depth, int maxDepth)
    {
        if (value == null || depth >= maxDepth)
            return false;

        Type type = value.GetType();
        if (IsSimpleInspectorType(type) ||
            value is UnityEngine.Object ||
            value is IDictionary ||
            value is IEnumerable)
        {
            return false;
        }

        string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return type.IsValueType ||
               assemblyName.Equals("Data", StringComparison.Ordinal) ||
               assemblyName.Equals("GamePlay", StringComparison.Ordinal) ||
               assemblyName.StartsWith("FlatWorld.", StringComparison.Ordinal);
    }

    private static bool IsSimpleInspectorType(Type type)
    {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(Vector2) ||
               type == typeof(Vector2Int) ||
               type == typeof(Vector3) ||
               type == typeof(Vector3Int) ||
               type == typeof(Quaternion) ||
               type == typeof(Color) ||
               type == typeof(Rect) ||
               type == typeof(Bounds);
    }

    private static string FormatInspectorValue(object value)
    {
        if (value == null)
            return "<null>";

        switch (value)
        {
            case string text:
                return string.IsNullOrEmpty(text) ? "\"\"" : text.Replace("\n", " ↵ ");
            case float floatValue:
                return floatValue.ToString("0.###", CultureInfo.InvariantCulture);
            case double doubleValue:
                return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
            case decimal decimalValue:
                return decimalValue.ToString("0.###", CultureInfo.InvariantCulture);
            case Vector2 vector2:
                return $"({vector2.x:0.###}, {vector2.y:0.###})";
            case Vector2Int vector2Int:
                return $"({vector2Int.x}, {vector2Int.y})";
            case Vector3 vector3:
                return $"({vector3.x:0.###}, {vector3.y:0.###}, {vector3.z:0.###})";
            case Vector3Int vector3Int:
                return $"({vector3Int.x}, {vector3Int.y}, {vector3Int.z})";
            case Quaternion quaternion:
                return $"({quaternion.x:0.###}, {quaternion.y:0.###}, {quaternion.z:0.###}, {quaternion.w:0.###})";
            case Color color:
                return $"RGBA({color.r:0.###}, {color.g:0.###}, {color.b:0.###}, {color.a:0.###})";
            case Rect rect:
                return $"({rect.x:0.###}, {rect.y:0.###}, {rect.width:0.###}, {rect.height:0.###})";
            case Bounds bounds:
                return $"Center={FormatInspectorValue(bounds.center)}, Size={FormatInspectorValue(bounds.size)}";
            case UnityEngine.Object unityObject:
                return unityObject != null
                    ? $"{unityObject.name} [{unityObject.GetType().Name}]"
                    : "<" + FlatWorldLocalizationService.GetUiText("销毁对象") + ">";
            case IDictionary dictionary:
                return FormatInspectorDictionary(dictionary);
            case IEnumerable enumerable when value is not string:
                return FormatInspectorEnumerable(enumerable);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    private static string FormatInspectorDictionary(IDictionary dictionary)
    {
        if (dictionary == null)
            return "<null>";

        StringBuilder builder = new StringBuilder();
        builder.Append($"Count={dictionary.Count}");
        if (dictionary.Count <= 0)
            return builder.ToString();

        builder.Append(" { ");
        int shown = 0;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (shown >= 8)
            {
                builder.Append("…");
                break;
            }

            if (shown > 0)
                builder.Append(", ");
            builder.Append(FormatInspectorValue(entry.Key));
            builder.Append(": ");
            builder.Append(FormatInspectorValue(entry.Value));
            shown++;
        }
        builder.Append(" }");
        return builder.ToString();
    }

    private static string FormatInspectorEnumerable(IEnumerable enumerable)
    {
        if (enumerable == null)
            return "<null>";

        StringBuilder builder = new StringBuilder();
        builder.Append("[");
        int count = 0;
        foreach (object entry in enumerable)
        {
            if (count >= 12)
            {
                builder.Append("…");
                break;
            }

            if (count > 0)
                builder.Append(", ");
            builder.Append(FormatInspectorValue(entry));
            count++;
        }
        builder.Append("]");
        return builder.ToString();
    }

    private static string BuildRelativeTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null || root == target)
            return string.Empty;

        Stack<string> names = new Stack<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", names) : target.name;
    }

    public void ClearTarget()
    {
        hoverInspectorTarget = null;
        hoverInspectorItem = null;
        hoverInspectorText = string.Empty;
        hoverInspectorTargetName = string.Empty;
        hoverInspectorScroll = Vector2.zero;
    }

    /// <summary>面板只负责显示，始终偏离光标，滚轮可在不离开目标的情况下浏览长参数。</summary>
    public void DrawOverlay()
    {
        if (!hoverInspectorEnabled)
            return;

        EnsureHoverInspectorStyles();

        Vector2 mousePosition = Event.current != null
            ? Event.current.mousePosition
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float panelWidth = Mathf.Clamp(Screen.width * 0.36f, 440f, 680f);
        float panelHeight = Mathf.Clamp(Screen.height * 0.68f, 240f, 720f);
        float x = mousePosition.x + 18f;
        if (x + panelWidth > Screen.width - 8f)
            x = mousePosition.x - panelWidth - 18f;
        float y = mousePosition.y + 18f;
        if (y + panelHeight > Screen.height - 8f)
            y = Screen.height - panelHeight - 8f;
        x = Mathf.Clamp(x, 8f, Mathf.Max(8f, Screen.width - panelWidth - 8f));
        y = Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - panelHeight - 8f));

        Rect panelRect = new Rect(x, y, panelWidth, panelHeight);
        Color previousColor = GUI.color;
        GUI.color = new Color(0.055f, 0.06f, 0.065f, 0.96f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = previousColor;

        Rect titleRect = new Rect(x + 14f, y + 10f, panelWidth - 28f, 28f);
        string localizedTitle = string.IsNullOrEmpty(hoverInspectorTargetName)
            ? FlatWorldLocalizationService.GetUiText("F6 属性观察器")
            : FlatWorldLocalizationService.GetUiFormat("F6 属性观察器  ·  {0}", hoverInspectorTargetName);
        GUI.Label(titleRect, localizedTitle, hoverInspectorTitleStyle);
        Rect hintRect = new Rect(x + 14f, y + 39f, panelWidth - 28f, 22f);
        GUI.Label(hintRect,
            FlatWorldLocalizationService.GetUiText("F6 关闭 · 滚轮浏览 · 参数约 8 次/秒刷新"),
            hoverInspectorHintStyle);

        Rect viewRect = new Rect(x + 10f, y + 66f, panelWidth - 20f, panelHeight - 76f);
        float contentWidth = Mathf.Max(100f, viewRect.width - 22f);
        float contentHeight = Mathf.Max(
            viewRect.height,
            hoverInspectorBodyStyle.CalcHeight(new GUIContent(hoverInspectorText), contentWidth) + 12f);
        hoverInspectorScroll.y = Mathf.Clamp(
            hoverInspectorScroll.y,
            0f,
            Mathf.Max(0f, contentHeight - viewRect.height));
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);
        hoverInspectorScroll = GUI.BeginScrollView(viewRect, hoverInspectorScroll, contentRect, false, true);
        GUI.Label(new Rect(4f, 2f, contentWidth - 8f, contentHeight - 4f), hoverInspectorText, hoverInspectorBodyStyle);
        GUI.EndScrollView();
    }

    private void EnsureHoverInspectorStyles()
    {
        hoverInspectorTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.86f, 0.42f) }
        };
        hoverInspectorBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            wordWrap = true,
            richText = false,
            normal = { textColor = new Color(0.92f, 0.94f, 0.95f) }
        };
        hoverInspectorHintStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.62f, 0.66f, 0.69f) }
        };
    }

    #endregion
}

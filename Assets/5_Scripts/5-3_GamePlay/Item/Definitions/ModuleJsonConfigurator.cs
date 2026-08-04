using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>把 ItemDefinition 中的参数应用到模块实例。</summary>
public static class ModuleJsonConfigurator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_Data", "ModSaveData", "MemoryPackableData", "item", "Item_Data",
        "gameObject", "transform"
    };

    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new UnitySerializedFieldContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public static void Apply(Module module, string itemId, string moduleName, string moduleId, string json)
    {
        if (module == null || string.IsNullOrWhiteSpace(json))
            return;

        JObject parameters;
        try
        {
            parameters = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"物品 {itemId} 的模块 {moduleName}({moduleId}) JSON 参数无效", exception);
        }

        ApplySpecialParameters(module, parameters);
        foreach (JProperty property in parameters.Properties())
        {
            if (ReservedNames.Contains(property.Name))
                throw new InvalidOperationException($"模块 {moduleName} 不允许配置保留字段：{property.Name}");
            if (property.Name.StartsWith("$", StringComparison.Ordinal))
                throw new InvalidOperationException($"模块 {moduleName} 包含未知特殊参数：{property.Name}");
        }

        try
        {
            JsonConvert.PopulateObject(parameters.ToString(Formatting.None), module, Settings);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"无法把 JSON 参数应用到物品 {itemId} 的模块 {moduleName}({moduleId})", exception);
        }
    }

    private static void ApplySpecialParameters(Module module, JObject parameters)
    {
        if (parameters["$transform"] is JObject transformObject)
        {
            Transform transform = module.transform;
            if (transformObject["localPosition"] != null)
                transform.localPosition = transformObject["localPosition"].ToObject<Vector3>();
            if (transformObject["localEulerAngles"] != null)
                transform.localEulerAngles = transformObject["localEulerAngles"].ToObject<Vector3>();
            if (transformObject["localScale"] != null)
                transform.localScale = transformObject["localScale"].ToObject<Vector3>();
            parameters.Remove("$transform");
        }

        if (parameters["$collider2D"] is JObject colliderObject)
        {
            ApplyCollider(module, colliderObject);
            parameters.Remove("$collider2D");
        }
    }

    private static void ApplyCollider(Module module, JObject data)
    {
        Collider2D collider = module.GetComponent<Collider2D>();
        if (collider == null)
            throw new MissingComponentException($"模块 {module.GetType().Name} 缺少 JSON 所需的 Collider2D");

        if (data["enabled"] != null) collider.enabled = data.Value<bool>("enabled");
        if (data["isTrigger"] != null) collider.isTrigger = data.Value<bool>("isTrigger");
        if (data["offset"] != null) collider.offset = data["offset"].ToObject<Vector2>();

        switch (collider)
        {
            case BoxCollider2D box when data["size"] != null:
                box.size = data["size"].ToObject<Vector2>();
                break;
            case CircleCollider2D circle when data["radius"] != null:
                circle.radius = data.Value<float>("radius");
                break;
            case CapsuleCollider2D capsule:
                if (data["size"] != null) capsule.size = data["size"].ToObject<Vector2>();
                if (data["direction"] != null)
                    capsule.direction = (CapsuleDirection2D)data.Value<int>("direction");
                break;
            case PolygonCollider2D polygon when data["points"] is JArray points:
                polygon.pathCount = 1;
                polygon.SetPath(0, points.ToObject<Vector2[]>());
                break;
        }
    }

    /// <summary>Json.NET 默认不读取 private [SerializeField]；这里与 Unity Inspector 的字段边界对齐。</summary>
    private sealed class UnitySerializedFieldContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            List<JsonProperty> properties = base.CreateProperties(type, memberSerialization).ToList();
            var names = new HashSet<string>(properties.Select(property => property.PropertyName), StringComparer.OrdinalIgnoreCase);

            for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool unitySerialized = field.IsPublic ||
                                           field.GetCustomAttribute<SerializeField>() != null ||
                                           field.GetCustomAttribute<SerializeReference>() != null;
                    if (!unitySerialized || field.IsStatic || field.IsInitOnly || field.IsNotSerialized ||
                        field.GetCustomAttribute<JsonIgnoreAttribute>() != null || names.Contains(field.Name))
                    {
                        continue;
                    }

                    JsonProperty property = base.CreateProperty(field, memberSerialization);
                    property.Readable = true;
                    property.Writable = true;
                    properties.Add(property);
                    names.Add(property.PropertyName);
                }
            }

            return properties;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>模块需要在普通字段校验外验证嵌套 JSON 结构时实现此契约。</summary>
public interface IModuleJsonParameterValidator
{
    void ValidateJsonParameters(JObject parameters);
}

/// <summary>把 ItemDefinition 中的参数应用到模块实例。</summary>
public static class ModuleJsonConfigurator
{
    #region 运行时配置计划

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_Data", "ModSaveData", "MemoryPackableData", "item", "Item_Data",
        "gameObject", "transform"
    };

    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new UnitySerializedFieldContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        // 仅允许迁移器登记的装备实例类型，避免开放 Json.NET 的任意类型反射。
        Converters =
        {
            new EquipmentInstanceJsonConverter(),
            new LayerMaskJsonConverter()
        }
    };

    public static void Apply(Module module, string itemId, string moduleName, string moduleId, string json)
    {
        if (module == null || string.IsNullOrWhiteSpace(json))
            return;

        Prepare(module, itemId, moduleName, moduleId, json).Apply(module);
    }

    /// <summary>装备放在库存中时没有运行时 Module，因此直接从定义参数还原静态装备效果。</summary>
    public static List<EquipmentInstance> ReadEquipmentInstances(
        string itemId,
        string moduleName,
        string moduleId,
        string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<EquipmentInstance>();

        JObject parameters = ParseParameters(itemId, moduleName, moduleId, json);
        JToken token = parameters[nameof(Mod_EquipmentStore.equipmentInstances)];
        if (token == null || token.Type == JTokenType.Null)
            return new List<EquipmentInstance>();

        JsonSerializer serializer = JsonSerializer.Create(Settings);
        return token.ToObject<List<EquipmentInstance>>(serializer) ?? new List<EquipmentInstance>();
    }

    /// <summary>
    /// 模块定义的只读配置计划。解析、严格校验与特殊字段拆分只执行一次，
    /// 每次出生仍向模块实例重新写入当前配置，避免复用对象保留上一次的玩法字段。
    /// </summary>
    public sealed class PreparedParameters
    {
        private readonly Type moduleType;
        private readonly string itemId;
        private readonly string moduleName;
        private readonly string moduleId;
        private readonly JObject regular;
        private readonly JObject transform;
        private readonly ItemColliderDefinitionDto collider;
        private readonly JsonSerializer serializer;
        private readonly Type colliderType;
        private readonly JObject customValidationParameters;

        internal PreparedParameters(Type moduleType, string itemId, string moduleName, string moduleId,
            JObject regular, JObject transform, ItemColliderDefinitionDto collider, Type colliderType, JObject customValidationParameters)
        {
            this.moduleType = moduleType;
            this.itemId = itemId;
            this.moduleName = moduleName;
            this.moduleId = moduleId;
            this.regular = regular;
            this.transform = transform;
            this.collider = collider;
            this.colliderType = colliderType;
            this.customValidationParameters = customValidationParameters;
            serializer = JsonSerializer.Create(Settings);
        }

        /// <summary>配置语法和字段类型已在编译时检查，只复查当前原型结构与模块自有约束。</summary>
        internal void ValidateTarget(Module module)
        {
            try
            {
                if (module == null || module.GetType() != moduleType)
                    throw new InvalidOperationException("模块类型与已编译配置不一致。");
                if (collider != null && module.GetComponent<Collider2D>()?.GetType() != colliderType)
                    throw new MissingComponentException("Collider2D 与已编译配置不一致。");
                if (module is IModuleJsonParameterValidator validator)
                    validator.ValidateJsonParameters((JObject)customValidationParameters.DeepClone());
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"物品 {itemId} 的模块 {moduleName}({moduleId}) 原型与已编译 JSON 参数不匹配", exception);
            }
        }

        /// <summary>将已校验参数应用到同类型模块，不重复解析和生成 JSON 字符串。</summary>
        public void Apply(Module module)
        {
            if (module == null || module.GetType() != moduleType)
                throw new InvalidOperationException($"物品 {itemId} 的模块 {moduleName} 类型与已编译配置不一致。");

            try
            {
                ApplySpecialParameters(module, transform, collider);
                if (!regular.HasValues)
                    return;

                using JsonReader reader = regular.CreateReader();
                serializer.Populate(reader, module);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"无法把 JSON 参数应用到物品 {itemId} 的模块 {moduleName}({moduleId})", exception);
            }
        }
    }

    /// <summary>根据当前模块结构解析并校验一次不可变配置。</summary>
    public static PreparedParameters Prepare(Module module, string itemId, string moduleName,
        string moduleId, string json)
    {
        JObject parameters = ParseParameters(itemId, moduleName, moduleId, json);
        try
        {
            ValidateParameters(module, moduleName, parameters);
            JObject transform = parameters["$transform"] as JObject;
            JObject collider = parameters["$collider2D"] as JObject;
            JObject customValidationParameters = module is IModuleJsonParameterValidator
                ? (JObject)parameters.DeepClone() : null;
            Type colliderType = collider != null ? module.GetComponent<Collider2D>().GetType() : null;
            parameters.Remove("$transform");
            parameters.Remove("$collider2D");
            return new PreparedParameters(module.GetType(), itemId, moduleName, moduleId,
                parameters, transform, collider?.ToObject<ItemColliderDefinitionDto>(), colliderType, customValidationParameters);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"物品 {itemId} 的模块 {moduleName}({moduleId}) JSON 参数无效", exception);
        }
    }

    #endregion

    /// <summary>按运行时同一套严格契约只读校验模块参数，不修改 Prefab 或运行时实例。</summary>
    public static void Validate(Module module, string itemId, string moduleName, string moduleId, string json)
    {
        if (module == null || string.IsNullOrWhiteSpace(json))
            return;

        JObject parameters = ParseParameters(itemId, moduleName, moduleId, json);
        try
        {
            ValidateParameters(module, moduleName, parameters);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"物品 {itemId} 的模块 {moduleName}({moduleId}) JSON 参数与 {module.GetType().Name} 不匹配",
                exception);
        }
    }

    /// <summary>解析模块参数根对象。</summary>
    private static JObject ParseParameters(
        string itemId,
        string moduleName,
        string moduleId,
        string json)
    {
        try
        {
            return JsonConfigurationCache.ParseObject(json);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"物品 {itemId} 的模块 {moduleName}({moduleId}) JSON 参数无效", exception);
        }
    }

    /// <summary>校验普通字段及特殊块，确保内容配置不会延迟到实例化时才失败。</summary>
    private static void ValidateParameters(Module module, string moduleName, JObject parameters)
    {
        JsonSerializer serializer = JsonSerializer.Create(Settings);
        if (serializer.ContractResolver.ResolveContract(module.GetType()) is not JsonObjectContract contract)
            throw new JsonSerializationException($"模块类型 {module.GetType().Name} 不是可配置对象");

        ValidateTransformParameters(parameters["$transform"], serializer);
        ValidateColliderParameters(module, parameters["$collider2D"], serializer);

        foreach (JProperty property in parameters.Properties())
        {
            if (ReservedNames.Contains(property.Name))
                throw new InvalidOperationException($"模块 {moduleName} 不允许配置保留字段：{property.Name}");
            if (property.Name.StartsWith("$", StringComparison.Ordinal))
            {
                if (property.Name is "$transform" or "$collider2D")
                    continue;
                throw new InvalidOperationException($"模块 {moduleName} 包含未知特殊参数：{property.Name}");
            }

            JsonProperty target = contract.Properties.GetClosestMatchProperty(property.Name);
            if (target == null || target.Ignored || !target.Writable)
                throw new JsonSerializationException(
                    $"模块 {module.GetType().Name} 不包含可写配置字段 {property.Name}");

            property.Value.ToObject(target.PropertyType, serializer);
        }

        if (module is IModuleJsonParameterValidator parameterValidator)
            parameterValidator.ValidateJsonParameters(parameters);
    }

    /// <summary>校验 Transform 特殊参数的字段和类型。</summary>
    private static void ValidateTransformParameters(JToken token, JsonSerializer serializer)
    {
        if (token == null)
            return;
        if (token is not JObject transform)
            throw new JsonSerializationException("$transform 必须是对象");

        foreach (JProperty property in transform.Properties())
        {
            if (property.Name is not ("localPosition" or "localEulerAngles" or "localScale"))
                throw new JsonSerializationException($"$transform 包含未知字段 {property.Name}");
            property.Value.ToObject<Vector3>(serializer);
        }
    }

    /// <summary>校验 Collider2D 特殊参数的字段、类型与目标组件。</summary>
    private static void ValidateColliderParameters(Module module, JToken token, JsonSerializer serializer)
    {
        if (token == null)
            return;
        if (token is not JObject data)
            throw new JsonSerializationException("$collider2D 必须是对象");

        Collider2D collider = module.GetComponent<Collider2D>();
        if (collider == null)
            throw new MissingComponentException($"模块 {module.GetType().Name} 缺少 JSON 所需的 Collider2D");

        foreach (JProperty property in data.Properties())
        {
            Type valueType = property.Name switch
            {
                "type" => typeof(string),
                "enabled" or "isTrigger" => typeof(bool),
                "offset" => typeof(Vector2),
                "size" when collider is BoxCollider2D or CapsuleCollider2D => typeof(Vector2),
                "edgeRadius" when collider is BoxCollider2D => typeof(float),
                "radius" when collider is CircleCollider2D => typeof(float),
                "direction" when collider is CapsuleCollider2D => typeof(int),
                "points" when collider is PolygonCollider2D => typeof(Vector2[]),
                _ => null
            };
            if (valueType == null)
                throw new JsonSerializationException(
                    $"{collider.GetType().Name} 不支持 $collider2D.{property.Name}");

            property.Value.ToObject(valueType, serializer);
        }

        string configuredType = data.Value<string>("type");
        if (!string.IsNullOrWhiteSpace(configuredType) &&
            !string.Equals(configuredType, collider.GetType().Name, StringComparison.Ordinal))
        {
            throw new JsonSerializationException(
                $"$collider2D.type={configuredType} 与目标组件 {collider.GetType().Name} 不一致");
        }
    }

    private static void ApplySpecialParameters(Module module, JObject transformObject, ItemColliderDefinitionDto colliderObject)
    {
        if (transformObject != null)
        {
            Transform transform = module.transform;
            if (transformObject["localPosition"] != null)
                transform.localPosition = transformObject["localPosition"].ToObject<Vector3>();
            if (transformObject["localEulerAngles"] != null)
                transform.localEulerAngles = transformObject["localEulerAngles"].ToObject<Vector3>();
            if (transformObject["localScale"] != null)
                transform.localScale = transformObject["localScale"].ToObject<Vector3>();
        }

        if (colliderObject != null)
            ItemPhysicsDefinitionRuntime.ApplyColliderParameters(module.GetComponent<Collider2D>(), colliderObject);
    }

    /// <summary>把 JSON 整数或字符串安全转换为 Unity LayerMask。</summary>
    private sealed class LayerMaskJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(LayerMask);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return existingValue ?? default(LayerMask);

            if (reader.TokenType == JsonToken.Integer)
                return new LayerMask { value = Convert.ToInt32(reader.Value) };

            if (reader.TokenType == JsonToken.String &&
                int.TryParse(reader.Value?.ToString(), out int value))
            {
                return new LayerMask { value = value };
            }

            throw new JsonSerializationException(
                $"LayerMask 只支持整数或数字字符串，实际类型：{reader.TokenType}");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((LayerMask)value).value);
        }
    }

    /// <summary>
    /// 还原装备模块中的 SerializeReference 多态项。类型名来自迁移器白名单，
    /// 不使用 TypeNameHandling，避免 JSON 配置获得任意类型实例化能力。
    /// </summary>
    private sealed class EquipmentInstanceJsonConverter : JsonConverter
    {
        private static readonly Dictionary<string, Type> AllowedTypes =
            new(StringComparer.Ordinal)
            {
                [nameof(EquipmentInstance_Debug)] = typeof(EquipmentInstance_Debug),
                [nameof(EquipmentInstance_Bag)] = typeof(EquipmentInstance_Bag),
                [nameof(EquipmentInstance_Speed)] = typeof(EquipmentInstance_Speed),
                [nameof(EquipmentInstance_Defense)] = typeof(EquipmentInstance_Defense),
                [nameof(EquipmentInstance_WaterInsulation)] = typeof(EquipmentInstance_WaterInsulation),
                [nameof(EquipmentInstance_ThermalInsulation)] = typeof(EquipmentInstance_ThermalInsulation),
                [nameof(EquipmentInstance_PressureProtection)] = typeof(EquipmentInstance_PressureProtection),
                [nameof(EquipmentInstance_SpacesuitSupport)] = typeof(EquipmentInstance_SpacesuitSupport)
            };

        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(EquipmentInstance);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            JObject value = JObject.Load(reader);
            string concreteTypeName = value.Value<string>("$concreteType");
            if (!AllowedTypes.TryGetValue(concreteTypeName ?? string.Empty, out Type concreteType))
            {
                throw new JsonSerializationException(
                    $"EquipmentInstance 类型无效或未登记：{concreteTypeName ?? "<empty>"}");
            }

            value.Remove("$concreteType");
            EquipmentInstance instance = (EquipmentInstance)Activator.CreateInstance(concreteType);
            using (JsonReader objectReader = value.CreateReader())
            {
                serializer.Populate(objectReader, instance);
            }
            return instance;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException("EquipmentInstanceJsonConverter 只负责读取迁移配置。");
        }
    }

    /// <summary>Json.NET 默认不读取 private [SerializeField]；这里与 Unity Inspector 的字段边界对齐。</summary>
    private sealed class UnitySerializedFieldContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            List<JsonProperty> properties = base.CreateProperties(type, memberSerialization).ToList();

            for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool unitySerialized = field.IsPublic ||
                                           field.GetCustomAttribute<SerializeField>() != null ||
                                           field.GetCustomAttribute<SerializeReference>() != null;
                    if (!unitySerialized || field.IsStatic || field.IsInitOnly || field.IsNotSerialized ||
                        field.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    {
                        continue;
                    }

                    int existingIndex = properties.FindIndex(candidate =>
                        string.Equals(candidate.PropertyName, field.Name, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0 && (properties[existingIndex].Writable || properties[existingIndex].Ignored))
                        continue;

                    JsonProperty property = base.CreateProperty(field, memberSerialization);
                    property.Readable = true;
                    property.Writable = true;
                    // 只读公开属性不能遮住同名 Unity 字段，配置仍绑定实际字段而非开放计算属性。
                    if (existingIndex >= 0) properties[existingIndex] = property;
                    else properties.Add(property);
                }
            }

            return properties;
        }
    }
}

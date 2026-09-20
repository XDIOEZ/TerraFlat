using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// 地块配置的严格 JSON 边界。禁止 $type 等元数据、重复键、未知字段和非有限数值；
/// 只映射配置字段，不序列化方法、Unity 对象或 TileData 的位置与工作进度。
/// 私有 SerializeField 参数仍可配置，避免迁移时漏掉冰面与雪地的现有参数。
/// </summary>
public static class TileDefinitionJson
{
    #region 严格读取
    private const int MaximumCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Error,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        MaxDepth = 32,
        ContractResolver = new ConfigurationFieldsResolver(),
        Converters = { new StringEnumConverter() }
    };

    /// <summary>读取单个根对象，保留可定位的 JSON 路径和行号。</summary>
    public static JObject Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumCharacters)
            throw new InvalidDataException("地块 JSON 为空或超过 4 MiB 字符限制。");
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        JObject root = JObject.Load(reader, new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        });
        while (reader.Read())
            if (reader.TokenType != JsonToken.Comment)
                throw new InvalidDataException("地块 JSON 只能有一个根对象。");
        ValidateTokens(root);
        return root;
    }

    /// <summary>本体、MOD 和编辑器统一使用相同的字段及类型限制。</summary>
    public static T Read<T>(JToken token)
    {
        if (token is not JObject) throw new InvalidDataException($"{typeof(T).Name} 必须是 JSON 对象。");
        ValidateTokens(token);
        T value = token.ToObject<T>(JsonSerializer.Create(Settings));
        if (value == null) throw new InvalidDataException($"{typeof(T).Name} 不能为空。");
        return value;
    }

    /// <summary>仅填充已经由显式工厂创建的类型，不允许 JSON 选择任意程序集类。</summary>
    public static void Populate(JObject parameters, object target)
    {
        if (parameters == null || target == null) throw new InvalidDataException("地块组件或 parameters 不能为空。");
        ValidateTokens(parameters);
        using JsonReader reader = parameters.CreateReader();
        JsonSerializer.Create(Settings).Populate(reader, target);
        ValidateFields(target, target.GetType().Name, 0);
    }

    /// <summary>配置副本及编辑器保存共用同一个字段契约。</summary>
    public static JObject Write(object value) => JObject.FromObject(value, JsonSerializer.Create(Settings));

    private static void ValidateTokens(JToken token)
    {
        if (token is JProperty property && property.Name.StartsWith("$", StringComparison.Ordinal))
            throw new InvalidDataException($"地块配置禁止类型/引用元数据：{property.Path}");
        if (token is JValue value && value.Type == JTokenType.Float)
        {
            double number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new InvalidDataException($"地块数值必须有限：{value.Path}");
        }
        if (token is JContainer container)
            foreach (JToken child in container.Children()) ValidateTokens(child);
    }
    #endregion

    #region 参数校验
    /// <summary>验证已有 C# 配置的 Min/Range 约束，同时防止数字溢出为 Infinity。</summary>
    public static void ValidateFields(object value, string path, int depth = 0)
    {
        if (value == null || value is string || value is JToken) return;
        if (depth > 16) throw new InvalidDataException($"地块配置嵌套过深：{path}");
        Type type = value.GetType();
        if (type.IsEnum)
        {
            if (!type.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(type, value))
                throw new InvalidDataException($"地块枚举无效：{path}={value}");
            return;
        }
        if (value is float single && (float.IsNaN(single) || float.IsInfinity(single)) ||
            value is double number && (double.IsNaN(number) || double.IsInfinity(number)))
            throw new InvalidDataException($"地块数值必须有限：{path}");
        if (type.IsPrimitive || type == typeof(decimal)) return;
        if (value is UnityEngine.Object)
            throw new InvalidDataException($"JSON 配置不能直接保存 Unity 对象：{path}");
        if (value is IEnumerable sequence)
        {
            int i = 0;
            foreach (object child in sequence) ValidateFields(child, $"{path}[{i++}]", depth + 1);
            return;
        }
        foreach (FieldInfo field in ConfigurationFieldsResolver.Fields(type))
        {
            object child = field.GetValue(value);
            string fieldPath = path + "." + field.Name;
            ValidateFields(child, fieldPath, depth + 1);
            if (child is not IConvertible || child is bool || child is string || field.FieldType.IsEnum) continue;
            double n = Convert.ToDouble(child, System.Globalization.CultureInfo.InvariantCulture);
            MinAttribute min = field.GetCustomAttribute<MinAttribute>();
            RangeAttribute range = field.GetCustomAttribute<RangeAttribute>();
            if (min != null && n < min.min || range != null && (n < range.min || n > range.max))
                throw new InvalidDataException($"地块参数超出允许范围：{fieldPath}={n}");
        }
    }
    #endregion

    #region 配置字段契约
    private sealed class ConfigurationFieldsResolver : DefaultContractResolver
    {
        public ConfigurationFieldsResolver() => NamingStrategy = new CamelCaseNamingStrategy();

        internal static IEnumerable<FieldInfo> Fields(Type type) =>
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => !field.IsStatic && !field.IsNotSerialized &&
                    (field.IsPublic || field.IsDefined(typeof(SerializeField), true) ||
                     field.IsDefined(typeof(JsonPropertyAttribute), true)))
                .Where(field => !typeof(TileData).IsAssignableFrom(type) ||
                    field.Name is not ("ID" or "Name" or "position" or "workTime" or "Version"));

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            return Fields(type).Select(field =>
            {
                JsonProperty property = CreateProperty(field, memberSerialization);
                property.Readable = true;
                property.Writable = !field.IsInitOnly;
                return property;
            }).ToList();
        }
    }
    #endregion
}

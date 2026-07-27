using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region MOD 本地化

/// <summary>MOD 文本注册表；使用命名空间键并提供语言回退。</summary>
public static class ModLocalizationRegistry
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    public static string CurrentLanguage { get; private set; } = ResolveSystemLanguage();

    public static void Clear()
    {
        Tables.Clear();
        CurrentLanguage = ResolveSystemLanguage();
    }

    public static void SetLanguage(string language)
    {
        CurrentLanguage = string.IsNullOrWhiteSpace(language) ? ResolveSystemLanguage() : language.Trim();
        PlayerPrefs.SetString("FlatWorld.Mods.Language", CurrentLanguage);
        PlayerPrefs.Save();
    }

    public static void Register(string modId, ModLocalizationDocument document)
    {
        if (document == null || string.IsNullOrWhiteSpace(document.Language))
            throw new InvalidDataException($"MOD {modId} 本地化文件缺少 language");

        if (!Tables.TryGetValue(document.Language.Trim(), out Dictionary<string, string> table))
        {
            table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Tables.Add(document.Language.Trim(), table);
        }

        foreach (KeyValuePair<string, string> entry in document.Entries ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            string key = entry.Key.Contains(":", StringComparison.Ordinal) ? entry.Key.Trim() : $"{modId}:{entry.Key.Trim()}";
            table[key] = entry.Value ?? string.Empty;
        }
    }

    public static string Translate(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback ?? string.Empty;

        if (TryGet(CurrentLanguage, key, out string value) ||
            TryGet(GetNeutralLanguage(CurrentLanguage), key, out value) ||
            TryGet("default", key, out value) ||
            TryGet("en", key, out value))
        {
            return value;
        }

        return fallback ?? key;
    }

    private static bool TryGet(string language, string key, out string value)
    {
        value = null;
        return !string.IsNullOrWhiteSpace(language) &&
               Tables.TryGetValue(language, out Dictionary<string, string> table) &&
               table.TryGetValue(key, out value);
    }

    private static string ResolveSystemLanguage()
    {
        string saved = PlayerPrefs.GetString("FlatWorld.Mods.Language", string.Empty);
        return string.IsNullOrWhiteSpace(saved) ? CultureInfo.CurrentUICulture.Name : saved;
    }

    private static string GetNeutralLanguage(string language)
    {
        int separator = language?.IndexOfAny(new[] { '-', '_' }) ?? -1;
        return separator > 0 ? language.Substring(0, separator) : language;
    }
}

#endregion

#region MOD 设置

/// <summary>MOD 设置 schema 与包外持久化；服务端和世界级设置参与联机指纹。</summary>
public static class ModSettingsRegistry
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Dictionary<string, ModSettingDefinition> Definitions = new(IdComparer);
    private static readonly Dictionary<string, string> Owners = new(IdComparer);
    private static readonly Dictionary<string, JToken> Values = new(IdComparer);

    public static string ConfigRootPath => Path.Combine(Application.persistentDataPath, "ModConfigs");

    public static void Clear()
    {
        Definitions.Clear();
        Owners.Clear();
        Values.Clear();
    }

    public static void Register(string modId, ModSettingsDocument document)
    {
        foreach (ModSettingDefinition definition in document?.Settings ?? Enumerable.Empty<ModSettingDefinition>())
        {
            string id = NormalizeId(modId, definition.Id);
            if (Definitions.ContainsKey(id))
                throw new InvalidDataException($"重复 MOD 设置 ID：{id}");

            definition.Id = id;
            ValidateDefinition(definition);
            Definitions.Add(id, definition);
            Owners.Add(id, modId);
            Values.Add(id, definition.DefaultValue?.DeepClone() ?? JValue.CreateNull());
        }

        LoadOwnerValues(modId);
    }

    public static string GetJson(string modId, string settingId)
    {
        string id = NormalizeId(modId, settingId);
        return Values.TryGetValue(id, out JToken value) ? value.ToString(Formatting.None) : string.Empty;
    }

    public static IReadOnlyList<ModSettingDefinition> GetDefinitions(string modId)
    {
        return Owners
            .Where(pair => IdComparer.Equals(pair.Value, modId))
            .Select(pair => Definitions[pair.Key])
            .OrderBy(definition => definition.Id, IdComparer)
            .ToList();
    }

    public static string GetDisplayValue(string settingId)
    {
        return Values.TryGetValue(settingId ?? string.Empty, out JToken value)
            ? value.Type == JTokenType.String ? value.Value<string>() : value.ToString(Formatting.None)
            : string.Empty;
    }

    public static bool GetBool(string modId, string settingId, bool fallback = false)
    {
        string id = NormalizeId(modId, settingId);
        return Values.TryGetValue(id, out JToken value) && value.Type == JTokenType.Boolean
            ? value.Value<bool>()
            : fallback;
    }

    public static double GetNumber(string modId, string settingId, double fallback = 0d)
    {
        string id = NormalizeId(modId, settingId);
        return Values.TryGetValue(id, out JToken value) &&
               (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            ? value.Value<double>()
            : fallback;
    }

    public static string GetString(string modId, string settingId, string fallback = "")
    {
        string id = NormalizeId(modId, settingId);
        return Values.TryGetValue(id, out JToken value) && value.Type == JTokenType.String
            ? value.Value<string>()
            : fallback;
    }

    public static void SetClientValue(string modId, string settingId, string jsonValue)
    {
        string id = NormalizeId(modId, settingId);
        if (!Definitions.TryGetValue(id, out ModSettingDefinition definition))
            throw new KeyNotFoundException($"找不到 MOD 设置：{id}");
        if (!string.Equals(definition.Scope, "client", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Lua 只能修改 client 设置：{id}");

        JToken value = string.IsNullOrWhiteSpace(jsonValue) ? JValue.CreateNull() : JToken.Parse(jsonValue);
        Values[id] = ValidateValue(definition, value);
        SaveOwnerValues(modId);
    }

    public static string ComputeAuthorityHash()
    {
        StringBuilder builder = new();
        foreach (KeyValuePair<string, ModSettingDefinition> pair in Definitions.OrderBy(pair => pair.Key, IdComparer))
        {
            if (string.Equals(pair.Value.Scope, "client", StringComparison.OrdinalIgnoreCase))
                continue;

            builder.Append(pair.Key).Append('=').Append(Values[pair.Key].ToString(Formatting.None)).Append('\n');
        }

        using SHA256 sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void LoadOwnerValues(string modId)
    {
        Directory.CreateDirectory(ConfigRootPath);
        string path = GetOwnerPath(modId);
        if (!File.Exists(path))
            return;

        JObject stored = JObject.Parse(File.ReadAllText(path));
        foreach (JProperty property in stored.Properties())
        {
            string id = NormalizeId(modId, property.Name);
            if (Definitions.TryGetValue(id, out ModSettingDefinition definition) &&
                IdComparer.Equals(Owners[id], modId))
            {
                Values[id] = ValidateValue(definition, property.Value);
            }
        }
    }

    private static void SaveOwnerValues(string modId)
    {
        Directory.CreateDirectory(ConfigRootPath);
        JObject document = new();
        foreach (string id in Owners.Where(pair => IdComparer.Equals(pair.Value, modId)).Select(pair => pair.Key).OrderBy(id => id, IdComparer))
            document[id] = Values[id].DeepClone();

        string path = GetOwnerPath(modId);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, document.ToString(Formatting.Indented));
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    private static string GetOwnerPath(string modId)
    {
        string safeName = new string(modId.Where(character => char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-').ToArray());
        return Path.Combine(ConfigRootPath, safeName + ".json");
    }

    private static string NormalizeId(string modId, string settingId)
    {
        if (string.IsNullOrWhiteSpace(settingId))
            throw new InvalidDataException($"MOD {modId} 包含空设置 ID");
        string trimmed = settingId.Trim();
        return trimmed.Contains(":", StringComparison.Ordinal) ? trimmed : $"{modId}:{trimmed}";
    }

    private static void ValidateDefinition(ModSettingDefinition definition)
    {
        string type = definition.Type?.Trim().ToLowerInvariant();
        if (type is not ("bool" or "int" or "float" or "string" or "enum" or "color" or "content"))
            throw new InvalidDataException($"MOD 设置 {definition.Id} 类型无效：{definition.Type}");

        string scope = definition.Scope?.Trim().ToLowerInvariant();
        if (scope is not ("client" or "world" or "server"))
            throw new InvalidDataException($"MOD 设置 {definition.Id} 作用域无效：{definition.Scope}");

        definition.Type = type;
        definition.Scope = scope;
        definition.DefaultValue = ValidateValue(definition, definition.DefaultValue ?? JValue.CreateNull());
    }

    private static JToken ValidateValue(ModSettingDefinition definition, JToken value)
    {
        switch (definition.Type)
        {
            case "bool":
                if (value.Type != JTokenType.Boolean)
                    throw new InvalidDataException($"设置 {definition.Id} 必须是 bool");
                return new JValue(value.Value<bool>());
            case "int":
                if (value.Type != JTokenType.Integer)
                    throw new InvalidDataException($"设置 {definition.Id} 必须是 int");
                return new JValue((long)Math.Round(ClampNumber(definition, value.Value<double>())));
            case "float":
                if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
                    throw new InvalidDataException($"设置 {definition.Id} 必须是 float");
                return new JValue(ClampNumber(definition, value.Value<double>()));
            case "enum":
            {
                string enumValue = value.Type == JTokenType.String ? value.Value<string>() : null;
                if (string.IsNullOrWhiteSpace(enumValue) || !definition.Options.Contains(enumValue, IdComparer))
                    throw new InvalidDataException($"设置 {definition.Id} 不在允许的枚举值中");
                return new JValue(enumValue);
            }
            default:
                if (value.Type != JTokenType.String)
                    throw new InvalidDataException($"设置 {definition.Id} 必须是字符串");
                return new JValue(value.Value<string>() ?? string.Empty);
        }
    }

    private static double ClampNumber(ModSettingDefinition definition, double value)
    {
        if (definition.Minimum.HasValue)
            value = Math.Max(value, definition.Minimum.Value);
        if (definition.Maximum.HasValue)
            value = Math.Min(value, definition.Maximum.Value);
        return value;
    }
}

#endregion

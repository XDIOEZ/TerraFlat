using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 一种可注册的地块污染指标定义。
/// 污染系统只维护数值与身份，不把具体疾病、作物或动物逻辑硬编码进定义。
/// </summary>
[Serializable]
public sealed class ContaminationDefinition
{
    public ContaminationDefinition(
        string id,
        string displayName,
        string description,
        string category,
        float minimumValue,
        float maximumValue,
        float defaultValue)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        DefaultValue = defaultValue;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public float MinimumValue { get; }
    public float MaximumValue { get; }
    public float DefaultValue { get; }

    /// <summary>映射到 ChunkTerrainData 的稳定环境层 ID。</summary>
    public string EnvironmentLayerId => $"flatworld.contamination.{Id}";

    /// <summary>把外部写入约束到定义允许的区间。</summary>
    public float Clamp(float value) => Math.Clamp(value, MinimumValue, MaximumValue);

    /// <summary>判断数值是否等于该污染指标的默认背景值。</summary>
    public bool IsDefault(float value) => Math.Abs(value - DefaultValue) <= 0.0001f;

    /// <summary>把实际值转换为 0～1 的归一化负荷，供感染概率或 UI 统一读取。</summary>
    public float Normalize(float value)
    {
        float range = MaximumValue - MinimumValue;
        return range <= 0.0001f ? 0f : Math.Clamp((Clamp(value) - MinimumValue) / range, 0f, 1f);
    }
}

/// <summary>污染定义 JSON DTO；本体和 MOD 共用同一份 schema。</summary>
[Serializable]
public sealed class ContaminationDefinitionDto
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("displayName", Required = Required.Always)]
    public string DisplayName;

    [JsonProperty("description")]
    public string Description = string.Empty;

    [JsonProperty("category", Required = Required.Always)]
    public string Category;

    [JsonProperty("minimumValue")]
    public float MinimumValue;

    [JsonProperty("maximumValue")]
    public float MaximumValue = 1f;

    [JsonProperty("defaultValue")]
    public float DefaultValue;

    [JsonProperty("labelKey")]
    public string LabelKey;

    [JsonProperty("descriptionKey")]
    public string DescriptionKey;
}

/// <summary>一个污染定义分包。</summary>
[Serializable]
public sealed class ContaminationCatalogDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion;

    [JsonProperty("contaminations", Required = Required.Always)]
    public List<ContaminationDefinitionDto> Contaminations = new();
}

/// <summary>污染目录清单。</summary>
[Serializable]
public sealed class ContaminationManifestDto
{
    [JsonProperty("schemaVersion", Required = Required.Always)]
    public int SchemaVersion;

    [JsonProperty("packages", Required = Required.Always)]
    public List<ContaminationPackageDto> Packages = new();
}

/// <summary>污染清单中的一个分包条目。</summary>
[Serializable]
public sealed class ContaminationPackageDto
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("path", Required = Required.Always)]
    public string Path;

    [JsonProperty("enabled")]
    public bool Enabled = true;
}

/// <summary>污染定义的严格解析和运行时构建入口。</summary>
public static class ContaminationDefinitionFactory
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error
    };

    /// <summary>严格解析一个污染定义，供 MOD definitionFiles 复用。</summary>
    public static ContaminationDefinitionDto DeserializeDefinition(JToken token)
    {
        if (token == null)
            throw new InvalidDataException("污染定义为空");

        JsonSerializer serializer = JsonSerializer.Create(StrictJsonSettings);
        return token.ToObject<ContaminationDefinitionDto>(serializer)
            ?? throw new InvalidDataException("污染定义无法反序列化");
    }

    /// <summary>严格解析一个本体污染分包。</summary>
    public static ContaminationCatalogDto DeserializeCatalog(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("污染定义分包为空");

        return JsonConvert.DeserializeObject<ContaminationCatalogDto>(json, StrictJsonSettings)
            ?? throw new InvalidDataException("污染定义分包无法反序列化");
    }

    /// <summary>校验并构建一个运行时污染定义。</summary>
    public static ContaminationDefinition Build(ContaminationDefinitionDto dto)
    {
        if (dto == null)
            throw new InvalidDataException("污染定义 DTO 为空");

        string id = NormalizeRequired(dto.Id, "污染 ID");
        if (!id.Contains(':', StringComparison.Ordinal) || id.StartsWith(":", StringComparison.Ordinal) ||
            id.EndsWith(":", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"污染 ID 必须包含稳定命名空间：{id}");
        }

        if (ContainsWhitespaceOrControl(id))
            throw new InvalidDataException($"污染 ID 不能包含空白或控制字符：{id}");

        string displayName = NormalizeRequired(dto.DisplayName, $"污染 {id} displayName");
        string category = NormalizeRequired(dto.Category, $"污染 {id} category").ToLowerInvariant();
        ValidateFinite(dto.MinimumValue, id, nameof(dto.MinimumValue));
        ValidateFinite(dto.MaximumValue, id, nameof(dto.MaximumValue));
        ValidateFinite(dto.DefaultValue, id, nameof(dto.DefaultValue));
        if (dto.MinimumValue < 0f)
            throw new InvalidDataException($"污染 {id} minimumValue 不能小于 0");
        if (dto.MaximumValue <= dto.MinimumValue)
            throw new InvalidDataException($"污染 {id} maximumValue 必须大于 minimumValue");
        if (dto.DefaultValue < dto.MinimumValue || dto.DefaultValue > dto.MaximumValue)
            throw new InvalidDataException($"污染 {id} defaultValue 超出允许区间");

        return new ContaminationDefinition(
            id,
            displayName,
            dto.Description?.Trim() ?? string.Empty,
            category,
            dto.MinimumValue,
            dto.MaximumValue,
            dto.DefaultValue);
    }

    /// <summary>校验并构建整个污染分包。</summary>
    public static List<ContaminationDefinition> BuildCatalog(ContaminationCatalogDto catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("污染目录为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的污染 schemaVersion：{catalog.SchemaVersion}");

        var definitions = new List<ContaminationDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContaminationDefinitionDto dto in catalog.Contaminations ?? new List<ContaminationDefinitionDto>())
        {
            ContaminationDefinition definition = Build(dto);
            if (!ids.Add(definition.Id))
                throw new InvalidDataException($"污染分包包含重复 ID：{definition.Id}");
            definitions.Add(definition);
        }
        return definitions;
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{field} 不能为空");
        return value.Trim();
    }

    private static void ValidateFinite(float value, string id, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"污染 {id} 的 {field} 必须是有限数值");
    }

    private static bool ContainsWhitespaceOrControl(string value)
    {
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                return true;
        }
        return false;
    }
}

/// <summary>本体污染指标的稳定 ID；具体疾病逻辑只引用 ID，不写死环境层字符串。</summary>
public static class ContaminationIds
{
    public const string Filth = "core:filth";
    public const string PlantBlight = "core:plant_blight";
    public const string AnimalPlague = "core:animal_plague";
    public const string MosquitoPressure = "core:mosquito_pressure";
}

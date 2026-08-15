using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// 文字库运行时访问接口。调用方只依赖分类查询和生成器，不关心 JSON 文件、缓存或随机数实现。
/// </summary>
public interface ITextLibraryService
{
    /// <summary>文字库是否已完成有效配置加载。</summary>
    bool IsReady { get; }

    /// <summary>当前可用的文字分类 ID。</summary>
    IReadOnlyList<string> LibraryIds { get; }

    /// <summary>从指定文字分类中随机取一条内容。</summary>
    bool TryGetRandom(string libraryId, out string value);

    /// <summary>按生成器配置随机组合多个文字分类。</summary>
    bool TryGenerate(string generatorId, out string value);

    /// <summary>生成失败时返回调用方提供的兜底值。</summary>
    string GenerateOrDefault(string generatorId, string fallback);
}

/// <summary>内建文字库和生成器使用的稳定 ID；MOD 或其他系统也可以直接使用自定义 ID。</summary>
public static class TextLibraryKeys
{
    public const string PersonFamilyNames = "person.familyNames";
    public const string PersonGivenNames = "person.givenNames";
    public const string PersonNicknames = "person.nicknames";
    public const string PlacePrefixes = "place.prefixes";
    public const string PlaceSuffixes = "place.suffixes";
    public const string BookTitles = "book.titles";
    public const string ItemNames = "item.names";

    public const string PlayerName = "generator.playerName";
    public const string CharacterName = "generator.characterName";
    public const string SaveName = "generator.saveName";
    public const string PlaceName = "generator.placeName";
    public const string BookTitle = "generator.bookTitle";
    public const string ItemName = "generator.itemName";
}

/// <summary>文字库 JSON 根对象。libraries 可随项目扩展，generators 只描述已有分类如何组合。</summary>
[Serializable]
public sealed class TextLibraryCatalogDto
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion;

    [JsonProperty("libraries")]
    public Dictionary<string, List<string>> Libraries =
        new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("generators")]
    public Dictionary<string, TextLibraryGeneratorDto> Generators =
        new Dictionary<string, TextLibraryGeneratorDto>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>生成器定义；parts 按顺序随机取值并使用 separator 拼接。</summary>
[Serializable]
public sealed class TextLibraryGeneratorDto
{
    [JsonProperty("parts")]
    public List<string> Parts = new List<string>();

    [JsonProperty("separator")]
    public string Separator = string.Empty;
}

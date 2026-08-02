using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 从 StreamingAssets 加载游戏本体 JSON Buff 目录。
/// </summary>
public static class BuffCatalogLoader
{
    public const string RelativeBuffRoot = "GameConfig/Buffs";
    public const string CatalogFileName = "buffs.json";
    public const string RelativeCatalogPath = RelativeBuffRoot + "/" + CatalogFileName;

    public static string BuiltInCatalogPath =>
        Path.Combine(Application.streamingAssetsPath, RelativeCatalogPath);

    public static int LoadBuiltIn(GameRes gameRes)
    {
        if (gameRes == null)
            throw new ArgumentNullException(nameof(gameRes));

        List<BuffDefinition> definitions = LoadBuiltInDefinitions();
        foreach (BuffDefinition definition in definitions)
            gameRes.RegisterBuff(definition);

        Debug.Log($"[BuffCatalog] 已从 JSON 加载 {definitions.Count} 个 Buff：{BuiltInCatalogPath}");
        return definitions.Count;
    }

    public static List<BuffDefinition> LoadBuiltInDefinitions()
    {
        string path = BuiltInCatalogPath;
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 Buff JSON：{path}", path);

        BuffCatalogDto catalog = BuffDefinitionFactory.Deserialize(File.ReadAllText(path));
        return BuffDefinitionFactory.BuildCatalog(catalog);
    }

    public static string Serialize(BuffCatalogDto catalog)
    {
        return JsonConvert.SerializeObject(catalog, Formatting.Indented);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>跨平台加载本体液体目录；MOD 复用同一 LiquidDefinitionFactory 注册外部液体。</summary>
public static class LiquidCatalogLoader
{
    public const string RelativePath = "GameConfig/Liquids/liquids.json";
    public const int BuiltInBinaryVersion = LiquidDefinitionFactory.SupportedSchemaVersion;

    public static string BuiltInPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativePath);

    [BuiltInContentBake]
    public static void BakeBuiltInConfiguration()
    {
        LiquidCatalogDto catalog = BuiltInContentBinaryCatalog.ReadFile(
            BuiltInPath, LiquidDefinitionFactory.DeserializeCatalog, BuiltInBinaryVersion);
        LiquidDefinitionFactory.BuildCatalog(catalog);
    }

    /// <summary>读取、完整校验后一次性注册本体液体定义。</summary>
    public static IEnumerator LoadBuiltInAsync(GameRes gameRes, Action<int> onCompleted, Action<Exception> onFailed)
        => LoadBuiltInPreparedAsync(gameRes, onCompleted, onFailed, null);

    internal static IEnumerator LoadBuiltInCatalogInputAsync(Action<LiquidCatalogDto> completed, Action<Exception> failed)
        => BuiltInContentBinaryCatalog.ReadFileAsync(BuiltInPath,
            LiquidDefinitionFactory.DeserializeCatalog, completed, failed, BuiltInBinaryVersion);

    internal static IEnumerator LoadBuiltInPreparedAsync(GameRes gameRes, Action<int> onCompleted,
        Action<Exception> onFailed, LiquidCatalogDto prepared)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        Exception readError = null;
        if (prepared == null)
            yield return LoadBuiltInCatalogInputAsync(value => prepared = value, exception => readError = exception);
        if (readError != null)
        {
            onFailed?.Invoke(new IOException($"液体目录读取失败：{BuiltInPath}", readError));
            yield break;
        }

        try
        {
            List<LiquidDefinition> definitions = LiquidDefinitionFactory.BuildCatalog(prepared);
            var availableLiquidIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiquidDefinition definition in definitions)
                availableLiquidIds.Add(definition.Id);

            LiquidDefinitionFactory.ValidateReferences(
                definitions,
                id => availableLiquidIds.Contains(id),
                itemId => gameRes.TryGetItemDefinition(itemId, out _),
                buffId => gameRes.BuffDefinitions.ContainsKey(buffId));

            foreach (LiquidDefinition definition in definitions)
                gameRes.RegisterLiquidDefinition(definition);

            Debug.Log($"[LiquidCatalog] 已加载 {definitions.Count} 个本体液体定义：{BuiltInPath}");
            onCompleted?.Invoke(definitions.Count);
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }
}

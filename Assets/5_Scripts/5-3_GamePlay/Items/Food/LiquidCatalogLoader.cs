using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>跨平台加载本体液体目录；MOD 复用同一 LiquidDefinitionFactory 注册外部液体。</summary>
public static class LiquidCatalogLoader
{
    public const string RelativePath = "GameConfig/Liquids/liquids.json";

    public static string BuiltInPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativePath);

    /// <summary>读取、完整校验后一次性注册本体液体定义。</summary>
    public static IEnumerator LoadBuiltInAsync(GameRes gameRes, Action<int> onCompleted, Action<Exception> onFailed)
    {
        if (gameRes == null)
        {
            onFailed?.Invoke(new ArgumentNullException(nameof(gameRes)));
            yield break;
        }

        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInPath,
            text => json = text,
            exception => readError = exception);
        if (readError != null)
        {
            onFailed?.Invoke(new IOException($"液体目录读取失败：{BuiltInPath}", readError));
            yield break;
        }

        try
        {
            List<LiquidDefinition> definitions = LiquidDefinitionFactory.BuildCatalog(
                LiquidDefinitionFactory.DeserializeCatalog(json));
            var availableLiquidIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiquidDefinition definition in definitions)
                availableLiquidIds.Add(definition.Id);

            LiquidDefinitionFactory.ValidateReferences(
                definitions,
                id => availableLiquidIds.Contains(id),
                itemId => gameRes.TryGetItemDefinition(itemId, out _));

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

using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>最终 MOD 目录就绪后建立会话液体表并加载世界外观，数组编号不会写回 JSON 或存档。</summary>
public partial class GameRes
{
    #region 世界液体资源
    public LiquidTypeCatalog LiquidTypes { get; private set; }

    /// <summary>正式生成必须使用完整资源会话的编号表，不能把启动中的空目录当成本体默认表。</summary>
    public static LiquidTypeCatalog RequireWorldLiquidTypes()
    {
        GameRes resources = ExistingInstance;
        if (resources == null || !resources.isLoadFinish || resources.LiquidTypes == null)
            throw new InvalidOperationException("完整液体资源目录尚未就绪，不能创建正式世界地形。");
        return resources.LiquidTypes;
    }

    /// <summary>纯生成器仅收到不可变编号副本；Unity 资源继续由本资源会话持有。</summary>
    private IEnumerator LoadWorldLiquidResources()
    {
        LiquidTypes = new LiquidTypeCatalog(LiquidDefinitions.Keys);
        var spriteAddresses = new Dictionary<string, string>(StringComparer.Ordinal);
        var materialAddresses = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectWorldLiquidResourceAddresses(LiquidDefinitions.Values, spriteAddresses, materialAddresses);
        var requests = new List<(string Address, string LiquidId, bool IsSprite)>(spriteAddresses.Count + materialAddresses.Count);
        foreach (var pair in spriteAddresses) requests.Add((pair.Key, pair.Value, true));
        foreach (var pair in materialAddresses) requests.Add((pair.Key, pair.Value, false));
        var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        var materials = new Dictionary<string, Material>(StringComparer.Ordinal);

        // 同地址只发一次请求，所有结果仍归当前资源会话持有。
        yield return ResourceLoadQueue.Run<AsyncOperationHandle>(requests.Count, index =>
        {
            var request = requests[index];
            if (request.IsSprite) return resourceAssets.Load<Sprite>(request.Address);
            return resourceAssets.Load<Material>(request.Address);
        }, handle => handle.IsDone, (index, handle) =>
        {
            var request = requests[index];
            if (request.IsSprite)
                sprites.Add(request.Address, ResourceAssetScope.Require(handle.Convert<Sprite>(), $"液体 {request.LiquidId} Sprite"));
            else
                materials.Add(request.Address, ResourceAssetScope.Require(handle.Convert<Material>(), $"液体 {request.LiquidId} Material"));
        }, ShouldYieldResourceWork);

        foreach (LiquidDefinition definition in LiquidDefinitions.Values)
        {
            WorldLiquidSettings settings = definition.WorldWater;
            if (settings == null) continue;
            if (settings.Sprite == null) settings.Sprite = sprites[settings.SpriteAddress?.Trim() ?? string.Empty];
            if (settings.Material == null) settings.Material = materials[settings.MaterialAddress?.Trim() ?? string.Empty];
            if (ShouldYieldResourceWork()) yield return null;
        }
    }

    internal static void CollectWorldLiquidResourceAddresses(IEnumerable<LiquidDefinition> definitions,
        IDictionary<string, string> sprites, IDictionary<string, string> materials)
    {
        foreach (LiquidDefinition definition in definitions)
        {
            WorldLiquidSettings settings = definition.WorldWater;
            if (settings == null) continue;
            settings.Validate(definition.Id);
            string spriteAddress = settings.SpriteAddress?.Trim() ?? string.Empty;
            string materialAddress = settings.MaterialAddress?.Trim() ?? string.Empty;
            if (settings.Sprite == null && !sprites.ContainsKey(spriteAddress)) sprites.Add(spriteAddress, definition.Id);
            if (settings.Material == null && !materials.ContainsKey(materialAddress)) materials.Add(materialAddress, definition.Id);
        }
    }
    #endregion
}

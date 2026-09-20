using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 建筑资源接线校验。检查召唤器到本体或 TileBlock、Tile/Sprite、地图数字 ID 和调色板的完整引用链，
/// 在资源发布前报告缺失，避免直到玩家手持建筑时才失败；与编辑器目录检查共用规则。
/// </summary>
public sealed class BuildingResourceCatalogValidator : IResourceCatalogValidator
{
    #region 引用检查

    public string Id => "buildings";

    public void Validate(GameRes resources, List<string> errors)
    {
        var tileRequests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (RuntimeItemDefinition definition in resources.ItemDefinitions.Values)
        {
            ItemData data = definition.CreateItemData();
            if (!Mod_Building.TryReadBuildingData(data, out Ex_ModData module, out Mod_Building.Building_Data state)) continue;
            if (string.IsNullOrWhiteSpace(module.BitData))
            { errors.Add($"建筑 {definition.Id} 缺少 BuildingData.BitData"); continue; }
            if (!string.IsNullOrWhiteSpace(state.TileBlockId)) tileRequests[definition.Id] = state.TileBlockId;
            else if (string.IsNullOrWhiteSpace(state.BuildingPrefabId) || !resources.ItemDefinitions.ContainsKey(state.BuildingPrefabId))
                errors.Add($"建筑 {definition.Id} -> 本体物品 {state.BuildingPrefabId} 未注册");
            if (string.IsNullOrWhiteSpace(state.SummonerPrefabId) || !resources.ItemDefinitions.ContainsKey(state.SummonerPrefabId))
                errors.Add($"建筑 {definition.Id} -> 召唤器 {state.SummonerPrefabId} 未注册");
        }
        ValidateTiles(tileRequests, resources.TileBlockDict, errors);
        foreach (RuntimeTileDefinition block in resources.TileBlockDict.Values.Distinct())
        {
            string drop = block.damageProfile?.DropItemId;
            if (!string.IsNullOrWhiteSpace(drop) && !resources.ItemDefinitions.ContainsKey(drop))
                errors.Add($"地块 {block.name} -> 掉落物品 {drop} 未注册");
        }
    }

    /// <summary>只检查已加载的 JSON 定义与生成配置，不创建地图、快照存档或建筑实例。</summary>
    public static void ValidateTiles(IReadOnlyDictionary<string, string> requests,
        IReadOnlyDictionary<string, RuntimeTileDefinition> blocks, List<string> errors)
    {
        ChunkGenerationProfileSO[] profiles = Resources.LoadAll<ChunkGenerationProfileSO>("Config/WorldModel");
        ChunkTilePaletteSO palette = Resources.Load<ChunkTilePaletteSO>("Config/WorldModel/ChunkTilePalette_Default");
        if (profiles.Length == 0) errors.Add("地图生成 Profile 目录为空：Config/WorldModel");
        if (palette == null) errors.Add("地图缺少 ChunkTilePalette_Default");
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChunkGenerationProfileSO profile in profiles)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in profile.CreateSnapshot().TextParameters)
            {
                const string prefix = "tile.block.";
                if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(entry.Key.Substring(prefix.Length), out int tileId) || tileId <= 0)
                { errors.Add($"Profile {profile.name} -> 无效 Tile ID {entry.Key}"); continue; }
                if (!seen.Add(entry.Value)) errors.Add($"Profile {profile.name} -> 地块 {entry.Value} 绑定了多个 Tile ID");
                registered.Add(entry.Value);
                if (!blocks.TryGetValue(entry.Value, out RuntimeTileDefinition block) || block == null)
                { errors.Add($"Profile {profile.name} -> {entry.Key} -> TileBlock {entry.Value} 未加载"); continue; }
                if (block.RuntimeTileId != tileId)
                    errors.Add($"Profile {profile.name} -> {entry.Key} 与 JSON 地块 {block.Id} 的稳定编号不一致");
                if (block.GetTileBaseAsset() is not Tile tile || tile.sprite == null)
                    errors.Add($"Profile {profile.name} -> {entry.Key} -> {entry.Value} 缺少可供 BRG 使用的静态 Tile 或 Sprite");
            }
        }
        foreach (KeyValuePair<string, string> request in requests)
        {
            string source = $"建筑 {request.Key} -> TileBlock {request.Value}";
            if (!blocks.TryGetValue(request.Value, out RuntimeTileDefinition block) || block == null)
            { errors.Add(source + " 未加载，请检查地块 JSON 清单"); continue; }
            if (block.GetTileBaseAsset() is not Tile tile || tile.sprite == null)
                errors.Add(source + " -> 缺少静态 Tile 或 Sprite");
            if (block.tileDataTemplate == null) errors.Add(source + " -> 缺少 TileData 模板");
            if (!registered.Contains(request.Value) && block.RuntimeTileId <= 0)
                errors.Add(source + " -> JSON 缺少稳定 runtimeTileId，生成 Profile 也没有注册 tile.block.*");
        }
    }

    #endregion
}

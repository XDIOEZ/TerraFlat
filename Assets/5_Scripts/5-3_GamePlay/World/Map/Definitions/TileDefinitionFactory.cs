using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine.Tilemaps;

/// <summary>
/// 本体与 MOD 共用的地块定义构建器。JSON 先严格校验，再解析资源键并创建共享行为；
/// 不修改数字地块编号、生成 Profile 或玩家存档，也不把 Unity 对象送入后台世界模型。
/// </summary>
public static class TileDefinitionFactory
{
    #region 构建
    public const int SupportedSchemaVersion = 1;
    public const int FirstModRuntimeTileId = 1000000;

    public static TileDefinitionDto DeserializeDefinition(JToken token) => TileDefinitionJson.Read<TileDefinitionDto>(token);

    public static TileDefinitionCatalogDto DeserializeCatalog(string json)
    {
        var catalog = TileDefinitionJson.Read<TileDefinitionCatalogDto>(TileDefinitionJson.Parse(json));
        if (catalog.SchemaVersion != SupportedSchemaVersion || catalog.Tiles == null)
            throw new InvalidDataException($"不支持的地块目录 schemaVersion：{catalog.SchemaVersion}");
        return catalog;
    }

    /// <summary>每种定义调用一次；保留独立源配置，防止调用方后续修改 DTO 污染共享定义。</summary>
    public static RuntimeTileDefinition Build(TileDefinitionDto input, Func<string, TileBase> resolveTile)
    {
        if (input == null || resolveTile == null) throw new ArgumentNullException(input == null ? nameof(input) : nameof(resolveTile));
        JObject source = TileDefinitionJson.Write(input);
        TileDefinitionDto dto = DeserializeDefinition(source);
        ValidateId(dto.Id, "地块 id");
        ValidateId(dto.TileAsset, $"{dto.Id}.tileAsset");
        if (dto.RuntimeTileId < 0) throw new InvalidDataException($"地块 {dto.Id} 的 runtimeTileId 不能为负数。");
        if (dto.Behaviours == null || dto.Behaviours.Count > 64)
            throw new InvalidDataException($"地块 {dto.Id} 的 behaviours 必须是最多 64 项的数组。");
        if (dto.DamageProfile == null) throw new InvalidDataException($"地块 {dto.Id} 的 damageProfile 不能为空。");

        TileBase tile = resolveTile(dto.TileAsset);
        if (tile == null) throw new InvalidDataException($"地块 {dto.Id} 找不到 TileBase 资源键：{dto.TileAsset}");
        TileData template = TileBehaviourRegistry.BuildData(dto.Data);
        template.ID = dto.Id;
        template.Name = dto.Id;
        ValidateTemplate(dto.Id, template);
        ValidateDamage(dto.Id, dto.DamageProfile);
        ValidateGroundPlacement(dto.Id, dto.GroundPlacement);

        var behaviours = new List<TileBlockBehaviour>(dto.Behaviours.Count);
        foreach (TileComponentDefinitionDto entry in dto.Behaviours)
        {
            TileBlockBehaviour behaviour = TileBehaviourRegistry.BuildBehaviour(entry);
            if (behaviour is Tile_Farmland && template is not TileData_Farmland)
                throw new InvalidDataException($"地块 {dto.Id} 的 farmland 行为要求 farmland 数据类型。");
            behaviours.Add(behaviour);
        }
        return new RuntimeTileDefinition(dto, tile, template, behaviours, source);
    }

    /// <summary>在发布前验证全目录身份，防止分包或 MOD 悄悄覆盖已有世界整数编号。</summary>
    public static void ValidateIdentities(IEnumerable<RuntimeTileDefinition> definitions)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new Dictionary<int, string>();
        foreach (RuntimeTileDefinition definition in definitions)
        {
            if (definition == null) throw new InvalidDataException("地块目录包含空定义。");
            if (!ids.Add(definition.Id)) throw new InvalidDataException($"重复地块 ID：{definition.Id}");
            if (definition.RuntimeTileId > 0 && !numbers.TryAdd(definition.RuntimeTileId, definition.Id))
                throw new InvalidDataException($"地块数字 ID {definition.RuntimeTileId} 冲突：{numbers[definition.RuntimeTileId]} / {definition.Id}");
        }
    }
    #endregion

    #region 配置校验
    public static void ValidateId(string id, string context)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Length > 200 ||
            id.Any(char.IsControl) || id.Contains('/') || id.Contains('\\'))
            throw new InvalidDataException($"{context} 不是有效的稳定 ID：{id}");
    }

    private static void ValidateTemplate(string id, TileData template)
    {
        TileDefinitionJson.ValidateFields(template, id + ".data");
        if (template.Penalty > short.MaxValue || template.DemolitionTime < 0)
            throw new InvalidDataException($"地块 {id} 的导航代价或拆除时间无效。");
        template.TileTag ??= string.Empty;
        if (template is TileData_Farmland soil &&
            (soil.fertilityValue == null || soil.Fertility < 0 || soil.maxWater <= 0 || soil.waterValue < 0 || soil.waterValue > soil.maxWater))
            throw new InvalidDataException($"地块 {id} 的耕地水分或肥力无效。");
        if (template is TileData_Grass grass && (grass.FertileValue == null || grass.FertileValue.Value < 0))
            throw new InvalidDataException($"地块 {id} 的草地肥力无效。");
        if (template is TileData_CellBuilding building && building.CurrentHp < 0)
            throw new InvalidDataException($"地块 {id} 的初始生命不能为负数。");
    }

    private static void ValidateDamage(string id, TileBuildingDamageProfile damage)
    {
        TileDefinitionJson.ValidateFields(damage, id + ".damageProfile");
        if (damage.DefenseValues == null || damage.DefenseValues.Cutting < 0 || damage.DefenseValues.Piercing < 0 ||
            damage.DefenseValues.Chopping < 0 || damage.DefenseValues.Blunt < 0 ||
            damage.DropAmount > 0 && string.IsNullOrWhiteSpace(damage.DropItemId))
            throw new InvalidDataException($"地块 {id} 的防御或掉落配置无效。");
    }

    private static void ValidateGroundPlacement(string id, GroundTilePlacementRule rule)
    {
        if (rule == null) return;
        TileDefinitionJson.ValidateFields(rule, id + ".groundPlacement");
        const TerrainCellFlags allowed = TerrainCellFlags.Walkable | TerrainCellFlags.Blocking |
                                         TerrainCellFlags.Occupied;
        if (((rule.RequiredSourceFlags | rule.ForbiddenSourceFlags) & ~allowed) != 0 ||
            (rule.RequiredSourceFlags & rule.ForbiddenSourceFlags) != 0)
            throw new InvalidDataException($"地块 {id} 的铺设来源标记无效或相互冲突。");
        ValidateId(rule.RefundItemId, id + ".groundPlacement.refundItemId");
    }
    #endregion
}

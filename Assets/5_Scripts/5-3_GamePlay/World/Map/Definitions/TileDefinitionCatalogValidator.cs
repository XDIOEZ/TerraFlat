using System.Collections.Generic;

/// <summary>地块配置的跨目录引用检查；在本体和 MOD 均构建完成后验证 Buff、返还和采挖产物。</summary>
public sealed class TileDefinitionCatalogValidator : IResourceCatalogValidator
{
    #region 引用检查
    public string Id => "tiles";

    public void Validate(GameRes resources, List<string> errors)
    {
        TileDefinitionFactory.ValidateIdentities(resources.TileBlockDict.Values);
        foreach (RuntimeTileDefinition definition in resources.TileBlockDict.Values)
        {
            string refund = definition.GroundPlacement?.RefundItemId;
            if (!string.IsNullOrWhiteSpace(refund) && !resources.ItemDefinitions.ContainsKey(refund))
                errors.Add($"地块 {definition.Id} -> 返还物品 {refund} 未注册。");
            GroundTileHarvestRule harvest = definition.GroundHarvest;
            if (harvest != null)
            {
                if (!resources.ItemDefinitions.TryGetValue(harvest.ItemId, out RuntimeItemDefinition product) ||
                    product.IsActor)
                    errors.Add($"地块 {definition.Id} -> 采挖产物 {harvest.ItemId} 不是有效物品。");
                if (!resources.TileBlockDict.TryGetValue(harvest.ReplacementTileId,
                        out RuntimeTileDefinition replacement) || replacement.RuntimeTileId <= 0)
                    errors.Add($"地块 {definition.Id} -> 挖后地表 {harvest.ReplacementTileId} 未注册。");
            }
            foreach (TileBlockBehaviour behaviour in definition.Behaviours)
            {
                IEnumerable<string> buffs = behaviour switch
                {
                    Tile_Grass grass => grass.BuffInfo,
                    Tile_Universal universal => universal.BuffInfo,
                    _ => null
                };
                if (buffs == null) continue;
                foreach (string buff in buffs)
                    if (string.IsNullOrWhiteSpace(buff) || !resources.BuffDefinitions.ContainsKey(buff))
                        errors.Add($"地块 {definition.Id} -> Buff {buff} 未注册。");
            }
        }
    }
    #endregion
}

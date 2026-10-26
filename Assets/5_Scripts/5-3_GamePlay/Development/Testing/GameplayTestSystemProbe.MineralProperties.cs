using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 矿物参考目录采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMineralPropertyRules()
    {
        Register("item_runtime", "mineral_properties", ProbeMineralProperties);
        Register("item_runtime", "giant_mines", ProbeGiantMines);
    }

    // 直接调用正式目录加载与查询接口，Lua 决定数值和边界是否符合预期。
    private static JObject ProbeMineralProperties(JObject args)
    {
        MineralPropertyCatalog catalog;
        try
        {
            catalog = args["json"] == null
                ? MineralPropertyCatalog.LoadBuiltIn()
                : MineralPropertyCatalog.Deserialize((string)args["json"]);
        }
        catch (InvalidDataException exception)
        {
            return new JObject { ["accepted"] = false, ["error"] = exception.Message };
        }

        var minerals = new JArray();
        bool sharedReferences = true;
        foreach (MineralPropertyReference mineral in catalog.Minerals)
        {
            foreach (string itemId in mineral.ItemIds)
                sharedReferences &= catalog.TryGetByItemId(itemId, out MineralPropertyReference queried) &&
                                    ReferenceEquals(mineral, queried);
            minerals.Add(JObject.FromObject(mineral));
        }
        bool caseInsensitive = catalog.TryGetByItemId("ore_diamond", out MineralPropertyReference diamond) &&
                               diamond.Id == "diamond";
        bool unknownFound = catalog.TryGetByItemId("probe:unknown", out _);
        bool nullFound = catalog.TryGetByItemId(null, out _);
        return new JObject { ["accepted"] = true, ["minerals"] = minerals,
            ["sharedReferences"] = sharedReferences, ["caseInsensitive"] = caseInsensitive,
            ["unknownFound"] = unknownFound, ["nullFound"] = nullFound };
    }

    // 采样正式继承结果和掉落接口，具体矿种、碰撞比例与产物约束交给 Lua。
    private static JObject ProbeGiantMines(JObject args)
    {
        Dictionary<string, ItemDefinitionDto> definitions = MineralContentDefinitions();
        Dictionary<string, RuntimeLootTable> loot = LootTableCatalogLoader.LoadBuiltIn()
            .ToDictionary(table => table.Id, StringComparer.OrdinalIgnoreCase);
        MineralPropertyCatalog minerals = MineralPropertyCatalog.LoadBuiltIn();
        var rows = new JArray();
        foreach (ItemDefinitionDto giant in definitions.Values.Where(definition =>
                     definition.Id.StartsWith("Mine_", StringComparison.Ordinal) &&
                     definition.Id.EndsWith("_Large", StringComparison.Ordinal)))
        {
            ItemDefinitionDto small = definitions[giant.Id.Substring(0, giant.Id.Length - "_Large".Length)];
            bool sharedMineral = minerals.TryGetByItemId(giant.Id, out MineralPropertyReference giantMineral) &&
                minerals.TryGetByItemId(small.Id, out MineralPropertyReference smallMineral) &&
                ReferenceEquals(giantMineral, smallMineral);
            rows.Add(new JObject
            {
                ["small"] = GiantMineContentSample(small, loot), ["giant"] = GiantMineContentSample(giant, loot),
                ["sharedMineral"] = sharedMineral
            });
        }
        return new JObject
        {
            ["mines"] = rows, ["bonusChance"] = ResourceHarvestHitBonus.TriggerChance,
            ["emptyRootOccupied"] = NaturalEntityEcsService.OccupiesCell(Array.Empty<GridCellOffset>(), Vector2Int.zero, Vector2Int.zero),
            ["emptyOtherOccupied"] = NaturalEntityEcsService.OccupiesCell(Array.Empty<GridCellOffset>(), Vector2Int.zero, Vector2Int.up)
        };
    }

    private static JObject GiantMineContentSample(ItemDefinitionDto definition, Dictionary<string, RuntimeLootTable> loot)
    {
        RuntimeLootTable table = loot[definition.LootTableId];
        ItemColliderDefinitionDto collider = definition.Visual?.Collider;
        Vector2 size = collider?.Size ?? Vector2.zero, offset = collider?.Offset ?? Vector2.zero;
        Vector3 scale = definition.Visual?.RendererLocalScale ?? Vector3.one;
        Vector3 position = definition.Visual?.RendererLocalPosition ?? Vector3.zero;
        NaturalEntitySpatialGeometry geometry = NaturalEntitySpatialGeometry.Compile(
            collider, definition.Modules[ModText.Hp].Parameters, false);
        Vector3 sortingAnchor = geometry.ResolveMovementBlockingSortingAnchor(Matrix4x4.identity);
        GridCellOffset[] cells = definition.WorldGridOccupancy?.Cells?.Select(cell => new GridCellOffset(cell.X, cell.Y)).ToArray()
            ?? Array.Empty<GridCellOffset>();
        return new JObject
        {
            ["id"] = definition.Id, ["sprite"] = definition.Visual?.SpriteAddress,
            ["lootTableId"] = definition.LootTableId, ["loot"] = table.CreateDamageReceiverEntries(),
            ["health"] = definition.Modules[ModText.Hp].Parameters.DeepClone(),
            ["harvest"] = definition.Modules.TryGetValue("harvestRequirement", out ItemModuleDefinitionDto harvest)
                ? JObject.FromObject(harvest) : JValue.CreateNull(),
            ["body"] = new JObject { ["width"] = size.x, ["height"] = size.y,
                ["x"] = offset.x, ["y"] = offset.y, ["isTrigger"] = collider?.IsTrigger ?? false },
            ["visual"] = new JObject { ["scaleX"] = scale.x, ["scaleY"] = scale.y, ["y"] = position.y },
            ["sortingAnchor"] = new JObject { ["x"] = sortingAnchor.x, ["y"] = sortingAnchor.y },
            ["cells"] = new JArray(cells.Select(cell => new JObject { ["x"] = cell.X, ["y"] = cell.Y })),
            ["rootOccupied"] = NaturalEntityEcsService.OccupiesCell(cells, Vector2Int.zero, Vector2Int.zero),
            ["bottomOccupied"] = NaturalEntityEcsService.OccupiesCell(cells, Vector2Int.zero,
                WorldTopologyRuntime.NormalizeCell(new Vector2Int(0, -2))),
            ["topOccupied"] = NaturalEntityEcsService.OccupiesCell(cells, Vector2Int.zero,
                WorldTopologyRuntime.NormalizeCell(new Vector2Int(0, 2))),
            ["bonusFirst"] = ResourceHarvestHitBonus.SelectLoot(table.Entries, 0d),
            ["bonusLast"] = ResourceHarvestHitBonus.SelectLoot(table.Entries, 1d)
        };
    }

    #endregion
}

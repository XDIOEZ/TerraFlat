using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class AncientStageAssetBuilder
{
    /// <summary>矿工营地沿用正式结构模板、稳定成员 ID 和固定容器内容，不增加第二条奖励链。</summary>
    private static void BuildMiningCamp()
    {
        const string templatePath = "Assets/4_ScriptObjects/World/Structures/Templates/mining_camp_template.asset";
        StructureTemplateSO template = AssetDatabase.LoadAssetAtPath<StructureTemplateSO>(templatePath);
        if (template == null)
        {
            template = ScriptableObject.CreateInstance<StructureTemplateSO>();
            template.TemplateId = "mining_camp_template";
            template.Size = new Vector2Int(12, 10);
            template.Pivot = new Vector2(6f, 5f);
            Tile_Block wall = AssetDatabase.LoadAssetAtPath<Tile_Block>("Assets/4_ScriptObjects/World/Tiles/TileBase_BuiltStoneWall.asset");
            // 断墙形成开口轮廓，不封死奖励和撤离路线。
            foreach (Vector2Int cell in new[] { new Vector2Int(2,7), new Vector2Int(3,7), new Vector2Int(4,7), new Vector2Int(2,6), new Vector2Int(8,7), new Vector2Int(9,7) })
                template.TileStamps.Add(new StructureTileStamp { LocalPosition = cell, TileBlock = wall, WriteMode = StructureTileWriteMode.AddLayer });
            template.ItemStamps.Add(CampMember("shelter", "Tent", 3.5f, 4.5f));
            template.ItemStamps.Add(CampMember("old-fire", "Bonfire", 7.5f, 4.5f));
            template.ItemStamps.Add(CampMember("iron-vein", "Mine_Iron", 9.5f, 6.5f));
            template.ItemStamps.Add(CampMember("stone-pile", "Mine_Stone", 10.5f, 2.5f));
            StructureItemStamp chest = CampMember("ore-cache", "Chest_Wood", 5.5f, 6.5f);
            chest.ContainerContents = new StructureContainerContents
            {
                OverrideContents = true,
                TargetInventoryIndex = 0,
                Items = new List<StructureContainerItemEntry>
                {
                    CampReward(0, "Ore_Iron", 6), CampReward(1, "Ore_Coal", 4),
                    CampReward(2, "Ore_Copper", 2), CampReward(3, "Ore_Tin", 1), CampReward(4, "MinersNote", 1)
                }
            };
            template.ItemStamps.Add(chest);
            template.Markers.Add(new StructureMarkerData { MarkerId = "entrance", Type = StructureMarkerType.Entrance, LocalPosition = new Vector2(6, 1) });
            AssetDatabase.CreateAsset(template, templatePath);
        }
        const string definitionPath = "Assets/4_ScriptObjects/World/Structures/Definitions/mining_camp.asset";
        StructureDefinitionSO definition = AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>(definitionPath);
        if (definition == null)
        {
            definition = Object.Instantiate(AssetDatabase.LoadAssetAtPath<StructureDefinitionSO>("Assets/4_ScriptObjects/World/Structures/Definitions/abandoned_camp.asset"));
            definition.StructureId = "mining_camp";
            definition.DisplayName = "废弃矿工营地";
            definition.SeedSalt = 83071;
            definition.RegionSizeInTiles = 96;
            definition.SpawnChance = 0.65f;
            definition.MinDistanceFromWorldOrigin = 32;
            definition.Templates = new List<WeightedStructureTemplate> { new() { Template = template } };
            AssetDatabase.CreateAsset(definition, definitionPath);
        }
        StructureCatalogSO catalog = AssetDatabase.LoadAssetAtPath<StructureCatalogSO>("Assets/Resources/Config/StructureCatalog_Default.asset");
        if (!catalog.Definitions.Contains(definition)) catalog.Definitions.Add(definition);
        EditorUtility.SetDirty(catalog);
    }
    /// <summary>成员身份与布局、旋转分离，读取已有容器时不重新发放初始奖励。</summary>
    private static StructureItemStamp CampMember(string id, string item, float x, float y) =>
        new() { MemberId = id, ItemPrefabId = item, LocalPosition = new Vector2(x, y), SeedSalt = 83071 };
    /// <summary>每类资源占一个固定槽位，奖励量不随重进区块增加。</summary>
    private static StructureContainerItemEntry CampReward(int slot, string item, int amount) =>
        new() { SlotIndex = slot, ItemPrefabId = item, Amount = amount };
}

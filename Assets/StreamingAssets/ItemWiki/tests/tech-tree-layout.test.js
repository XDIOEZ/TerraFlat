"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const model = require("../tech-tree-model.js");
const mechanics = require("../mechanics.js");
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(require.resolve("../tech-tree.js"), "utf8"), context);
const layout = context.window.ItemTechTree.layout;
const entry = (id, body = {}) => ({ kind: "item", displayName: id, final: { id, ...body }, source: body });
const config = {
    eras: [{ id: "early", order: 0 }, { id: "late", order: 1 }],
    stages: [{ id: "raw", eraId: "early", order: 0, layoutBand: "natural" },
        { id: "tools", eraId: "early", order: 1 }, { id: "electric", eraId: "late", order: 2 }],
    assignments: [{ stageId: "raw", itemIds: ["Sand", "Portal", "GatheredCrop", "CoconutMeat", "Special"] },
        { stageId: "tools", itemIds: ["Glass", "Tree", "Mine", "Ore", "Log", "Ingot", "Crop", "CropFood", "Unknown"] },
        { stageId: "electric", itemIds: ["Rubber", "Wire"] }],
    specialItems: [{ label: "调试", itemIds: ["Special"] }]
};
const recipe = (id, sourceKind, input, output) => ({ id, sourceKind,
    inputs: [{ itemId: input, match: "exact_item", amount: 1 }], outputs: [{ itemId: output, amount: 1 }] });

//#region 自然来源与分区回归
// 自然获取与展示时代分离，不把缺配方的未知物品或加工产物当成天然原料。
test("显式自然分区优先，其他阶段仍按自然来源识别，加工和农耕不自动归入", () => {
    const graph = model.build([entry("Sand"), entry("Glass"), entry("Portal", { modules: { portal: { prefab: "BuildingFeature_DimensionPortal" } } }),
        entry("Tree", { tags: ["Tree"] }), entry("Mine", { modules: { harvest: { prefab: "Module_ResourceHarvest" } } }),
        ...["Ore", "Log", "Ingot", "Crop", "CropFood", "Unknown", "Rubber", "Wire", "GatheredCrop", "CoconutMeat", "Special"].map(id => entry(id))], [
        recipe("collect-rubber", "loot", "Tree", "Rubber"), recipe("collect-log", "loot", "Tree", "Log"),
        recipe("mine", "harvest", "Mine", "Ore"), recipe("smelt", "crafting", "Ore", "Ingot"),
        recipe("melt", "crafting", "Sand", "Glass"), recipe("wire", "crafting", "Rubber", "Wire"),
        recipe("crop", "harvest", "Crop", "CropFood"), recipe("plant", "planting", "Sand", "GatheredCrop"),
        recipe("coconut", "crafting", "Sand", "CoconutMeat")], config);
    const byId = new Map(graph.nodes.map(node => [node.id, node]));
    for (const id of ["Sand", "Portal", "GatheredCrop", "CoconutMeat", "Tree", "Mine", "Ore", "Log", "Rubber"]) assert.equal(byId.get(id).naturalResource, true, id);
    for (const id of ["Glass", "Ingot", "Crop", "CropFood", "Unknown", "Wire", "Special"]) assert.equal(byId.get(id).naturalResource, false, id);
    assert.equal(layout(graph.nodes, graph.stages, graph.eras, true, graph.edges).positions.get("Special").natural, undefined);
    assert.equal(byId.get("Rubber").eraId, "late");
    assert.ok(graph.edges.some(edge => edge.source === "Rubber" && edge.target === "Wire"));
});

// 直接读取当前物品目录，确保天然原料不再残留第二个展示区域。
test("真实天然原料组统一位于资源区，洞口和椰子加工关系保留且节点唯一", () => {
    const read = filename => JSON.parse(fs.readFileSync(filename, "utf8"));
    const directory = path.resolve(__dirname, "../../GameConfig/Items");
    const sources = new Map();
    for (const pkg of read(path.join(directory, "item-manifest.json")).packages.filter(pkg => pkg.enabled !== false))
        for (const item of read(path.join(directory, pkg.path)).items) sources.set(item.id.toLowerCase(), item);
    const entries = [...mechanics.resolveDefinitions(sources).values()].map(final => ({ kind: "item", final, source: sources.get(final.id.toLowerCase()) }));
    const eraConfig = read(path.resolve(__dirname, "../tech-tree-eras.json"));
    const loadPackages = (folder, manifest) => read(path.join(directory, "..", folder, manifest)).packages
        .filter(pkg => pkg.enabled !== false).map(pkg => read(path.join(directory, "..", folder, pkg.path)));
    const graph = model.build(entries, [], eraConfig, {
        naturalItems: loadPackages("WorldGeneration/NaturalItems", "natural-item-manifest.json"),
        tiles: loadPackages("Tiles", "tile-manifest.json").flatMap(pkg => pkg.tiles),
        lootTables: new Map(read(path.join(directory, "../LootTables/loot-tables.json")).lootTables.map(table => [table.id.toLowerCase(), table]))
    });
    const positions = layout(graph.nodes, graph.stages, graph.eras, true, graph.edges);
    assert.equal(positions.positions.size, graph.nodes.length);
    assert.equal(new Set(graph.nodes.map(node => node.key)).size, graph.nodes.length);
    assert.ok(!positions.stages.some(box => box.stage.layoutBand === "natural"));
    for (const id of ["NaturalMineEntrance", "CaveExit", "ChiliTree", "Coconut_Shell_Building", "Coconut_Half", "Coconut_Nude", "Coconut_Shell", "CoconutMeat"])
        assert.equal(positions.positions.get(id)?.natural, true, id);
    for (const node of graph.nodes.filter(node => node.stageId === "gathering" && !node.special))
        assert.equal(positions.positions.get(node.id)?.natural, true, node.id);
    assert.ok(graph.edges.some(edge => edge.source === "Coconut_Shell" && edge.target === "Coconut_Shell_Building"));
    assert.ok(graph.edges.some(edge => edge.source === "Chili" && edge.target === "ChiliTree"));
    const byId = new Map(graph.nodes.map(node => [node.id, node]));
    for (const id of ["Tree_Coconut", "Coconut_Green", "Coconut_Half", "CoconutMeat", "Coconut_Shell_Building"])
        assert.deepEqual(byId.get(id).resourceHabitats, ["海滩"], id);
    assert.deepEqual(byId.get("MushroomCrop").resourceHabitats, ["洞穴"]);
    assert.deepEqual(byId.get("Earth").resourceHabitats, ["地表采挖"]);
    assert.ok(byId.get("RadishCrop").resourceHabitats.includes("森林"));
    assert.equal(byId.get("Ingot_Copper").naturalResource, false);
});

// 群系传播只沿自然采集链，零掩码、伴生和洞穴作用域不能被扩成所有地表群系。
test("群系来源支持伴生、多段掉落、洞穴与小行星，排除停用生成和加工推断", () => {
    const ids = ["Palm", "Fruit", "Half", "SplitOnly", "Forest", "Leaf", "Crop", "Ore", "Diamond", "AsteroidOre", "Soil", "Egg", "Unknown", "Disabled", "Plate", "Cultivated", "FarmFood"];
    const entries = ids.map(id => entry(id));
    entries.find(e => e.final.id === "Palm").final.modules = { fruit: { prefab: "Module_CanopyFruit", parameters: { FruitItemId: "Fruit", SplitItemId: "SplitOnly", Settings: { GrowthSeconds: 20 } } } };
    entries.push({ kind: "actor", displayName: "野鸟", final: { id: "Bird", modules: { production: { prefab: "Module_Production", parameters: { ProductionList: [{ itemName: "Egg", itemCountMin: 1, itemCountMax: 1 }] } } } } });
    const rule = (ruleId, itemId, biomeMask, fields = {}) => ({ ruleId, itemId, biomeMask, spawnChance: 1, ...fields });
    const graph = model.build(entries, [recipe("split", "loot", "Fruit", "Half"), recipe("ore", "harvest", "Forest", "Ore"),
        recipe("smelt", "crafting", "Ore", "Plate"), recipe("place", "placement", "Palm", "Plate"), recipe("farm", "harvest", "Cultivated", "FarmFood")], {
        eras: [{ id: "first", order: 0 }], stages: [{ id: "main", eraId: "first", order: 0 }], assignments: [{ stageId: "main", itemIds: ids }]
    }, { naturalItems: [{ ecologyRules: [
        rule("surface.palm", "Palm", 4, { providedTags: ["Tree"] }), rule("surface.forest", "Forest", 32, { providedTags: ["Tree"] }),
        rule("surface.leaf", "Leaf", 0, { companionOnly: true, companionHostTag: "Tree", companionSpawnChance: 1, spawnChance: 0 }),
        rule("surface.crop", "Crop", 48), rule("surface.unknown", "Unknown", 0), rule("surface.disabled", "Disabled", 32, { spawnChance: 0 }),
        rule("surface.asteroid.ore", "AsteroidOre", 128)
    ], caveResourceRules: [{ ruleId: "cave.diamond", itemId: "Diamond" }] }], tiles: [{ groundHarvest: { itemId: "Soil" } }] });
    const byId = new Map(graph.nodes.map(node => [node.id, node]));
    for (const id of ["Fruit", "Half"]) assert.deepEqual(byId.get(id).resourceHabitats, ["海滩"]);
    assert.deepEqual(byId.get("Leaf").resourceHabitats, ["海滩", "森林"]);
    assert.deepEqual(byId.get("Crop").resourceHabitats, ["草原", "森林"]);
    assert.deepEqual(byId.get("Diamond").resourceHabitats, ["洞穴"]);
    assert.deepEqual(byId.get("AsteroidOre").resourceHabitats, ["小行星"]);
    assert.deepEqual(byId.get("Unknown").resourceHabitats, ["群系未标注"]);
    assert.deepEqual(byId.get("Soil").resourceHabitats, ["地表采挖"]);
    assert.deepEqual(byId.get("Egg").resourceHabitats, ["生物产物"]);
    const canopy = graph.routes.find(route => route.id.startsWith("canopy:"));
    assert.ok(canopy.requirements.some(requirement => /自然掉落/.test(requirement.label)));
    assert.ok(!canopy.requirements.some(requirement => /产出概率/.test(requirement.label)));
    for (const id of ["SplitOnly", "Disabled", "Plate", "Cultivated", "FarmFood"]) assert.equal(byId.get(id).naturalResource, false, id);
    assert.ok(!graph.edges.some(edge => edge.source === "Palm" && edge.target === "SplitOnly"));
});

// 检查全图与筛选图的资源位置、卡片无重叠及唯一节点，避免改布局后丢失物品。
test("自然资源按群系分区居中放在技术下方，筛选后分区与卡片均不重叠", () => {
    const eras = Array.from({ length: 9 }, (_, i) => ({ id: `era${i}`, order: i }));
    const stages = eras.map((era, i) => ({ id: `stage${i}`, eraId: era.id, order: i }));
    const main = stages.map((stage, i) => ({ id: `tech${i}`, name: `Tech${i}`, stageId: stage.id }));
    const natural = Array.from({ length: 40 }, (_, i) => ({ id: `raw${i}`, name: `Raw${i}`, naturalResource: true,
        resourceGroup: { id: i < 20 ? "forest" : "cave", name: i < 20 ? "森林" : "洞穴", order: i < 20 ? 0 : 1 } }));
    const edges = natural.map((node, i) => ({ source: node.id, target: i < 20 ? "tech0" : "tech8", kind: "ingredient" }));
    const full = layout([...main, ...natural], stages, eras, true, edges);
    assert.equal(full.positions.size, 49);
    assert.equal(full.resourceBox.x + full.resourceBox.width / 2, full.width / 2);
    assert.equal(full.resourceBox.groups.length, 2);
    assert.equal(full.resourceBox.groups.reduce((sum, group) => sum + group.count, 0), 40);
    for (const group of full.resourceBox.groups) for (const node of group.members) {
        const p = full.positions.get(node.id);
        assert.ok(p.x >= group.x && p.y >= group.y + 88);
        assert.ok(p.x + 210 <= group.x + group.width && p.y + 70 <= group.y + group.height);
        assert.ok(group.x >= full.resourceBox.x && group.x + group.width <= full.resourceBox.x + full.resourceBox.width);
    }
    const cards = [...full.positions.values()];
    for (const node of natural) assert.ok(full.positions.get(node.id).y > full.mainHeight);
    for (let i = 0; i < cards.length; i++) for (let j = i + 1; j < cards.length; j++) {
        const a = cards[i], b = cards[j];
        assert.ok(Math.abs(a.x - b.x) >= 210 || Math.abs(a.y - b.y) >= 70);
    }
    assert.ok(full.positions.get("raw0").x < full.positions.get("raw39").x);
    const filtered = layout(natural.slice(0, 1), stages, eras, false, edges);
    assert.equal(filtered.positions.size, 1);
    assert.ok(filtered.resourceBox.x >= 0);
    assert.ok(filtered.height > filtered.positions.get("raw0").y + 70);
    assert.equal(layout(main, stages, eras, true, edges).resourceBox, null);
});
//#endregion

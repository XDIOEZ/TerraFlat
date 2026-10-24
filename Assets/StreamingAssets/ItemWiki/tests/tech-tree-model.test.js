"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const model = require("../tech-tree-model.js");
const mechanics = require("../mechanics.js");

//#region 模型语义回归
const entry = (id, body = {}) => ({ id, kind: "item", displayName: id, source: body, final: { id, ...body } });
const config = {
    eras: [{ id: "first", name: "初始", order: 0 }], stages: [{ id: "first", eraId: "first", name: "初始", order: 0 }],
    assignments: [{ stageId: "first", itemIds: ["A", "B", "C", "D", "Tool", "Tool2", "Machine"] }]
};

test("新物品进入待分期，生物、BUFF和抽象定义不成为物品节点", () => {
    const graph = model.build([entry("A"), entry("NewSpaceDevice"), entry("Template", { abstract: true }),
        { ...entry("Actor"), kind: "actor" }, { ...entry("Buff"), kind: "buff" }], [], config);
    assert.deepEqual(graph.nodes.map(node => node.id), ["A", "NewSpaceDevice"]);
    assert.equal(graph.nodes[1].stageId, "unclassified");
    assert.equal(graph.nodes[1].sourceStatus, "no-known-route");
});

test("单料加工不继承且不会重复消费已归一化的路线", () => {
    const response = { grind: { inputAmount: 1, outputs: [{ itemId: "B", amount: 2 }] } };
    const items = [entry("A", { processing: response }), entry("B"), { ...entry("C"), final: { id: "C", processing: response } }];
    const graph = model.build(items, [{ id: "processing:A:grind", sourceKind: "processing", capability: "grind",
        inputs: [{ match: "exact_item", itemId: "A", amount: 1 }], outputs: [{ itemId: "B", amount: 2 }] }], config);
    assert.equal(graph.routes.filter(route => route.type === "processing").length, 1);
    assert.ok(!graph.edges.some(edge => edge.source === "C"));
});

test("标签候选是一个OR组，其他材料仍是独立AND输入", () => {
    const graph = model.build([entry("A"), entry("B"), entry("Tool", { tags: ["Knife"] }), entry("Tool2", { tags: ["Knife"] })], [
        { id: "cut", inputs: [{ match: "exact_item", itemId: "A", amount: 2 }, { match: "tag", tag: "Knife", amount: 0 }], outputs: [{ itemId: "B", amount: 1 }] }
    ], config);
    const route = graph.routes[0];
    assert.equal(route.inputRelation, "AND");
    assert.deepEqual(route.inputs[1].candidates, ["Tool", "Tool2"]);
    assert.equal(route.inputs[1].optional, true);
    const tools = graph.edges.filter(edge => edge.kind === "tool");
    assert.equal(tools.length, 2);
    assert.equal(tools[0].groupId, tools[1].groupId);
    assert.ok(tools.every(edge => edge.optional));
});

test("加工等级只匹配能用的工具，工作台互认保留随身手工路线", () => {
    const graph = model.build([entry("A", { processing: { cut: { inputAmount: 1, minLevel: 3, maxLevel: 5, outputs: [{ itemId: "B", amount: 1 }] } } }),
        entry("B"), entry("Tool", { processingCapabilities: { cut: { level: 1 } } }), entry("Machine", { processingCapabilities: { cut: { level: 3 } } })],
    [{ id: "hand", requiredStation: "workbench", inputs: [{ match: "exact_item", itemId: "A", amount: 1 }], outputs: [{ itemId: "B", amount: 1 }] }],
    { ...config, stationAliases: [{ station: "workbench", portable: true, label: "工作台或随身手工" }] });
    assert.deepEqual(graph.routes.find(route => route.type === "processing").requirements[0].candidates, ["Machine"]);
    assert.equal(graph.routes.find(route => route.id === "hand").requirements[0].portable, true);
});

test("循环与多路线不会让层级无限增长，强化不伪造新工具生产路线", () => {
    const graph = model.build([entry("A"), entry("B"), entry("C"), entry("Tool", { tags: ["Tool"] })], [
        { id: "ab", inputs: [{ match: "exact_item", itemId: "A", amount: 1 }], outputs: [{ itemId: "B", amount: 1 }] },
        { id: "ba", inputs: [{ match: "exact_item", itemId: "B", amount: 1 }], outputs: [{ itemId: "A", amount: 1 }] },
        { id: "bc", inputs: [{ match: "exact_item", itemId: "B", amount: 1 }], outputs: [{ itemId: "C", amount: 1 }] },
        { id: "upgrade", inputs: [{ match: "tag", tag: "Tool", amount: 1 }, { match: "exact_item", itemId: "A", amount: 1 }], outputs: [{ copyInputTag: "Tool", reinforceWithAdhesive: true, amount: 1 }] }
    ], config);
    assert.ok(graph.nodes.every(node => Number.isFinite(node.depth) && node.depth < 4));
    assert.ok(graph.edges.some(edge => edge.feedback));
    assert.equal(graph.nodes.find(node => node.id === "Tool").recipes.length, 0);
    assert.equal(graph.nodes.find(node => node.id === "Tool").upgrades.length, 1);
});

test("周期生产和交互采集保留条件，生物只作为外部来源", () => {
    const graph = model.build([entry("A", { modules: { collect: { prefab: "Module_Collectable", parameters: { CollectItemId: "B", MaxStock: 3 } } } }),
        entry("B"), { ...entry("Chicken", { modules: { production: { prefab: "Module_Production", parameters: { ProductionList: [{ itemName: "B", itemCountMin: 1, itemCountMax: 3, SpawnProbability: 0.5 }] } } } }), kind: "actor" }], [], config);
    assert.equal(graph.nodes.length, 2);
    const collect = graph.routes.find(route => route.label === "交互采集");
    assert.equal(collect.inputs[0].tool, false);
    assert.match(collect.inputs[0].label, /来源对象/);
    const production = graph.routes.find(route => route.label === "周期生产");
    assert.equal(production.conditions.externalSource, "Chicken");
    assert.equal(production.outputs[0].probability, 0.5);
});
//#endregion

//#region 当前目录的关键材料链
test("真实目录覆盖九时代和全部具体物品，并保留真实材料链与太空节点", () => {
    const gameConfig = path.resolve(__dirname, "../../GameConfig");
    const read = filename => JSON.parse(fs.readFileSync(filename, "utf8"));
    const load = (directory, manifestName, collection) => {
        const sources = new Map(), packages = new Map();
        for (const pkg of read(path.join(gameConfig, directory, manifestName)).packages.filter(pkg => pkg.enabled !== false))
            for (const source of read(path.join(gameConfig, directory, pkg.path))[collection]) {
                sources.set(source.id.toLowerCase(), source);
                packages.set(source.id.toLowerCase(), pkg);
            }
        return { sources, packages };
    };
    const items = load("Items", "item-manifest.json", "items");
    const actors = load("Actors", "actor-manifest.json", "actors");
    const recipes = load("Recipes", "recipe-manifest.json", "recipes");
    const entries = [...mechanics.resolveDefinitions(items.sources).values()].map(final => ({ kind: "item", final,
        source: items.sources.get(final.id.toLowerCase()), package: items.packages.get(final.id.toLowerCase()), displayName: final.gameName }));
    entries.push(...[...mechanics.resolveDefinitions(actors.sources).values()].map(final => ({ kind: "actor", final,
        source: actors.sources.get(final.id.toLowerCase()), package: actors.packages.get(final.id.toLowerCase()), displayName: final.gameName })));
    const eraConfig = read(path.join(__dirname, "../tech-tree-eras.json"));
    const graph = model.build(entries, [...recipes.sources.values()], eraConfig, {
        mechanicalCatalog: read(path.resolve(gameConfig, "../../Resources/Config/Mechanical/mechanical-catalog.json")),
        liquids: read(path.join(gameConfig, "Liquids/liquids.json"))
    });
    assert.equal(graph.nodes.length, entries.filter(entry => entry.kind === "item" && !entry.final.abstract).length);
    assert.equal(graph.stats.unclassifiedCount, 0);
    assert.equal(graph.stats.eraCount, 9);
    assert.deepEqual(graph.eras.map(era => [era.id, era.name]), [
        ["primitive", "原始采集"], ["stone", "石器"], ["bronze", "铜与青铜"], ["iron", "铁器"],
        ["mechanical", "机械动力时代"], ["electricity", "电力时代"], ["chemical", "化工时代"],
        ["electronics", "电子信息时代"], ["space", "太空时代"]
    ]);
    const nodeById = new Map(graph.nodes.map(node => [node.id, node]));
    const assignedIds = new Set();
    for (const group of eraConfig.assignments) for (const id of group.itemIds) {
        assert.ok(!assignedIds.has(id.toLowerCase()), `${id} 只能归属一个阶段`);
        assignedIds.add(id.toLowerCase());
    }
    for (const era of graph.eras) assert.ok(graph.nodes.some(node => node.eraId === era.id), `${era.name} 不能是空时代`);
    for (const [eraId, ids] of [
        ["mechanical", ["HandCrank", "WaterWheel", "Windmill", "Shaft_Wood", "MechanicalLoom", "MechanicalHammer", "Conveyor_Cloth"]],
        ["electricity", ["Rubber", "Generator_Basic", "Battery_Basic", "Wire_Copper", "Wire_Hidden", "ElectricLightBulb", "InductionStove", "ElectricHeater"]],
        ["chemical", ["FluidPipe", "GasTankBlock_Iron", "GasPump_Mechanical", "LiquidPump_Electric", "GasCompressor_Electric", "PressureProbe_Mechanical", "WaterElectrolyzer", "GasEngine", "Plastic"]],
        ["electronics", ["SiliconPlate", "PressureProbe_Electronic"]],
        ["space", ["Spacesuit_Torso", "ShipFloor_Iron", "ShipConsole", "ShipNavigation", "ShipFiber", "SpaceElevatorTerminal"]]
    ]) for (const id of ids) {
        assert.equal(nodeById.get(id)?.eraId, eraId, `${id} 的时代归属`);
        if (nodeById.has(`${id}_Summoner`)) assert.equal(nodeById.get(`${id}_Summoner`).eraId, eraId, `${id} 的放置物与建筑本体同属一个时代`);
    }
    assert.equal(nodeById.get("Plastic").stageId, "chemical_materials");
    assert.equal(nodeById.get("SiliconPlate").stageId, "semiconductors");
    assert.equal(nodeById.get("PressureProbe_Electronic").stageId, "electronic_measurement");
    for (const [source, target] of [["Ore_Copper", "Ingot_Copper"], ["Ingot_Copper", "Ingot_Bronze"], ["Ingot_Tin", "Ingot_Bronze"],
        ["Ore_Iron", "Ingot_RawIron"], ["Ore_Coal", "Ingot_Steel"], ["Ingot_WroughtIron", "IronPlate"], ["Log", "DrilledLog"], ["Sand", "Glass"]])
        assert.ok(graph.edges.some(edge => edge.source === source && edge.target === target), `${source} → ${target}`);
    assert.ok(graph.routes.some(route => route.conditions.intermediateLiquid?.id === "core:molten_copper"));
    assert.equal(graph.nodes.find(node => node.id === "ShipConsole").eraId, "space");
    assert.equal(graph.nodes.find(node => node.id === "Creative_Power_Source").special, true);
    const shield = graph.nodes.find(node => node.id === "Shield_Wood");
    const nailUpgrade = shield.upgrades.find(route => route.id === "core:wood_shield_nail_reinforcement");
    assert.ok(nailUpgrade, "精确实例复制必须登记为木盾加固路线");
    assert.ok(!shield.recipes.some(route => route.id === nailUpgrade.id), "加固不能被当作新木盾生产路线");
    assert.deepEqual(nailUpgrade.itemIds, ["Shield_Wood"]);
    assert.equal(nailUpgrade.inputs.find(input => input.itemId === "Nail").amount, 3);
    assert.equal(nailUpgrade.output.durabilityMultiplier, 1.5);
    assert.ok(graph.nodes.find(node => node.id === "Wall_Stone_Summoner").externalTargets.length > 0);
    assert.deepEqual(graph.warnings, []);
});
//#endregion

"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const model = require("../tech-tree-model.js");
const context = { window: {} };
vm.runInNewContext(fs.readFileSync(require.resolve("../tech-tree.js"), "utf8"), context);
const layout = context.window.ItemTechTree.layout;
const entry = (id, body = {}) => ({ kind: "item", displayName: id, final: { id, ...body }, source: body });
const config = {
    eras: [{ id: "early", order: 0 }, { id: "late", order: 1 }],
    stages: [{ id: "raw", eraId: "early", order: 0, layoutBand: "natural" },
        { id: "tools", eraId: "early", order: 1 }, { id: "electric", eraId: "late", order: 2 }],
    assignments: [{ stageId: "raw", itemIds: ["Sand", "Glass", "Portal"] },
        { stageId: "tools", itemIds: ["Tree", "Mine", "Ore", "Log", "Ingot", "Crop", "CropFood", "Unknown"] },
        { stageId: "electric", itemIds: ["Rubber", "Wire"] }]
};
const recipe = (id, sourceKind, input, output) => ({ id, sourceKind,
    inputs: [{ itemId: input, match: "exact_item", amount: 1 }], outputs: [{ itemId: output, amount: 1 }] });

//#region 自然来源与分区回归
// 自然获取与展示时代分离，不把缺配方的未知物品或加工产物当成天然原料。
test("自然来源跨时代识别，采矿与树木掉落进入资源区，加工和农耕产物保留主线", () => {
    const graph = model.build([entry("Sand"), entry("Glass"), entry("Portal", { modules: { portal: { prefab: "BuildingFeature_DimensionPortal" } } }),
        entry("Tree", { tags: ["Tree"] }), entry("Mine", { modules: { harvest: { prefab: "Module_ResourceHarvest" } } }),
        ...["Ore", "Log", "Ingot", "Crop", "CropFood", "Unknown", "Rubber", "Wire"].map(id => entry(id))], [
        recipe("collect-rubber", "loot", "Tree", "Rubber"), recipe("collect-log", "loot", "Tree", "Log"),
        recipe("mine", "harvest", "Mine", "Ore"), recipe("smelt", "crafting", "Ore", "Ingot"),
        recipe("melt", "crafting", "Sand", "Glass"), recipe("wire", "crafting", "Rubber", "Wire"),
        recipe("crop", "harvest", "Crop", "CropFood")], config);
    const byId = new Map(graph.nodes.map(node => [node.id, node]));
    for (const id of ["Sand", "Tree", "Mine", "Ore", "Log", "Rubber"]) assert.equal(byId.get(id).naturalResource, true, id);
    for (const id of ["Glass", "Portal", "Ingot", "Crop", "CropFood", "Unknown", "Wire"]) assert.equal(byId.get(id).naturalResource, false, id);
    assert.equal(byId.get("Rubber").eraId, "late");
    assert.ok(graph.edges.some(edge => edge.source === "Rubber" && edge.target === "Wire"));
});

// 检查全图与筛选图的资源位置、卡片无重叠及唯一节点，避免改布局后丢失物品。
test("自然资源居中放在技术下方，按消费者排列且筛选后也不重叠", () => {
    const eras = Array.from({ length: 9 }, (_, i) => ({ id: `era${i}`, order: i }));
    const stages = eras.map((era, i) => ({ id: `stage${i}`, eraId: era.id, order: i }));
    const main = stages.map((stage, i) => ({ id: `tech${i}`, name: `Tech${i}`, stageId: stage.id }));
    const natural = Array.from({ length: 40 }, (_, i) => ({ id: `raw${i}`, name: `Raw${i}`, naturalResource: true }));
    const edges = natural.map((node, i) => ({ source: node.id, target: i < 20 ? "tech0" : "tech8", kind: "ingredient" }));
    const full = layout([...main, ...natural], stages, eras, true, edges);
    assert.equal(full.positions.size, 49);
    assert.equal(full.resourceBox.x + full.resourceBox.width / 2, full.width / 2);
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

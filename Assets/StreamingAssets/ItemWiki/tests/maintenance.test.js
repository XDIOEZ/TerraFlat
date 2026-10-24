"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const mechanics = require("../mechanics.js");
require("../mechanics-rules.js");

// #region 物品字典与游戏数据契约
async function run() {
    const sources = new Map([
        ["base", { id: "Base", gameName: "父项", formerIds: ["Old"], processing: { grind: { inputAmount: 1, work: 2, outputs: [{ itemId: "Powder", amount: 1 }] } }, modules: { old: { prefab: "Old", parameters: { stale: 1 } }, removed: { prefab: "Removed" } } }],
        ["child", { id: "Child", parent: "Base", gameName: "子项", modules: { old: { prefab: "New", parameters: { fresh: 2 } } }, removeModules: ["removed"] }]
    ]);
    const child = mechanics.resolveDefinitions(sources).get("child");
    assert.equal(child.processing, undefined);
    assert.equal(child.formerIds, undefined);
    assert.equal(child.modules.removed, undefined);
    assert.deepEqual(child.modules.old.parameters, { fresh: 2 });
    assert.ok(sources.get("base").processing, "解析不得修改原始 JSON 对象");
    assert.equal(mechanics.matches({ match: "tag", tag: "Tool", excludeTag: "AdhesiveReinforced" }, { tags: ["Tool", "AdhesiveReinforced"] }), false);
    assert.equal(mechanics.aggregateInputs([
        { match: "exact_item", itemId: "Resin", amount: 1 },
        { match: "exact_item", itemId: "Resin", amount: 1, minTemperature: 200 }
    ]).length, 2, "温度条件不同的材料不能合并成无条件材料");

    const configRoot = path.resolve(__dirname, "../../GameConfig");
    const fetchJson = async name => JSON.parse((await fs.readFile(name, "utf8")).replace(/^\uFEFF/, ""));
    const [items, actors, recipes] = await Promise.all([
        mechanics.loadManifest(fetchJson, configRoot + "/Items/", "item-manifest.json", "items"),
        mechanics.loadManifest(fetchJson, configRoot + "/Actors/", "actor-manifest.json", "actors"),
        mechanics.loadManifest(fetchJson, configRoot + "/Recipes/", "recipe-manifest.json", "recipes")
    ]);
    const entries = [...items.resolved.values()].map(final => ({ final, displayName: final.gameName, kind: "item" }));
    assert.equal(items.resolved.get("clay").processing, undefined, "黏土不得继承石头磨石粉的配方");
    assert.equal(items.resolved.get("shovel_base").modules.resourceTool, undefined);
    assert.equal(actors.resolved.get("snowleopard").modules.biteAttackSkill, undefined);
    const processing = mechanics.processingRecipes(entries);
    assert.equal(processing.length, [...items.sources.values()].reduce((count, item) => count + Object.keys(item.processing || {}).length, 0));
    const context = mechanics.createContext(entries, [...recipes.sources.values()], new Map());
    assert.equal(context.recipes.length, recipes.sources.size + processing.length);
    assert.equal(mechanics.createContext(entries, context.recipes, new Map()).recipes.length, context.recipes.length, "重复创建上下文不得重复添加加工配方");
    assert.ok(context.relations.get("orepowder_iron").outputs.some(recipe => recipe.sourceItemId === "Ore_Iron"));
    const reinforcement = mechanics.recipeText(recipes.sources.get("core:tool_reinforcement_hot_resin"), context);
    assert.match(reinforcement, /200℃/);
    assert.match(reinforcement, /原来的工具/);
    assert.match(reinforcement, /30%/);
    const shieldReinforcement = mechanics.recipeText(recipes.sources.get("core:wood_shield_nail_reinforcement"), context);
    assert.match(shieldReinforcement, /原来的木盾/);
    assert.match(shieldReinforcement, /钉子 ×3/);
    assert.match(shieldReinforcement, /耐久倍率 1.5/);
    assert.match(shieldReinforcement, /同类加固仅限一次/);
    assert.ok(context.relations.get("shield_wood").outputs.some(recipe => recipe.id === "core:wood_shield_nail_reinforcement"));
    const leaf = entries.find(entry => entry.final.id === "Leaf");
    assert.ok(mechanics.describe(leaf, context).some(text => text.includes("次食用操作")));
    console.log(`Item Wiki contracts: ${items.sources.size} item definitions, ${actors.sources.size} actors, ${recipes.sources.size} recipes, ${processing.length} processing definitions.`);
}
run().catch(error => { console.error(error); process.exitCode = 1; });
// #endregion

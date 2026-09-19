"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const mechanics = require("../mechanics.js");
require("../mechanics-rules.js");
const config = path.resolve(__dirname, "../../GameConfig");
const fetchJson = async name => JSON.parse(fs.readFileSync(path.join(config, name), "utf8").replace(/^\uFEFF/, ""));

class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.textContent = ""; }
    appendChild(child) { this.children.push(child); return child; }
    append(...children) { this.children.push(...children); }
    set innerHTML(value) { throw new Error("机制渲染不得写入未转义 HTML：" + value); }
}

(async () => {
    const items = await mechanics.loadManifest(fetchJson, "Items/", "item-manifest.json", "items");
    const actors = await mechanics.loadManifest(fetchJson, "Actors/", "actor-manifest.json", "actors");
    const recipes = await mechanics.loadManifest(fetchJson, "Recipes/", "recipe-manifest.json", "recipes");
    const entries = [];
    for (const [kind, catalog] of [["item", items], ["actor", actors]]) {
        for (const final of catalog.resolved.values()) {
            if (!final.abstract) entries.push({ kind, final, displayName: final.gameName || final.id });
        }
    }
    const tables = await fetchJson("LootTables/loot-tables.json");
    const context = mechanics.createContext(entries, [...recipes.resolved.values()],
        new Map(tables.lootTables.map(table => [mechanics.keyOf(table.id), table])));
    const unknown = new Set();
    for (const entry of entries) {
        const sections = mechanics.describe(entry, context);
        assert(sections.length >= 4, entry.final.id + " 缺少独立机制说明");
        assert(sections.every(section => section.lines.every(line => typeof line === "string")), entry.final.id);
        for (const [, body] of mechanics.activeModules(entry.final))
            if (!mechanics.rules.has(body.prefab)) unknown.add(body.prefab);
        mechanics.render(entry, context, { createElement: tag => new Element(tag) });
    }
    assert(mechanics.matches({ match: "tag", tag: "Wood" }, { id: "stick", tags: ["wood"] }));
    assert(!mechanics.matches({ match: "tag", tag: "" }, { id: "stick", tags: [""] }));
    assert.deepEqual(mechanics.aggregateInputs([
        { match: "exact_item", itemId: "Stick_Wood", amount: 2, slot: 0 },
        { match: "exact_item", itemId: "Stick_Wood", amount: 3, slot: 1 },
    ]).map(input => input.amount), [5]);
    const inherited = mechanics.resolveDefinitions(new Map([
        ["parent", { id: "parent", abstract: true, modules: { power: { prefab: "A", enabled: true, parameters: { x: 3 } } } }],
        ["child", { id: "child", parent: "parent", modules: { power: { enabled: false } } }],
        ["replacement", { id: "replacement", parent: "parent", modules: { power: { prefab: "B" } } }],
    ]));
    assert.equal(inherited.get("child").abstract, false);
    assert.equal(mechanics.activeModules(inherited.get("child")).length, 0);
    assert.equal(inherited.get("replacement").modules.power.parameters, undefined);
    assert.throws(() => mechanics.resolveDefinitions(new Map([
        ["a", { id: "a", parent: "b" }], ["b", { id: "b", parent: "a" }],
    ])), /继承循环/);
    await assert.rejects(mechanics.loadManifest(async () => ({ schemaVersion: 1,
        packages: [{ path: "../private.json" }] }), "Items/", "item-manifest.json", "items"), /无效分包路径/);
    const hostile = { kind: "item", displayName: "<img src=x onerror=alert(1)>", final: {
        id: "malicious", modules: { unknown: { prefab: "<script>", parameters: { text: "</pre><script>alert(1)</script>" } } },
    } };
    const hostileContext = mechanics.createContext([hostile], [], new Map());
    assert(mechanics.describe(hostile, hostileContext).some(section => section.title.startsWith("未登记解释")));
    mechanics.render(hostile, hostileContext, { createElement: tag => new Element(tag) });
    assert(!Object.prototype.polluted);
    const result = { passed: true, items: entries.filter(entry => entry.kind === "item").length,
        actors: entries.filter(entry => entry.kind === "actor").length,
        recipes: recipes.resolved.size, unregisteredModules: [...unknown].sort() };
    console.log(JSON.stringify(result, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });

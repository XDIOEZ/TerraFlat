"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const mechanics = require("../mechanics.js");
const model = require("../behavior-tree-model.js");

function loadRealCatalog() {
    const root = path.resolve(__dirname, "../../GameConfig/Actors");
    const read = filename => JSON.parse(fs.readFileSync(filename, "utf8").replace(/^\uFEFF/, ""));
    const manifest = read(path.join(root, "actor-manifest.json"));
    const sources = new Map();
    const packages = new Map();
    for (const pkg of manifest.packages.filter(entry => entry.enabled !== false)) {
        for (const actor of read(path.join(root, pkg.path)).actors) {
            sources.set(actor.id.toLowerCase(), actor);
            packages.set(actor.id.toLowerCase(), pkg);
        }
    }
    const entries = [...mechanics.resolveDefinitions(sources).values()].map(final => ({
        kind: "actor", final, displayName: final.gameName,
        package: packages.get(final.id.toLowerCase())
    }));
    const library = read(path.join(root, manifest.behaviorSubtrees));
    library.subtreePaths = library.subtrees;
    library.subtrees = Object.fromEntries(Object.entries(library.subtreePaths).map(([name, file]) => [name, read(path.join(root, file))]));
    return { entries, library };
}

function actorEntry(id, root, subtrees = {}, capabilities = {}) {
    const final = { id, gameName: id, modules: { ai: { parameters: { behaviorTree: { root, subtrees, capabilities } } } } };
    return { kind: "actor", final, displayName: id, package: { id: "test", path: "test.json" } };
}

test("真实 Actor 继承目录包含 13 个树宿主，Bear 整体替换 pack，Seagull 继承 Bird", () => {
    const { entries, library } = loadRealCatalog();
    const catalog = model.createCatalog(entries, library);
    const byId = new Map(catalog.actors.map(actor => [actor.id, actor]));
    assert.equal(catalog.actors.length, 13);
    assert.equal(entries.find(entry => entry.final.id === "Bear").final.modules.ai.inherit, undefined);
    assert.equal(byId.get("Bear").capabilities.pack, undefined);
    assert.deepEqual(byId.get("Seagull").root, byId.get("Bird").root);
    assert.ok(byId.has("Seagull"));

    const wolf = model.buildTree(byId.get("Wolf"), catalog);
    const zombie = model.buildTree(byId.get("Zombie"), catalog);
    assert.equal(wolf.root.subtree, "packHunt");
    assert.equal(wolf.root.parameters.chaseDistance, 28);
    assert.equal(zombie.root.parameters.chaseDistance, 22);
    assert.equal(zombie.root.parameters.chaseLossDistance, 32);
    for (const actor of catalog.actors) {
        const visit = node => {
            assert.equal(node.error, undefined, `${actor.id}: ${node.error}`);
            node.children.forEach(visit);
        };
        visit(model.buildTree(actor, catalog).root);
    }
});

test("子树默认值、调用覆盖与嵌套参数按每次引用分别展开", () => {
    const library = { subtrees: {
        target: { parameters: { range: 4, source: "default" }, root: { type: "condition", name: "land.canChase", parameters: { range: "$range", source: "$source" } } },
        nested: { parameters: { distance: 5 }, root: { type: "subtree", name: "leaf", parameters: { range: "$distance" } } },
        leaf: { parameters: { range: 1 }, root: { type: "action", name: "land.chase", parameters: { range: "$range" } } }
    } };
    const entry = actorEntry("TestWolf", { type: "selector", children: [
        { type: "subtree", name: "target", parameters: { range: 8, source: "wolf" } },
        { type: "subtree", name: "target", parameters: { range: 15, source: "zombie" } },
        { type: "subtree", name: "nested", parameters: { distance: 22 } }
    ] });
    const catalog = model.createCatalog([entry], library);
    const actor = catalog.actors[0];
    const tree = model.buildTree(actor, catalog);
    const [first, second, nested] = tree.root.children;
    assert.deepEqual(first.parameters, { range: 8, source: "wolf" });
    assert.deepEqual(second.parameters, { range: 15, source: "zombie" });
    assert.deepEqual(first.children[0].parameters, { range: 8, source: "wolf" });
    assert.deepEqual(second.children[0].parameters, { range: 15, source: "zombie" });
    assert.equal(nested.children[0].children[0].parameters.range, 22);
    assert.notStrictEqual(first.parameters, second.parameters);
    assert.notStrictEqual(first.children[0].parameters, second.children[0].parameters);
    assert.equal(tree.referenceCount, 4);
    const secondBuild = model.buildTree(actor, catalog);
    assert.equal(secondBuild.root.children[0].key, first.key);
    assert.notStrictEqual(secondBuild.root.children[0], first);
});

test("Actor 子树覆盖公共同名定义，使用者按实际递归引用统计", () => {
    const library = { subtrees: { inspect: { parameters: {}, root: { type: "action", name: "shared.inspect" } } } };
    const entry = actorEntry("LocalActor", { type: "subtree", name: "inspect" }, {
        inspect: { parameters: {}, root: { type: "action", name: "local.inspect" } }
    });
    const catalog = model.createCatalog([entry], library);
    const tree = model.buildTree(catalog.actors[0], catalog);
    const local = catalog.subtrees.find(item => item.scope === "actor");
    const shared = catalog.subtrees.find(item => item.scope === "shared");
    assert.equal(tree.root.children[0].name, "local.inspect");
    assert.deepEqual(local.usedBy, ["LocalActor"]);
    assert.deepEqual(shared.usedBy, []);
});

test("未知子树与循环引用保留错误节点并停止递归", () => {
    const library = { subtrees: {
        alpha: { parameters: {}, root: { type: "subtree", name: "beta" } },
        beta: { parameters: {}, root: { type: "subtree", name: "alpha" } }
    } };
    const entry = actorEntry("LoopActor", { type: "selector", children: [
        { type: "subtree", name: "alpha" }, { type: "subtree", name: "missing" }
    ] });
    const catalog = model.createCatalog([entry], library);
    const tree = model.buildTree(catalog.actors[0], catalog);
    const cycle = tree.root.children[0].children[0].children[0];
    const unknown = tree.root.children[1];
    assert.match(cycle.error, /循环引用/);
    assert.match(unknown.error, /找不到子树/);
    assert.equal(tree.nodeCount, 5);
    assert.equal(tree.referenceCount, 4);
});

test("modules.*.inherit=false 完全替换该继承模块并消费标记", () => {
    const sources = new Map([
        ["parent", { id: "Parent", modules: { ai: { prefab: "AI", parameters: { stale: true } }, health: { prefab: "Health" } } }],
        ["child", { id: "Child", parent: "Parent", modules: { ai: { inherit: false, prefab: "AI", parameters: { fresh: true } } } }]
    ]);
    const resolved = mechanics.resolveDefinitions(sources).get("child");
    assert.deepEqual(resolved.modules.ai, { prefab: "AI", parameters: { fresh: true } });
    assert.deepEqual(resolved.modules.health, { prefab: "Health" });
    assert.equal(sources.get("parent").modules.ai.parameters.stale, true);
    assert.equal(sources.get("child").modules.ai.inherit, false);
});

test("loadManifest 返回原始 Manifest 以供共享子树路径读取", async () => {
    const root = path.resolve(__dirname, "../../GameConfig/Actors") + path.sep;
    const fetchJson = async filename => JSON.parse(fs.readFileSync(filename, "utf8").replace(/^\uFEFF/, ""));
    const loaded = await mechanics.loadManifest(fetchJson, root, "actor-manifest.json", "actors");
    assert.equal(loaded.manifest.behaviorSubtrees, "behavior-subtrees.json");
    assert.ok(loaded.sources instanceof Map);
    assert.ok(loaded.resolved instanceof Map);
});

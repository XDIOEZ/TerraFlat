"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const buffs = require("../buffs.js");
const root = path.resolve(__dirname, "../../GameConfig/Buffs");
const manifest = JSON.parse(fs.readFileSync(path.join(root, "buff-manifest.json"), "utf8").replace(/^\uFEFF/, ""));
const definitions = manifest.packages.filter(p => p.enabled !== false).flatMap(p =>
    JSON.parse(fs.readFileSync(path.join(root, p.path), "utf8").replace(/^\uFEFF/, "")).buffs);

test("全部 BUFF 都有独立名称、检索数据和机制说明", () => {
    for (const source of definitions) {
        const entry = buffs.makeEntry(source, { id: "test", path: "test.json" });
        assert.equal(entry.id, `buff:${source.id}`);
        assert.equal(entry.kind, "buff");
        assert.ok(entry.searchBlob.includes(source.id.toLowerCase()));
        assert.ok(buffs.describe(source).length >= 2);
        assert.equal(buffs.stats(source).length, 10);
    }
});

test("燃烧说明按实际配置生成 1..5 HP/s 与特效倍率", () => {
    const source = definitions.find(value => value.id === "燃烧");
    const description = buffs.describe(source).join("\n");
    for (let count = 1; count <= 5; count++) assert.ok(description.includes(`${count}层：${count} HP/s`));
    const edited = JSON.parse(JSON.stringify(source));
    edited.effects[0].value = 2;
    assert.ok(buffs.describe(edited).join("\n").includes("5层：10 HP/s"));
    assert.ok(description.includes("0.6 +（层数 − 1）× 0.2"));
});

test("水深各档上限和编辑路径与游戏配置一致", () => {
    const wet = definitions.find(value => value.id === "潮湿");
    const description = buffs.describe(wet).join("\n");
    [3, 6, 9, 10].forEach((cap, i) => assert.ok(description.includes(`${i + 1}/10 水深：最多 ${cap} 层`)));
    assert.equal(buffs.stats(wet)[0][2].nullableNumber, true);
    assert.equal(buffs.effectStats(wet.effects[0], 0).find(row => row[0] === "数值")[2].path, "effects.0.value");
});

// 仅抑制页面启动，执行 app.js 的真实编辑/路由函数，不在测试中重写其实现。
function loadAppLogic() {
    const elements = new Map();
    const requests = [];
    const sandbox = {
        console, BuffWiki: buffs,
        document: { getElementById(id) {
            if (!elements.has(id)) elements.set(id, { value: "", checked: false });
            return elements.get(id);
        } },
        fetch: async (url, options) => {
            requests.push({ url, body: JSON.parse(options.body) });
            return { ok: true, json: async () => ({ ok: true, hash: "saved" }) };
        }
    };
    let source = fs.readFileSync(path.join(__dirname, "../app.js"), "utf8");
    const startup = /\binit\(\);(?=\s*\}\)\(\);\s*$)/;
    assert.match(source, startup);
    source = source.replace(startup, "globalThis.testApi = { state, els, setNestedValue, parseNullableNumber, getCatalogEntries, getOverviewEntries, saveItemSourceRequest };");
    vm.runInNewContext(source, sandbox, { filename: "app.js" });
    return { ...sandbox.testApi, requests };
}

test("编辑某个效果数值不破坏数组和其它效果", () => {
    const app = loadAppLogic();
    const source = { effects: [{ value: 1, typeId: "core:true_damage" }, { value: -2 }] };
    app.setNestedValue(source, "effects.0.value", 3);
    assert.ok(Array.isArray(source.effects));
    assert.deepEqual(source.effects, [{ value: 3, typeId: "core:true_damage" }, { value: -2 }]);
    assert.throws(() => app.setNestedValue(source, "effects.__proto__", {}));
    assert.equal(app.parseNullableNumber("null", "持续时间"), null);
    assert.equal(app.parseNullableNumber("2.5", "持续时间"), 2.5);
    assert.throws(() => app.parseNullableNumber("", "持续时间"));
});

test("BUFF 页独立筛选且保存使用专用指纹和原始稳定 ID", async () => {
    const app = loadAppLogic();
    const source = definitions.find(value => value.id === "燃烧");
    const entry = buffs.makeEntry(source, { id: "shared-package-id", path: "periodic_damage.json" });
    const item = { ...entry, kind: "item", id: "Torch", final: { id: "Torch" } };
    app.state.entries = [entry, item];
    app.state.activeView = "buffs";
    assert.equal(app.getCatalogEntries().length, 1);
    assert.equal(app.getCatalogEntries()[0].kind, "buff");
    assert.equal(app.getOverviewEntries().length, 1);
    assert.equal(app.getOverviewEntries()[0].kind, "item");
    app.state.buffHashes.set(entry.package.id, "buff-hash");
    app.state.packageHashes.set(entry.package.id, "item-hash");
    await app.saveItemSourceRequest(entry, source);
    assert.equal(app.requests[0].url, "/api/buffs/save");
    assert.equal(app.requests[0].body.itemId, "燃烧");
    assert.equal(app.requests[0].body.expectedHash, "buff-hash");
    await app.saveItemSourceRequest(item, item.final);
    assert.equal(app.requests[1].url, "/api/items/save");
    assert.equal(app.requests[1].body.expectedHash, "item-hash");
});

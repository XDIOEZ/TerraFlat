"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const root = path.resolve(__dirname, "../../..");
const indexUrl = "http://wiki.local/Assets/StreamingAssets/GameConfig/Actors/behavior-subtrees.json";
const app = fs.readFileSync(path.join(root, "Assets/StreamingAssets/ItemWiki/app.js"), "utf8");
const start = app.indexOf("    async function fetchJson(path) {");
const end = app.indexOf("\n    // 独立命名空间", start);
assert.ok(start >= 0 && end > start);

// 使用真实浏览器读取函数和磁盘配置，HTTP 环境由内存响应代替。
function browserLoader(missingFile = "") {
    const requests = [];
    const fetchJson = vm.runInNewContext(`(${app.slice(start, end).trim()})`, {
        URL,
        fetch: async input => {
            const url = new URL(input, indexUrl);
            const relative = decodeURIComponent(url.pathname).slice(1);
            requests.push(relative);
            if (relative.endsWith(missingFile) && missingFile) {
                return { ok: false, status: 404, statusText: "Missing subtree" };
            }
            return {
                ok: true, url: url.href,
                json: async () => JSON.parse(fs.readFileSync(path.join(root, relative), "utf8"))
            };
        }
    });
    return { fetchJson, requests };
}

test("实际 Wiki 读取函数加载中文子树路径并保留独立源文件", async () => {
    const { fetchJson, requests } = browserLoader();
    const library = await fetchJson(indexUrl);
    const names = Object.keys(library.subtreePaths);
    assert.equal(requests.length, names.length + 1);
    assert.equal(new Set(requests).size, requests.length);
    for (const name of names) {
        assert.equal(typeof library.subtreePaths[name], "string");
        assert.ok(library.subtrees[name].root, name);
    }
    assert.match(library.subtreePaths.lowHealthFlee, /残血逃跑\.json$/);
    assert.equal(library.subtrees.lowHealthFlee.parameters.enter, 0.2);
    assert.equal(library.subtrees.packLowHealthFlee.root.children[2].name, "lowHealthFlee");
    assert.equal(library.subtrees.solitaryHunt.root.children[1].name, "huntAndWander");
    assert.equal(library.subtrees.territorialHunt.root.children[1].name, "huntAndWander");
});

test("独立子树读取失败会报告对应文件", async () => {
    const { fetchJson } = browserLoader("残血逃跑.json");
    await assert.rejects(fetchJson(indexUrl), /404 Missing subtree/);
});

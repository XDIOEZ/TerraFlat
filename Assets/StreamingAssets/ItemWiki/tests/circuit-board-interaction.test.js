"use strict";
const { test, before, after, beforeEach, afterEach } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require(process.env.ITEM_WIKI_PLAYWRIGHT_MODULE || "playwright-core");

const wiki = path.resolve(__dirname, "..");
const catalog = JSON.parse(fs.readFileSync(path.resolve(wiki, "../../Resources/Config/Mechanical/mechanical-catalog.json"), "utf8").replace(/^\uFEFF/, ""));
let browser, page, errors;

before(async () => {
    browser = await chromium.launch({
        headless: true,
        ...(process.env.ITEM_WIKI_CHROMIUM_PATH ? { executablePath: process.env.ITEM_WIKI_CHROMIUM_PATH } : {})
    });
});
after(async () => browser?.close());
beforeEach(async () => {
    page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
    errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("dialog", dialog => dialog.accept());
    await page.route("http://circuit.test/**", route => route.fulfill({
        contentType: "text/html", body: '<!doctype html><meta charset="utf-8"><style>body{margin:0}</style><div id="root"></div>'
    }));
    await page.goto("http://circuit.test/");
    await page.addStyleTag({ path: path.join(wiki, "circuit-board.css") });
    await page.addScriptTag({ path: path.join(wiki, "circuit-board-model.js") });
    await page.addScriptTag({ path: path.join(wiki, "circuit-board.js") });
    await page.evaluate(catalog => {
        window.circuit = ItemCircuitBoard.create({ root: document.getElementById("root") });
        circuit.setCatalog(catalog, []);
        circuit.show();
    }, catalog);
    await page.locator('[data-action="clear"]').click();
});
afterEach(async () => {
    await page?.close();
    assert.deepEqual(errors, [], "编辑器操作不应触发浏览器脚本错误");
});

async function center(x, y) { return page.evaluate(([x, y]) => circuit.getCellClientPoint(x, y), [x, y]); }
async function place(type, x, y) {
    await page.locator('.cb-palette-list [data-tool="' + type + '"]').click();
    const p = await center(x, y); await page.mouse.click(p.x, p.y);
}
async function port(x, y, direction, power = false) {
    return page.evaluate(([x, y, direction, power]) => {
        const p = circuit.getCellClientPoint(x, y), next = circuit.getCellClientPoint(x + 1, y);
        const zoom = (next.x - p.x) / 64;
        const delta = [[1, 0], [0, 1], [-1, 0], [0, -1]][direction];
        return { x: p.x + (power ? 12 : delta[0] * 25) * zoom, y: p.y + (power ? 12 : delta[1] * 25) * zoom };
    }, [x, y, direction, power]);
}
async function connectByDrag(a, b) {
    await page.locator('[data-tool="select"]').click();
    await page.mouse.move(a.x, a.y);
    await page.mouse.down();
    await page.mouse.move(b.x, b.y, { steps: 12 });
    await page.mouse.up();
}

test("引脚拖到引脚自动连线，自动绕过设备，一次撤销全部恢复", async () => {
    await place("sim:switch", 6, 5);
    await place("sim:probe", 12, 5);
    await place("sim:and", 9, 5);
    await connectByDrag(await port(6, 5, 0), await port(12, 5, 2));
    const stats = await page.evaluate(() => ({
        cells: Array.from(circuit.getBoard().signal.keys()),
        atObstacle: circuit.getBoard().signal.has("9,5"),
        networkCount: circuit.getBoard().signalNetworks.length
    }));
    assert.ok(stats.cells.includes("7,5") && stats.cells.includes("11,5"));
    assert.ok(stats.cells.length > 5, "绕过障碍应增加格子数");
    assert.equal(stats.atObstacle, false);
    assert.ok(stats.networkCount >= 1);
    await page.locator('[data-action="undo"]').click();
    assert.equal(await page.evaluate(() => circuit.getBoard().signal.size), 0);
});

test("自动布线绕开不相关信号网，不会悄悄把第三条网络短接", async () => {
    await place("sim:switch", 6, 5); await place("sim:probe", 12, 5);
    await page.evaluate(() => { circuit.getBoard().paint("sim:signal", 9, 5); circuit.getBoard().refresh(); });
    await connectByDrag(await port(6, 5, 0), await port(12, 5, 2));
    const networks = await page.evaluate(() => {
        const board = circuit.getBoard();
        return {
            source: board.signalIndex.get("7,5:a"),
            destination: board.signalIndex.get("11,5:a"),
            third: board.signalIndex.get("9,5:a"),
            size: board.signal.size
        };
    });
    assert.equal(networks.source, networks.destination, "目标两个接口应连成同一网");
    assert.notEqual(networks.third, networks.source, "原有第三条信号线应保持独立");
    assert.ok(networks.size > 5, "自动路径应绕开第三条线路");
});

test("全高度的隔离线阻断路径时，不修改原线路并提示安全失败", async () => {
    await place("sim:switch", 6, 5); await place("sim:probe", 12, 5);
    await page.evaluate(() => {
        const board = circuit.getBoard();
        for (let y = 0; y < board.height; y++) board.paint("sim:signal", 9, y);
        board.refresh();
    });
    await connectByDrag(await port(6, 5, 0), await port(12, 5, 2));
    assert.equal(await page.evaluate(() => circuit.getBoard().signal.size), 40);
    assert.match(await page.locator('[data-role="status"]').textContent(), /安全路径/);
    await page.keyboard.press("Escape");
    assert.equal(await page.evaluate(() => circuit.getBoard().signal.size), 40);
});

test("信号端口拖到电力端口会显示类型冲突且不修改布置", async () => {
    await place("sim:switch", 6, 5); await place("sim:source", 12, 5);
    await page.locator('[data-tool="select"]').click();
    const a = await port(6, 5, 0), b = await port(12, 5, 0, true);
    await page.mouse.move(a.x, a.y); await page.mouse.down();
    await page.mouse.move(b.x, b.y, { steps: 7 });
    assert.equal(await page.locator('.cb-connect-invalid').count(), 1);
    await page.mouse.up();
    assert.match(await page.locator('[data-role="status"]').textContent(), /类型不匹配/);
    assert.deepEqual(await page.evaluate(() => ({
        signal: circuit.getBoard().signal.size, power: circuit.getBoard().power.size
    })), { signal: 0, power: 0 });
});

test("支持两次点击完成接线，Esc 与右键取消时不改变布置", async () => {
    await place("sim:switch", 6, 5); await place("sim:probe", 12, 5);
    await page.locator('[data-tool="select"]').click();
    const a = await port(6, 5, 0), b = await port(12, 5, 2);
    await page.mouse.click(a.x, a.y);
    assert.match(await page.locator('[data-role="status"]').textContent(), /另一兼容接口/);
    await page.keyboard.press("Escape");
    assert.equal(await page.evaluate(() => circuit.getBoard().signal.size), 0);
    await page.mouse.click(a.x, a.y);
    await page.mouse.click(b.x, b.y);
    assert.ok(await page.evaluate(() => circuit.getBoard().signal.size >= 5));
    await page.locator('[data-action="undo"]').click();
    await page.mouse.click(a.x, a.y);
    await page.mouse.click(a.x, a.y, { button: "right" });
    assert.equal(await page.evaluate(() => circuit.getBoard().signal.size), 0);
});

test("电源接口自动布线；测试模式实时切换开关不暂停仿真", async () => {
    await place("sim:source", 6, 5); await place("sim:load", 12, 5);
    await connectByDrag(await port(6, 5, 0, true), await port(12, 5, 0, true));
    assert.ok(await page.evaluate(() => circuit.getBoard().power.has("6,5") && circuit.getBoard().power.has("12,5")));
    await page.locator('[data-action="clear"]').click();
    await place("sim:switch", 6, 5);
    await page.locator('[data-action="run"]').click();
    const initial = await page.evaluate(() => circuit.getBoard().params(circuit.getBoard().at(6, 5)).on);
    await page.locator('[data-tool="poke"]').click();
    const p = await center(6, 5);
    await page.mouse.click(p.x, p.y);
    assert.equal(await page.evaluate(() => circuit.getBoard().params(circuit.getBoard().at(6, 5)).on), !initial);
    assert.match(await page.locator('[data-action="run"]').textContent(), /暂停/);
    const ticks = await page.evaluate(() => circuit.getBoard().ticks);
    await page.waitForFunction(previous => circuit.getBoard().ticks > previous, ticks);
});

test("D 复制、方向键移动、Shift 加速以及 Esc 退出工具", async () => {
    await place("sim:switch", 6, 5);
    await page.locator('[data-tool="select"]').click();
    let p = await center(6, 5); await page.mouse.click(p.x, p.y);
    await page.keyboard.press("d");
    assert.equal(await page.evaluate(() => circuit.getBoard().devices.size), 2);
    const before = await page.evaluate(() => [...circuit.getBoard().devices.values()].find(d => d.x !== 6).x);
    await page.keyboard.press("ArrowRight");
    assert.ok(await page.evaluate(x => [...circuit.getBoard().devices.values()].some(d => d.x === x + 1), before));
    await page.keyboard.press("Shift+ArrowRight");
    assert.ok(await page.evaluate(x => [...circuit.getBoard().devices.values()].some(d => d.x === x + 5), before));
    await page.keyboard.press("w");
    await page.keyboard.press("Escape");
    assert.equal(await page.locator('[data-tool="select"].active').count(), 1);
});

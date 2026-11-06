"use strict";
const { test, before, after, beforeEach, afterEach } = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs");
const { chromium } = require(process.env.ITEM_WIKI_PLAYWRIGHT_MODULE || "playwright-core");
const catalog = JSON.parse(fs.readFileSync(path.resolve(__dirname, "../../../Resources/Config/Mechanical/mechanical-catalog.json"), "utf8").replace(/^\uFEFF/, ""));
const fiber = JSON.parse(fs.readFileSync(path.resolve(__dirname, "../../GameConfig/Items/shells/space_ships.json"), "utf8").replace(/^\uFEFF/, "")).items.find(item => item.id === "ShipFiber");
const base = path.resolve(__dirname, ".."), project = path.resolve(base, "../../.."), output = path.join(project, "Library/ItemWikiBrowserValidation");
let browser, page, errors;
before(async () => {
    browser = await chromium.launch({ headless: true, ...(process.env.ITEM_WIKI_CHROMIUM_PATH ? { executablePath: process.env.ITEM_WIKI_CHROMIUM_PATH } : {}) });
    fs.mkdirSync(output, { recursive: true });
});
after(async () => browser?.close());
async function install() {
    await page.goto("http://circuit.test/");
    await page.addStyleTag({ path: path.join(base, "circuit-board.css") });
    await page.addScriptTag({ path: path.join(base, "circuit-board-model.js") });
    await page.addScriptTag({ path: path.join(base, "circuit-adder-panel.js") });
    await page.addScriptTag({ path: path.join(base, "circuit-board.js") });
    await page.evaluate(({ catalog, fiber }) => {
        window.circuit = ItemCircuitBoard.create({ root: document.getElementById("root") });
        const names = { Battery_Basic: "蓄电池", Battery_LithiumIronPhosphate: "磷酸铁锂电池", ElectricLightBulb: "电灯", Wire_Copper: "铜电线", LiquidPump_Electric: "电动水泵", Generator_Basic: "双向电机", Creative_Power_Source: "创造电源" };
        circuit.setCatalog(catalog, [...Object.entries(names).map(([id, displayName]) => ({ id, displayName })), { final: fiber, displayName: fiber.gameName }]);
        circuit.show();
    }, { catalog, fiber });
    await page.locator('[data-action="fit"]').click();
}
beforeEach(async () => {
    errors = []; page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
    page.on("pageerror", error => errors.push(error.message));
    page.on("dialog", dialog => dialog.accept());
    await page.route("http://circuit.test/**", route => route.fulfill({ contentType: "text/html", body: '<!doctype html><meta charset="utf-8"><style>body{margin:0}</style><div id="root"></div>' }));
    await install();
});
afterEach(async () => { await page?.close(); assert.deepEqual(errors, [], "浏览器不应出现脚本异常"); });
async function point(x, y) { return page.evaluate(([x, y]) => circuit.getCellClientPoint(x, y), [x, y]); }
async function clickCell(x, y) { const p = await point(x, y); await page.mouse.click(p.x, p.y); }

test("加载真实配置、运行单步、切页暂停、桌面布局无横向溢出", async () => {
    assert.equal(await page.locator('.cb-component.cb-game').count(), catalog.Nodes.filter(n => n.Electrical).length + 1);
    await page.locator('[data-action="step"]').click(); assert.equal(await page.evaluate(() => circuit.getBoard().ticks), 1);
    await page.locator('[data-action="run"]').click(); await page.waitForFunction(() => circuit.getBoard().ticks > 3);
    await page.evaluate(() => circuit.hide()); const ticks = await page.evaluate(() => circuit.getBoard().ticks); await page.waitForTimeout(160);
    assert.equal(await page.evaluate(() => circuit.getBoard().ticks), ticks); await page.evaluate(() => circuit.show());
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    await page.screenshot({ path: path.join(output, "circuit-board-desktop.png"), fullPage: true });
});
test("点击放置、移动、旋转、参数修改、撤销和重做", async () => {
    await page.locator('[data-tool="sim:switch"]').click(); await clickCell(7, 5);
    assert.equal(await page.evaluate(() => circuit.getBoard().at(7, 5).type), "sim:switch");
    await page.locator('[data-param="on"]').uncheck(); assert.equal(await page.evaluate(() => circuit.getBoard().params(circuit.getBoard().at(7, 5)).on), false);
    await page.locator('[data-tool="select"]').click(); const a = await point(7, 5), b = await point(8, 5);
    await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y, { steps: 5 }); await page.mouse.up();
    assert.equal(await page.evaluate(() => !!circuit.getBoard().at(8, 5)), true);
    await page.keyboard.press("r"); assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 5).rotation), 1);
    await page.locator('[data-action="undo"]').click(); assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 5).rotation), 0);
    await page.locator('[data-action="redo"]').click(); assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 5).rotation), 1);
});
test("一次快速拖动连续铺线，按层擦除，不误删设备", async () => {
    await page.locator('[data-tool="Wire_Copper"]').click(); const a = await point(5, 5), b = await point(9, 5);
    await page.mouse.move(a.x, a.y); await page.mouse.down(); await page.mouse.move(b.x, b.y); await page.mouse.up();
    assert.equal(await page.evaluate(() => [5, 6, 7, 8, 9].every(x => circuit.getBoard().power.has(`${x},5`))), true);
    await page.locator('[data-tool="sim:signal"]').click(); await clickCell(7, 5);
    const p = await point(7, 5); await page.mouse.click(p.x, p.y, { button: "right" });
    assert.equal(await page.evaluate(() => circuit.getBoard().power.has("7,5") && !circuit.getBoard().signal.has("7,5")), true);
});
test("参数校验、四种示例、无效导入保持当前方案", async () => {
    await clickCell(4, 8); await page.locator('[data-param="high"]').fill("-1"); await page.locator('[data-param="high"]').press("Tab");
    assert.match(await page.locator('[data-role="status"]').textContent(), /超出范围/);
    assert.equal(await page.evaluate(() => circuit.getBoard().params(circuit.getBoard().at(4, 8)).high), 1);
    for (const name of ["tank", "battery", "logic", "blink"]) {
        await page.locator('[data-role="example"]').selectOption(name); await page.locator('[data-action="step"]').click(); assert.ok(await page.evaluate(() => circuit.getBoard().devices.size >= 3));
    }
    const old = await page.evaluate(() => JSON.stringify(circuit.getBoard().serialize()));
    await page.locator('[data-role="file"]').setInputFiles({ name: "invalid.json", mimeType: "application/json", buffer: Buffer.from('{"format":"bad"}') });
    await page.waitForFunction(() => document.querySelector('[data-role="status"]').textContent.includes("导入失败"));
    assert.equal(await page.evaluate(() => JSON.stringify(circuit.getBoard().serialize())), old);
});
test("方案导出再导入、自动保存跨页面重载恢复", async () => {
    await page.locator('[data-tool="sim:not"]').click(); await clickCell(7, 5);
    const downloadPromise = page.waitForEvent("download"); await page.locator('[data-action="export"]').click(); const download = await downloadPromise;
    const exported = JSON.parse(fs.readFileSync(await download.path(), "utf8")); assert.equal(exported.devices.length, 4);
    await page.locator('[data-action="clear"]').click(); assert.equal(await page.evaluate(() => circuit.getBoard().devices.size), 0);
    await page.locator('[data-role="file"]').setInputFiles({ name: "valid.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(exported)) });
    await page.waitForFunction(() => circuit.getBoard().devices.size === 4); await page.evaluate(() => circuit.hide());
    await install(); assert.equal(await page.evaluate(() => circuit.getBoard().devices.size), 4);
});
test("铺满退出与窄屏不产生横向滚动", async () => {
    await page.locator('[data-action="expand"]').click(); assert.equal(await page.locator('.cb-expanded').count(), 1);
    await page.keyboard.press("Escape"); assert.equal(await page.locator('.cb-expanded').count(), 0);
    for (const width of [1100, 800, 430]) {
        await page.setViewportSize({ width, height: 1000 }); await page.waitForTimeout(80);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `宽度 ${width} 不应溢出`);
    }
    await page.screenshot({ path: path.join(output, "circuit-board-mobile.png"), fullPage: true });
});

test("分类筛选、F 快速添加、快捷键与属性面板收合", async () => {
    await page.locator('[data-category="逻辑"]').click();
    assert.equal(await page.locator('.cb-palette-list [data-tool="sim:and"]').count(), 1);
    assert.equal(await page.locator('.cb-palette-list [data-tool="sim:switch"]').count(), 0);
    await page.locator('.cb-canvas').focus(); await page.keyboard.press("f");
    await page.locator('[data-role="quick-search"]').fill("与门");
    assert.equal(await page.locator('.cb-quick-result').count(), 1);
    await page.keyboard.press("Enter");
    assert.equal(await page.locator('.cb-quick-picker:visible').count(), 0);
    await clickCell(8, 9);
    assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 9)?.type), "sim:and");
    await page.locator('.cb-map-toolbar [data-action="toggle-inspector"]').click();
    assert.equal(await page.locator('.cb-inspector-collapsed').count(), 1);
    await page.keyboard.press("i");
    assert.equal(await page.locator('.cb-inspector-collapsed').count(), 0);
    await page.locator('.cb-canvas').focus();
    await page.keyboard.press("v"); assert.match(await page.locator('[data-role="tool-name"]').textContent(), /选择/);
    await page.keyboard.press("w"); assert.equal(await page.locator('.cb-palette-list [data-tool="Wire_Copper"].active').count(), 0);
    assert.equal(await page.locator('.cb-quicktools [data-quick-tool="Wire_Copper"].active').count(), 1);
    await page.keyboard.press("s"); assert.equal(await page.locator('.cb-quicktools [data-quick-tool="sim:signal"].active').count(), 1);
});

test("从元件库拖放到画布可撤销，窄屏自动保持示例可见", async () => {
    await page.locator('[data-category="控制"]').click();
    const bounds = await page.locator('.cb-canvas').boundingBox(), target = await point(8, 9);
    await page.locator('.cb-palette-list [data-tool="sim:switch"]').dragTo(page.locator('.cb-canvas'), {
        targetPosition: { x: target.x - bounds.x, y: target.y - bounds.y }
    });
    assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 9)?.type), "sim:switch");
    await page.locator('[data-action="undo"]').click();
    assert.equal(await page.evaluate(() => circuit.getBoard().at(8, 9) ?? null), null);
    await page.setViewportSize({ width: 430, height: 1000 }); await page.waitForTimeout(120);
    const view = await page.evaluate(() => {
        const rectangle = document.querySelector(".cb-canvas").getBoundingClientRect();
        return { rect: { left: rectangle.left, right: rectangle.right, top: rectangle.top, bottom: rectangle.bottom },
            points: [circuit.getCellClientPoint(4, 8), circuit.getCellClientPoint(12, 3)] };
    });
    for (const point of view.points) {
        assert.ok(point.x > view.rect.left && point.x < view.rect.right, "缩窄视窗后示例仍在横向可见范围");
        assert.ok(point.y > view.rect.top && point.y < view.rect.bottom, "缩窄视窗后示例仍在纵向可见范围");
    }
});

test("隐藏时加载再显示自动居中，运行时选择观察不会暂停", async () => {
    await page.evaluate(catalog => {
        circuit.hide(); document.getElementById("root").hidden = true; circuit.setCatalog(catalog, []);
        document.getElementById("root").hidden = false; circuit.show();
    }, catalog);
    const timer = await point(4, 8), battery = await point(12, 3), bounds = await page.locator('.cb-canvas').boundingBox();
    for (const p of [timer, battery]) assert.ok(p.x > bounds.x && p.x < bounds.x + bounds.width && p.y > bounds.y && p.y < bounds.y + bounds.height);
    await page.locator('[data-action="run"]').click(); await clickCell(4, 8);
    assert.match(await page.locator('[data-action="run"]').textContent(), /暂停/);
    const ticks = await page.evaluate(() => circuit.getBoard().ticks); await page.waitForFunction(old => circuit.getBoard().ticks > old, ticks);
});

test("无关目录刷新保留运行进度、选择和撤销记录", async () => {
    await page.locator('[data-tool="sim:switch"]').click(); await clickCell(7, 5);
    await page.locator('[data-action="step"]').click();
    await page.evaluate(catalog => {
        const registry = circuit.getBoard().registry;
        const entries = [...registry.values()].filter(def => def.game).map(def => ({ id: def.id, displayName: def.name }));
        const fiberEntry = entries.find(entry => entry.id === "ShipFiber");
        fiberEntry.final = { id: "ShipFiber", modules: { "ship-part": { parameters: { Configuration: { Kind: "Fiber" } } } } };
        circuit.setCatalog(catalog, entries);
    }, catalog);
    assert.equal(await page.evaluate(() => circuit.getBoard().ticks), 1);
    assert.equal(await page.locator('[data-action="undo"]').isEnabled(), true);
    assert.equal(await page.locator('[data-param="on"]').count(), 1);
});

test("真实 ItemWiki 导航、深链接和目录加载", { skip: !process.env.ITEM_WIKI_BASE_URL }, async () => {
    await page.goto(`${process.env.ITEM_WIKI_BASE_URL}#circuit-board`, { waitUntil: "domcontentloaded" });
    await page.locator('#circuitBoardRoot [data-tool="Wire_Copper"]').waitFor({ state: "visible", timeout: 60000 });
    assert.equal(await page.locator('#circuitBoardBook').isVisible(), true);
    assert.equal(await page.locator('#catalogBook').isVisible(), false);
    assert.equal(await page.locator('#circuitBoardViewButton').getAttribute("aria-pressed"), "true");
    assert.equal(await page.locator('#circuitBoardRoot .cb-component.cb-game').count(), catalog.Nodes.filter(n => n.Electrical).length + 1);
    await page.locator('#circuitBoardRoot [data-action="step"]').click();
    assert.match(await page.locator('#circuitBoardRoot [data-role="clock"]').textContent(), /00:00.05/);
    await page.locator('#catalogViewButton').click(); assert.equal(await page.locator('#circuitBoardBook').isVisible(), false);
    await page.locator('#circuitBoardViewButton').click(); assert.equal(await page.locator('#circuitBoardBook').isVisible(), true);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    await page.screenshot({ path: path.join(output, "circuit-board-itemwiki.png"), fullPage: true });
});

test("科技树配置失败不会阻断电路实验室", { skip: !process.env.ITEM_WIKI_BASE_URL }, async () => {
    await page.route("**/tech-tree-eras.json*", route => route.fulfill({ status: 503, contentType: "text/plain", body: "test-only unavailable" }));
    await page.goto(`${process.env.ITEM_WIKI_BASE_URL}#circuit-board`, { waitUntil: "domcontentloaded" });
    await page.locator('#circuitBoardRoot [data-tool="Wire_Copper"]').waitFor({ state: "visible", timeout: 60000 });
    await page.locator('#circuitBoardRoot [data-action="step"]').click();
    assert.match(await page.locator('#circuitBoardRoot [data-role="clock"]').textContent(), /00:00.05/);
    assert.match(await page.locator('#statusText').textContent(), /科技树暂不可用/);
});

test("64 位加法示例在真实棋盘供电，显示第 65 位进位和完整结果", async () => {
    await page.locator('[data-role="example"]').selectOption("adder64");
    await page.locator('[data-adder-role="result"]').waitFor({ state: "visible" });
    assert.equal(await page.evaluate(() => circuit.getBoard().busNetworks.length), 3);
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0x1" + "0".repeat(16));
    assert.match(await page.locator('[data-adder-role="metrics"]').textContent(), /64 \/ 64/);
    assert.match(await page.locator('[data-adder-role="metrics"]').textContent(), /64.*第 65 位.*1/s);
    await page.locator('[data-adder-role="format"]').selectOption("decimal");
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), (1n << 64n).toString(10));
    await page.locator('[data-adder-role="format"]').selectOption("binary");
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0b1" + "0".repeat(64));
    await page.locator('[data-adder-action="last"]').click();
    assert.match(await page.locator('[data-adder-role="bit-table"]').textContent(), /#63/);
    await page.screenshot({ path: path.join(output, "circuit-board-adder64.png"), fullPage: true });
});

test("加法工作台修改 64 位输入、阻断供电与撤销后都正确更新", async () => {
    await page.locator('[data-role="example"]').selectOption("adder64");
    const input = page.locator('[data-adder-operand="a"]');
    await input.fill("0xABCDEF"); await input.press("Tab");
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0xABCDF0");
    await page.locator('[data-adder-action="simple"]').click();
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0xBE0245");
    await page.locator('[data-adder-operand="b"]').fill("0x" + "F".repeat(17));
    await page.locator('[data-adder-operand="b"]').press("Tab");
    assert.match(await page.locator('[data-adder-role="status"]').textContent(), /超过 64 位/);
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0xBE0245");
    await page.evaluate(() => {
        const b = circuit.getBoard(); b.erase(10, 12, "power"); b.refresh();
    });
    await page.locator('[data-action="step"]').click();
    assert.match(await page.locator('[data-adder-role="status"]').textContent(), /供电不足/);
    assert.match(await page.locator('[data-adder-role="result"]').textContent(), /检查 A\/B/);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
});

test("64 位示例导出导入后保留数据总线、输入数值及计算结果", async () => {
    await page.locator('[data-role="example"]').selectOption("adder64");
    const before = await page.evaluate(() => JSON.stringify(circuit.getBoard().serialize()));
    const downloadPromise = page.waitForEvent("download");
    await page.locator('[data-action="export"]').click();
    const exported = JSON.parse(fs.readFileSync(await (await downloadPromise).path(), "utf8"));
    assert.equal(exported.signal.filter(s => s.type === "sim:bus64").length, 18);
    await page.locator('[data-action="clear"]').click();
    await page.locator('[data-role="file"]').setInputFiles({ name: "adder64.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(exported)) });
    await page.waitForFunction(() => circuit.getBoard().busNetworks.length === 3);
    assert.equal(await page.evaluate(() => JSON.stringify(circuit.getBoard().serialize())), before);
    assert.equal(await page.locator('[data-adder-role="result"]').textContent(), "0x1" + "0".repeat(16));
});

test("真实 ItemWiki 的 64 位新示例资源和工作台可完整读取", { skip: !process.env.ITEM_WIKI_BASE_URL }, async () => {
    await page.goto(`${process.env.ITEM_WIKI_BASE_URL}#circuit-board`, { waitUntil: "domcontentloaded" });
    await page.locator('#circuitBoardRoot [data-role="example"]').selectOption("adder64");
    await page.locator('#circuitBoardRoot [data-adder-role="result"]').waitFor({ state: "visible" });
    assert.equal(await page.locator('#circuitBoardRoot [data-adder-role="result"]').textContent(), "0x1" + "0".repeat(16));
    await page.locator('#circuitBoardRoot [data-adder-action="simple"]').click();
    assert.equal(await page.locator('#circuitBoardRoot [data-adder-role="result"]').textContent(), "0xBE0245");
});

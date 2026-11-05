"use strict";
const { test, before, after, beforeEach, afterEach } = require("node:test");
const assert = require("node:assert/strict");
const path = require("node:path");
const { chromium } = require(process.env.ITEM_WIKI_PLAYWRIGHT_MODULE || "playwright-core");
const model = require("../tech-tree-model.js");

//#region 连线与缩放的真实浏览器回归
const eras = Array.from({ length: 10 }, (_, i) => ({ id: `era${i}`, name: `时代${i}`, order: i }));
const stages = eras.map((era, i) => ({ id: `stage${i}`, name: `阶段${i}`, eraId: era.id, order: i }));
const ids = ["A", "B", "C", "D", ...Array.from({ length: 120 }, (_, i) => `Other${i}`)];
const graph = model.build(ids.map(id => ({ kind: "item", displayName: id,
    final: { id, visual: { spriteAddress: id } } })), [
    ["ab", "A", "B"], ["cb", "C", "B"], ["bd", "B", "D"], ["downstream", "D", "Other0"]
].map(([id, input, output]) => ({ id, inputs: [{ itemId: input, match: "exact_item", amount: 1 }],
    outputs: [{ itemId: output, amount: 1 }] })), { eras, stages,
    assignments: stages.map((stage, i) => ({ stageId: stage.id, itemIds: ids.filter((_, index) => index % stages.length === i) })) });
let browser, page;
before(async () => {
    browser = await chromium.launch({ headless: true,
        ...(process.env.ITEM_WIKI_CHROMIUM_PATH ? { executablePath: process.env.ITEM_WIKI_CHROMIUM_PATH } : {}) });
});
after(async () => browser?.close());
beforeEach(async () => {
    page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
    await page.setContent('<div id="root"></div>');
    await page.addStyleTag({ path: path.resolve(__dirname, "../tech-tree.css") });
    await page.addScriptTag({ path: path.resolve(__dirname, "../tech-tree.js") });
    await page.evaluate(graph => {
        window.iconLoads = 0;
        window.tree = ItemTechTree.create({ root: document.querySelector("#root"), loadSprite: async () => {
            window.iconLoads++;
            return { url: 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8" fill="red"/></svg>' };
        } });
        window.tree.setModel(graph);
        window.tree.show();
    }, graph);
});
afterEach(async () => page?.close());

// 真实点击覆盖路线筛选与依赖链切换，防止默认线和无关线重新出现。
test("默认无线，选择仅高亮直接关系，取消依赖链选择恢复全图", async () => {
    assert.equal(await page.locator(".tt-edge").count(), 0);
    await page.evaluate(() => tree.select("B", { center: false }));
    assert.equal(await page.locator(".tt-edge").count(), 3);
    assert.equal(await page.locator(".tt-edge.is-focused").count(), 3);
    const titles = await page.locator(".tt-edge title").allTextContents();
    assert.ok(titles.every(title => /A → B|C → B|B → D/.test(title)));
    await page.locator(".tt-route-focus").first().click();
    assert.equal(await page.locator(".tt-edge").count(), 1);
    await page.locator("#ttChain").check();
    assert.equal(await page.locator(".tt-node").count(), 5);
    assert.equal(await page.locator(".tt-edge").count(), 1);
    await page.getByRole("button", { name: "取消选择", exact: true }).click();
    assert.equal(await page.locator(".tt-node").count(), graph.nodes.length);
    assert.equal(await page.locator(".tt-edge").count(), 0);
    assert.equal(await page.locator("#ttChain").isChecked(), false);
    await page.evaluate(() => tree.select("B"));
    await page.locator("#ttChain").check();
    await page.locator("#ttViewport").focus();
    await page.keyboard.press("Escape");
    assert.equal(await page.locator(".tt-node").count(), graph.nodes.length);
    assert.equal(await page.locator(".tt-edge").count(), 0);
});

test("远景只显名称与固定字号全名，停载图标，回近景和铺满往返正常", async () => {
    await page.evaluate(graph => {
        tree.hide(); tree.setModel(graph); window.iconLoads = 0;
        tree.show(); document.querySelector("#ttFit").click();
    }, graph);
    await page.waitForSelector("#ttSurface.is-overview");
    const loads = await page.evaluate(() => iconLoads);
    assert.equal(loads, 0, "直接进入远景时不加载整图图标");
    await page.locator("#ttZoomOut").click();
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    assert.equal(await page.evaluate(() => iconLoads), loads);
    assert.equal(await page.locator(".tt-node-copy > strong").first().isVisible(), true);
    assert.equal(await page.locator(".tt-node-copy > small").first().isVisible(), false);
    assert.equal(await page.locator(".tt-node-copy > span").first().isVisible(), false);
    assert.equal(await page.locator(".tt-node-icon").first().isVisible(), false);
    await page.locator('.tt-node[data-id="A"]').hover();
    assert.equal(await page.locator("#ttOverviewName").isVisible(), true);
    assert.equal(await page.locator("#ttOverviewName").textContent(), "A");
    assert.equal(await page.locator("#ttOverviewName").evaluate(el => getComputedStyle(el).fontSize), "14px");
    await page.mouse.move(0, 0);
    await page.locator('.tt-node[data-id="C"]').focus();
    assert.equal(await page.locator("#ttOverviewName").textContent(), "C");
    await page.locator('.tt-node[data-id="A"]').hover();
    assert.equal(await page.locator("#ttOverviewName").textContent(), "A");
    await page.mouse.move(0, 0);
    assert.equal(await page.locator("#ttOverviewName").textContent(), "C");
    await page.evaluate(() => tree.select("B", { center: false }));
    await page.locator("#ttZoomOut").click({ clickCount: 20 });
    await page.waitForFunction(() => document.querySelector("#ttZoomValue").textContent === "5%");
    assert.equal(await page.locator("#ttOverviewName").textContent(), "B");
    assert.equal(await page.locator("#ttOverviewName").evaluate(el => el.getBoundingClientRect().height >= 37), true);
    await page.locator("#ttHome").click();
    await page.waitForFunction(() => !document.querySelector("#ttSurface").classList.contains("is-overview") && iconLoads > 0);
    assert.equal(await page.locator(".tt-node-copy").first().isVisible(), true);
    assert.equal(await page.locator("#ttOverviewName").isVisible(), false);
    await page.evaluate(() => tree.select("B"));
    await page.locator("#ttExpand").click();
    assert.equal(await page.locator(".tt-canvas-overlay #ttViewport").count(), 1);
    assert.equal(await page.locator(".tt-edge").count(), 3);
    await page.keyboard.press("Escape");
    assert.equal(await page.locator("#root #ttViewport").count(), 1);
    assert.equal(await page.evaluate(() => tree.getSelectedId()), "B");
    assert.equal(await page.locator(".tt-edge").count(), 3);
});
//#endregion

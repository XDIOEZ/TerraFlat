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

async function getClickableEdgePoint(destination, offset = 4) {
    return page.locator(`.tt-edge-link[href="#tech-tree/${destination}"] .tt-edge-hit`).evaluate((hit, offset) => {
        const length = hit.getTotalLength(), matrix = hit.getScreenCTM();
        for (const distance of [offset, 0]) for (let fraction = .1; fraction < .95; fraction += .025) {
            const point = hit.getPointAtLength(length * fraction).matrixTransform(matrix);
            const next = hit.getPointAtLength(length * fraction + 1).matrixTransform(matrix);
            const dx = next.x - point.x, dy = next.y - point.y, size = Math.hypot(dx, dy);
            const x = point.x - dy / size * distance, y = point.y + dx / size * distance;
            if (document.elementFromPoint(x, y)?.closest(".tt-edge-link")?.dataset.edgeId === hit.parentNode.dataset.edgeId) return { x, y };
        }
        throw new Error("未找到可点击且未被卡片遮挡的线段");
    }, offset);
}

test("连线悬停解释，点击另一端并反向返回，在线上拖动不误跳", async () => {
    await page.evaluate(() => tree.select("B"));
    await page.locator("#ttFit").click();
    await page.locator("#ttExpand").click();
    const point = await getClickableEdgePoint("A");
    await page.mouse.move(point.x, point.y);
    assert.equal(await page.locator("#ttEdgeTooltip").isVisible(), true);
    assert.match(await page.locator("#ttEdgeTooltip").textContent(), /A → B.*材料.*路线：ab.*点击定位：A/s);
    await page.mouse.click(point.x, point.y);
    assert.equal(await page.evaluate(() => tree.getSelectedId()), "A");
    assert.equal(await page.locator("#ttEdgeTooltip").isVisible(), false);
    await page.evaluate(() => new Promise(requestAnimationFrame));
    const back = await getClickableEdgePoint("B");
    await page.mouse.click(back.x, back.y);
    assert.equal(await page.evaluate(() => tree.getSelectedId()), "B");
    await page.evaluate(() => new Promise(requestAnimationFrame));
    const drag = await getClickableEdgePoint("A");
    await page.mouse.move(drag.x, drag.y);
    await page.mouse.down();
    await page.mouse.move(drag.x - 40, drag.y + 25, { steps: 5 });
    await page.mouse.up();
    assert.equal(await page.evaluate(() => tree.getSelectedId()), "B");
    await page.waitForTimeout(220);
    await page.locator('.tt-edge-link[href="#tech-tree/A"]:not([aria-hidden])').focus();
    assert.equal(await page.locator("#ttEdgeTooltip").isVisible(), true);
    await page.keyboard.press("Enter");
    assert.equal(await page.evaluate(() => tree.getSelectedId()), "A");
    const selected = await page.locator('.tt-node[data-id="A"]').boundingBox();
    const viewport = await page.locator("#ttViewport").boundingBox();
    assert.ok(selected.x + selected.width / 2 >= viewport.x && selected.x + selected.width / 2 <= viewport.x + viewport.width);
    assert.ok(selected.y + selected.height / 2 >= viewport.y && selected.y + selected.height / 2 <= viewport.y + viewport.height);
});

test("连线说明保留工具不消耗、候选与随身手工条件，清选后关闭", async () => {
    const smallGraph = model.build(["A", "B", "C", "D"].map(id => ({ kind: "item", displayName: id,
        final: { id, ...(id === "C" ? { tags: ["Needle"] } : {}) } })), [{ id: "sew", displayName: "缝制",
        requiredStation: "D", minTemperature: 100,
        inputs: [{ itemId: "A", match: "exact_item", amount: 2 }, { tag: "Needle", match: "tag", amount: 0, minTemperature: 200 }],
        outputs: [{ itemId: "B", amount: 1 }] }], { eras, stages,
        assignments: [{ stageId: "stage0", itemIds: ["A", "B", "C", "D"] }],
        stationAliases: [{ station: "D", portable: true, label: "工作台或随身手工" }] });
    await page.evaluate(graph => { tree.setModel(graph); tree.select("B"); }, smallGraph);
    await page.locator('.tt-edge-link[href="#tech-tree/C"]:not([aria-hidden])').focus();
    assert.match(await page.locator("#ttEdgeTooltip").textContent(), /工具条件.*不消耗.*候选之一.*缝制.*100.*200/s);
    await page.locator('.tt-edge-link[href="#tech-tree/D"]:not([aria-hidden])').focus();
    assert.match(await page.locator("#ttEdgeTooltip").textContent(), /设备条件.*随身手工.*无须专门建造/s);
    await page.evaluate(() => tree.select(""));
    assert.equal(await page.locator("#ttEdgeTooltip").isVisible(), false);
});

// 真实点击覆盖路线筛选与依赖链切换，防止默认线和无关线重新出现。
test("默认无线，选择仅高亮直接关系，取消依赖链选择恢复全图", async () => {
    assert.equal(await page.locator(".tt-edge").count(), 0);
    await page.evaluate(() => tree.select("B", { center: false }));
    assert.equal(await page.locator(".tt-edge").count(), 3);
    assert.equal(await page.locator(".tt-edge.is-focused").count(), 3);
    const titles = await page.locator(".tt-edge-link > title").allTextContents();
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
// 群系组合只生成一个物品节点，搜索和详情仍能看到额外来源。
test("资源群系分块、跨群系唯一卡片与来源搜索详情", async () => {
    const ids = ["Palm", "Cactus", "Cotton", "Mushroom"];
    const resources = model.build(ids.map(id => ({ kind: "item", displayName: id, final: { id } })), [], {
        eras: [{ id: "early", name: "早期", order: 0 }], stages: [{ id: "tools", eraId: "early", order: 0 }],
        assignments: [{ stageId: "tools", itemIds: ids }]
    }, { naturalItems: [{ ecologyRules: [
        { ruleId: "surface.palm", itemId: "Palm", spawnChance: 1, biomeMask: 4 },
        { ruleId: "surface.cactus", itemId: "Cactus", spawnChance: 1, biomeMask: 8 },
        { ruleId: "surface.cotton", itemId: "Cotton", spawnChance: 1, biomeMask: 48 },
        { ruleId: "cave.mushroom", itemId: "Mushroom", spawnChance: 1, biomeMask: 0 }
    ] }] });
    await page.evaluate(resources => tree.setModel(resources), resources);
    assert.equal(await page.locator(".tt-resource-group").count(), 4);
    assert.equal(await page.locator(".tt-node").count(), 4);
    await page.locator("#ttSearch").fill("森林");
    await page.waitForFunction(() => document.querySelectorAll(".tt-node").length === 1);
    assert.equal(await page.locator(".tt-node").getAttribute("data-id"), "Cotton");
    assert.match(await page.locator(".tt-resource-group").textContent(), /草原.*森林/);
    await page.evaluate(() => tree.select("Cotton"));
    assert.match(await page.locator("#ttDetail").textContent(), /已知来源环境.*草原.*森林/);
    assert.equal(await page.locator('.tt-node[data-id="Cotton"]').count(), 1);
});
//#endregion

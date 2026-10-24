(() => {
    "use strict";

    // #region 图谱展示基础
    const NODE_WIDTH = 210;
    const NODE_HEIGHT = 70;
    const TRACK_WIDTH = 256;
    const ROW_HEIGHT = 88;
    const ROWS_PER_TRACK = 13;
    const SVG_NS = "http://www.w3.org/2000/svg";
    const KIND_NAMES = { ingredient: "材料", tool: "工具条件", station: "设备条件", processing: "加工", placement: "放置", transition: "状态转化", loot: "采集与掉落", harvest: "采收", crafting: "制作", smelting: "冶炼", reaction: "反应", mechanical: "机械加工", production: "生产" };
    const CONDITION_NAMES = {
        duration: "耗时", durationSeconds: "耗时（秒）", timeSeconds: "耗时（秒）", workSeconds: "工作时间（秒）",
        minLevel: "最低等级", maxLevel: "最高等级", capability: "能力", channel: "通道", station: "工位",
        minTemperature: "最低温度", maxTemperature: "最高温度", minTemperatureC: "最低温度（℃）",
        maxTemperatureC: "最高温度（℃）", temperature: "温度", temperatureC: "温度（℃）", probability: "概率",
        chance: "概率", requiredStation: "工位", requiredLiquid: "液体", requiredLiquidAmount: "液体数量",
        energy: "能量", energyCost: "能量消耗", power: "功率", work: "工作量", batchSize: "每批数量",
        min: "最少", max: "最多", mode: "方式", context: "上下文", action: "操作", actions: "加工动作", processId: "加工 ID",
        inputRule: "输入规则", intermediateLiquid: "中间液体", unresolvedLiquid: "液体产出", externalSource: "外部来源",
        minMoisture: "最低含水率", maxMoisture: "最高含水率", conditions: "附加条件",
        durabilityMultiplier: "耐久倍率", addTag: "加固标记", amount: "数量"
    };
    const keyOf = value => String(value || "").toLowerCase();
    const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
    const colorOf = value => /^#[0-9a-f]{3,8}$/i.test(value || "") ? value : "#b39775";
    const element = (tag, className, text) => {
        const result = document.createElement(tag);
        if (className) result.className = className;
        if (text !== undefined) result.textContent = text;
        return result;
    };
    const valueText = value => typeof value === "object" ? JSON.stringify(value) : String(value);

    // 技术按时代排列，自然资源在中下方按下游用途排序，缩短跨时代连线。
    function layout(nodes, stages, eras, includeEmpty, edges = []) {
        const positions = new Map(), stageBoxes = [], eraBoxes = [];
        const byStage = new Map(), natural = nodes.filter(node => node.naturalResource);
        for (const node of nodes) {
            if (node.naturalResource) continue;
            if (!byStage.has(node.stageId)) byStage.set(node.stageId, []);
            byStage.get(node.stageId).push(node);
        }
        let x = 24, maxRows = 1;
        for (const era of eras) {
            const eraStart = x;
            const eraStages = stages.filter(stage => stage.eraId === era.id);
            let eraCount = 0;
            for (const stage of eraStages) {
                const members = (byStage.get(stage.id) || []).slice().sort((a, b) =>
                    (a.depth || 0) - (b.depth || 0) || String(a.entry?.category || "").localeCompare(String(b.entry?.category || ""), "zh-CN") || a.name.localeCompare(b.name, "zh-CN"));
                if (!members.length && (!includeEmpty || stage.layoutBand === "natural")) continue;
                const tracks = Math.max(1, Math.ceil(members.length / ROWS_PER_TRACK));
                const rows = Math.max(1, Math.ceil(members.length / tracks));
                maxRows = Math.max(maxRows, rows);
                const width = tracks * TRACK_WIDTH;
                stageBoxes.push({ stage, era, x, width, count: members.length });
                members.forEach((node, index) => {
                    const track = Math.floor(index / rows), row = index % rows;
                    positions.set(node.id, { x: x + 20 + track * TRACK_WIDTH, y: 144 + row * ROW_HEIGHT, node });
                });
                x += width + 12;
                eraCount += members.length;
            }
            if (x !== eraStart) {
                eraBoxes.push({ era, x: eraStart, width: x - eraStart - 12, count: eraCount });
                x += 52;
            }
        }
        const width = Math.max(720, x), mainHeight = Math.max(480, 176 + maxRows * ROW_HEIGHT);
        let resourceBox = null;
        if (natural.length) {
            const outgoing = new Map(), naturalIds = new Set(natural.map(node => node.id));
            for (const edge of edges) {
                if (["tool", "station"].includes(edge.kind)) continue;
                if (!outgoing.has(edge.source)) outgoing.set(edge.source, new Set());
                outgoing.get(edge.source).add(edge.target);
            }
            const preferredX = node => {
                const consumers = new Set(), visited = new Set([node.id]), queue = [node.id];
                for (let index = 0; index < queue.length; index++) {
                    for (const id of outgoing.get(queue[index]) || []) {
                        if (visited.has(id)) continue;
                        visited.add(id);
                        if (naturalIds.has(id)) queue.push(id);
                        else if (positions.has(id)) consumers.add(positions.get(id).x + NODE_WIDTH / 2);
                    }
                }
                const values = [...consumers].sort((a, b) => a - b);
                return values.length ? values[Math.floor(values.length / 2)] : width / 2;
            };
            const ordered = natural.map(node => ({ node, preferred: preferredX(node) }))
                .sort((a, b) => a.preferred - b.preferred || a.node.name.localeCompare(b.node.name, "zh-CN"));
            const columns = Math.min(natural.length, Math.max(1, Math.min(Math.floor(width * .5 / TRACK_WIDTH), Math.ceil(Math.sqrt(natural.length * 1.5)))));
            const rows = Math.ceil(natural.length / columns), bandWidth = columns * TRACK_WIDTH + 40;
            resourceBox = { x: (width - bandWidth) / 2, y: mainHeight + 64, width: bandWidth, height: 112 + rows * ROW_HEIGHT, count: natural.length };
            const occupied = Array(columns).fill(0);
            for (const { node, preferred } of ordered) {
                const ideal = Math.max(0, Math.min(columns - 1, Math.round((preferred - resourceBox.x - 20 - NODE_WIDTH / 2) / TRACK_WIDTH)));
                let column = -1;
                for (let candidate = 0; candidate < columns; candidate++) {
                    if (occupied[candidate] >= rows) continue;
                    if (column < 0 || Math.abs(candidate - ideal) < Math.abs(column - ideal)) column = candidate;
                }
                positions.set(node.id, { x: resourceBox.x + 20 + column * TRACK_WIDTH,
                    y: resourceBox.y + 84 + occupied[column]++ * ROW_HEIGHT, node, natural: true });
            }
        }
        return { positions, stages: stageBoxes, eras: eraBoxes, width, mainHeight, resourceBox,
            height: resourceBox ? resourceBox.y + resourceBox.height + 24 : mainHeight };
    }
    // #endregion

    function create({ root, loadSprite, openItem, onSelect, clearGlobalSearch }) {
        // #region 页面结构与状态
        root.innerHTML = `
            <header class="tt-heading">
                <div><div class="tt-kicker">FLATWORLD · MATERIALS TO MACHINES</div><h2>物品科技树 <span>从左往右，走过每个时代</span></h2>
                <p>拆到每种原料、中间产物、工具与设备。点一个物品，查看真实配方、加工条件和后续用途。</p></div>
                <div class="tt-counts" id="ttCounts" aria-live="polite"></div>
            </header>
            <nav class="tt-era-nav" id="ttEraNav" aria-label="跳转到时代"></nav>
            <div class="tt-toolbar">
                <label class="tt-search"><span>找物品</span><input id="ttSearch" type="search" autocomplete="off" placeholder="名称、ID、材料…"></label>
                <label><span>时代</span><select id="ttEra"><option value="">全部时代</option></select></label>
                <label><span>类别</span><select id="ttCategory"><option value="">全部类别</option></select></label>
                <label><span>连线</span><select id="ttEdgeMode"><option value="trunk">主干＋所选依赖</option><option value="all">全部真实关系</option><option value="focus">仅所选关系</option></select></label>
                <label class="tt-chain-toggle"><input id="ttChain" type="checkbox"><span>只看所选依赖链</span></label>
                <button id="ttResetFilters" type="button">清空筛选</button>
            </div>
            <div class="tt-results" id="ttResults" hidden></div>
            <div class="tt-workspace">
                <div class="tt-map-column">
                    <div class="tt-map-actions" aria-label="科技树画布控制">
                        <div class="tt-map-controls"><button id="ttZoomOut" type="button" aria-label="缩小科技树">−</button><button id="ttZoomValue" type="button" title="恢复 100%">85%</button><button id="ttZoomIn" type="button" aria-label="放大科技树">＋</button>
                        <button id="ttHome" type="button">回到起点</button><button id="ttFit" type="button">全图概览</button><button id="ttLocate" type="button" disabled>定位所选</button><button id="ttExpand" type="button" aria-haspopup="dialog">铺满网页</button></div>
                        <span class="tt-direction">早期 <span>────────→</span> 后期</span>
                    </div>
                    <div id="ttViewport" class="tt-viewport" tabindex="0" role="region" aria-label="可拖拽与缩放的科技树；方向键平移，加减键缩放，Home 回到起点">
                        <div id="ttSurface" class="tt-surface"><div id="ttRegions" class="tt-regions" aria-hidden="true"></div><svg id="ttEdges" class="tt-edges" aria-hidden="true"></svg><div id="ttNodes" class="tt-nodes"></div></div>
                        <div id="ttEmpty" class="tt-empty" hidden></div>
                    </div>
                    <div class="tt-panbar"><span>横向浏览</span><input id="ttPan" type="range" min="0" max="1000" value="0" aria-label="横向移动科技树"><span id="ttPanEra"></span></div>
                    <div class="tt-map-status"><span id="ttVisible" role="status"></span><span>拖拽平移 · 滚轮缩放 · Shift＋滚轮横移</span></div>
                    <div class="tt-legend"><span><i class="tt-line material"></i>材料 / 加工</span><span><i class="tt-line requirement"></i>工具 / 工位</span><span><i class="tt-line optional"></i>候选之一（OR）</span><span><i class="tt-line feedback"></i>状态 / 回收</span></div>
                    <p class="tt-edge-note" id="ttEdgeNote"></p>
                </div>
                <aside class="tt-detail" id="ttDetail" aria-label="所选物品的完整依赖与配方"></aside>
            </div>
            <details class="tt-notes"><summary>分期规则与数据说明 <span id="ttWarningCount"></span></summary><div id="ttNotesBody"></div></details>`;
        const get = id => root.querySelector(`#${id}`);
        const els = Object.fromEntries(["Counts", "EraNav", "Search", "Era", "Category", "EdgeMode", "Chain", "ResetFilters", "Results", "ZoomOut", "ZoomValue", "ZoomIn", "Home", "Fit", "Locate", "Expand", "Viewport", "Surface", "Regions", "Edges", "Nodes", "Empty", "Pan", "PanEra", "Visible", "EdgeNote", "Detail", "WarningCount", "NotesBody"].map(name => [name, get(`tt${name}`)]));
        const state = {
            model: null, nodes: new Map(), routes: new Map(), incoming: new Map(), outgoing: new Map(),
            selected: "", route: "", query: "", globalQuery: "", visible: [], shown: false, layout: null,
            zoom: .85, x: 20, y: 12, initialized: false, generation: 0, observer: null, frame: 0,
            drag: null, suppressClickUntil: 0, pointers: new Map(), pinch: null, searchTimer: 0
        };
        // #endregion

        // #region 图谱数据与筛选
        function setModel(model) {
            state.model = model;
            state.nodes = new Map(model.nodes.map(node => [keyOf(node.id), node]));
            state.routes = new Map((model.routes || []).map(route => [route.id, route]));
            state.incoming = new Map(); state.outgoing = new Map();
            model.edges.forEach(edge => {
                if (!state.incoming.has(edge.target)) state.incoming.set(edge.target, []);
                if (!state.outgoing.has(edge.source)) state.outgoing.set(edge.source, []);
                state.incoming.get(edge.target).push(edge);
                state.outgoing.get(edge.source).push(edge);
            });
            if (!state.nodes.has(keyOf(state.selected))) state.selected = "";
            state.route = "";
            updateOptions(els.Era, model.eras.map(era => [era.id, era.name]), "全部时代");
            updateOptions(els.Category, [...new Set(model.nodes.map(node => node.entry?.category || "其他"))].sort().map(category => [category, category]), "全部类别");
            els.Counts.innerHTML = `<span><strong>${model.nodes.length}</strong>物品</span><span><strong>${model.stages.length}</strong>细阶段</span><span><strong>${model.edges.length}</strong>真实关系</span>`;
            renderEraNav(); renderNotes(); render(); renderDetail();
        }

        function updateOptions(select, values, caption) {
            const old = select.value;
            select.replaceChildren(new Option(caption, ""), ...values.map(([id, name]) => new Option(name, id)));
            if (values.some(([id]) => id === old)) select.value = old;
        }

        function renderEraNav() {
            els.EraNav.replaceChildren();
            state.model.eras.forEach((era, index) => {
                const button = element("button", "tt-era-button");
                button.type = "button";
                button.style.setProperty("--era-color", colorOf(era.color));
                const count = state.model.nodes.filter(node => node.eraId === era.id).length;
                const stages = state.model.stages.filter(stage => stage.eraId === era.id).length;
                button.innerHTML = `<small>${String(index + 1).padStart(2, "0")} / ${stages} 阶段</small><strong>${escapeHtml(era.name)}</strong><span>${count} 个物品 <b>→</b></span>`;
                button.dataset.era = era.id;
                button.addEventListener("click", () => {
                    if (els.Era.value && els.Era.value !== era.id) { els.Era.value = ""; render(); }
                    const box = state.layout?.eras.find(value => value.era.id === era.id);
                    if (!box) { els.Visible.textContent = `当前筛选下，${era.name}没有物品。可清空筛选后浏览。`; return; }
                    state.zoom = Math.max(.72, state.zoom);
                    state.x = 22 - box.x * state.zoom; state.y = 12;
                    updateTransform();
                });
                els.EraNav.append(button);
            });
            const resourceCount = state.model.nodes.filter(node => node.naturalResource).length;
            if (resourceCount) {
                const button = element("button", "tt-era-button tt-resource-nav");
                button.type = "button";
                button.style.setProperty("--era-color", "#8db999");
                button.innerHTML = `<small>跨时代 · 下方资源区</small><strong>自然资源</strong><span>${resourceCount} 个物品 <b>↓</b></span>`;
                button.addEventListener("click", () => {
                    const box = state.layout?.resourceBox;
                    if (!box) { els.Visible.textContent = "当前筛选下没有自然资源，可清空筛选后浏览。"; return; }
                    state.zoom = Math.max(.72, state.zoom);
                    state.x = els.Viewport.clientWidth / 2 - (box.x + box.width / 2) * state.zoom;
                    state.y = 12 - box.y * state.zoom;
                    updateTransform();
                });
                els.EraNav.append(button);
            }
        }

        function filterNodes() {
            const words = `${state.query} ${state.globalQuery}`.trim().toLowerCase().split(/\s+/).filter(Boolean);
            const chain = els.Chain.checked && state.selected ? traceChain(state.selected) : null;
            return state.model.nodes.filter(node => {
                if (els.Era.value && node.eraId !== els.Era.value) return false;
                if (els.Category.value && (node.entry?.category || "其他") !== els.Category.value) return false;
                if (chain && !chain.has(node.id)) return false;
                const search = `${node.name} ${node.id} ${node.entry?.searchBlob || ""}`.toLowerCase();
                return words.every(word => search.includes(word));
            });
        }

        // 分别追溯祖先和后代，避免从共同原料扩散到无关支线。
        function traceChain(id) {
            const all = new Set([id]);
            for (const direction of ["up", "down"]) {
                const visited = new Set([id]), queue = [id];
                for (let index = 0; index < queue.length; index++) {
                    const edges = (direction === "up" ? state.incoming : state.outgoing).get(queue[index]) || [];
                    for (const edge of edges) {
                        const next = direction === "up" ? edge.source : edge.target;
                        if (visited.has(next)) continue;
                        visited.add(next); all.add(next); queue.push(next);
                    }
                }
            }
            return all;
        }

        function clearFilters() {
            state.query = ""; state.globalQuery = ""; els.Search.value = "";
            clearGlobalSearch?.();
            els.Era.value = ""; els.Category.value = ""; els.Chain.checked = false;
            render(); renderDetail(); home();
        }

        function setSearch(query) {
            state.globalQuery = String(query || "");
            if (state.model) { render(); renderDetail(); if (state.shown) fit(); }
        }

        function setError(message) {
            state.model = null;
            state.layout = null;
            state.observer?.disconnect();
            els.Empty.hidden = false;
            els.Empty.textContent = `科技树读取失败：${message}。点击页面上方“重新读取 JSON”重试。`;
            els.Nodes.replaceChildren(); els.Regions.replaceChildren(); els.Edges.replaceChildren();
            els.Counts.textContent = "数据暂不可用";
            els.Detail.replaceChildren(); els.EraNav.replaceChildren(); els.NotesBody.replaceChildren(); els.Results.hidden = true;
            els.WarningCount.textContent = ""; els.EdgeNote.textContent = ""; els.Visible.textContent = "";
        }
        // #endregion

        // #region 节点、阶段与真实连线
        function render() {
            if (!state.model) return;
            state.generation++;
            state.observer?.disconnect();
            state.visible = filterNodes();
            const filtered = !!(els.Era.value || els.Category.value || state.query || state.globalQuery || els.Chain.checked);
            state.layout = layout(state.visible, state.model.stages, state.model.eras, !filtered, state.model.edges);
            els.Surface.style.width = `${state.layout.width}px`;
            els.Surface.style.height = `${state.layout.height}px`;
            els.Edges.setAttribute("width", String(state.layout.width));
            els.Edges.setAttribute("height", String(state.layout.height));
            renderRegions();
            const fragment = document.createDocumentFragment();
            for (const { node, x, y } of state.layout.positions.values()) {
                const button = element("button", "tt-node");
                button.type = "button"; button.dataset.id = node.id;
                button.style.left = `${x}px`; button.style.top = `${y}px`;
                button.style.setProperty("--era-color", colorOf(state.model.eras.find(era => era.id === node.eraId)?.color));
                const routeCount = node.recipes?.length || 0;
                button.innerHTML = `<span class="tt-node-icon" aria-hidden="true">◇</span><span class="tt-node-copy"><strong>${escapeHtml(node.name)}</strong><small>${escapeHtml(node.id)}</small><span>${node.naturalResource ? "自然资源" : escapeHtml(node.entry?.category || "其他")} <b>${node.special ? escapeHtml(node.specialLabel || "特殊") : routeCount ? `${routeCount} 条来源` : "查看来源"}</b></span></span>`;
                button.title = `${node.name}\n${node.id}\n${node.naturalResource ? "自然资源 · 跨时代共用\n" : ""}${node.classification?.reason || ""}`;
                button.addEventListener("click", () => {
                    if (performance.now() < state.suppressClickUntil) return;
                    select(node.id, { center: false });
                });
                button.addEventListener("focus", () => ensureInView(node.id));
                fragment.append(button);
            }
            els.Nodes.replaceChildren(fragment);
            els.Empty.hidden = !!state.visible.length;
            els.Empty.textContent = "没有符合条件的物品。清空筛选，或换一个名称 / ID。";
            renderResults(); updateSelection(); renderEdges();
            els.Visible.textContent = `${state.visible.length} / ${state.model.nodes.length} 个物品 · ${state.layout.stages.length} 个技术阶段${state.layout.resourceBox ? ` · ${state.layout.resourceBox.count} 种自然资源` : ""}${els.Chain.checked ? " · 所选物品上下游" : ""}`;
            if (state.shown) updateTransform();
            observeIcons();
        }

        function renderRegions() {
            const fragment = document.createDocumentFragment();
            for (const box of state.layout.eras) {
                const region = element("div", "tt-era-region");
                region.style.cssText = `left:${box.x}px;width:${box.width}px;height:${state.layout.mainHeight - 24}px;--era-color:${colorOf(box.era.color)}`;
                region.innerHTML = `<strong class="tt-era-title">${escapeHtml(box.era.name)} <span>${box.count} 个物品</span></strong>`;
                fragment.append(region);
            }
            for (const box of state.layout.stages) {
                const stage = element("div", "tt-stage-region");
                stage.style.cssText = `left:${box.x}px;top:62px;width:${box.width}px;height:${state.layout.mainHeight - 100}px`;
                stage.innerHTML = `<div class="tt-stage-heading"><small>${String((box.stage.index ?? box.stage.order ?? 0) + 1).padStart(2, "0")}</small><strong>${escapeHtml(box.stage.name)}</strong><span>${box.count}</span></div><p>${escapeHtml(box.stage.description || "")}</p>${box.count ? "" : '<div class="tt-stage-empty">当前暂无物品</div>'}`;
                fragment.append(stage);
            }
            const resources = state.layout.resourceBox;
            if (resources) {
                const band = element("div", "tt-resource-region");
                band.style.cssText = `left:${resources.x}px;top:${resources.y}px;width:${resources.width}px;height:${resources.height}px`;
                band.innerHTML = `<strong class="tt-era-title">自然资源 <span>${resources.count} 个物品 · 跨时代共用</span></strong><p>自然采集的资源按用途靠近相关技术排列，不代表获取时代。</p>`;
                fragment.append(band);
            }
            els.Regions.replaceChildren(fragment);
        }

        function renderResults() {
            const searching = !!(state.query || state.globalQuery);
            els.Results.hidden = !searching;
            els.Results.replaceChildren();
            if (!searching) return;
            els.Results.append(element("span", "tt-result-caption", `找到 ${state.visible.length} 个，点击定位：`));
            state.visible.slice(0, 24).forEach(node => els.Results.append(itemButton(node.id, node.name)));
            if (state.visible.length > 24) els.Results.append(element("span", "tt-result-caption", `另有 ${state.visible.length - 24} 个，可在图中继续浏览或缩小搜索范围。`));
        }

        function relatedEdges() {
            const edges = [...(state.incoming.get(state.selected) || []), ...(state.outgoing.get(state.selected) || [])];
            return state.route ? edges.filter(edge => edge.routeId === state.route) : edges;
        }

        function updateSelection() {
            const related = new Set(relatedEdges().flatMap(edge => [edge.source, edge.target]));
            for (const button of els.Nodes.children) {
                const selected = button.dataset.id === state.selected;
                button.classList.toggle("is-selected", selected);
                button.classList.toggle("is-related", !selected && related.has(button.dataset.id));
                button.classList.toggle("is-muted", !!state.selected && !selected && !related.has(button.dataset.id));
                button.setAttribute("aria-pressed", String(selected));
            }
            els.Locate.disabled = !state.selected;
            els.Chain.disabled = !state.selected;
        }

        function renderEdges() {
            if (!state.layout) return;
            const positions = state.layout.positions;
            const available = state.model.edges.filter(edge => edge.source !== edge.target && positions.has(edge.source) && positions.has(edge.target));
            const focused = new Set(relatedEdges().map(edge => edge.id));
            let edges = [];
            if (els.EdgeMode.value === "all" || els.Chain.checked) edges = available;
            else if (els.EdgeMode.value === "trunk") {
                const trunks = new Map();
                for (const edge of available) {
                    if (edge.feedback) continue;
                    const source = state.nodes.get(keyOf(edge.source)), target = state.nodes.get(keyOf(edge.target));
                    if (!source || !target || source.stageIndex > target.stageIndex) continue;
                    const score = (edge.optional ? 10000 : 0) + (["tool", "station"].includes(edge.kind) ? 5000 : 0) + Math.abs(target.stageIndex - source.stageIndex) * 100 + Math.abs((target.depth || 0) - (source.depth || 0));
                    if (!trunks.has(edge.target) || trunks.get(edge.target).score > score) trunks.set(edge.target, { edge, score });
                }
                const selected = new Map([...trunks.values()].map(({ edge }) => [edge.id, edge]));
                available.filter(edge => focused.has(edge.id)).forEach(edge => selected.set(edge.id, edge));
                edges = [...selected.values()];
            } else edges = available.filter(edge => focused.has(edge.id));
            edges.sort((a, b) => Number(focused.has(a.id)) - Number(focused.has(b.id)));
            const fragment = document.createDocumentFragment();
            const defs = document.createElementNS(SVG_NS, "defs");
            for (const [name, color] of [["material", "#9fbb9a"], ["requirement", "#8caed2"], ["optional", "#ddb878"], ["feedback", "#bf93b1"]]) {
                const marker = document.createElementNS(SVG_NS, "marker");
                marker.id = `ttArrow-${name}`;
                marker.setAttribute("viewBox", "0 0 10 10"); marker.setAttribute("refX", "9"); marker.setAttribute("refY", "5");
                marker.setAttribute("markerWidth", "5"); marker.setAttribute("markerHeight", "5"); marker.setAttribute("orient", "auto-start-reverse");
                const head = document.createElementNS(SVG_NS, "path"); head.setAttribute("d", "M0 1 L9 5 L0 9 z"); head.setAttribute("fill", color);
                marker.append(head); defs.append(marker);
            }
            fragment.append(defs);
            for (const edge of edges) {
                const source = positions.get(edge.source), target = positions.get(edge.target);
                const x1 = source.x + NODE_WIDTH, y1 = source.y + NODE_HEIGHT / 2, x2 = target.x, y2 = target.y + NODE_HEIGHT / 2;
                const forward = x2 > x1;
                const bend = forward ? Math.min(260, Math.max(30, (x2 - x1) * .45)) : 70;
                const path = document.createElementNS(SVG_NS, "path");
                const kind = edge.optional ? "optional" : edge.feedback || edge.kind === "transition" ? "feedback" : ["tool", "station"].includes(edge.kind) ? "requirement" : "material";
                path.setAttribute("class", `tt-edge ${kind}${focused.has(edge.id) ? " is-focused" : state.selected ? " is-muted" : ""}`);
                path.setAttribute("d", forward ? `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}` : `M${x1},${y1} C${x1 + bend},${y1} ${x1 + bend},${Math.min(y1, y2) - 55} ${x1},${Math.min(y1, y2) - 55} L${x2 - bend},${Math.min(y1, y2) - 55} C${x2 - bend},${Math.min(y1, y2) - 55} ${x2 - bend},${y2} ${x2},${y2}`);
                // 资源区与技术主线之间从上下边缘连接，避免绕回最左边。
                if (Boolean(source.natural) !== Boolean(target.natural)) {
                    const sx = source.x + NODE_WIDTH / 2, tx = target.x + NODE_WIDTH / 2;
                    const sy = source.natural ? source.y : source.y + NODE_HEIGHT;
                    const ty = target.natural ? target.y : target.y + NODE_HEIGHT;
                    const curve = Math.max(40, Math.abs(ty - sy) * .45);
                    const direction = target.natural ? 1 : -1;
                    path.setAttribute("d", `M${sx},${sy} C${sx},${sy + direction * curve} ${tx},${ty - direction * curve} ${tx},${ty}`);
                }
                path.setAttribute("marker-end", `url(#ttArrow-${kind})`);
                const title = document.createElementNS(SVG_NS, "title");
                title.textContent = `${edge.source} → ${edge.target} · ${edge.label || KIND_NAMES[edge.kind] || edge.kind}${edge.optional ? "（可替换候选）" : ""}`;
                path.append(title); fragment.append(path);
            }
            els.Edges.replaceChildren(fragment);
            els.EdgeNote.textContent = els.Chain.checked ? `依赖链已展开，当前绘制 ${edges.length} 条关系。替代路线和循环关系仍分别保留。`
                : els.EdgeMode.value === "trunk" ? `主干模式：每个物品保留一条真实前置关系，点选后补全它的全部相邻关系；当前 ${edges.length} 条。`
                : els.EdgeMode.value === "focus" ? `仅显示所选物品的直接关系，共 ${edges.length} 条。`
                : `显示当前物品之间的全部关系，共 ${edges.length} 条。连线密集时可切回主干模式。`;
        }

        function observeIcons() {
            const generation = state.generation;
            const fill = async button => {
                const node = state.nodes.get(keyOf(button.dataset.id));
                if (!node) return;
                const host = button.querySelector(".tt-node-icon");
                await fillIcon(host, node, generation);
            };
            if (typeof IntersectionObserver === "function") {
                state.observer = new IntersectionObserver(entries => {
                    entries.filter(entry => entry.isIntersecting).forEach(entry => { state.observer.unobserve(entry.target); fill(entry.target); });
                }, { root: els.Viewport, rootMargin: "160px" });
                for (const button of els.Nodes.children) state.observer.observe(button);
            } else if (state.shown) for (const button of els.Nodes.children) fill(button);
        }

        async function fillIcon(host, node, generation) {
            const address = node.entry?.final?.visual?.spriteAddress;
            if (!address || !host) return;
            try {
                const sprite = await loadSprite(address);
                if (!host.isConnected || generation !== state.generation || !sprite?.url) return;
                const image = element("img"); image.alt = ""; image.draggable = false; image.src = sprite.url;
                host.replaceChildren(image);
            } catch { host.title = "当前图标不可用"; }
        }
        // #endregion

        // #region 物品来源、替代组与下游详情
        function select(id, options = {}) {
            if (!state.model) return;
            const node = state.nodes.get(keyOf(id));
            state.selected = node?.id || ""; state.route = "";
            if (node && options.reveal !== false && !state.layout.positions.has(node.id)) {
                state.query = ""; state.globalQuery = ""; els.Search.value = ""; els.Era.value = ""; els.Category.value = "";
                clearGlobalSearch?.();
                render();
            } else if (els.Chain.checked) render();
            else { updateSelection(); renderEdges(); }
            renderDetail();
            if (options.notify !== false) onSelect?.(state.selected);
            if (node && options.center !== false && state.shown) centerOn(node.id);
        }

        function itemButton(id, label, suffix = "") {
            const node = state.nodes.get(keyOf(id));
            const button = element("button", "tt-item-link", `${label || node?.name || id}${suffix}`);
            button.type = "button"; button.title = node ? `${node.id} · 点击定位` : String(id);
            button.disabled = !node;
            button.addEventListener("click", () => select(id, { center: true }));
            return button;
        }

        function renderDetail() {
            if (!state.model) return;
            const node = state.nodes.get(keyOf(state.selected));
            els.Detail.replaceChildren();
            if (!node) {
                els.Detail.innerHTML = `<div class="tt-detail-kicker">沿着物品看进度</div><h3>点一个物品，<br>追它的来路。</h3><p>先从上方选择时代，或搜索一个名字。左边是早期，右边是后期。</p><div class="tt-help-step"><b>01</b><span>每张卡片就是一种真实物品，细阶段里还会逐个展开。</span></div><div class="tt-help-step"><b>02</b><span>点选高亮直接上下游；勾选“只看所选依赖链”追溯全部材料和后续用途。</span></div><div class="tt-help-step"><b>03</b><span>同一配方的各组材料一起需要；不同制作路线任选其一，虚线候选按组任选。</span></div><p class="tt-detail-footnote">时代是百科整理规则，可以后续调整。它不代表游戏中已经存在科技解锁限制。</p>`;
                return;
            }
            const era = state.model.eras.find(value => value.id === node.eraId);
            const stage = state.model.stages.find(value => value.id === node.stageId);
            const hero = element("div", "tt-detail-hero");
            hero.innerHTML = `<span class="tt-detail-icon">◇</span><div><small>${escapeHtml(era?.name || "未分类")} / ${escapeHtml(stage?.name || "待分期")}</small><h3>${escapeHtml(node.name)}</h3><code>${escapeHtml(node.id)}</code></div>`;
            els.Detail.append(hero); fillIcon(hero.querySelector(".tt-detail-icon"), node, state.generation);
            const actions = element("div", "tt-detail-actions");
            const archive = element("button", "tt-primary", "打开物品档案 ↗"); archive.type = "button"; archive.addEventListener("click", () => openItem(node.id));
            const clear = element("button", "", "取消选择"); clear.type = "button"; clear.addEventListener("click", () => { els.Chain.checked = false; select(""); });
            actions.append(archive, clear); els.Detail.append(actions);
            if (!state.layout.positions.has(node.id)) {
                const warning = element("div", "tt-detail-warning", "所选物品被当前筛选隐藏。 ");
                const reveal = element("button", "tt-item-link", "清除筛选并定位"); reveal.type = "button"; reveal.addEventListener("click", () => select(node.id, { center: true }));
                warning.append(reveal); els.Detail.append(warning);
            }
            if (node.special) els.Detail.append(element("p", "tt-special-note", node.specialLabel || "此物品属于特殊或内部用途，不代表正常进度解锁。"));
            els.Detail.append(element("p", "tt-classification", node.classification?.reason || "按当前物品用途和材料归入这一阶段。"));
            if (node.sourceNote) els.Detail.append(element("p", node.sourceStatus === "no-known-route" ? "tt-source-note" : "tt-small-note", node.sourceNote));
            const incoming = state.incoming.get(node.id) || [], outgoing = state.outgoing.get(node.id) || [];
            const routeIds = [...new Set([...(node.recipes || []), ...incoming.map(edge => edge.routeId)])].filter(id => state.routes.has(id));
            const routeTitle = element("h4", "tt-section-title", `如何获得 · ${routeIds.length} 条路线`);
            els.Detail.append(routeTitle);
            if (routeIds.length > 1) els.Detail.append(element("p", "tt-small-note", "下列路线分别成立，任选可用的一条。"));
            if (!routeIds.length && !node.sourceNote) els.Detail.append(element("p", "tt-source-note", "当前没有配置制作或加工来源。可能来自世界生成、采集、战利品或其他机制；未推测一条配方。"));
            routeIds.forEach((id, index) => els.Detail.append(renderRoute(state.routes.get(id), index, node.id)));
            if (node.upgrades?.length) {
                els.Detail.append(element("h4", "tt-section-title", `强化现有工具 · ${node.upgrades.length} 条路线`));
                els.Detail.append(element("p", "tt-small-note", "以下操作以现有工具为输入，强化后仍是这件工具，不会凭空制作新工具。"));
                node.upgrades.forEach((route, index) => els.Detail.append(renderRoute(route, index, node.id, true)));
            }
            if (node.externalTargets?.length) {
                els.Detail.append(element("h4", "tt-section-title", "放置到世界"));
                node.externalTargets.forEach(target => {
                    els.Detail.append(element("p", "tt-source-note", target.label || target.id));
                    if (target.evidence) els.Detail.append(element("code", "tt-evidence", target.evidence));
                });
            }
            const targets = new Map();
            outgoing.forEach(edge => {
                if (edge.target === node.id) return;
                if (!targets.has(edge.target)) targets.set(edge.target, new Set());
                targets.get(edge.target).add(KIND_NAMES[edge.kind] || edge.kind);
            });
            els.Detail.append(element("h4", "tt-section-title", `后续用途 · ${targets.size} 种物品`));
            const uses = element("div", "tt-downstream");
            targets.forEach((kinds, id) => {
                const row = element("div", "tt-downstream-row"); row.append(itemButton(id), element("small", "", [...kinds].join(" / "))); uses.append(row);
            });
            if (!targets.size) uses.append(element("p", "tt-small-note", "当前配置没有它通向其他物品的关系。"));
            els.Detail.append(uses);
        }

        function renderRoute(route, index, selectedId, upgrade = false) {
            const details = element("details", "tt-route"); details.open = index < 3;
            const summary = element("summary");
            summary.append(element("span", "tt-route-number", String(index + 1).padStart(2, "0")), element("strong", "", route.label || route.id), element("small", "", KIND_NAMES[route.type] || ({ recipe: "配方", reaction: "反应", mechanical: "机械加工" })[route.type] || route.type));
            details.append(summary);
            const body = element("div", "tt-route-body");
            const focus = element("button", "tt-route-focus", state.route === route.id ? "显示此物品全部路线" : "高亮这条路线"); focus.type = "button";
            focus.addEventListener("click", () => { state.route = state.route === route.id ? "" : route.id; updateSelection(); renderEdges(); renderDetail(); });
            if (!upgrade) body.append(focus);
            const groups = route.inputs || [];
            if (groups.length) body.append(element("h5", "", ["loot", "harvest"].includes(route.type) ? "获取来源" : "所需材料 / 工具（各组同时满足）"));
            groups.forEach(group => {
                const box = element("div", `tt-input-group${group.optional || group.match !== "exact_item" ? " is-alternative" : ""}`);
                const candidates = [...new Set(group.candidates || (group.itemId ? [group.itemId] : []))];
                const alternative = group.match !== "exact_item" || candidates.length > 1;
                const sourceObject = ["loot", "harvest"].includes(route.type) && group.amount === 0;
                const quantity = !group.tool && !sourceObject && group.amount !== undefined && group.amount !== null ? ` × ${group.amount}` : "";
                box.append(element("strong", "tt-group-label", `${sourceObject ? "来源对象" : group.tool ? "工具条件（不消耗）" : "材料"}${quantity}${alternative ? " · 候选任选" : ""}`));
                if (group.label) box.append(element("small", "tt-group-note", group.label));
                const choices = element("div", "tt-choices");
                candidates.forEach(id => choices.append(itemButton(id)));
                if (!candidates.length) choices.append(element("span", "tt-missing", group.tag ? `标签 ${group.tag}：没有匹配物品` : "未解析出匹配物品"));
                box.append(choices); appendConditions(box, group.conditions); body.append(box);
            });
            const requirements = route.requirements || [];
            if (requirements.length) body.append(element("h5", "", "设备与条件"));
            requirements.forEach(requirement => {
                const box = element("div", "tt-requirement");
                box.append(element("strong", "", requirement.label || requirement.kind));
                if (requirement.portable) box.append(element("small", "tt-group-note", "也可通过随身手工完成，不必专门建造这座工作站。"));
                else if ((requirement.candidates || []).length > 1) box.append(element("small", "tt-group-note", "至少需要一种满足条件的工具或设备，下列候选任选。"));
                const choices = element("div", "tt-choices");
                (requirement.candidates || []).forEach(id => choices.append(itemButton(id)));
                box.append(choices); appendConditions(box, requirement.conditions); body.append(box);
            });
            if ((route.outputs || []).length) {
                body.append(element("h5", "", "产出"));
                const outputs = element("div", "tt-choices");
                route.outputs.forEach(output => {
                    const id = output.itemId;
                    const amount = output.amount ?? output.Amount;
                    const range = output.minAmount !== undefined || output.maxAmount !== undefined ? ` × ${output.minAmount ?? "?"}～${output.maxAmount ?? "?"}` : "";
                    const probability = Number.isFinite(Number(output.probability)) && output.probability !== null
                        ? ` · 概率 ${Number((Number(output.probability) * 100).toFixed(2))}%` : "";
                    const chip = itemButton(id, undefined, `${amount !== undefined ? ` × ${amount}` : range}${probability}`);
                    chip.classList.toggle("is-output", keyOf(id) === keyOf(selectedId)); outputs.append(chip);
                });
                body.append(outputs);
            }
            if (upgrade && route.output) {
                body.append(element("h5", "", "强化结果"));
                const result = { ...route.output };
                delete result.copyInputTag;
                delete result.copyInputItemId;
                const sourceName = route.output.copyInputItemId
                    ? state.nodes.get(keyOf(route.output.copyInputItemId))?.name || route.output.copyInputItemId
                    : `${route.output.copyInputTag} 类物品`;
                body.append(element("p", "tt-small-note", `保留输入的 ${sourceName} 并应用：`));
                appendConditions(body, result);
            }
            appendConditions(body, route.conditions);
            if (route.evidence) body.append(element("code", "tt-evidence", typeof route.evidence === "string" ? route.evidence : JSON.stringify(route.evidence)));
            details.append(body);
            return details;
        }

        function appendConditions(host, conditions) {
            if (!conditions || !Object.keys(conditions).length) return;
            const list = element("dl", "tt-conditions");
            for (const [key, value] of Object.entries(conditions)) {
                if (value === null || value === undefined || value === "") continue;
                list.append(element("dt", "", CONDITION_NAMES[key] || key), element("dd", "", valueText(value)));
            }
            if (list.childElementCount) host.append(list);
        }

        function renderNotes() {
            const warnings = state.model.warnings || [];
            els.WarningCount.textContent = warnings.length ? `· ${warnings.length} 条待核对信息` : "";
            els.NotesBody.replaceChildren();
            els.NotesBody.append(element("p", "", "每个大时代下面继续分细阶段，每张节点都对应当前 JSON 中的一种具体物品。时代只用于整理，调整 ItemWiki/tech-tree-eras.json 后重新读取即可；不改变游戏解锁、配方和物品属性。"));
            els.NotesBody.append(element("p", "", "自然资源独立放在中下方，按下游用途就近排列；自然采集、采矿与树木掉落可跨时代使用，加工产物仍沿时代主线排列。"));
            els.NotesBody.append(element("p", "", "关系读取启用分包的配方、物品自带加工与反应、机械加工、放置和状态转换。不同路线可替代，同一路线的材料组一起需要，标签 / 能力组内部任选。已知的循环关系保留为回线；缺少来源不推测补齐。"));
            if (warnings.length) {
                const list = element("ul", "tt-warnings");
                warnings.forEach(warning => list.append(element("li", "", typeof warning === "string" ? warning : warning.message || JSON.stringify(warning))));
                els.NotesBody.append(list);
            }
        }
        // #endregion

        // #region 画布定位、缩放与输入
        function updateTransform() {
            if (!state.layout || !state.shown) return;
            const width = els.Viewport.clientWidth, height = els.Viewport.clientHeight;
            state.x = Math.min(36, Math.max(Math.min(36, width - state.layout.width * state.zoom - 24), state.x));
            state.y = Math.min(20, Math.max(Math.min(20, height - state.layout.height * state.zoom - 24), state.y));
            if (state.frame) return;
            state.frame = requestAnimationFrame(() => {
                state.frame = 0;
                if (!state.layout || !state.shown) return;
                els.Surface.style.transform = `translate(${state.x}px, ${state.y}px) scale(${state.zoom})`;
                els.ZoomValue.textContent = `${Math.round(state.zoom * 100)}%`;
                const distance = Math.max(0, state.layout.width * state.zoom - els.Viewport.clientWidth + 60);
                els.Pan.disabled = distance === 0;
                els.Pan.value = String(distance ? Math.round((36 - state.x) / distance * 1000) : 0);
                const worldX = (els.Viewport.clientWidth * .35 - state.x) / state.zoom;
                const box = state.layout.eras.find(value => worldX >= value.x && worldX <= value.x + value.width) || state.layout.eras.find(value => value.x > worldX) || state.layout.eras.at(-1);
                els.PanEra.textContent = box?.era.name || "";
                for (const button of els.EraNav.children) button.classList.toggle("is-current", button.dataset.era === box?.era.id);
            });
        }

        function zoomAt(next, x = els.Viewport.clientWidth / 2, y = els.Viewport.clientHeight / 2) {
            const zoom = Math.min(1.75, Math.max(.045, next));
            state.x = x - (x - state.x) * zoom / state.zoom;
            state.y = y - (y - state.y) * zoom / state.zoom;
            state.zoom = zoom; updateTransform();
        }

        function centerOn(id) {
            const position = state.layout?.positions.get(id);
            if (!position) return;
            state.zoom = Math.max(.85, state.zoom);
            state.x = els.Viewport.clientWidth / 2 - (position.x + NODE_WIDTH / 2) * state.zoom;
            state.y = Math.min(20, els.Viewport.clientHeight / 2 - (position.y + NODE_HEIGHT / 2) * state.zoom);
            updateTransform();
        }

        function ensureInView(id) {
            const position = state.layout?.positions.get(id);
            if (!position || !state.shown || state.drag) return;
            const x = state.x + position.x * state.zoom, y = state.y + position.y * state.zoom;
            if (x < 0 || y < 0 || x + NODE_WIDTH * state.zoom > els.Viewport.clientWidth || y + NODE_HEIGHT * state.zoom > els.Viewport.clientHeight) centerOn(id);
        }

        function home() { state.zoom = .85; state.x = 20; state.y = 12; updateTransform(); }
        function fit() {
            if (!state.layout || !state.shown) return;
            state.zoom = Math.min(1, Math.max(.045, Math.min((els.Viewport.clientWidth - 36) / state.layout.width, (els.Viewport.clientHeight - 28) / state.layout.height)));
            state.x = 18; state.y = 12; updateTransform();
        }
        function show() {
            state.shown = true;
            if (!state.initialized) { state.initialized = true; home(); }
            requestAnimationFrame(() => {
                updateTransform();
                state.observer?.disconnect();
                observeIcons();
            });
        }
        function hide() { setExpanded(false); state.shown = false; }

        // 铺满网页时复用原画布，退出后保留浏览位置并恢复页面焦点。
        const overlay = element("section", "tt-canvas-overlay");
        overlay.hidden = true;
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.setAttribute("aria-label", "物品科技树画布");
        const exitExpanded = element("button", "tt-exit-expanded", "退出铺满 · Esc");
        exitExpanded.type = "button";
        overlay.append(exitExpanded);
        document.body.append(overlay);
        const viewportParent = els.Viewport.parentNode, viewportNext = els.Viewport.nextSibling;
        const inertBefore = new Map();
        let expanded = false;
        function setExpanded(value) {
            if (value === expanded) return;
            const width = els.Viewport.clientWidth, height = els.Viewport.clientHeight;
            expanded = value;
            if (value) {
                for (const child of document.body.children) {
                    if (child === overlay) continue;
                    inertBefore.set(child, child.inert);
                    child.inert = true;
                }
                overlay.hidden = false;
                overlay.prepend(els.Viewport);
            } else {
                viewportParent.insertBefore(els.Viewport, viewportNext);
                overlay.hidden = true;
                for (const [child, inert] of inertBefore) child.inert = inert;
                inertBefore.clear();
            }
            document.body.classList.toggle("tt-canvas-expanded", value);
            state.x += (els.Viewport.clientWidth - width) / 2;
            state.y += (els.Viewport.clientHeight - height) / 2;
            updateTransform();
            (value ? els.Viewport : els.Expand).focus({ preventScroll: true });
        }
        els.Expand.addEventListener("click", () => setExpanded(true));
        exitExpanded.addEventListener("click", () => setExpanded(false));
        document.addEventListener("keydown", event => {
            if (!expanded || event.key !== "Escape") return;
            event.preventDefault();
            event.stopPropagation();
            setExpanded(false);
        }, true);

        els.Search.addEventListener("input", () => {
            clearTimeout(state.searchTimer);
            state.searchTimer = setTimeout(() => { state.query = els.Search.value; render(); renderDetail(); fit(); }, 120);
        });
        [els.Era, els.Category].forEach(control => control.addEventListener("change", () => { render(); renderDetail(); fit(); }));
        // 展开完整依赖链时解除定位用的筛选，避免搜索结果截断上下游。
        els.Chain.addEventListener("change", () => {
            if (els.Chain.checked) {
                clearTimeout(state.searchTimer);
                state.query = ""; state.globalQuery = ""; els.Search.value = "";
                els.Era.value = ""; els.Category.value = "";
                clearGlobalSearch?.();
            }
            render(); renderDetail(); fit();
        });
        els.EdgeMode.addEventListener("change", renderEdges);
        els.ResetFilters.addEventListener("click", clearFilters);
        els.ZoomOut.addEventListener("click", () => zoomAt(state.zoom / 1.25));
        els.ZoomIn.addEventListener("click", () => zoomAt(state.zoom * 1.25));
        els.ZoomValue.addEventListener("click", () => zoomAt(1));
        els.Fit.addEventListener("click", fit); els.Home.addEventListener("click", home);
        els.Locate.addEventListener("click", () => select(state.selected, { center: true }));
        els.Pan.addEventListener("input", () => {
            if (!state.layout) return;
            const distance = Math.max(0, state.layout.width * state.zoom - els.Viewport.clientWidth + 60);
            state.x = 36 - Number(els.Pan.value) / 1000 * distance; updateTransform();
        });
        els.Viewport.addEventListener("wheel", event => {
            if (!state.layout) return;
            event.preventDefault();
            if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) { state.x -= event.deltaX || event.deltaY; updateTransform(); return; }
            const rect = els.Viewport.getBoundingClientRect();
            zoomAt(state.zoom * Math.exp(-event.deltaY * .0015), event.clientX - rect.left, event.clientY - rect.top);
        }, { passive: false });
        els.Viewport.addEventListener("pointerdown", event => {
            if (event.button !== 0) return;
            state.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (state.pointers.size === 2) {
                const [a, b] = [...state.pointers.values()];
                state.pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y), zoom: state.zoom };
                state.drag = null; state.suppressClickUntil = performance.now() + 600;
                return;
            }
            state.drag = { pointer: event.pointerId, x: event.clientX, y: event.clientY, originX: state.x, originY: state.y, moved: false };
        });
        els.Viewport.addEventListener("pointermove", event => {
            if (state.pointers.has(event.pointerId)) state.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (state.pinch && state.pointers.size === 2) {
                const [a, b] = [...state.pointers.values()], rect = els.Viewport.getBoundingClientRect();
                zoomAt(state.pinch.zoom * Math.hypot(a.x - b.x, a.y - b.y) / Math.max(1, state.pinch.distance), (a.x + b.x) / 2 - rect.left, (a.y + b.y) / 2 - rect.top);
                return;
            }
            const drag = state.drag;
            if (!drag || drag.pointer !== event.pointerId) return;
            const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
            if (!drag.moved && Math.hypot(dx, dy) < 5) return;
            drag.moved = true;
            els.Viewport.classList.add("is-dragging");
            if (!els.Viewport.hasPointerCapture(event.pointerId)) els.Viewport.setPointerCapture(event.pointerId);
            state.x = drag.originX + dx; state.y = drag.originY + dy; updateTransform();
        });
        function stopDrag(event) {
            state.pointers.delete(event.pointerId);
            if (state.drag?.moved || state.pinch) state.suppressClickUntil = performance.now() + 200;
            state.drag = null; state.pinch = null;
            els.Viewport.classList.remove("is-dragging");
            if (els.Viewport.hasPointerCapture(event.pointerId)) els.Viewport.releasePointerCapture(event.pointerId);
        }
        els.Viewport.addEventListener("pointerup", stopDrag);
        els.Viewport.addEventListener("pointercancel", stopDrag);
        els.Viewport.addEventListener("lostpointercapture", stopDrag);
        els.Viewport.addEventListener("dragstart", event => event.preventDefault());
        els.Viewport.addEventListener("keydown", event => {
            if (event.target !== els.Viewport || event.ctrlKey || event.metaKey || event.altKey) return;
            const step = event.shiftKey ? 280 : 90;
            if (event.key === "ArrowLeft") state.x += step;
            else if (event.key === "ArrowRight") state.x -= step;
            else if (event.key === "ArrowUp") state.y += step;
            else if (event.key === "ArrowDown") state.y -= step;
            else if (event.key === "+" || event.key === "=") zoomAt(state.zoom * 1.25);
            else if (event.key === "-") zoomAt(state.zoom / 1.25);
            else if (event.key === "Home") home();
            else if (event.key === "Escape") select("");
            else return;
            event.preventDefault(); updateTransform();
        });
        if (typeof ResizeObserver === "function") new ResizeObserver(() => { if (state.shown) updateTransform(); }).observe(els.Viewport);
        // #endregion
        return { setModel, setError, setSearch, select, show, hide, getSelectedId: () => state.selected };
    }

    window.ItemTechTree = { create, layout };
})();

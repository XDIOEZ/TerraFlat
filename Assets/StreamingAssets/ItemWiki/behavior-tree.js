(() => {
    "use strict";

    // #region 行为树分页与节点图
    const NS = "http://www.w3.org/2000/svg";
    const WIDTH = 218, HEIGHT = 68, COLUMN = 262, ROW = 92;
    const TYPES = { selector: "优先选择", sequence: "顺序执行", condition: "条件", action: "动作", subtree: "公共子树", service: "伴随服务", inverter: "取反" };
    const ACTORS = { Bear: "熊", Bird: "小鸟", Seagull: "海鸥", Bee: "蜜蜂", Chicken: "小鸡", WildBoar: "野猪", Wolf: "狼", Ghost: "幽灵", Zombie: "僵尸", SmallFish: "小鱼", Sheep: "羊", Rabbit: "白兔", SnowLeopard: "雪豹" };
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const svg = (tag, attributes, text) => {
        const node = document.createElementNS(NS, tag);
        for (const [key, value] of Object.entries(attributes || {})) node.setAttribute(key, value);
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const routeOf = record => record.kind === "actor" ? `actor/${record.id}` : record.id;
    const titleOf = record => record.kind === "actor" ? ACTORS[record.id] || record.name : record.name;
    const short = (text, length = 27) => text.length > length ? text.slice(0, length - 1) + "…" : text;
    const valueText = value => typeof value === "object" ? JSON.stringify(value) : String(value);

    function create({ root, onSelect, openActor }) {
        root.innerHTML = `
            <header class="bt-heading"><div><div class="bt-kicker">FLATWORLD · CREATURE BEHAVIOUR</div>
                <h2>生物行为树 <span>一个节点，一种能力</span></h2>
                <p>选择生物，沿优先级阅读决策；展开公共子树，查看这次调用实际使用的参数。</p></div>
                <div class="bt-counts" aria-live="polite"></div></header>
            <div class="bt-workspace">
                <aside class="bt-index"><div class="bt-tabs" aria-label="行为树目录">
                    <button type="button" data-kind="actor" aria-pressed="true">生物</button>
                    <button type="button" data-kind="subtree" aria-pressed="false">公共子树</button></div>
                    <label class="bt-search-label">查找配置<input class="bt-search" type="search" placeholder="名称、ID、节点…" autocomplete="off"></label>
                    <div class="bt-record-list"></div></aside>
                <section class="bt-map-column"><div class="bt-toolbar"><div class="bt-current-title"></div>
                    <div class="bt-map-controls"><button type="button" data-command="expand">展开子树</button><button type="button" data-command="collapse">折叠子树</button>
                    <button type="button" data-command="out" aria-label="缩小行为树">−</button><button type="button" data-command="reset" class="bt-zoom">100%</button><button type="button" data-command="in" aria-label="放大行为树">＋</button><button type="button" data-command="fit">适配</button></div></div>
                    <div class="bt-viewport" tabindex="0" aria-label="行为树关系图，可拖动画布"><svg class="bt-graph" aria-label="行为树节点"></svg></div>
                    <div class="bt-legend"><span data-type="selector">优先选择</span><span data-type="sequence">顺序执行</span><span data-type="condition">条件</span><span data-type="action">动作</span><span data-type="subtree">子树引用</span><small>从左到右 · 同级从上到下 · 拖动平移</small></div>
                </section>
                <aside class="bt-inspector" aria-label="节点与能力参数"></aside>
            </div>`;
        const find = selector => root.querySelector(selector);
        const els = { counts: find(".bt-counts"), list: find(".bt-record-list"), search: find(".bt-search"),
            title: find(".bt-current-title"), viewport: find(".bt-viewport"), graph: find(".bt-graph"),
            inspector: find(".bt-inspector"), zoom: find(".bt-zoom") };
        const state = { catalog: null, record: null, tree: null, kind: "actor", collapsed: new Set(), selected: null, nodes: new Map(), zoom: 1, width: 800, height: 520 };

        function records() { return state.kind === "actor" ? state.catalog?.actors || [] : state.catalog?.subtrees || []; }
        function renderIndex() {
            for (const button of root.querySelectorAll("[data-kind]")) button.setAttribute("aria-pressed", String(button.dataset.kind === state.kind));
            els.list.replaceChildren();
            const query = els.search.value.trim().toLowerCase();
            const matches = records().filter(record => `${titleOf(record)} ${record.id} ${record.name} ${JSON.stringify(record.root)} ${record.usedBy.join(" ")}`.toLowerCase().includes(query));
            for (const record of matches) {
                const button = element("button", "bt-record" + (record === state.record ? " selected" : ""));
                button.type = "button";
                const mark = element("span", "bt-record-mark", record.kind === "actor" ? titleOf(record).slice(0, 1) : "↳");
                const text = element("span", "bt-record-text");
                text.append(element("strong", "", titleOf(record)), element("code", "", record.kind === "actor" ? record.id : `${record.usedBy.length} 种生物引用${record.ownerId ? " · 局部" : ""}`));
                button.append(mark, text);
                button.setAttribute("aria-label", `${record.kind === "actor" ? "查看生物" : "查看子树"} ${titleOf(record)} ${record.id}`);
                button.addEventListener("click", () => select(routeOf(record)));
                els.list.append(button);
            }
            if (!matches.length) els.list.append(element("p", "bt-empty", "没有匹配的配置。"));
        }

        function select(id, { notify = true } = {}) {
            if (!state.catalog) return;
            const all = [...state.catalog.actors, ...state.catalog.subtrees];
            const record = all.find(value => routeOf(value) === id || value.id === id) ||
                state.catalog.actors.find(value => value.id === "Wolf") || all[0];
            if (!record) return;
            state.record = record;
            if (state.kind !== record.kind) els.search.value = "";
            state.kind = record.kind;
            state.tree = ItemBehaviorTreeModel.buildTree(record, state.catalog);
            state.nodes.clear();
            state.collapsed.clear();
            const visit = node => {
                state.nodes.set(node.key, node);
                if (node.type === "subtree" && node !== state.tree.root) state.collapsed.add(node.key);
                node.children.forEach(visit);
            };
            visit(state.tree.root);
            state.selected = state.tree.root.key;
            els.title.replaceChildren(element("strong", "", titleOf(record)), element("small", "", `${state.tree.nodeCount} 节点 · ${state.tree.referenceCount} 次子树引用`));
            renderIndex();
            renderMap();
            renderInspector();
            els.viewport.scrollTo(0, 0);
            if (notify) onSelect?.(routeOf(record));
        }

        function renderMap() {
            if (!state.tree) return;
            const positions = new Map(), edges = [];
            let row = 0, depthMax = 0;
            const place = (node, depth) => {
                depthMax = Math.max(depthMax, depth);
                const children = state.collapsed.has(node.key) ? [] : node.children;
                const childPoints = children.map((child, order) => {
                    const point = place(child, depth + 1);
                    edges.push({ parent: node, child, order: order + 1 });
                    return point;
                });
                const y = childPoints.length ? childPoints[0].y : 28 + row++ * ROW;
                const point = { x: 28 + depth * COLUMN, y, node };
                positions.set(node.key, point);
                return point;
            };
            place(state.tree.root, 0);
            state.width = 56 + depthMax * COLUMN + WIDTH;
            state.height = Math.max(340, row * ROW + 40);
            els.graph.replaceChildren();
            els.graph.setAttribute("viewBox", `0 0 ${state.width} ${state.height}`);
            for (const edge of edges) {
                const a = positions.get(edge.parent.key), b = positions.get(edge.child.key);
                const x = a.x + WIDTH, y = a.y + HEIGHT / 2, endY = b.y + HEIGHT / 2;
                els.graph.append(svg("path", { d: `M ${x} ${y} H ${x + 20} V ${endY} H ${b.x}`, class: "bt-edge" }));
                els.graph.append(svg("text", { x: b.x - 12, y: endY - 7, class: "bt-edge-order" }, String(edge.order)));
            }
            for (const { x, y, node } of positions.values()) {
                const group = svg("g", { transform: `translate(${x} ${y})`, class: `bt-node bt-${node.type}${node.error ? " bt-error" : ""}${node.key === state.selected ? " selected" : ""}`,
                    "data-node-key": node.key, role: "button", tabindex: "0", "aria-label": `${TYPES[node.type] || node.type} ${node.name || node.id || ""}` });
                group.append(svg("rect", { width: WIDTH, height: HEIGHT, rx: 3 }));
                group.append(svg("rect", { width: 4, height: HEIGHT, class: "bt-node-stripe" }));
                group.append(svg("text", { x: 14, y: 20, class: "bt-node-type" }, node.type.toUpperCase()));
                group.append(svg("text", { x: 14, y: 42, class: "bt-node-name" }, short(node.name || node.id || TYPES[node.type] || node.type, 24)));
                const preview = node.error || Object.entries(node.parameters || {}).slice(0, 2).map(([key, value]) => `${key}: ${valueText(value)}`).join(" · ");
                group.append(svg("text", { x: 14, y: 58, class: "bt-node-preview" }, short(preview, 35)));
                group.append(svg("title", {}, node.error || `${node.label}\n${JSON.stringify(node.parameters, null, 2)}`));
                if (node.children.length) {
                    const toggle = svg("g", { transform: `translate(${WIDTH - 32} 5)`, role: "button", tabindex: "0", "data-toggle-key": node.key,
                        "aria-label": `${state.collapsed.has(node.key) ? "展开" : "折叠"}${node.type === "subtree" ? "子树" : "分支"} ${node.name || TYPES[node.type]}` });
                    toggle.append(svg("rect", { width: 26, height: 24, rx: 2, class: "bt-toggle-bg" }), svg("text", { x: 13, y: 17, "text-anchor": "middle", class: "bt-toggle-text" }, state.collapsed.has(node.key) ? "+" : "−"));
                    group.append(toggle);
                }
                els.graph.append(group);
            }
            setZoom(state.zoom);
        }

        function appendParameters(parent, title, parameters) {
            parent.append(element("h4", "", title));
            const entries = Object.entries(parameters || {});
            if (!entries.length) { parent.append(element("p", "bt-muted", "此节点没有额外参数。")); return; }
            const list = element("dl", "bt-parameters");
            for (const [key, value] of entries) list.append(element("dt", "", key), element("dd", "", valueText(value)));
            parent.append(list);
        }

        function renderInspector() {
            const node = state.nodes.get(state.selected), record = state.record;
            if (!node || !record) return;
            const pane = els.inspector;
            pane.replaceChildren(element("div", "bt-kicker", "NODE INSPECTOR"), element("h3", "", TYPES[node.type] || node.type), element("code", "bt-node-id", node.name || node.id || "root"));
            const descriptions = { selector: "按顺序选择首个可执行分支；高优先级可以打断后续分支。", sequence: "按顺序执行；失败即返回，执行中的动作会继续。", condition: "检查当前事实，决定分支是否满足条件。", action: "调用能力执行动作，返回成功、失败或执行中。", subtree: "引用公共流程；下面显示本次调用合并后的参数。", service: "前面的节点持续提供服务，最后一个子节点控制主行为。", inverter: "反转成功与失败，执行中保持不变。" };
            pane.append(element("p", "bt-explanation", descriptions[node.type] || ""));
            if (node.error) pane.append(element("p", "bt-error-message", node.error));
            appendParameters(pane, "节点参数 · 实际值", node.parameters);
            if (node.type === "subtree") {
                const target = state.catalog.subtrees.find(value => value.name === node.subtree && value.ownerId === (record.kind === "actor" ? record.id : record.ownerId)) ||
                    state.catalog.subtrees.find(value => value.name === node.subtree && value.scope === "shared");
                if (target) {
                    const button = element("button", "bt-link-button", "打开子树定义 ↗");
                    button.type = "button";
                    button.addEventListener("click", () => select(routeOf(target)));
                    pane.append(button);
                }
            }
            const config = element("details", "bt-config");
            config.append(element("summary", "", record.kind === "actor" ? "此生物的能力配置" : "子树默认参数"), element("pre", "", JSON.stringify(record.kind === "actor" ? record.capabilities : record.parameters, null, 2)));
            pane.append(config);
            if (record.kind === "subtree") {
                pane.append(element("h4", "", `引用生物 · ${record.usedBy.length}`));
                const users = element("div", "bt-users");
                for (const id of record.usedBy) {
                    const button = element("button", "bt-link-button", ACTORS[id] || id);
                    button.type = "button";
                    button.addEventListener("click", () => select(`actor/${id}`));
                    users.append(button);
                }
                if (!record.usedBy.length) users.append(element("span", "bt-muted", "当前没有生物引用。"));
                pane.append(users);
            } else {
                const button = element("button", "bt-link-button", "查看生物档案 ↗");
                button.type = "button";
                button.addEventListener("click", () => openActor?.(record.id));
                pane.append(button);
            }
            const source = element("div", "bt-source");
            source.append(element("small", "", "JSON 来源"), element("code", "", record.sourcePath));
            pane.append(source);
        }

        function setZoom(value) {
            state.zoom = Math.min(1.6, Math.max(.25, value));
            els.graph.style.width = `${state.width * state.zoom}px`;
            els.graph.style.height = `${state.height * state.zoom}px`;
            els.zoom.textContent = `${Math.round(state.zoom * 100)}%`;
        }

        function activateNode(event) {
            if (event.type === "keydown" && !["Enter", " "].includes(event.key)) return;
            if (event.type === "keydown") event.preventDefault();
            const toggle = event.target.closest("[data-toggle-key]");
            const target = event.target.closest("[data-node-key]");
            if (!target) return;
            state.selected = target.dataset.nodeKey;
            if (toggle) {
                const key = toggle.dataset.toggleKey;
                if (state.collapsed.has(key)) state.collapsed.delete(key); else state.collapsed.add(key);
            }
            renderMap();
            renderInspector();
            if (event.type === "keydown") [...els.graph.querySelectorAll("[data-node-key]")].find(group => group.dataset.nodeKey === state.selected)?.focus();
        }
        els.graph.addEventListener("click", activateNode);
        els.graph.addEventListener("keydown", activateNode);
        els.search.addEventListener("input", renderIndex);
        for (const button of root.querySelectorAll("[data-kind]")) button.addEventListener("click", () => {
            if (state.kind !== button.dataset.kind) els.search.value = "";
            state.kind = button.dataset.kind;
            renderIndex();
        });
        for (const button of root.querySelectorAll("[data-command]")) button.addEventListener("click", () => {
            const command = button.dataset.command;
            if (command === "expand") state.collapsed.clear();
            if (command === "collapse") for (const node of state.nodes.values()) if (node.type === "subtree" && node !== state.tree?.root) state.collapsed.add(node.key);
            if (command === "expand" || command === "collapse") renderMap();
            else setZoom(command === "fit" ? Math.min(1, (els.viewport.clientWidth - 20) / state.width, (els.viewport.clientHeight - 20) / state.height) : command === "reset" ? 1 : state.zoom + (command === "in" ? .15 : -.15));
        });
        let drag;
        els.viewport.addEventListener("pointerdown", event => {
            if (event.button !== 0 || event.target.closest("[data-node-key]")) return;
            drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: els.viewport.scrollLeft, top: els.viewport.scrollTop };
            els.viewport.setPointerCapture(event.pointerId);
            els.viewport.classList.add("dragging");
        });
        els.viewport.addEventListener("pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            els.viewport.scrollLeft = drag.left - event.clientX + drag.x;
            els.viewport.scrollTop = drag.top - event.clientY + drag.y;
        });
        for (const name of ["pointerup", "pointercancel", "lostpointercapture"]) els.viewport.addEventListener(name, () => { drag = null; els.viewport.classList.remove("dragging"); });
        els.viewport.addEventListener("wheel", event => {
            if (!event.ctrlKey) return;
            event.preventDefault();
            setZoom(state.zoom + (event.deltaY < 0 ? .1 : -.1));
        }, { passive: false });

        return {
            setCatalog(catalog) {
                const previous = state.record && routeOf(state.record);
                state.catalog = catalog;
                els.counts.textContent = `${catalog.actors.length} 种生物 / ${catalog.subtrees.length} 个子树`;
                select(previous, { notify: false });
            },
            setError(message) {
                state.catalog = state.record = state.tree = null;
                state.nodes.clear();
                els.graph.replaceChildren();
                els.inspector.replaceChildren(element("p", "bt-error-message", message));
                els.counts.textContent = "未加载";
                els.title.textContent = "行为树读取失败";
                els.list.replaceChildren(element("p", "bt-error-message", message));
            },
            select,
            getSelectedId: () => state.record ? routeOf(state.record) : "",
            show: () => { if (state.tree) renderMap(); }
        };
    }
    window.ItemBehaviorTreeViewer = { create };
    // #endregion
})();

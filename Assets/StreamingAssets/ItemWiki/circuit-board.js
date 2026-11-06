/* ItemWiki 电路实验室：输入、显示与纯数据模型分离。 */
(function (root) {
    "use strict";
    const M = root.ItemCircuitModel;
    const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const format = value => Number.isFinite(value) ? value.toLocaleString("zh-CN", { maximumFractionDigits: 2 }) : "—";
    const COLORS = { power: "#f1b956", signal: "#58e8b1", low: "#466b68", wireOff: "#806747", error: "#ff7589", text: "#e4ecee", muted: "#789098", device: "#182c34", border: "#36515b", selected: "#a2f4d0" };

    function create({ root: host }) {
        if (!host || !M) return null;
        host.innerHTML = `<div class="cb-shell">
            <header class="cb-heading"><div><div class="cb-eyebrow">FLATWORLD / AUTOMATION LAB</div><h2>电路实验室 <span>SIMULATION</span></h2><p>在同一张地图上，连接能量与逻辑。</p></div><button data-action="expand" title="铺满网页 / Esc 退出">⛶ 铺满网页</button></header>
            <div class="cb-toolbar"><div class="cb-toolbar-group"><button data-action="run" class="cb-primary">▶ 运行</button><button data-action="step" title="推进 0.05 秒">单步</button><button data-action="reset" title="保留布置，重置计时与储量">↺ 重置</button><select data-role="speed" aria-label="模拟速度"><option value="0.25">0.25×</option><option value="1" selected>1×</option><option value="2">2×</option><option value="4">4×</option><option value="8">8×</option></select></div>
            <div class="cb-toolbar-group"><button data-action="undo" title="Ctrl+Z，恢复布置而非运行进度">↶</button><button data-action="redo" title="Ctrl+Y">↷</button><select data-role="example" aria-label="加载示例"><option value="">加载示例…</option><option value="blink">01 · 亮 1 秒，灭 1 秒</option><option value="tank">02 · 低水位开泵，高水位停泵</option><option value="battery">03 · 太阳能与电池充放电</option><option value="logic">04 · 两个开关控制与门</option><option value="adder64">05 · 64 位二进制加法器</option></select></div>
            <div class="cb-toolbar-group cb-file-tools"><button data-action="export">导出方案</button><button data-action="import">导入</button><button data-action="clear">新建</button><input data-role="file" type="file" accept=".json,application/json" hidden></div></div>
            <div class="cb-workbench"><aside class="cb-palette"><div class="cb-panel-title">元件库 <span data-role="component-count">—</span></div><input class="cb-search" data-role="search" type="search" placeholder="搜索元件 / ID" aria-label="搜索电路元件"><div class="cb-tool-grid"><button data-tool="select" class="active">↖ 选择 / 移动</button><button data-tool="erase">⌫ 擦除</button></div><label class="cb-layer-choice">当前擦除层<select data-role="erase-layer"><option value="device">设备层</option><option value="power">电力线层</option><option value="signal">信号线层</option></select></label><div class="cb-palette-list" data-role="palette"><p>正在读取游戏设备…</p></div><div class="cb-library-note"><i class="cb-game-dot"></i> 游戏配置　<i class="cb-extension-dot"></i> 模拟扩展</div></aside>
            <section class="cb-map-panel"><div class="cb-map-toolbar"><span class="cb-tool-name" data-role="tool-name">选择 / 移动</span><div><button data-action="rotate" title="R 旋转 90°">↻ <span data-role="rotation">0°</span></button><button data-action="zoom-out" aria-label="缩小">−</button><button data-action="fit">适配</button><button data-action="zoom-in" aria-label="放大">＋</button></div></div>
            <div class="cb-viewport"><canvas class="cb-canvas" tabindex="0" aria-label="网格电路棋盘：选择元件后点击放置，拖动布线，空格拖动地图，滚轮缩放"></canvas><div class="cb-map-badges"><label><input data-layer="power" type="checkbox" checked><i class="cb-power-dot"></i> 电力</label><label><input data-layer="signal" type="checkbox" checked><i class="cb-signal-dot"></i> 信号 / 数据总线</label><label><input data-layer="device" type="checkbox" checked>设备</label></div><div class="cb-hover" data-role="hover">64 × 40 网格 · 每拍 0.05 秒</div><div class="cb-empty" data-role="empty" hidden>从左侧选择元件，点击网格开始搭建。<small>电线可从设备下方穿过；信号线连接设备边缘端口。</small></div></div>
            <div class="cb-map-footer"><span data-role="clock">00:00.00</span><span data-role="network-count">0 个电网 / 0 个信号网</span><span data-role="zoom">100%</span></div><div class="cb-environment"><strong>实验环境</strong><label>光照 <input data-role="light" type="range" min="0" max="100" value="100"><span data-role="light-value">100%</span></label><label>温度 <input data-role="temperature" type="number" min="-1000" max="1000000" value="25" step="1"> ℃</label></div></section>
            <aside class="cb-inspector"><div class="cb-panel-title">检查器 <span>INSPECTOR</span></div><div data-role="inspector"></div><div class="cb-scope-heading">信号观察 <span>最近 8 秒</span></div><canvas class="cb-scope" width="500" height="140" aria-label="所选元件的数字信号波形"></canvas><div class="cb-net-summary" data-role="summary"></div></aside></div>
            <div data-role="adder-lab" class="cb-adder-host" hidden></div>
            <div class="cb-status" role="status" data-role="status">方案只保存在当前浏览器或导出的 JSON 中，不写回游戏。</div>
            <details class="cb-help"><summary>操作说明与模拟边界</summary><div><p><b>搭建：</b>点击左侧元件后放置；拖动绘制线路。选择模式拖动设备，R 旋转，右键擦除当前层，空格 / 中键拖动地图，滚轮缩放，Delete 删除选中设备。编辑自动暂停；切走分页或浏览器后台也会暂停，不补算离线时间。</p><p><b>连接：</b>黄色线供电，绿色线传 0/1，两层交叉不相连。蓝色线是宽数据总线，最多传 65 位，不能与绿色的 0/1 线混用；一个宽总线不能有多个数据源。游戏设备按实际配置接同格或方向电力口；通常左侧 C 是扩展控制口，未接信号线时沿用启用状态。逻辑门右侧 Q 输出，左侧 A/S、上侧 B/R 输入；悬停或选中可查看。中心不能铺信号线；同层十字默认相连，跨线桥的横竖不相连。</p><p><b>64 位加法：</b>示例中蓝色 A/B 总线输入加法模块，内部的 64 级一位全加器逐级传进位，输出最多 65 位；下方绿色线传最高进位 0/1。下方工作台可修改输入、查看 64 级进位。模块封装只是为了方便观察，不代表已经在游戏里制造了 64 个实体门电路。</p><p><b>时间：</b>每拍 0.05 秒，每级逻辑门延迟一拍；信号网多输出采用 OR，输入悬空为 0，SR 同时置位和复位时复位优先。反馈环逐拍演化，不做无限递归；计时参数按最近的 0.05 秒取整。</p><p><b>电力：</b>读取当前 mechanical-catalog.json，使用 W、J、V；优先发电，余电充电、缺电放电，同网负载按比例分摊。相异源电压会停网，过载只报警，不自动断电，不求支路电流或压降。</p><p><b>边界：</b>所有自动化控制口和标紫色的元件均为设计实验。游戏设备这里只模拟电气负载，不运行 Unity 的配方、氧气、推力、重力或完整气液逻辑；双向电机只模拟受外部动力驱动的发电模式。水箱虚线是抽象水路，泵送不计算压力和水源。导出、撤销和自动保存记录布置及初始参数，不记录运行进度。</p></div></details>
        </div>`;
        const shell = host.querySelector(".cb-shell"), $ = role => host.querySelector(`[data-role="${role}"]`);
        const canvas = host.querySelector(".cb-canvas"), context = canvas.getContext("2d"), scope = host.querySelector(".cb-scope"), scopeContext = scope.getContext("2d");
        // 编辑器控件只增强现有 ItemWiki 视图，不改变模拟器数据模型。
        const palettePane = shell.querySelector(".cb-palette"), inspectorPane = shell.querySelector(".cb-inspector");
        shell.querySelector(".cb-heading").insertAdjacentHTML("beforeend", '<span class="cb-run-pill" data-role="run-state">● 已暂停</span>');
        palettePane.querySelector(".cb-search").insertAdjacentHTML("beforebegin", '<button type="button" class="cb-find-trigger" data-action="quick-add">⌕ 快速查找元件 <kbd>F</kbd></button>');
        palettePane.querySelector(".cb-palette-list").insertAdjacentHTML("beforebegin", '<div class="cb-categories" data-role="categories" aria-label="按用途筛选元件"></div>');
        palettePane.querySelector(".cb-tool-grid").insertAdjacentHTML("afterend", '<div class="cb-quicktools" data-role="quicktools"></div>');
        shell.querySelector(".cb-map-toolbar > div").insertAdjacentHTML("afterbegin", '<button type="button" class="cb-map-add" data-action="quick-add">＋ 添加 <kbd>F</kbd></button>');
        shell.querySelector(".cb-map-toolbar > div").insertAdjacentHTML("beforeend", '<button type="button" data-action="toggle-inspector" aria-pressed="true">☷ 属性</button>');
        inspectorPane.querySelector(".cb-panel-title").insertAdjacentHTML("beforeend", '<button type="button" class="cb-inspector-close" data-action="toggle-inspector" aria-label="收起属性面板" title="收起属性面板（I）">×</button>');
        shell.querySelector(".cb-viewport").insertAdjacentHTML("beforeend",
            '<div class="cb-viewport-hint">⌕ F 添加　·　V 选择　·　W 电线　·　S 信号　·　空格平移</div>' +
            '<section class="cb-quick-picker" data-role="quick-picker" aria-label="快速选择元件" hidden>' +
            '<label class="cb-quick-search-row"><span>⌕</span><input data-role="quick-search" type="search" placeholder="输入元件名称 / ID，回车选择…" autocomplete="off" aria-label="快速搜索元件"><kbd>ESC</kbd></label>' +
            '<div class="cb-quick-results" data-role="quick-results"></div><div class="cb-quick-footer">↑ ↓ 选择　·　Enter 确认　·　Esc 关闭</div></section>');
        let registry = M.createRegistry(null), board = new M.CircuitBoard(registry), ready = false, visible = false, running = false;
        let tool = "select", rotation = 0, selected = null, zoom = 1, panX = 32, panY = 32, cssWidth = 1, cssHeight = 1;
        let pointer = null, space = false, hover = null, accumulator = 0, lastFrame = 0, frameId = 0, lastReadout = 0, revision = 0, importId = 0;
        let undo = [], redo = [], wave = [], saveTimer = 0, needsFit = true, catalogSignature = "", savedStatus = "浏览器自动保存已启用";
        let category = "全部", pickerOpen = false, pickerIndex = 0, pickerMatches = [], dragTool = null;
        const layers = { power: true, signal: true, device: true }, storageKey = `flatworld-circuit-board-v1:${location.pathname}`;
        const button = action => host.querySelector(`[data-action="${action}"]`);
        function message(text, error = false) { $("status").textContent = text; $("status").classList.toggle("cb-error", error); }
        function setRunning(value) {
            running = value && ready && visible; accumulator = 0;
            button("run").textContent = running ? "Ⅱ 暂停" : "▶ 运行";
            button("run").classList.toggle("running", running);
            shell.classList.toggle("cb-is-running", running);
            $("run-state").textContent = running ? "● 正在仿真" : "● 已暂停";
        }
        function snapshot() { return JSON.stringify(board.serialize()); }
        function persist() {
            clearTimeout(saveTimer); if (!ready) return;
            try { localStorage.setItem(storageKey, snapshot()); savedStatus = "已自动保存在当前浏览器"; }
            catch { savedStatus = "浏览器未允许保存，请使用导出方案"; message(savedStatus, true); }
        }
        function beforeEdit() { setRunning(false); return snapshot(); }
        function finishEdit(previous, text) {
            const changed = snapshot() !== previous;
            if (changed) { undo.push(previous); if (undo.length > 40) undo.shift(); redo = []; revision++; clearTimeout(saveTimer); saveTimer = setTimeout(persist, 300); }
            board.refresh(); updateInspector(); updateReadouts(); renderPaletteSelection();
            if (text) message(text); else if (changed) message("布置已更新，模拟已暂停；点击运行继续。" );
            return changed;
        }
        function transaction(action, text) {
            const previous = beforeEdit();
            try { action(); finishEdit(previous, text); }
            catch (error) { message(error.message, true); board.refresh(); }
        }
        const adderPanel = window.ItemCircuitAdderPanel?.create({
            root: $("adder-lab"),
            getBoard: () => board,
            setOperands: (changes, text) => transaction(() => {
                for (const [deviceId, wordHex] of changes) board.configure(deviceId, { wordHex });
            }, text || "64 位输入已经更新。"),
            focusAdder: id => select(id)
        });
        function setTool(value) {
            tool = value; if (registry.has(value)) $("erase-layer").value = registry.get(value).kind === "wire" ? "power" : ["signal", "cross", "databus"].includes(registry.get(value).kind) ? "signal" : "device";
            if (pickerOpen) closeQuickPicker();
            renderPaletteSelection();
            $("tool-name").textContent = value === "select" ? "选择 / 移动" : value === "erase" ? "擦除当前层" : registry.get(value)?.name || value;
            canvas.dataset.mode = value === "select" ? "select" : value === "erase" ? "erase" : "place";
        }
        function renderPaletteSelection() {
            for (const element of host.querySelectorAll("[data-tool]")) { const active = element.dataset.tool === tool; element.classList.toggle("active", active); element.setAttribute("aria-pressed", String(active)); }
            for (const element of $("quicktools").querySelectorAll("[data-quick-tool]")) {
                const active = element.dataset.quickTool === tool;
                element.classList.toggle("active", active); element.setAttribute("aria-pressed", String(active));
            }
            $("rotation").textContent = `${(tool === "select" && selectedDevice() ? selectedDevice().rotation : rotation) * 90}°`;
        }
        function renderCategories() {
            const priority = ["线路", "控制", "逻辑", "检测", "游戏设备", "实验设备"];
            const groups = [...new Set(Array.from(registry.values(), definition => definition.group))].sort((a, b) => {
                const left = priority.indexOf(a), right = priority.indexOf(b);
                return (left < 0 ? 99 : left) - (right < 0 ? 99 : right) || a.localeCompare(b, "zh-CN");
            });
            if (category !== "全部" && !groups.includes(category)) category = "全部";
            $("categories").innerHTML = ["全部", ...groups].map(name =>
                '<button type="button" data-category="' + escape(name) + '" aria-pressed="' + (category === name) + '">' + escape(name) + '</button>').join("");
        }
        function renderPalette() {
            const query = $("search").value.trim().toLowerCase(), groups = new Map();
            for (const definition of registry.values()) {
                if (category !== "全部" && definition.group !== category) continue;
                if (query && !`${definition.name} ${definition.id} ${definition.group}`.toLowerCase().includes(query)) continue;
                if (!groups.has(definition.group)) groups.set(definition.group, []); groups.get(definition.group).push(definition);
            }
            $("palette").innerHTML = Array.from(groups, ([group, definitions]) => `<div class="cb-palette-group"><h3>${escape(group)}</h3>${definitions.map(d => `<button class="cb-component ${d.game ? "cb-game" : "cb-extension"}" data-tool="${escape(d.id)}" title="${escape(d.description)}"><span class="cb-component-symbol">${escape(d.symbol)}</span><span><strong>${escape(d.name)}</strong><small>${d.game ? "游戏配置" : "模拟扩展"}${d.defaults.watts ? ` · ${format(d.defaults.watts)} W` : ""}</small></span></button>`).join("")}</div>`).join("") || "<p class='cb-muted'>没有匹配元件。</p>";
            $("palette").querySelectorAll(".cb-component").forEach(element => element.draggable = true);
            $("component-count").textContent = registry.size; renderPaletteSelection();
        }
        function renderQuickTools() {
            const shortcuts = [["Wire_Copper", "电线", "W"], ["sim:signal", "信号", "S"], [Array.from(registry.values()).find(def => def.kind === "databus")?.id, "数据线", ""], ["sim:switch", "开关", ""]];
            $("quicktools").innerHTML = '<span>快捷元件</span><div>' + shortcuts.filter(([id]) => registry.has(id)).map(([id, name, key]) =>
                '<button type="button" data-quick-tool="' + escape(id) + '" title="' + escape(name) + '">' + escape(name) + (key ? ' <kbd>' + key + '</kbd>' : '') + '</button>').join("") + '</div>';
            renderPaletteSelection();
        }
        function renderQuickPicker() {
            const query = $("quick-search").value.trim().toLowerCase();
            pickerMatches = Array.from(registry.values()).filter(definition =>
                !query || (definition.name + " " + definition.id + " " + definition.group).toLowerCase().includes(query)).slice(0, 24);
            pickerIndex = Math.min(pickerIndex, Math.max(0, pickerMatches.length - 1));
            $("quick-results").innerHTML = pickerMatches.map((definition, index) =>
                '<button type="button" class="cb-quick-result' + (index === pickerIndex ? ' chosen' : '') + '" data-quick-tool="' + escape(definition.id) + '">' +
                '<span class="cb-quick-symbol">' + escape(definition.symbol) + '</span><span><b>' + escape(definition.name) + '</b><small>' + escape(definition.group) + ' · ' + (definition.game ? '游戏配置' : '模拟扩展') + '</small></span></button>').join("") ||
                '<p class="cb-no-results">没有找到元件，试试其他关键词。</p>';
        }
        function closeQuickPicker(restoreFocus = false) {
            pickerOpen = false; $("quick-picker").hidden = true;
            if (restoreFocus) canvas.focus();
        }
        function openQuickPicker() {
            if (!ready) return;
            pickerOpen = true; pickerIndex = 0; $("quick-search").value = ""; $("quick-picker").hidden = false;
            renderQuickPicker(); $("quick-search").focus();
        }
        function chooseQuickTool(id) {
            if (!registry.has(id)) return;
            setTool(id); canvas.focus(); message("已选择「" + registry.get(id).name + "」，点击画布放置或拖动布线。");
        }
        function toggleInspector() {
            const collapsed = shell.classList.toggle("cb-inspector-collapsed");
            inspectorPane.setAttribute("aria-hidden", String(collapsed));
            for (const control of host.querySelectorAll('[data-action="toggle-inspector"]')) {
                control.setAttribute("aria-pressed", String(!collapsed));
                control.setAttribute("title", collapsed ? "展开属性面板（I）" : "收起属性面板（I）");
            }
            requestAnimationFrame(resize);
        }
        function selectedDevice() { return board.devices.get(selected); }
        function targetOptions(device, current) {
            const kind = board.definition(device).kind, wanted = kind === "charge" ? "battery" : "tank";
            const targets = Array.from(board.devices.values()).filter(d => board.definition(d).kind === wanted);
            let html = `<option value="">请选择目标…</option>`;
            if (current && !targets.some(d => d.id === current)) html += `<option value="${escape(current)}" selected>目标已删除 (${escape(current)})</option>`;
            return html + targets.map(d => `<option value="${escape(d.id)}" ${d.id === current ? "selected" : ""}>${escape(board.definition(d).name)} · ${d.x},${d.y} (${d.id})</option>`).join("");
        }
        function updateInspector() {
            const device = selectedDevice();
            if (!device) {
                $("inspector").innerHTML = `<div class="cb-inspector-empty"><div>⌘</div><h3>选择一个元件</h3><p>查看接口、调整参数，<br>观察信号与实际供电。</p><small>游戏设备的控制口为实验扩展。<br>不修改任何游戏物品数据。</small></div>`; return;
            }
            const def = board.definition(device), p = board.params(device), pins = M.signalPins(def), dataPins = M.busPins(def);
            $("inspector").innerHTML = `<div class="cb-selection-heading"><span class="cb-selection-symbol">${escape(def.symbol)}</span><div><h3>${escape(def.name)}</h3><span class="cb-source-badge ${def.game ? "game" : ""}">${def.game ? "游戏配置 · 控制口扩展" : "模拟扩展"}</span></div></div><div class="cb-device-id">${escape(device.type)}<br>${device.id} · (${device.x}, ${device.y}) · ${device.rotation * 90}°</div><p class="cb-description">${escape(def.description)}</p>
                <div class="cb-runtime" data-role="runtime"></div><div class="cb-ports">${pins.map(pin => { const point = M.position(device, pin.direction); return `<span class="${pin.input ? "" : "output"}">${pin.name} ${pin.input ? "输入" : "输出"} → ${point.x},${point.y}</span>`; }).join("")}${dataPins.map(pin => { const point = M.position(device, pin.direction); return `<span class="data">${pin.name} ${pin.input ? "宽总线输入" : "宽总线输出"} → ${point.x},${point.y}</span>`; }).join("")}${def.electrical ? (() => { const point = M.powerPosition(def, device); return `<span class="power">电力接点 ${point.x},${point.y}</span>`; })() : ""}</div>
                <div class="cb-fields">${Object.entries(p).map(([name, value]) => {
                    const rule = M.PARAMS[name]; if (!rule) return "";
                    const input = rule.type === "boolean" ? `<input type="checkbox" data-param="${name}" ${value ? "checked" : ""}>` :
                        rule.type === "target" ? `<select data-param="${name}">${targetOptions(device, value)}</select>` :
                        rule.type === "select" ? `<select data-param="${name}">${rule.options.map(([v, label]) => `<option value="${v}" ${v === value ? "selected" : ""}>${label}</option>`).join("")}</select>` :
                        rule.type === "word64" ? `<textarea data-param="${name}" rows="4" spellcheck="false" placeholder="0x… / 0b… / 十进制">${escape(value)}</textarea>` :
                        `<input type="number" data-param="${name}" min="${rule.min}" max="${rule.max}" step="${rule.step}" value="${value}">`;
                    return `<label class="${rule.type === "boolean" ? "cb-toggle-field" : ""}"><span>${rule.label}</span>${input}</label>`;
                }).join("")}</div><div class="cb-selected-actions"><button data-action="rotate-selected">↻ 旋转</button><button data-action="duplicate">复制</button><button data-action="delete">删除</button></div>`;
            updateRuntime();
        }
        function updateRuntime() {
            const device = selectedDevice(), runtime = $("runtime"); if (!device || !runtime) return;
            const def = board.definition(device), p = board.params(device), r = device.runtime, output = def.electrical ? board.readInput(device, 2) : null;
            const lines = [];
            if (def.kind === "wordsource") lines.push(["宽数据输出", `0x${M.parseWord64(p.wordHex).toString(16).toUpperCase()}`], ["位数", String(M.parseWord64(p.wordHex).toString(2).length)]);
            else if (def.operation === "rippleAdder64") lines.push(["64 级计算", r.wordStatus || "待机"], ["最高进位", String(r.carry || 0)], ["数据输出", r.sumHex || "—"], ["供电", `${format(r.watts)} / ${format(p.watts)} W`]);
            else if (def.kind === "worddisplay") lines.push(["结果状态", r.wordStatus || "未接入"], ["输出位宽", r.sumHex ? String(BigInt(r.sumHex).toString(2).length) : "—"], ["结果", r.sumHex || "—"]);
            else if (def.kind === "battery") lines.push(["储能", `${format(r.energy)} / ${format(p.capacity)} J`], ["剩余", `${format(r.energy / p.capacity * 100)}%`], [r.watts >= 0 ? "充电功率" : "放电功率", `${format(Math.abs(r.watts))} W`]);
            else if (def.electrical) lines.push([def.kind === "generator" ? "实际发电" : "实际供电", `${format(r.watts)} W`], ["控制输入", output.connected ? Number(output.value) : "未接线（启用）"]);
            else if (def.kind === "tank") lines.push(["储水", `${format(r.water)} / ${format(p.tankCapacity)} L`], ["液位", `${format(r.water / p.tankCapacity * 100)}%`]);
            else if (def.kind === "relay") lines.push(["左右电路", r.closed ? "已接通" : "已断开"]);
            else lines.push(["输出", r.output ? "1 · 高电平" : "0 · 低电平"]);
            if (def.electrical) lines.push(["状态", r.status]);
            if (def.kind === "counter") lines.push(["累计上升沿", r.count]);
            if (def.kind === "delay") lines.push(["高电平累计", `${format(r.delayTicks * M.STEP)} s`]);
            if (["level", "charge", "temperature", "light"].includes(def.kind)) lines.push(["检测值", board.sensorValue(device) === null ? "未关联有效目标" : format(board.sensorValue(device))]);
            runtime.innerHTML = lines.map(([label, value]) => `<div><span>${label}</span><strong>${escape(value)}</strong></div>`).join("");
        }
        function updateReadouts() {
            const seconds = board.time, minutes = Math.floor(seconds / 60);
            $("clock").textContent = `${String(minutes).padStart(2, "0")}:${(seconds % 60).toFixed(2).padStart(5, "0")} ${running ? "运行中" : "已暂停"}`;
            $("network-count").textContent = `${board.powerNetworks.length} 电网 / ${board.signalNetworks.length} 信号网 / ${board.busNetworks.length} 数据总线`;
            $("zoom").textContent = `${Math.round(zoom * 100)}%`; $("empty").hidden = board.devices.size > 0 || board.power.size > 0 || board.signal.size > 0;
            button("undo").disabled = !undo.length; button("redo").disabled = !redo.length;
            const totals = board.powerNetworks.reduce((sum, n) => ({ generated: sum.generated + n.generated, demand: sum.demand + n.demand, delivered: sum.delivered + n.delivered, charging: sum.charging + n.charging, discharging: sum.discharging + n.discharging }), { generated: 0, demand: 0, delivered: 0, charging: 0, discharging: 0 });
            const warnings = [...new Set(board.warnings)];
            $("run-state").classList.toggle("cb-has-alert", warnings.length > 0);
            $("run-state").title = warnings.length ? warnings.join("；") : "编辑器仿真状态";
            $("summary").innerHTML = `<h3>全图电力概览</h3><div><span>发电 / 需求</span><b>${format(totals.generated)} / ${format(totals.demand)} W</b></div><div><span>实际供电</span><b>${format(totals.delivered)} W</b></div><div><span>充电 / 放电</span><b>${format(totals.charging)} / ${format(totals.discharging)} W</b></div>${warnings.map(text => `<p class="cb-warning">⚠ ${escape(text)}</p>`).join("")}`;
            updateRuntime();
            adderPanel?.render();
        }
        function select(id) { selected = id || null; wave = []; updateInspector(); renderPaletteSelection(); }
        function sample() {
            const device = selectedDevice();
            if (!device) return;
            const definition = board.definition(device), value = definition.operation === "rippleAdder64" ? device.runtime.output : definition.electrical ? board.readInput(device, 2).value : definition.kind === "relay" ? device.runtime.closed : device.runtime.output;
            wave.push({ time: board.time, value: !!value }); if (wave.length > 161) wave.shift();
        }
        function drawScope() {
            const ctx = scopeContext, w = scope.width, h = scope.height; ctx.clearRect(0, 0, w, h); ctx.fillStyle = "#0c1b22"; ctx.fillRect(0, 0, w, h);
            ctx.strokeStyle = "#233b44"; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(32, 28); ctx.lineTo(w - 12, 28); ctx.moveTo(32, h - 28); ctx.lineTo(w - 12, h - 28); ctx.stroke();
            ctx.fillStyle = COLORS.muted; ctx.font = "18px monospace"; ctx.fillText("1", 10, 34); ctx.fillText("0", 10, h - 22);
            if (!wave.length) { ctx.font = "17px sans-serif"; ctx.fillText("选中元件后运行 / 单步观察", 75, 76); return; }
            ctx.strokeStyle = COLORS.signal; ctx.lineWidth = 3; ctx.beginPath();
            const end = board.time, start = end - 8;
            wave.forEach((point, index) => { const x = 34 + (point.time - start) / 8 * (w - 50), y = point.value ? 28 : h - 28; if (!index) ctx.moveTo(x, y); else { ctx.lineTo(x, wave[index - 1].value ? 28 : h - 28); ctx.lineTo(x, y); } }); ctx.stroke();
        }
        function resize() {
            const rect = canvas.parentElement.getBoundingClientRect(), nextWidth = Math.max(1, rect.width), nextHeight = Math.max(1, rect.height);
            const previousWidth = cssWidth;
            // 面板宽度与窗口尺寸变化时固定视野中心，避免缩窄后电路整个消失。
            if (cssWidth > 1 && cssHeight > 1 && nextWidth > 1 && nextHeight > 1) {
                panX += (nextWidth - cssWidth) / 2; panY += (nextHeight - cssHeight) / 2;
            }
            cssWidth = nextWidth; cssHeight = nextHeight;
            if (visible && previousWidth >= 520 && nextWidth < 520 && board.devices.size) fit();
            const ratio = Math.min(devicePixelRatio || 1, 2); canvas.width = Math.round(cssWidth * ratio); canvas.height = Math.round(cssHeight * ratio);
            context.setTransform(ratio, 0, 0, ratio, 0, 0);
        }
        function fit() {
            const points = [...board.devices.values()].map(d => [d.x, d.y]);
            for (const cell of [...board.power.keys(), ...board.signal.keys()]) points.push(cell.split(",").map(Number));
            if (!points.length) { zoom = 1; panX = 40; panY = 40; return; }
            const xs = points.map(p => p[0]), ys = points.map(p => p[1]), minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
            zoom = M.clamp(Math.min(cssWidth / ((maxX - minX + 5) * 64), cssHeight / ((maxY - minY + 5) * 64)), 0.3, 1.35);
            panX = cssWidth / 2 - (minX + maxX + 1) / 2 * 64 * zoom; panY = cssHeight / 2 - (minY + maxY + 1) / 2 * 64 * zoom;
        }
        function roundRect(ctx, x, y, w, h, r) {
            ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r); ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
        }
        function drawWires(layer) {
            const ctx = context, map = board[layer]; ctx.lineCap = "round";
            for (const [cell, type] of map) {
                const [x, y] = cell.split(",").map(Number); if (!inView(x, y)) continue;
                const cx = x * 64 + 32, cy = y * 64 + 32;
                const isBus = layer === "signal" && registry.get(type)?.kind === "databus";
                for (const channel of layer === "signal" && type === "sim:cross" ? ["v", "h"] : ["a"]) {
                    let color;
                    if (layer === "power") { const n = board.powerNetworks[board.powerIndex.get(cell)]; color = n?.overloaded || ["电压冲突", "线路电压不兼容"].includes(n?.status) ? COLORS.error : n?.voltage ? COLORS.power : COLORS.wireOff; }
                    else if (isBus) {
                        const bus = board.busValues[board.busIndex.get(cell)];
                        color = bus?.status === "多驱动冲突" ? COLORS.error : bus?.hex ? "#5FC8F5" : "#52788C";
                    }
                    else color = board.signalValues[board.signalIndex.get(`${cell}:${channel}`)] ? COLORS.signal : COLORS.low;
                    ctx.strokeStyle = "#0a171e"; ctx.lineWidth = isBus ? 18 : layer === "power" ? 12 : 10; ctx.setLineDash([]);
                    const drawSegments = () => {
                        ctx.beginPath(); let connected = false;
                        for (let direction = 0; direction < 4; direction++) {
                            if (channel !== "a" && channel !== ((direction & 1) === 0 ? "h" : "v")) continue;
                            const [dx, dy] = M.DIRECTIONS[direction];
                            if (map.has(M.key(x + dx, y + dy))) { ctx.moveTo(cx, cy); ctx.lineTo(cx + dx * 32, cy + dy * 32); connected = true; }
                        }
                        if (!connected) { ctx.moveTo(cx - 2, cy); ctx.lineTo(cx + 2, cy); } ctx.stroke();
                    };
                    drawSegments(); ctx.strokeStyle = color; ctx.lineWidth = isBus ? 10 : layer === "power" ? 6 : 4;
                    if (registry.get(type)?.electricalConfig?.Hidden) ctx.setLineDash([6, 4]); drawSegments(); ctx.setLineDash([]);
                    if (channel === "a") { ctx.fillStyle = color; ctx.beginPath(); ctx.arc(cx, cy, isBus ? 5 : layer === "power" ? 4 : 3, 0, Math.PI * 2); ctx.fill(); }
                    else if (channel === "h") { ctx.strokeStyle = "#0a171e"; ctx.lineWidth = 9; ctx.beginPath(); ctx.arc(cx, cy, 8, Math.PI, 0); ctx.stroke(); ctx.strokeStyle = color; ctx.lineWidth = 4; ctx.stroke(); }
                }
            }
        }
        function inView(x, y) { return (x + 1) * 64 * zoom + panX > -64 && x * 64 * zoom + panX < cssWidth + 64 && (y + 1) * 64 * zoom + panY > -64 && y * 64 * zoom + panY < cssHeight + 64; }
        function drawDevice(device, ghost = false) {
            const ctx = context, def = board.definition(device), p = board.params(device), r = device.runtime || {}, x = device.x * 64, y = device.y * 64, active = def.electrical ? r.watts > 0 : r.output;
            ctx.save(); if (ghost) ctx.globalAlpha = 0.55;
            if (layers.signal) for (const pin of M.signalPins(def)) {
                const direction = (pin.direction + device.rotation) & 3, [dx, dy] = M.DIRECTIONS[direction], high = pin.input ? board.readInput(device, pin.direction).value : r.output;
                const point = M.position(device, pin.direction), length = board.signal.has(M.key(point.x, point.y)) ? 64 : 32;
                ctx.strokeStyle = high ? COLORS.signal : COLORS.low; ctx.lineWidth = 3; ctx.beginPath(); ctx.moveTo(x + 32 + dx * 24, y + 32 + dy * 24); ctx.lineTo(x + 32 + dx * length, y + 32 + dy * length); ctx.stroke();
            }
            if (layers.signal) for (const pin of M.busPins(def)) {
                const [dx, dy] = M.DIRECTIONS[(pin.direction + device.rotation) & 3], point = M.position(device, pin.direction);
                const length = board.busIndex.has(M.key(point.x, point.y)) ? 64 : 32;
                ctx.strokeStyle = "#5FC8F5"; ctx.lineWidth = 8; ctx.beginPath();
                ctx.moveTo(x + 32 + dx * 24, y + 32 + dy * 24);
                ctx.lineTo(x + 32 + dx * length, y + 32 + dy * length); ctx.stroke();
            }
            if (layers.power && def.electrical) {
                const port = M.powerPosition(def, device); ctx.strokeStyle = COLORS.power; ctx.lineWidth = 4;
                ctx.beginPath(); ctx.moveTo(x + 32, y + 32); ctx.lineTo(port.x * 64 + 32, port.y * 64 + 32); ctx.stroke();
            }
            if (layers.power && def.kind === "relay") for (const direction of [0, 2]) {
                const port = M.position(device, direction), [dx, dy] = M.DIRECTIONS[(direction + device.rotation) & 3], length = board.power.has(M.key(port.x, port.y)) ? 64 : 32;
                ctx.strokeStyle = COLORS.power; ctx.lineWidth = 4; ctx.beginPath(); ctx.moveTo(x + 32 + dx * 24, y + 32 + dy * 24); ctx.lineTo(x + 32 + dx * length, y + 32 + dy * length); ctx.stroke();
            }
            ctx.fillStyle = def.game ? "#1b3038" : "#28273d"; ctx.strokeStyle = selected === device.id ? COLORS.selected : active ? (def.electrical ? COLORS.power : COLORS.signal) : COLORS.border; ctx.lineWidth = selected === device.id ? 2.5 : 1.5;
            if (active) { ctx.shadowColor = def.electrical ? "#f1b95677" : "#58e8b166"; ctx.shadowBlur = 12; }
            roundRect(ctx, x + 9, y + 8, 46, 48, 8); ctx.fill(); ctx.stroke(); ctx.shadowBlur = 0;
            if (def.kind === "battery") {
                const fraction = M.clamp((r.energy || 0) / p.capacity, 0, 1); ctx.strokeStyle = COLORS.muted; ctx.lineWidth = 2; ctx.strokeRect(x + 21, y + 16, 22, 25); ctx.fillStyle = COLORS.muted; ctx.fillRect(x + 27, y + 12, 10, 4); ctx.fillStyle = fraction < 0.2 ? COLORS.error : COLORS.signal; ctx.fillRect(x + 24, y + 38 - 19 * fraction, 16, 19 * fraction);
            } else if (def.kind === "tank") {
                const fraction = M.clamp((r.water || 0) / p.tankCapacity, 0, 1); ctx.strokeStyle = "#88bded"; ctx.lineWidth = 2; ctx.strokeRect(x + 19, y + 14, 26, 29); ctx.fillStyle = "#5296c8"; ctx.fillRect(x + 21, y + 41 - fraction * 25, 22, fraction * 25);
            } else if (device.type === "ElectricLightBulb") {
                ctx.fillStyle = active ? COLORS.power : "#44534d"; ctx.beginPath(); ctx.arc(x + 32, y + 26, 11, 0, Math.PI * 2); ctx.fill(); ctx.fillStyle = COLORS.muted; ctx.fillRect(x + 27, y + 37, 10, 4);
            } else if (def.kind === "relay") {
                ctx.save(); ctx.translate(x + 32, y + 28); ctx.rotate(device.rotation * Math.PI / 2); ctx.strokeStyle = r.closed ? COLORS.power : COLORS.muted; ctx.lineWidth = 3; ctx.beginPath(); ctx.moveTo(-15, 0); ctx.lineTo(-6, 0); ctx.moveTo(-6, 0); ctx.lineTo(10, r.closed ? 0 : -12); ctx.moveTo(10, 0); ctx.lineTo(16, 0); ctx.stroke(); ctx.restore();
            } else {
                ctx.fillStyle = active ? (def.electrical ? COLORS.power : COLORS.signal) : COLORS.text; ctx.font = `bold ${def.symbol.length > 2 ? 15 : 23}px sans-serif`; ctx.textAlign = "center"; ctx.textBaseline = "middle"; ctx.fillText(def.symbol, x + 32, y + 28);
            }
            ctx.fillStyle = COLORS.text; ctx.font = "9px sans-serif"; ctx.textAlign = "center"; ctx.textBaseline = "alphabetic";
            const label = def.name.length > 6 ? `${def.name.slice(0, 5)}…` : def.name; ctx.fillText(label, x + 32, y + 51, 44);
            if (layers.signal) for (const pin of M.signalPins(def)) {
                const [dx, dy] = M.DIRECTIONS[(pin.direction + device.rotation) & 3]; ctx.fillStyle = pin.input ? "#9bbbb5" : COLORS.signal; ctx.beginPath(); ctx.arc(x + 32 + dx * 25, y + 32 + dy * 25, 3.5, 0, Math.PI * 2); ctx.fill();
                if (zoom > 0.65) { ctx.fillStyle = "#b1c5c6"; ctx.font = "8px monospace"; ctx.fillText(pin.name, x + 32 + dx * 18 + (dy ? 7 : 0), y + 35 + dy * 18); }
            }
            if (layers.signal) for (const pin of M.busPins(def)) {
                const [dx, dy] = M.DIRECTIONS[(pin.direction + device.rotation) & 3];
                ctx.fillStyle = "#B7ECFF"; ctx.fillRect(x + 32 + dx * 25 - 4, y + 32 + dy * 25 - 4, 8, 8);
                if (zoom > 0.65) { ctx.font = "8px monospace"; ctx.textAlign = "center"; ctx.fillText(pin.name, x + 32 + dx * 14, y + 35 + dy * 14); }
            }
            if (def.electrical && layers.power) { ctx.fillStyle = COLORS.power; ctx.fillRect(x + 42, y + 44, 5, 5); }
            ctx.restore();
        }
        function draw() {
            const ctx = context; ctx.clearRect(0, 0, cssWidth, cssHeight); ctx.fillStyle = "#0c1920"; ctx.fillRect(0, 0, cssWidth, cssHeight);
            ctx.save(); ctx.translate(panX, panY); ctx.scale(zoom, zoom);
            const startX = Math.max(0, Math.floor(-panX / (64 * zoom))), endX = Math.min(board.width, Math.ceil((cssWidth - panX) / (64 * zoom)));
            const startY = Math.max(0, Math.floor(-panY / (64 * zoom))), endY = Math.min(board.height, Math.ceil((cssHeight - panY) / (64 * zoom)));
            ctx.fillStyle = "#102129"; ctx.fillRect(0, 0, board.width * 64, board.height * 64);
            ctx.lineWidth = 1 / zoom;
            for (let x = startX; x <= endX; x++) { ctx.strokeStyle = x % 4 === 0 ? "#23404b" : "#1b3039"; ctx.beginPath(); ctx.moveTo(x * 64, startY * 64); ctx.lineTo(x * 64, endY * 64); ctx.stroke(); }
            for (let y = startY; y <= endY; y++) { ctx.strokeStyle = y % 4 === 0 ? "#23404b" : "#1b3039"; ctx.beginPath(); ctx.moveTo(startX * 64, y * 64); ctx.lineTo(endX * 64, y * 64); ctx.stroke(); }
            if (layers.power) drawWires("power"); if (layers.signal) drawWires("signal");
            if (layers.device) {
                for (const device of board.devices.values()) if (device.type === "LiquidPump_Electric") {
                    const target = board.devices.get(board.params(device).target); if (!target) continue;
                    ctx.strokeStyle = "#5a9dc677"; ctx.lineWidth = 2; ctx.setLineDash([7, 6]); ctx.beginPath(); ctx.moveTo(device.x * 64 + 32, device.y * 64 + 32); ctx.lineTo(target.x * 64 + 32, target.y * 64 + 32); ctx.stroke(); ctx.setLineDash([]);
                }
                for (const device of board.devices.values()) if (inView(device.x, device.y)) drawDevice(device);
            }
            if (hover && board.inside(hover.x, hover.y)) {
                ctx.fillStyle = tool === "erase" ? "#ff758922" : "#b6e6da12"; ctx.fillRect(hover.x * 64, hover.y * 64, 64, 64); ctx.strokeStyle = tool === "erase" ? COLORS.error : "#85b8ab88"; ctx.lineWidth = 1.5; ctx.strokeRect(hover.x * 64 + 1, hover.y * 64 + 1, 62, 62);
                const def = registry.get(tool); if (def && !["wire", "signal", "cross", "databus"].includes(def.kind) && !board.at(hover.x, hover.y)) drawDevice({ id: "preview", type: tool, x: hover.x, y: hover.y, rotation, params: {}, runtime: {} }, true);
            }
            ctx.restore(); drawScope();
        }
        function animation(now) {
            frameId = 0; if (!visible) return;
            const elapsed = lastFrame ? Math.min(0.25, (now - lastFrame) / 1000) : 0; lastFrame = now;
            if (running && !document.hidden) {
                accumulator += elapsed * Number($("speed").value);
                let steps = 0;
                while (accumulator + 1e-9 >= M.STEP && steps++ < 80) { board.step(); sample(); accumulator -= M.STEP; }
            }
            if (now - lastReadout >= 150) { updateReadouts(); lastReadout = now; }
            draw(); frameId = requestAnimationFrame(animation);
        }
        function canvasPoint(event) { const rect = canvas.getBoundingClientRect(); return { px: event.clientX - rect.left, py: event.clientY - rect.top }; }
        function cellPoint(event) { const p = canvasPoint(event); return { ...p, x: Math.floor((p.px - panX) / (64 * zoom)), y: Math.floor((p.py - panY) / (64 * zoom)) }; }
        function zoomAt(factor, px = cssWidth / 2, py = cssHeight / 2) { const old = zoom; zoom = M.clamp(zoom * factor, 0.25, 2); panX = px - (px - panX) / old * zoom; panY = py - (py - panY) / old * zoom; }
        function paintCell(cell, erase = false) {
            if (!board.inside(cell.x, cell.y)) return;
            if (erase || tool === "erase") { const old = board.at(cell.x, cell.y); board.erase(cell.x, cell.y, $("erase-layer").value); if (old?.id === selected && !board.devices.has(selected)) selected = null; }
            else if (registry.has(tool)) {
                const def = registry.get(tool);
                if (["wire", "signal", "cross", "databus"].includes(def.kind)) board.paint(tool, cell.x, cell.y);
                else if (!board.at(cell.x, cell.y)) { const placed = board.add(tool, cell.x, cell.y, rotation); selected = placed.id; }
            }
        }
        function paintPath(from, to, erase) {
            let x = from.x, y = from.y; const max = board.width + board.height + 4; let steps = 0;
            while ((x !== to.x || y !== to.y) && steps++ < max) { if (x !== to.x) x += Math.sign(to.x - x); else y += Math.sign(to.y - y); paintCell({ x, y }, erase); }
        }
        canvas.addEventListener("pointerdown", event => {
            if (!ready || pointer || ![0, 1, 2].includes(event.button)) return;
            event.preventDefault(); canvas.focus(); const point = cellPoint(event);
            if (event.button === 1 || space) pointer = { mode: "pan", px: point.px, py: point.py, originX: panX, originY: panY };
            else {
                const previous = tool === "select" && event.button === 0 ? snapshot() : beforeEdit(), device = board.at(point.x, point.y);
                if (tool === "select" && event.button === 0) { select(device?.id); pointer = { mode: device ? "move" : "select", device: device?.id, previous }; }
                else {
                    const def = registry.get(tool), erasing = event.button === 2 || tool === "erase";
                    pointer = { mode: erasing || ["wire", "signal", "cross", "databus"].includes(def?.kind) ? "paint" : "place", previous, last: point, erase: erasing };
                    try { paintCell(point, erasing); board.refresh(); } catch (error) { message(error.message, true); }
                }
            }
            pointer.id = event.pointerId; canvas.setPointerCapture(event.pointerId);
        });
        canvas.addEventListener("pointermove", event => {
            const point = cellPoint(event); hover = point;
            const device = board.at(point.x, point.y), cell = M.key(point.x, point.y), net = board.powerNetworks[board.powerIndex.get(cell)];
            $("hover").textContent = board.inside(point.x, point.y) ? `${point.x}, ${point.y}${device ? ` · ${board.definition(device).name}` : ""}${net ? ` · ${format(net.voltage)} V / ${format(net.current)} A · ${net.status}` : ""}` : "棋盘之外 · 空格拖动返回";
            if (!pointer || pointer.id !== event.pointerId) return;
            if (pointer.mode === "pan") { panX = pointer.originX + point.px - pointer.px; panY = pointer.originY + point.py - pointer.py; }
            else if (pointer.mode === "move") { if (board.move(pointer.device, point.x, point.y)) { setRunning(false); board.refresh(); } }
            else if (pointer.mode === "paint" && (point.x !== pointer.last.x || point.y !== pointer.last.y)) { paintPath(pointer.last, point, pointer.erase); pointer.last = point; board.refresh(); }
        });
        function finishPointer(event) {
            if (!pointer || (event && event.pointerId !== pointer.id)) return;
            const completed = pointer; pointer = null;
            if (canvas.hasPointerCapture(completed.id)) canvas.releasePointerCapture(completed.id);
            if (completed.previous !== undefined) finishEdit(completed.previous);
        }
        canvas.addEventListener("pointerup", finishPointer); canvas.addEventListener("pointercancel", finishPointer); canvas.addEventListener("lostpointercapture", finishPointer);
        canvas.addEventListener("pointerleave", () => { if (!pointer) hover = null; });
        canvas.addEventListener("contextmenu", event => event.preventDefault());
        canvas.addEventListener("wheel", event => { event.preventDefault(); const p = canvasPoint(event); zoomAt(Math.exp(-event.deltaY * 0.001), p.px, p.py); }, { passive: false });
        // 原生拖放仅在落点提交一次布置事务，拖动途中不更改电路。
        $("palette").addEventListener("dragstart", event => {
            const component = event.target.closest("[data-tool]");
            if (!component || !registry.has(component.dataset.tool)) return;
            dragTool = component.dataset.tool;
            event.dataTransfer.setData("text/plain", dragTool);
            event.dataTransfer.effectAllowed = "copy";
            component.classList.add("cb-dragging");
        });
        $("palette").addEventListener("dragend", () => {
            dragTool = null; hover = null;
            $("palette").querySelectorAll(".cb-dragging").forEach(element => element.classList.remove("cb-dragging"));
        });
        canvas.addEventListener("dragover", event => {
            if (!dragTool || !ready) return;
            event.preventDefault(); event.dataTransfer.dropEffect = "copy"; hover = cellPoint(event);
        });
        canvas.addEventListener("drop", event => {
            event.preventDefault();
            const type = dragTool || event.dataTransfer.getData("text/plain"), point = cellPoint(event);
            dragTool = null; hover = null;
            if (!ready || !registry.has(type)) return;
            const previous = beforeEdit();
            try {
                const definition = registry.get(type);
                if (["wire", "signal", "cross", "databus"].includes(definition.kind)) board.paint(type, point.x, point.y);
                else selected = board.add(type, point.x, point.y, rotation).id;
                finishEdit(previous, "已拖放「" + definition.name + "」到 " + point.x + ", " + point.y + "。");
                setTool("select"); canvas.focus();
            } catch (error) { message(error.message, true); board.refresh(); }
        });
        canvas.addEventListener("dblclick", event => { const point = cellPoint(event), device = board.at(point.x, point.y); if (tool === "select" && device && board.definition(device).kind === "switch") transaction(() => board.configure(device.id, { on: !board.params(device).on }), "手动开关已切换；点击运行传递信号。"); });
        function rotateSelected() { const device = selectedDevice(); if (!device) return; transaction(() => { device.rotation = (device.rotation + 1) & 3; board.markDirty(); }); }
        function restoreHistory(from, to) {
            if (!from.length) return; setRunning(false);
            try { const restored = M.CircuitBoard.restore(registry, JSON.parse(from[from.length - 1])); to.push(snapshot()); from.pop(); board = restored; revision++; selected = null; wave = []; updateInspector(); updateReadouts(); persist(); syncEnvironment(); message("已恢复布置与初始参数；运行进度已重置。"); } catch (error) { message(error.message, true); }
        }
        function syncEnvironment() { $("light").value = board.environment.light; $("light-value").textContent = `${board.environment.light}%`; $("temperature").value = board.environment.temperature; }
        function replaceBoard(next, text) { const previous = beforeEdit(); board = next; selected = null; wave = []; syncEnvironment(); finishEdit(previous, text); fit(); }
        const actions = {
            run: () => setRunning(!running), step: () => { setRunning(false); board.step(); sample(); updateReadouts(); },
            reset: () => { setRunning(false); board.reset(); wave = []; updateReadouts(); message("已重置时间、记忆状态和储量，布置保留。"); },
            undo: () => restoreHistory(undo, redo), redo: () => restoreHistory(redo, undo),
            rotate: () => { if (tool === "select") rotateSelected(); else { rotation = (rotation + 1) & 3; renderPaletteSelection(); } }, "rotate-selected": rotateSelected,
            delete: () => { const device = selectedDevice(); if (device) transaction(() => { board.erase(device.x, device.y, "device"); selected = null; }); },
            duplicate: () => { const device = selectedDevice(); if (!device) return; transaction(() => { for (let radius = 1; radius < 5; radius++) for (const [dx, dy] of M.DIRECTIONS) { const x = device.x + dx * radius, y = device.y + dy * radius; if (board.inside(x, y) && !board.at(x, y) && !board.signal.has(M.key(x, y)) && !(board.definition(device).kind === "relay" && board.power.has(M.key(x, y)))) { selected = board.add(device.type, x, y, device.rotation, device.params).id; return; } } throw new Error("附近没有可复制到的空格。"); }); },
            fit, "zoom-in": () => zoomAt(1.2), "zoom-out": () => zoomAt(1 / 1.2),
            expand: () => { const expanded = shell.classList.toggle("cb-expanded"); document.body.classList.toggle("circuit-board-fullscreen", expanded); button("expand").textContent = expanded ? "⛶ 退出铺满" : "⛶ 铺满网页"; resize(); },
            clear: () => { if (confirm("新建空白棋盘？当前布置可用撤销恢复。")) replaceBoard(new M.CircuitBoard(registry), "已创建空白棋盘。"); },
            export: () => { const blob = new Blob([JSON.stringify(board.serialize(), null, 2)], { type: "application/json" }), url = URL.createObjectURL(blob), anchor = document.createElement("a"); anchor.href = url; anchor.download = `FlatWorld-电路方案-${new Date().toISOString().slice(0, 10)}.json`; anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); message("已导出布置及初始参数，不包含运行进度。"); },
            import: () => $("file").click(),
            "quick-add": openQuickPicker, "toggle-inspector": toggleInspector
        };
        host.addEventListener("click", event => {
            const quick = event.target.closest("[data-quick-tool]");
            if (quick) { chooseQuickTool(quick.dataset.quickTool); return; }
            const categoryButton = event.target.closest("[data-category]");
            if (categoryButton) {
                category = categoryButton.dataset.category;
                host.querySelectorAll("[data-category]").forEach(element => element.setAttribute("aria-pressed", String(element === categoryButton)));
                renderPalette(); return;
            }
            const toolButton = event.target.closest("[data-tool]"); if (toolButton) { setTool(toolButton.dataset.tool); return; }
            const action = event.target.closest("[data-action]")?.dataset.action;
            if (!action || (!ready && !["expand", "fit", "zoom-in", "zoom-out"].includes(action))) return;
            try { actions[action]?.(); } catch (error) { setRunning(false); message(error.message, true); }
        });
        host.addEventListener("change", event => {
            const element = event.target;
            if (element.dataset.param && selectedDevice()) {
                const name = element.dataset.param, rule = M.PARAMS[name], value = rule.type === "boolean" ? element.checked : ["target", "select", "word64"].includes(rule.type) ? element.value : element.value.trim() === "" ? NaN : Number(element.value);
                transaction(() => board.configure(selected, { [name]: value }));
            }
            if (element.dataset.layer) layers[element.dataset.layer] = element.checked;
        });
        $("search").addEventListener("input", renderPalette);
        $("quick-search").addEventListener("input", () => { pickerIndex = 0; renderQuickPicker(); });
        $("quick-search").addEventListener("keydown", event => {
            if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); closeQuickPicker(true); return; }
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                pickerIndex = Math.max(0, Math.min(pickerMatches.length - 1, pickerIndex + (event.key === "ArrowDown" ? 1 : -1)));
                renderQuickPicker();
                $("quick-results").querySelector(".chosen")?.scrollIntoView({ block: "nearest" });
            }
            if (event.key === "Enter" && pickerMatches[pickerIndex]) {
                event.preventDefault(); chooseQuickTool(pickerMatches[pickerIndex].id);
            }
        });
        host.addEventListener("pointerdown", event => {
            if (pickerOpen && !event.target.closest(".cb-quick-picker, [data-action='quick-add']")) closeQuickPicker();
        });
        $("example").addEventListener("change", event => { const name = event.target.value; event.target.value = ""; if (!name || !ready) return; if (!confirm("用示例替换当前布置？原布置可用撤销恢复。")) return; try { replaceBoard(M.createExample(registry, name), "示例已就绪。点击运行，选择元件查看实时数据。"); } catch (error) { message(error.message, true); } });
        $("light").addEventListener("input", event => $("light-value").textContent = `${event.target.value}%`);
        $("light").addEventListener("change", event => transaction(() => { board.environment.light = Number(event.target.value); }, "实验光照已更新。"));
        $("temperature").addEventListener("change", event => transaction(() => { const value = event.target.value.trim() === "" ? NaN : Number(event.target.value); if (!Number.isFinite(value) || value < -1000 || value > 1e6) throw new Error("请输入有效实验温度。"); board.environment.temperature = value; }, "实验温度已更新。"));
        $("file").addEventListener("change", async event => {
            const file = event.target.files[0]; event.target.value = ""; if (!file) return;
            const startRevision = revision, id = ++importId; setRunning(false);
            try {
                if (file.size > 2 * 1024 * 1024) throw new Error("方案文件不能超过 2 MB。");
                const data = JSON.parse(await file.text());
                if (id !== importId || revision !== startRevision) throw new Error("读取期间布置已改变，已取消旧导入，请重新选择文件。");
                const next = M.CircuitBoard.restore(registry, data);
                if (!confirm("导入方案将替换当前布置，原布置可撤销恢复。继续吗？")) return;
                replaceBoard(next, "方案导入成功，运行进度从初始状态开始。");
            } catch (error) { message(`导入失败，当前布置保留：${error.message}`, true); }
        });
        host.addEventListener("keydown", event => {
            if (["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName)) return;
            if (!event.ctrlKey && !event.metaKey && !event.altKey) {
                const key = event.key.toLowerCase();
                if (key === "f") { event.preventDefault(); openQuickPicker(); return; }
                if (key === "v") { event.preventDefault(); setTool("select"); return; }
                if (key === "e") { event.preventDefault(); setTool("erase"); return; }
                if (key === "w") { event.preventDefault(); setTool(registry.has("Wire_Copper") ? "Wire_Copper" : Array.from(registry.values()).find(def => def.kind === "wire")?.id || "select"); return; }
                if (key === "s") { event.preventDefault(); setTool("sim:signal"); return; }
                if (key === "i") { event.preventDefault(); toggleInspector(); return; }
                if (event.key === "/") { event.preventDefault(); $("search").focus(); $("search").select(); return; }
            }
            if (event.key === " ") { event.preventDefault(); space = true; }
            if (event.key.toLowerCase() === "r") { event.preventDefault(); actions.rotate(); }
            if (event.key === "Delete" || event.key === "Backspace") { event.preventDefault(); actions.delete(); }
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") { event.preventDefault(); event.shiftKey ? actions.redo() : actions.undo(); }
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "y") { event.preventDefault(); actions.redo(); }
        });
        document.addEventListener("keyup", event => { if (event.key === " ") space = false; });
        document.addEventListener("keydown", event => { if (visible && !pickerOpen && event.key === "Escape" && shell.classList.contains("cb-expanded")) actions.expand(); });
        document.addEventListener("visibilitychange", () => { if (document.hidden) { setRunning(false); space = false; persist(); } });
        window.addEventListener("blur", () => { space = false; finishPointer(); });
        window.addEventListener("pagehide", persist);
        const observer = new ResizeObserver(resize); observer.observe(canvas.parentElement);
        updateInspector();
        return {
            setCatalog(catalog, entries) {
                if (!catalog?.Nodes) { message("游戏电气目录未读取成功，请使用顶部“重新读取 JSON”。", true); return; }
                try {
                    const nextRegistry = M.createRegistry(catalog, entries), nextSignature = JSON.stringify(Array.from(nextRegistry.values())); let nextBoard;
                    // 无关档案刷新不重置电路进度、选择和撤销记录。
                    if (ready && catalogSignature === nextSignature) return;
                    if (ready) nextBoard = M.CircuitBoard.restore(nextRegistry, board.serialize());
                    else {
                        let raw = null; try { raw = localStorage.getItem(storageKey); } catch { savedStatus = "浏览器存储不可用，请导出方案"; }
                        if (raw) try { nextBoard = M.CircuitBoard.restore(nextRegistry, JSON.parse(raw)); } catch (error) { try { localStorage.setItem(`${storageKey}:recovery`, raw); } catch {} savedStatus = `旧自动保存不能恢复（原文保留 recovery）：${error.message}`; }
                        if (!nextBoard) nextBoard = M.createExample(nextRegistry, "blink");
                    }
                    registry = nextRegistry; board = nextBoard; catalogSignature = nextSignature; ready = true; setRunning(false); revision++; selected = null; wave = []; undo = []; redo = [];
                    syncEnvironment(); renderCategories(); renderPalette(); renderQuickTools(); updateInspector(); updateReadouts(); resize(); needsFit = !visible; if (visible) fit(); message(`${Array.from(registry.values()).filter(d => d.game).length} 种游戏元件（含抽象光纤适配）+ ${Array.from(registry.values()).filter(d => !d.game).length} 种模拟扩展。${savedStatus}。`);
                } catch (error) { message(`目录刷新未应用，原棋盘保留：${error.message}`, true); }
            },
            show() { visible = true; lastFrame = 0; resize(); if (needsFit) { fit(); needsFit = false; } if (!frameId) frameId = requestAnimationFrame(animation); },
            hide() { visible = false; setRunning(false); if (frameId) cancelAnimationFrame(frameId); frameId = 0; space = false; closeQuickPicker(); finishPointer(); if (shell.classList.contains("cb-expanded")) actions.expand(); persist(); },
            getBoard: () => board,
            getCellClientPoint(x, y) { const rect = canvas.getBoundingClientRect(); return { x: rect.left + panX + (x + 0.5) * 64 * zoom, y: rect.top + panY + (y + 0.5) * 64 * zoom }; },
            setError: text => message(text, true)
        };
    }
    root.ItemCircuitBoard = { create };
})(globalThis);

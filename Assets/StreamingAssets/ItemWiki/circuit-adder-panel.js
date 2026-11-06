/* 64 位进位链的专用观察器；只读模型状态，不参与计算或保存。 */
(function (root) {
    "use strict";
    const M = root.ItemCircuitModel;
    const ROWS = 4, COLS = 16, PAGE = 16;
    const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));
    const clamp = (n, min, max) => Math.min(max, Math.max(min, n));

    function create({ root: host, getBoard, setOperands, focusAdder }) {
        if (!host || !M) return null;
        let sourceIds = "", offset = 0, format = "hex", message = "";

        function findCircuit() {
            const board = getBoard();
            const adder = Array.from(board.devices.values()).find(d => board.definition(d).operation === "rippleAdder64");
            if (!adder) return null;
            const sources = {};
            for (const [name, direction] of [["a", 2], ["b", 3]]) {
                const input = board.busPinNetwork(adder, direction);
                sources[name] = Array.from(board.devices.values()).find(device =>
                    board.definition(device).kind === "wordsource" && input !== undefined &&
                    board.busPinNetwork(device, 0) === input) || null;
            }
            return { board, adder, sources };
        }
        function getElement(name) { return host.querySelector(`[data-adder-role="${name}"]`); }
        function build(circuit) {
            host.hidden = false;
            host.innerHTML = `<section class="cb-adder-panel">
                <div class="cb-adder-heading"><div><div class="cb-adder-kicker">64 × FULL ADDER · RIPPLE CARRY</div><h3>64 位加法工作台</h3><p>棋盘上是模块化封装，内部运行 64 级 XOR / AND / OR，最高进位作为第 65 位。结果不使用浮点数。</p></div>
                <button type="button" data-adder-action="focus">⌖ 定位加法器</button></div>
                <div class="cb-adder-operands">
                    <label><span>A · 64 位输入 <small>支持 0x、0b、十进制</small></span><textarea data-adder-operand="a" aria-label="64 位加法器输入 A" rows="3" spellcheck="false" placeholder="0x..."></textarea></label>
                    <label><span>B · 64 位输入 <small>支持 0x、0b、十进制</small></span><textarea data-adder-operand="b" aria-label="64 位加法器输入 B" rows="3" spellcheck="false" placeholder="0x..."></textarea></label>
                </div>
                <div class="cb-adder-quick"><button type="button" data-adder-action="max">全进位：最大值 + 1</button><button type="button" data-adder-action="simple">普通加法：ABCDEF + 123456</button><span>修改输入后失焦即可重新计算</span></div>
                <div class="cb-adder-results">
                    <div class="cb-adder-result-heading"><strong>SUM · 精确结果</strong><div><select data-adder-role="format" aria-label="输出进制"><option value="hex">十六进制</option><option value="decimal">十进制</option><option value="binary">二进制</option></select><button type="button" data-adder-action="copy">复制结果</button></div></div>
                    <div data-adder-role="status" class="cb-adder-status" role="status"></div>
                    <pre data-adder-role="result" class="cb-adder-result">等待接线和供电…</pre>
                    <div data-adder-role="metrics" class="cb-adder-metrics"></div>
                </div>
                <div class="cb-adder-trace-heading"><div><h4>64 级进位链</h4><p>从第 0 位（最低位）开始，蓝色格表示该级有进位输出。点击任意格查看 XOR、AND、OR 的中间值。</p></div><div class="cb-adder-jump">
                    <button type="button" data-adder-action="first">最低位</button><button type="button" data-adder-action="previous">← 16 位</button>
                    <input data-adder-role="offset" aria-label="查看的起始位" type="number" min="0" max="48" step="1" value="0">
                    <button type="button" data-adder-action="next">16 位 →</button><button type="button" data-adder-action="last">最高位</button>
                </div></div>
                <canvas data-adder-role="heatmap" class="cb-adder-heatmap" width="800" height="152" aria-label="64 级全加器进位图，点击定位对应位"></canvas>
                <div data-adder-role="bit-table" class="cb-adder-bit-table"></div>
            </section>`;
        }
        function outputText(runtime) {
            if (!runtime.sumHex) return "";
            const n = BigInt(runtime.sumHex);
            return format === "decimal" ? n.toString(10) : format === "binary" ? "0b" + n.toString(2) : runtime.sumHex;
        }
        function paintCarry(stages) {
            const canvas = getElement("heatmap");
            if (!canvas) return;
            const ctx = canvas.getContext("2d");
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = "#11252d"; ctx.fillRect(0, 0, canvas.width, canvas.height);
            for (let i = 0; i < M.WORD_BITS; i++) {
                const x = (i % COLS) * 50 + 3, y = Math.floor(i / COLS) * 38 + 4;
                ctx.fillStyle = !stages ? "#304955" : stages[i].carryOut ? "#5fc8f5" : stages[i].sum ? "#3fa58d" : "#31515b";
                ctx.fillRect(x, y, 44, 28);
                if (i >= offset && i < offset + PAGE) { ctx.strokeStyle = "#f7d37c"; ctx.lineWidth = 1.5; ctx.strokeRect(x - 1, y - 1, 46, 30); }
            }
        }
        function renderBitTable(stages) {
            const target = getElement("bit-table");
            if (!target) return;
            getElement("offset").value = String(offset);
            if (!stages) { target.textContent = "输入未就绪或供电不足，无法展示逐位进位过程。"; return; }
            const slice = stages.slice(offset, offset + PAGE);
            target.innerHTML = `<table><thead><tr><th>位</th><th>A</th><th>B</th><th>进位入</th><th>A XOR B</th><th>和</th><th>进位出</th></tr></thead><tbody>${slice.map(s =>
                `<tr class="${s.carryOut ? "cb-adder-carry" : ""}"><td>#${s.index}</td><td>${s.a}</td><td>${s.b}</td><td>${s.carryIn}</td><td>${s.a ^ s.b}</td><td>${s.sum}</td><td>${s.carryOut}</td></tr>`
            ).join("")}</tbody></table>`;
        }
        function render() {
            const circuit = findCircuit();
            if (!circuit) { host.hidden = true; sourceIds = ""; return; }
            const { board, adder, sources } = circuit, runtime = adder.runtime;
            const signature = `${adder.id}|${sources.a?.id || ""}|${sources.b?.id || ""}`;
            if (signature !== sourceIds) { sourceIds = signature; offset = 0; format = "hex"; message = ""; build(circuit); }
            for (const name of ["a", "b"]) {
                const textarea = host.querySelector(`[data-adder-operand="${name}"]`);
                if (!textarea) continue;
                const source = sources[name];
                textarea.disabled = !source;
                textarea.placeholder = source ? "输入非负整数" : `输入 ${name.toUpperCase()} 的宽数据总线未连接`;
                if (document.activeElement !== textarea) textarea.value = source ? board.params(source).wordHex : "";
            }
            const status = getElement("status"), hex = runtime.sumHex;
            const display = Array.from(board.devices.values()).find(d => board.definition(d).kind === "worddisplay" &&
                board.busPinNetwork(d, 2) === board.busPinNetwork(adder, 0));
            const displayReady = !!display && display.runtime.sumHex === runtime.sumHex && !!hex;
            status.textContent = `计算状态：${runtime.wordStatus || "等待输入"} · 结果显示器${displayReady ? "已收到完整数据" : "尚未接收到结果"}${message ? " · " + message : ""}`;
            status.classList.toggle("cb-adder-invalid", !hex);
            const result = getElement("result"); result.textContent = hex ? outputText(runtime) : "—（检查 A/B 蓝色数据总线与黄色电力线）";
            const carryCount = runtime.stages ? runtime.stages.filter(stage => stage.carryOut).length : 0;
            getElement("metrics").innerHTML = `<span>位宽 <b>64 + 64 → 65</b></span><span>已计算级数 <b>${runtime.stages?.length || 0} / 64</b></span><span>产生进位的级数 <b>${carryCount}</b></span><span>第 65 位 <b>${hex ? runtime.carry : "—"}</b></span><span>模块供电 <b>${runtime.watts || 0} W</b></span>`;
            paintCarry(runtime.stages);
            renderBitTable(runtime.stages);
        }
        host.addEventListener("change", event => {
            const inputName = event.target.dataset.adderOperand;
            if (inputName) {
                const circuit = findCircuit(), source = circuit?.sources[inputName];
                if (!source) return;
                message = "";
                try { M.parseWord64(event.target.value); setOperands([[source.id, event.target.value]], "64 位输入已更新。"); }
                catch (error) { message = error.message; }
                render();
            } else if (event.target.dataset.adderRole === "offset") {
                const n = Number(event.target.value);
                offset = Number.isFinite(n) ? clamp(Math.round(n), 0, M.WORD_BITS - PAGE) : 0; render();
            } else if (event.target.dataset.adderRole === "format") { format = event.target.value; render(); }
        });
        host.addEventListener("click", async event => {
            const action = event.target.closest("[data-adder-action]")?.dataset.adderAction;
            if (!action) return;
            const circuit = findCircuit(); if (!circuit) return;
            const { adder, sources } = circuit;
            if (action === "focus") focusAdder(adder.id);
            if (action === "max" || action === "simple") {
                if (!sources.a || !sources.b) { message = "请先把两路输入寄存器用蓝色总线连接到加法器。"; render(); return; }
                message = "";
                setOperands([[sources.a.id, action === "max" ? "0x" + "F".repeat(16) : "0xABCDEF"],
                    [sources.b.id, action === "max" ? "0x1" : "0x123456"]], "已加载加法器测试数值。");
                render();
            }
            if (action === "copy") {
                const value = outputText(adder.runtime);
                if (!value) return;
                try { await navigator.clipboard.writeText(value); message = "完整结果已复制。"; }
                catch { message = "复制未获浏览器许可，可直接选中下方完整结果。"; }
                render();
            }
            if (action === "first") offset = 0;
            if (action === "last") offset = M.WORD_BITS - PAGE;
            if (action === "previous") offset = clamp(offset - PAGE, 0, M.WORD_BITS - PAGE);
            if (action === "next") offset = clamp(offset + PAGE, 0, M.WORD_BITS - PAGE);
            render();
        });
        host.addEventListener("click", event => {
            if (event.target.dataset.adderRole !== "heatmap") return;
            const rect = event.target.getBoundingClientRect();
            const col = clamp(Math.floor((event.clientX - rect.left) / rect.width * COLS), 0, COLS - 1);
            const row = clamp(Math.floor((event.clientY - rect.top) / rect.height * ROWS), 0, ROWS - 1);
            offset = clamp(row * COLS + col - 7, 0, M.WORD_BITS - PAGE);
            render();
        });
        return { render };
    }
    root.ItemCircuitAdderPanel = { create };
})(globalThis);

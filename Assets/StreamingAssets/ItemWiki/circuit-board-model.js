/* 网格电路的纯数据模拟；不读写游戏配置，不依赖 DOM。 */
(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.ItemCircuitModel = api;
})(globalThis, function () {
    "use strict";
    const STEP = 0.05;
    const DIRECTIONS = [[1, 0], [0, 1], [-1, 0], [0, -1]];
    const key = (x, y) => `${x},${y}`;
    const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
    const number = (value, fallback = 0) => Number.isFinite(value) ? value : fallback;
    const copy = value => JSON.parse(JSON.stringify(value));
    const PARAMS = {
        enabled: { label: "启用", type: "boolean" }, on: { label: "开关输出 1", type: "boolean" },
        watts: { label: "额定功率 W", min: 0, max: 1e7, step: 10 },
        voltage: { label: "额定电压 V", min: 0.1, max: 1e5, step: 1 },
        capacity: { label: "电池容量 J", min: 1, max: 1e12, step: 1000 },
        chargeRate: { label: "最大充电 W", min: 0, max: 1e7, step: 10 },
        dischargeRate: { label: "最大放电 W", min: 0, max: 1e7, step: 10 },
        initialPercent: { label: "初始储量 %（重置生效）", min: 0, max: 100, step: 5 },
        sourcePercent: { label: "外部动力 / 出力 %", min: 0, max: 100, step: 5 },
        high: { label: "输出 1 持续秒数", min: STEP, max: 86400, step: STEP },
        low: { label: "输出 0 持续秒数", min: STEP, max: 86400, step: STEP },
        delay: { label: "持续高电平延时 s", min: STEP, max: 86400, step: STEP },
        threshold: { label: "传感器阈值", min: -1000, max: 1e6, step: 1 },
        compare: { label: "条件", type: "select", options: [["below", "低于阈值"], ["above", "高于阈值"]] },
        target: { label: "关联实验对象", type: "target" },
        count: { label: "计数达到 N 输出 1", min: 1, max: 1e6, step: 1 },
        tankCapacity: { label: "水箱容量 L", min: 1, max: 1e9, step: 10 },
        drainRate: { label: "水箱持续耗水 L/s", min: 0, max: 1e6, step: 0.1 },
        flowRate: { label: "水泵满功率流量 L/s", min: 0, max: 1e6, step: 0.5 }
    };
    const EXTENSIONS = [
        { id: "sim:signal", name: "信号线", kind: "signal", group: "线路", symbol: "01", description: "只传递 0/1，不输送能量。同网任一输出为 1，整网为 1。" },
        { id: "sim:cross", name: "信号跨线桥", kind: "cross", group: "线路", symbol: "╋", description: "水平与垂直信号穿过同格但不相连；可与电线叠放。" },
        { id: "sim:switch", name: "手动开关", kind: "switch", group: "控制", symbol: "⏻", defaults: { on: true }, description: "双击元件或修改参数，切换输出。逻辑元件理想化，不额外耗电。" },
        { id: "sim:timer", name: "循环计时器", kind: "timer", group: "控制", symbol: "◷", defaults: { high: 1, low: 1 }, description: "从高电平开始循环；以 0.05 秒为时间精度。" },
        { id: "sim:and", name: "与门 AND", kind: "and", group: "逻辑", symbol: "&", description: "A、B 同为 1 才输出 1。" },
        { id: "sim:or", name: "或门 OR", kind: "or", group: "逻辑", symbol: "≥1", description: "A、B 任一为 1 就输出 1。" },
        { id: "sim:not", name: "非门 NOT", kind: "not", group: "逻辑", symbol: "!", description: "输入 0 输出 1，输入 1 输出 0。未接线的逻辑输入为 0。" },
        { id: "sim:xor", name: "异或门 XOR", kind: "xor", group: "逻辑", symbol: "=1", description: "两个输入不同才输出 1。" },
        { id: "sim:latch", name: "记忆器 SR", kind: "latch", group: "逻辑", symbol: "SR", description: "左 S 置 1、上 R 复位；同时为 1 时复位优先，其他时候保持。" },
        { id: "sim:toggle", name: "翻转器 T", kind: "toggle", group: "逻辑", symbol: "T", description: "每次 A 从 0 变 1 时翻转输出；上方 R 复位优先。" },
        { id: "sim:delay", name: "通电延时", kind: "delay", group: "控制", symbol: "⌛", defaults: { delay: 1 }, description: "输入连续为 1 达到设定时长才输出 1；输入变 0 立即复位。" },
        { id: "sim:counter", name: "脉冲计数器", kind: "counter", group: "控制", symbol: "#", defaults: { count: 3 }, description: "数 A 的上升沿，达到 N 输出 1；上方 R 清零。" },
        { id: "sim:relay", name: "电力继电器", kind: "relay", group: "控制", symbol: "⎍", description: "上方信号为 1 时接通左右电线；中心禁止铺电线，绕路供电仍然有效。" },
        { id: "sim:level", name: "液位传感器", kind: "level", group: "检测", symbol: "%", defaults: { target: "", threshold: 30, compare: "below" }, description: "检测选定实验水箱的储水百分比，必须指定目标。" },
        { id: "sim:charge", name: "电量传感器", kind: "charge", group: "检测", symbol: "SOC", defaults: { target: "", threshold: 30, compare: "below" }, description: "检测选定电池的储能百分比，必须指定目标。" },
        { id: "sim:temperature", name: "温度传感器", kind: "temperature", group: "检测", symbol: "℃", defaults: { threshold: 30, compare: "above" }, description: "检测工具栏中的实验温度；不模拟世界传热。" },
        { id: "sim:light", name: "光照传感器", kind: "light", group: "检测", symbol: "☀", defaults: { threshold: 30, compare: "below" }, description: "检测工具栏中的实验光照百分比。" },
        { id: "sim:probe", name: "信号指示灯", kind: "probe", group: "检测", symbol: "●", description: "只观察左侧信号，不耗电；实际用电灯泡请使用游戏电灯。" },
        { id: "sim:solar", name: "太阳能板", kind: "generator", group: "实验设备", symbol: "▦", electrical: true, defaults: { enabled: true, watts: 600, voltage: 120, sourcePercent: 100 }, description: "扩展电源：输出随实验光照变化，不代表游戏已有太阳能机制。" },
        { id: "sim:source", name: "可调电源", kind: "generator", group: "实验设备", symbol: "G", electrical: true, defaults: { enabled: true, watts: 600, voltage: 120, sourcePercent: 100 }, description: "用于测试供需、充放电与不同电压，不消耗燃料。" },
        { id: "sim:load", name: "可调负载", kind: "consumer", group: "实验设备", symbol: "W", electrical: true, defaults: { enabled: true, watts: 100, voltage: 120 }, description: "通用耗电设备；左侧自动化口未接线时运行，接线后由 0/1 控制。" },
        { id: "sim:tank", name: "实验水箱", kind: "tank", group: "实验设备", symbol: "≈", defaults: { tankCapacity: 100, initialPercent: 20, drainRate: 1 }, description: "通过水泵的“关联实验对象”接收水；蓝色虚线表示抽象水路，不是电线。" }
    ];

    // 只从实际目录登记游戏电气设备；新增控制能力显式标记为模拟扩展。
    function createRegistry(catalog, entries = []) {
        const registry = new Map();
        const names = new Map(entries.map(entry => [entry.final?.id || entry.id, entry.displayName || entry.final?.gameName]));
        for (const node of catalog?.Nodes || []) {
            const e = node.Electrical;
            if (!e) continue;
            const kind = e.Role === "converter" ? "generator" : e.Role;
            if (!["wire", "generator", "consumer", "battery"].includes(kind)) continue;
            const defaults = kind === "wire" ? {} : { enabled: true, voltage: e.NominalVoltage ?? 120 };
            if ((kind === "generator" && !e.UnlimitedGeneration) || kind === "consumer") defaults.watts = e.PowerWatts || 0;
            if (kind === "generator") defaults.sourcePercent = 100;
            if (kind === "battery") Object.assign(defaults, { capacity: e.CapacityJoules, chargeRate: e.MaxChargeWatts, dischargeRate: e.MaxDischargeWatts, initialPercent: 50 });
            if (node.Id === "LiquidPump_Electric") Object.assign(defaults, { flowRate: 5, target: "" });
            const description = kind === "wire" ? "读取游戏线缆额定参数；过载仅告警，不自动烧毁或断路。" :
                e.Role === "converter" ? "游戏双向电机在这里简化为发电模式；外部机械动力由出力百分比代替，不模拟传动。" :
                kind === "battery" ? "优先用发电供负载，余电充电、缺口放电；储能与速率读取游戏配置。" :
                node.Id === "LiquidPump_Electric" ? "额定功率来自游戏；实验流量和水箱关联是模拟扩展，不模拟管道压力。" :
                "额定电气参数来自游戏；左侧信号控制口为模拟扩展，仅模拟耗电与运行状态。";
            registry.set(node.Id, { id: node.Id, name: names.get(node.Id) || node.Id, kind, game: true, group: kind === "wire" ? "线路" : "游戏设备", electrical: kind !== "wire", connection: e.Connection || "cell", electricalConfig: copy(e), defaults,
                symbol: kind === "wire" ? "ϟ" : kind === "battery" ? "BAT" : kind === "generator" ? "G" : node.Id === "ElectricLightBulb" ? "☼" : node.Id === "LiquidPump_Electric" ? "P" : "W", description });
        }
        // 飞船光纤不是 Electrical 节点，只有正式物品存在且类型吻合才登记其抽象信号适配。
        const fiber = entries.find(entry => (entry.final?.id || entry.id) === "ShipFiber");
        if (fiber?.final?.modules?.["ship-part"]?.parameters?.Configuration?.Kind === "Fiber") {
            registry.set("ShipFiber", { id: "ShipFiber", name: names.get("ShipFiber") || "飞船光纤", kind: "signal", game: true, group: "线路", symbol: "◇", defaults: {}, connection: "cell",
                description: "游戏已有的飞船控制光纤。这里适配为可与普通信号线连接的 0/1 线路，不模拟船体归属和飞船控制协议。" });
        }
        for (const definition of EXTENSIONS) registry.set(definition.id, { game: false, defaults: {}, connection: "cell", ...copy(definition) });
        return registry;
    }

    function parameters(definition, device) { return { ...definition.defaults, ...device.params }; }
    function validateParameters(definition, input) {
        if (!input || typeof input !== "object" || Array.isArray(input)) throw new Error("元件参数格式错误。");
        const result = {};
        for (const [name, value] of Object.entries(input)) {
            if (!Object.hasOwn(definition.defaults, name)) throw new Error(`不支持的参数：${name}`);
            const rule = PARAMS[name];
            if (rule.type === "boolean") {
                if (typeof value !== "boolean") throw new Error(`${rule.label} 必须为布尔值。`);
            } else if (rule.type === "target") {
                if (typeof value !== "string" || value.length > 80) throw new Error("目标 ID 无效。");
            } else if (rule.type === "select") {
                if (!rule.options.some(option => option[0] === value)) throw new Error("比较条件无效。");
            } else if (!Number.isFinite(value) || value < rule.min || value > rule.max) throw new Error(`${rule.label} 超出范围。`);
            if (name === "count" && !Number.isInteger(value)) throw new Error("计数阈值必须为整数。");
            result[name] = value;
        }
        return result;
    }
    function offset(direction, rotation) { return DIRECTIONS[(direction + rotation) & 3]; }
    function position(device, direction) { const [dx, dy] = offset(direction, device.rotation); return { x: device.x + dx, y: device.y + dy }; }
    function signalPins(definition) {
        if (["wire", "signal", "cross", "tank", "battery"].includes(definition.kind)) return [];
        if (definition.kind === "relay") return [{ name: "C", direction: 3, input: true }];
        if (["consumer", "generator", "probe"].includes(definition.kind)) return [{ name: "C", direction: 2, input: true }];
        const pins = [{ name: "Q", direction: 0, input: false }];
        if (["and", "or", "not", "xor", "latch", "toggle", "delay", "counter"].includes(definition.kind)) pins.push({ name: definition.kind === "latch" ? "S" : "A", direction: 2, input: true });
        if (["and", "or", "xor", "latch", "toggle", "counter"].includes(definition.kind)) pins.push({ name: ["latch", "toggle", "counter"].includes(definition.kind) ? "R" : "B", direction: 3, input: true });
        return pins;
    }
    function powerPosition(definition, device) {
        const direction = { right: 0, down: 1, left: 2, up: 3 }[definition.connection];
        return direction === undefined ? { x: device.x, y: device.y } : position(device, direction);
    }

    class CircuitBoard {
        constructor(registry, width = 64, height = 40) {
            this.registry = registry; this.width = width; this.height = height;
            this.devices = new Map(); this.deviceCells = new Map(); this.power = new Map(); this.signal = new Map();
            this.environment = { temperature: 25, light: 100 };
            this.ticks = 0; this.nextId = 1; this.dirty = true; this.powerDirty = true;
            this.signalNetworks = []; this.powerNetworks = []; this.signalIndex = new Map(); this.powerIndex = new Map();
            this.signalValues = []; this.warnings = [];
        }
        get time() { return this.ticks * STEP; }
        inside(x, y) { return Number.isInteger(x) && Number.isInteger(y) && x >= 0 && y >= 0 && x < this.width && y < this.height; }
        at(x, y) { return this.devices.get(this.deviceCells.get(key(x, y))); }
        definition(device) { return this.registry.get(device.type); }
        params(device) { return parameters(this.definition(device), device); }
        markDirty() { this.dirty = true; this.powerDirty = true; }
        add(type, x, y, rotation = 0, params = {}) {
            const definition = this.registry.get(type);
            if (!definition) throw new Error(`未知元件：${type}`);
            if (!this.inside(x, y)) throw new Error("超出棋盘边界。");
            if (["wire", "signal", "cross"].includes(definition.kind)) return this.paint(type, x, y);
            if (this.at(x, y)) throw new Error("该格已有设备，请先移动或删除。");
            if (this.signal.has(key(x, y))) throw new Error("设备中心不能穿过信号线，请先擦除该格信号线。");
            if (definition.kind === "relay" && this.power.has(key(x, y))) throw new Error("继电器中心不能铺电线，请先擦除中心电线。");
            const device = { id: `d${this.nextId++}`, type, x, y, rotation: rotation & 3, params: validateParameters(definition, params), runtime: {} };
            this.devices.set(device.id, device); this.deviceCells.set(key(x, y), device.id); this.resetDevice(device); this.markDirty();
            return device;
        }
        paint(type, x, y) {
            const definition = this.registry.get(type);
            if (!definition || !["wire", "signal", "cross"].includes(definition.kind) || !this.inside(x, y)) return false;
            const cell = key(x, y), device = this.at(x, y);
            if (definition.kind === "wire") {
                if (device && this.definition(device).kind === "relay") return false;
                if (this.power.get(cell) === type) return false;
                this.power.set(cell, type);
            } else {
                if (device) return false;
                if (this.signal.get(cell) === type) return false;
                this.signal.set(cell, type);
            }
            this.markDirty(); return true;
        }
        erase(x, y, layer) {
            const cell = key(x, y);
            const changed = layer === "power" ? this.power.delete(cell) : layer === "signal" ? this.signal.delete(cell) : this.devices.delete(this.deviceCells.get(cell));
            if (layer === "device" && changed) this.deviceCells.delete(cell);
            if (changed) this.markDirty();
            return changed;
        }
        move(id, x, y) {
            const device = this.devices.get(id);
            if (!device || !this.inside(x, y) || (this.at(x, y) && this.at(x, y) !== device) || this.signal.has(key(x, y))) return false;
            if (device.x === x && device.y === y) return false;
            if (this.definition(device).kind === "relay" && this.power.has(key(x, y))) return false;
            this.deviceCells.delete(key(device.x, device.y)); device.x = x; device.y = y; this.deviceCells.set(key(x, y), id); this.markDirty(); return true;
        }
        configure(id, changes) {
            const device = this.devices.get(id); if (!device) return;
            device.params = validateParameters(this.definition(device), { ...device.params, ...changes });
            const p = this.params(device);
            if (p.capacity) device.runtime.energy = clamp(device.runtime.energy || 0, 0, p.capacity);
            if (p.tankCapacity) device.runtime.water = clamp(device.runtime.water || 0, 0, p.tankCapacity);
            this.powerDirty = true;
        }
        resetDevice(device) {
            const p = this.params(device), kind = this.definition(device).kind;
            device.runtime = { output: kind === "switch" ? p.on : kind === "timer", previous: false, count: 0, delayTicks: 0, closed: false, watts: 0, ratio: 0,
                energy: (p.capacity || 0) * (p.initialPercent || 0) / 100, water: (p.tankCapacity || 0) * (p.initialPercent || 0) / 100 };
        }
        reset() { this.ticks = 0; for (const device of this.devices.values()) this.resetDevice(device); this.markDirty(); this.refresh(); }
        signalNode(x, y, direction) {
            const cell = key(x, y), type = this.signal.get(cell);
            return !type ? null : `${cell}:${type === "sim:cross" ? ((direction & 1) === 0 ? "h" : "v") : "a"}`;
        }
        // 跨线桥拆成两个节点，避免水平和垂直线路误短接。
        rebuildSignals() {
            this.signalNetworks = []; this.signalIndex.clear();
            for (const [cell, type] of this.signal) {
                for (const lane of type === "sim:cross" ? ["h", "v"] : ["a"]) {
                    const root = `${cell}:${lane}`; if (this.signalIndex.has(root)) continue;
                    const index = this.signalNetworks.length, members = [], queue = [root]; this.signalIndex.set(root, index);
                    for (let i = 0; i < queue.length; i++) {
                        const node = queue[i], [coordinate, channel] = node.split(":"), [x, y] = coordinate.split(",").map(Number); members.push(node);
                        for (let direction = 0; direction < 4; direction++) {
                            if (channel !== "a" && channel !== ((direction & 1) === 0 ? "h" : "v")) continue;
                            const [dx, dy] = DIRECTIONS[direction], next = this.signalNode(x + dx, y + dy, direction);
                            if (next && !this.signalIndex.has(next)) { this.signalIndex.set(next, index); queue.push(next); }
                        }
                    }
                    this.signalNetworks.push(members);
                }
            }
        }
        pinNetwork(device, direction) {
            const p = position(device, direction), node = this.signalNode(p.x, p.y, direction + device.rotation);
            return node == null ? undefined : this.signalIndex.get(node);
        }
        collectSignals() {
            const values = this.signalNetworks.map(() => false);
            for (const device of this.devices.values()) {
                if (!device.runtime.output) continue;
                for (const pin of signalPins(this.definition(device))) if (!pin.input) {
                    const index = this.pinNetwork(device, pin.direction); if (index !== undefined) values[index] = true;
                }
            }
            return values;
        }
        readInput(device, direction, values = this.signalValues) {
            const index = this.pinNetwork(device, direction); return { connected: index !== undefined, value: index !== undefined && !!values[index] };
        }
        rebuildPower() {
            this.powerNetworks = []; this.powerIndex.clear();
            const bridges = new Map();
            for (const device of this.devices.values()) if (this.definition(device).kind === "relay" && device.runtime.closed) {
                const a = position(device, 0), b = position(device, 2), ka = key(a.x, a.y), kb = key(b.x, b.y);
                if (!this.power.has(ka) || !this.power.has(kb)) continue;
                if (!bridges.has(ka)) bridges.set(ka, []); if (!bridges.has(kb)) bridges.set(kb, []);
                bridges.get(ka).push(kb); bridges.get(kb).push(ka);
            }
            for (const cell of this.power.keys()) {
                if (this.powerIndex.has(cell)) continue;
                const index = this.powerNetworks.length, network = { cells: [], devices: [] }, queue = [cell]; this.powerIndex.set(cell, index);
                for (let i = 0; i < queue.length; i++) {
                    const current = queue[i], [x, y] = current.split(",").map(Number); network.cells.push(current);
                    const neighbors = DIRECTIONS.map(([dx, dy]) => key(x + dx, y + dy)).concat(bridges.get(current) || []);
                    for (const neighbor of neighbors) if (this.power.has(neighbor) && !this.powerIndex.has(neighbor)) { this.powerIndex.set(neighbor, index); queue.push(neighbor); }
                }
                this.powerNetworks.push(network);
            }
            for (const device of this.devices.values()) {
                const definition = this.definition(device); device.runtime.network = undefined;
                if (!definition.electrical) continue;
                const p = powerPosition(definition, device), index = this.powerIndex.get(key(p.x, p.y));
                if (index !== undefined) { this.powerNetworks[index].devices.push(device); device.runtime.network = index; }
            }
            for (const network of this.powerNetworks) network.devices.sort((a, b) => Number(a.id.slice(1)) - Number(b.id.slice(1)));
            this.powerDirty = false;
        }
        ensureTopology() { if (this.dirty) { this.rebuildSignals(); this.dirty = false; } if (this.powerDirty) this.rebuildPower(); }
        accepts(definition, params, voltage) {
            const e = definition.electricalConfig || {};
            return voltage > 0 && voltage + 0.001 >= (e.MinimumVoltage || params.voltage) && voltage <= (e.MaximumVoltage || params.voltage) + 0.001;
        }
        enabled(device) {
            const p = this.params(device), input = this.definition(device).kind === "battery" ? { connected: false } : this.readInput(device, 2);
            return p.enabled !== false && (!input.connected || input.value);
        }
        // 与游戏相同：整网分摊、源电压冲突停网、过载只报警，电阻不计算压降。
        solvePower(seconds) {
            const interval = seconds || STEP; this.warnings = [];
            for (const device of this.devices.values()) { device.runtime.watts = 0; device.runtime.ratio = 0; device.runtime.status = "未接电线"; }
            for (const network of this.powerNetworks) {
                Object.assign(network, { voltage: 0, generated: 0, demand: 0, delivered: 0, charging: 0, discharging: 0, wasted: 0, current: 0, overloaded: false, status: "待机" });
                const generators = [], batteries = [], loads = [];
                for (const device of network.devices) {
                    const def = this.definition(device), p = this.params(device);
                    if (def.kind === "generator" && this.enabled(device)) {
                        let factor = p.sourcePercent / 100; if (device.type === "sim:solar") factor *= this.environment.light / 100;
                        if (factor > 0 && (p.watts > 0 || def.electricalConfig?.UnlimitedGeneration)) generators.push({ device, def, p, factor });
                    } else if (def.kind === "battery" && p.enabled) batteries.push({ device, def, p });
                    else if (def.kind === "consumer" && this.enabled(device)) { loads.push({ device, def, p }); network.demand += p.watts; }
                }
                const sources = generators.concat(batteries.filter(b => b.device.runtime.energy > 0.001));
                network.voltage = sources[0]?.p.voltage || 0;
                if (sources.some(source => Math.abs(source.p.voltage - network.voltage) > 0.001)) network.status = "电压冲突";
                else if (network.cells.some(cell => { const e = this.registry.get(this.power.get(cell)).electricalConfig; return e.MaximumVoltage > 0 && network.voltage > e.MaximumVoltage + 0.001; })) network.status = "线路电压不兼容";
                else if (!network.voltage) network.status = network.demand ? "无供电" : "待机";
                if (!network.voltage || ["电压冲突", "线路电压不兼容"].includes(network.status)) {
                    for (const device of network.devices) device.runtime.status = network.status;
                    if (network.status !== "待机") this.warnings.push(network.status); continue;
                }
                const compatibleLoads = loads.filter(load => this.accepts(load.def, load.p, network.voltage));
                const compatibleBatteries = batteries.filter(b => this.accepts(b.def, b.p, network.voltage));
                const demand = compatibleLoads.reduce((sum, load) => sum + load.p.watts, 0);
                let chargingPotential = 0;
                for (const b of compatibleBatteries) chargingPotential += Math.min(b.p.chargeRate, (b.p.capacity - b.device.runtime.energy) / interval);
                const unlimited = [];
                for (const g of generators) {
                    if (g.def.electricalConfig?.UnlimitedGeneration) unlimited.push(g);
                    else { g.device.runtime.watts = g.p.watts * g.factor; network.generated += g.device.runtime.watts; }
                }
                const missing = Math.max(0, demand + chargingPotential - network.generated);
                for (const g of unlimited) { g.device.runtime.watts = missing / unlimited.length; network.generated += g.device.runtime.watts; }
                let deficit = Math.max(0, demand - network.generated);
                for (const b of compatibleBatteries) {
                    const amount = Math.min(deficit, b.p.dischargeRate, b.device.runtime.energy / interval);
                    if (seconds) b.device.runtime.energy = Math.max(0, b.device.runtime.energy - amount * seconds);
                    b.device.runtime.watts = -amount; network.discharging += amount; deficit -= amount;
                }
                network.delivered = Math.min(demand, network.generated + network.discharging);
                const ratio = demand > 0 ? network.delivered / demand : 1;
                for (const load of compatibleLoads) { load.device.runtime.watts = load.p.watts * ratio; load.device.runtime.ratio = ratio; }
                let surplus = Math.max(0, network.generated - network.delivered);
                for (const b of compatibleBatteries) {
                    const amount = Math.min(surplus, b.p.chargeRate, (b.p.capacity - b.device.runtime.energy) / interval);
                    if (seconds) b.device.runtime.energy = Math.min(b.p.capacity, b.device.runtime.energy + amount * seconds);
                    b.device.runtime.watts += amount; network.charging += amount; surplus -= amount;
                }
                network.wasted = surplus; network.current = (network.delivered + network.charging) / network.voltage;
                network.safeCurrent = Math.min(...network.cells.map(cell => this.registry.get(this.power.get(cell)).electricalConfig.MaxCurrentAmps || Infinity));
                network.overloaded = network.current > network.safeCurrent + 0.001;
                const incompatible = compatibleLoads.length !== loads.length || compatibleBatteries.length !== batteries.length;
                network.status = network.overloaded ? "过载" : incompatible ? "部分设备电压不兼容" : ratio < 0.999999 ? "供电不足" : network.generated || network.discharging ? "运行中" : "待机";
                for (const device of network.devices) device.runtime.status = this.enabled(device) ? network.status : "控制关闭";
                for (const load of loads) if (!this.accepts(load.def, load.p, network.voltage)) load.device.runtime.status = "电压不兼容";
                if (!["运行中", "待机"].includes(network.status)) this.warnings.push(network.status);
            }
        }
        sensorValue(device) {
            const kind = this.definition(device).kind, p = this.params(device), target = this.devices.get(p.target);
            if (kind === "temperature") return this.environment.temperature;
            if (kind === "light") return this.environment.light;
            if (kind === "level" && target && this.definition(target).kind === "tank") return target.runtime.water / this.params(target).tankCapacity * 100;
            if (kind === "charge" && target && this.definition(target).kind === "battery") return target.runtime.energy / this.params(target).capacity * 100;
            return null;
        }
        refresh() { this.ensureTopology(); this.signalValues = this.collectSignals(); this.solvePower(0); }
        // 所有逻辑门读上一拍并统一提交，反馈回路不会递归求解或依赖遍历顺序。
        step() {
            this.ensureTopology(); const oldSignals = this.collectSignals(), updates = [];
            this.ticks++;
            for (const device of this.devices.values()) {
                const kind = this.definition(device).kind, p = this.params(device), r = device.runtime;
                const a = this.readInput(device, 2, oldSignals).value, b = this.readInput(device, 3, oldSignals).value;
                const next = { output: r.output, previous: a, count: r.count, delayTicks: r.delayTicks, closed: r.closed };
                switch (kind) {
                    case "switch": next.output = p.on; break;
                    case "timer": { const high = Math.max(1, Math.round(p.high / STEP)), low = Math.max(1, Math.round(p.low / STEP)); next.output = this.ticks % (high + low) < high; break; }
                    case "and": next.output = a && b; break;
                    case "or": next.output = a || b; break;
                    case "xor": next.output = a !== b; break;
                    case "not": next.output = !a; break;
                    case "latch": next.output = b ? false : a ? true : r.output; break;
                    case "toggle": next.output = b ? false : a && !r.previous ? !r.output : r.output; break;
                    case "counter": next.count = b ? 0 : Math.min(1e9, r.count + (a && !r.previous ? 1 : 0)); next.output = next.count >= p.count; break;
                    case "delay": next.delayTicks = a ? Math.min(1e9, r.delayTicks + 1) : 0; next.output = next.delayTicks >= Math.max(1, Math.round(p.delay / STEP)); break;
                    case "relay": next.closed = b; if (next.closed !== r.closed) this.powerDirty = true; break;
                    case "probe": next.output = a; break;
                    case "level": case "charge": case "temperature": case "light": { const value = this.sensorValue(device); next.output = value !== null && (p.compare === "below" ? value < p.threshold : value > p.threshold); break; }
                }
                updates.push([device, next]);
            }
            for (const [device, update] of updates) Object.assign(device.runtime, update);
            if (this.powerDirty) this.rebuildPower();
            this.signalValues = this.collectSignals(); this.solvePower(STEP);
            // 水箱是显式关联的实验负载，不假装复刻游戏气液网络。
            const tankFlows = new Map();
            for (const device of this.devices.values()) {
                const p = this.params(device);
                if (this.definition(device).kind === "tank") tankFlows.set(device.id, -p.drainRate);
            }
            for (const device of this.devices.values()) {
                const p = this.params(device);
                if (device.type === "LiquidPump_Electric" && tankFlows.has(p.target)) tankFlows.set(p.target, tankFlows.get(p.target) + p.flowRate * device.runtime.ratio);
            }
            for (const [id, flow] of tankFlows) { const device = this.devices.get(id); device.runtime.water = clamp(device.runtime.water + flow * STEP, 0, this.params(device).tankCapacity); }
        }
        serialize() {
            return { format: "flatworld-circuit-board", version: 1, width: this.width, height: this.height, environment: copy(this.environment),
                devices: Array.from(this.devices.values(), ({ id, type, x, y, rotation, params }) => ({ id, type, x, y, rotation, params: copy(params) })),
                power: Array.from(this.power, ([cell, type]) => ({ cell, type })), signal: Array.from(this.signal, ([cell, type]) => ({ cell, type })) };
        }
        // 先完整校验临时棋盘，再由调用方替换，错误文件不破坏当前方案。
        static restore(registry, data) {
            if (!data || data.format !== "flatworld-circuit-board" || data.version !== 1) throw new Error("不是受支持的电路方案（版本 1）。");
            if (!Number.isInteger(data.width) || !Number.isInteger(data.height) || data.width < 8 || data.height < 8 || data.width > 96 || data.height > 64) throw new Error("棋盘尺寸应在 8×8 至 96×64 之间。");
            const board = new CircuitBoard(registry, data.width, data.height), limit = data.width * data.height;
            for (const name of ["devices", "power", "signal"]) if (!Array.isArray(data[name]) || data[name].length > limit) throw new Error("方案元件数量或格式无效。");
            const ids = new Set();
            for (const source of data.devices) {
                if (!source || !/^d[1-9][0-9]{0,7}$/.test(source.id) || ids.has(source.id) || !Number.isInteger(source.rotation) || source.rotation < 0 || source.rotation > 3) throw new Error("设备 ID 重复或旋转值无效。");
                const definition = registry.get(source.type);
                if (!definition || ["wire", "signal", "cross"].includes(definition.kind)) throw new Error(`当前目录不支持设备：${source.type}`);
                const device = board.add(source.type, source.x, source.y, source.rotation, source.params);
                board.devices.delete(device.id); device.id = source.id; board.devices.set(device.id, device); board.deviceCells.set(key(device.x, device.y), device.id); ids.add(device.id);
                board.nextId = Math.max(board.nextId, Number(device.id.slice(1)) + 1);
            }
            for (const layer of ["power", "signal"]) for (const wire of data[layer]) {
                if (!wire || typeof wire.cell !== "string" || !/^\d+,\d+$/.test(wire.cell)) throw new Error("线路坐标无效。");
                const [x, y] = wire.cell.split(",").map(Number), definition = registry.get(wire.type);
                if (!definition || (layer === "power" ? definition.kind !== "wire" : !["signal", "cross"].includes(definition.kind)) || board[layer].has(wire.cell) || !board.paint(wire.type, x, y)) throw new Error("线路越界、重复或与设备冲突。");
            }
            if (!data.environment || !Number.isFinite(data.environment.temperature) || data.environment.temperature < -1000 || data.environment.temperature > 1e6 || !Number.isFinite(data.environment.light) || data.environment.light < 0 || data.environment.light > 100) throw new Error("实验环境参数无效。");
            board.environment = { temperature: data.environment.temperature, light: data.environment.light }; board.reset(); return board;
        }
    }

    // 示例同样经过正式放置和连线接口，避免展示特殊但无法由玩家构建的电路。
    function createExample(registry, name) {
        const board = new CircuitBoard(registry), wire = registry.has("Wire_Copper") ? "Wire_Copper" : Array.from(registry.values()).find(d => d.kind === "wire")?.id;
        if (!wire) throw new Error("尚未读取游戏电线配置。");
        function line(type, x1, y1, x2, y2) { let x = x1, y = y1; board.paint(type, x, y); while (x !== x2) { x += Math.sign(x2 - x); board.paint(type, x, y); } while (y !== y2) { y += Math.sign(y2 - y); board.paint(type, x, y); } }
        if (name === "tank") {
            const tank = board.add("sim:tank", 17, 7), low = board.add("sim:level", 3, 8, 0, { target: tank.id, threshold: 30, compare: "below" });
            board.add("sim:level", 3, 3, 0, { target: tank.id, threshold: 90, compare: "above" });
            board.add("sim:latch", 8, 8); board.add("LiquidPump_Electric", 13, 8, 0, { target: tank.id });
            board.add("sim:source", 13, 3, 0, { watts: 800 });
            line(wire, 13, 3, 13, 8); line("sim:signal", low.x + 1, low.y, 7, 8); line("sim:signal", 4, 3, 8, 3); line("sim:signal", 8, 3, 8, 7); line("sim:signal", 9, 8, 12, 8);
        } else if (name === "battery") {
            board.add("sim:solar", 4, 5, 0, { watts: 600 }); board.add("Battery_Basic", 9, 5, 0, { initialPercent: 20 });
            board.add("sim:load", 14, 5, 0, { watts: 300 }); line(wire, 4, 5, 14, 5);
        } else if (name === "logic") {
            board.add("sim:switch", 3, 7); board.add("sim:switch", 6, 3, 1); board.add("sim:and", 6, 7); board.add("sim:probe", 11, 7);
            line("sim:signal", 4, 7, 5, 7); line("sim:signal", 6, 4, 6, 6); line("sim:signal", 7, 7, 10, 7);
        } else {
            board.add("Battery_Basic", 12, 3); board.add("ElectricLightBulb", 12, 8); board.add("sim:timer", 4, 8);
            line(wire, 12, 3, 12, 8); line("sim:signal", 5, 8, 11, 8);
        }
        board.reset(); return board;
    }
    return { STEP, DIRECTIONS, PARAMS, CircuitBoard, createRegistry, createExample, signalPins, powerPosition, position, key, clamp, parameters };
});

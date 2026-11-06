const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const M = require("../circuit-board-model.js");
const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, "../../../Resources/Config/Mechanical/mechanical-catalog.json"), "utf8").replace(/^\uFEFF/, ""));
const registry = M.createRegistry(catalog);
const create = () => new M.CircuitBoard(registry);
function line(board, layer, x1, y1, x2, y2) { let x = x1, y = y1; board.paint(layer, x, y); while (x !== x2) { x += Math.sign(x2 - x); board.paint(layer, x, y); } while (y !== y2) { y += Math.sign(y2 - y); board.paint(layer, x, y); } }
function steps(board, count) { for (let i = 0; i < count; i++) board.step(); }
function close(actual, expected, tolerance = 1e-6) { assert.ok(Math.abs(actual - expected) < tolerance, `${actual} != ${expected}`); }
function powered({ generation = 100, load = 60, battery = true, batteryParams = {} } = {}) {
    const b = create(), source = b.add("sim:source", 1, 4, 0, { watts: generation }), consumer = b.add("sim:load", 8, 4, 0, { watts: load });
    const storage = battery ? b.add("Battery_Basic", 4, 4, 0, batteryParams) : null;
    line(b, "Wire_Copper", 1, 4, 8, 4); b.refresh(); return { b, source, consumer, storage };
}
function twoInputs(type) {
    const b = create(), a = b.add("sim:switch", 2, 5), r = b.add("sim:switch", 5, 2, 1, { on: false }), gate = b.add(type, 5, 5);
    line(b, "sim:signal", 3, 5, 4, 5); line(b, "sim:signal", 5, 3, 5, 4); b.refresh(); return { b, a, r, gate };
}

test("游戏设备来自实际目录，扩展元件不冒充已实现机制", () => {
    assert.equal([...registry.values()].filter(d => d.game).length, catalog.Nodes.filter(n => n.Electrical).length);
    assert.equal(registry.get("Battery_Basic").defaults.capacity, 60000);
    assert.equal(registry.get("ElectricLightBulb").defaults.watts, 60);
    assert.equal(registry.get("Generator_Basic").connection, "left");
    assert.equal(registry.get("sim:timer").game, false);
});
test("真实飞船光纤显式适配信号线路，不凭名称捏造已有元件", () => {
    const source = JSON.parse(fs.readFileSync(path.join(__dirname, "../../GameConfig/Items/shells/space_ships.json"), "utf8").replace(/^\uFEFF/, ""));
    const fiber = source.items.find(item => item.id === "ShipFiber");
    const adapted = M.createRegistry(catalog, [{ final: fiber, displayName: fiber.gameName }]);
    assert.equal(adapted.get("ShipFiber").kind, "signal"); assert.equal(adapted.get("ShipFiber").game, true);
    assert.equal(registry.has("ShipFiber"), false);
    const b = new M.CircuitBoard(adapted); b.add("sim:switch", 1, 3); const probe = b.add("sim:probe", 5, 3);
    line(b, "ShipFiber", 2, 3, 3, 3); b.paint("sim:signal", 4, 3); b.step(); assert.equal(probe.runtime.output, true);
    assert.deepEqual(M.CircuitBoard.restore(adapted, b.serialize()).serialize(), b.serialize());
});
test("1s 高、1s 低，运行到边界无浮点漂移", () => {
    const b = M.createExample(registry, "blink"), timer = [...b.devices.values()].find(d => d.type === "sim:timer");
    assert.equal(timer.runtime.output, true); steps(b, 19); assert.equal(timer.runtime.output, true);
    b.step(); assert.equal(timer.runtime.output, false); close(b.time, 1);
    steps(b, 19); assert.equal(timer.runtime.output, false); b.step(); assert.equal(timer.runtime.output, true);
    steps(b, 40000); assert.equal(timer.runtime.output, true); close(b.time, 2002);
});
test("逻辑门真值表与悬空输入规则", () => {
    for (const kind of ["and", "or", "xor"]) for (const av of [false, true]) for (const bv of [false, true]) {
        const { b, a, r, gate } = twoInputs(`sim:${kind}`); b.configure(a.id, { on: av }); b.configure(r.id, { on: bv }); steps(b, 2);
        assert.equal(gate.runtime.output, kind === "and" ? av && bv : kind === "or" ? av || bv : av !== bv);
    }
    const b = create(), not = b.add("sim:not", 2, 2); b.step(); assert.equal(not.runtime.output, true);
});
test("记忆器保持状态，复位优先", () => {
    const { b, a, r, gate } = twoInputs("sim:latch"); b.step(); assert.equal(gate.runtime.output, true);
    b.configure(a.id, { on: false }); steps(b, 3); assert.equal(gate.runtime.output, true);
    b.configure(a.id, { on: true }); b.configure(r.id, { on: true }); steps(b, 2); assert.equal(gate.runtime.output, false);
});
test("翻转与计数只响应上升沿，不把持续高电平当多个脉冲", () => {
    for (const type of ["sim:toggle", "sim:counter"]) {
        const { b, a, r, gate } = twoInputs(type); steps(b, 50);
        assert.equal(type === "sim:counter" ? gate.runtime.count : gate.runtime.output, type === "sim:counter" ? 1 : true);
        b.configure(a.id, { on: false }); steps(b, 2); b.configure(a.id, { on: true }); steps(b, 2);
        assert.equal(type === "sim:counter" ? gate.runtime.count : gate.runtime.output, type === "sim:counter" ? 2 : false);
        b.configure(r.id, { on: true }); steps(b, 2); assert.equal(gate.runtime.count, 0); assert.equal(gate.runtime.output, false);
    }
});
test("延时要求连续高电平，断开后清零", () => {
    const { b, a, gate } = twoInputs("sim:delay"); steps(b, 19); assert.equal(gate.runtime.output, false); b.step(); assert.equal(gate.runtime.output, true);
    b.configure(a.id, { on: false }); steps(b, 2); assert.equal(gate.runtime.output, false); assert.equal(gate.runtime.delayTicks, 0);
});
test("反馈环逐拍演化，不递归或死循环", () => {
    const b = create(), gate = b.add("sim:not", 3, 3);
    line(b, "sim:signal", 4, 3, 5, 3); line(b, "sim:signal", 5, 3, 5, 1); line(b, "sim:signal", 5, 1, 1, 1); line(b, "sim:signal", 1, 1, 1, 3); line(b, "sim:signal", 1, 3, 2, 3);
    for (let i = 0; i < 100; i++) { b.step(); assert.equal(gate.runtime.output, i % 2 === 0); }
});
test("设备遍历顺序不改变同步逻辑结果", () => {
    const a = M.createExample(registry, "tank"), data = a.serialize(); data.devices.reverse(); const b = M.CircuitBoard.restore(registry, data);
    steps(a, 1000); steps(b, 1000);
    for (const [id, device] of a.devices) { assert.equal(device.runtime.output, b.devices.get(id).runtime.output); close(device.runtime.water, b.devices.get(id).runtime.water); }
});
test("电线与信号同格不互通，拓扑只在修改时重建", () => {
    const b = create(); line(b, "Wire_Copper", 1, 2, 5, 2); line(b, "sim:signal", 3, 1, 3, 4); b.refresh();
    assert.equal(b.powerNetworks.length, 1); assert.equal(b.signalNetworks.length, 1);
    const signals = b.signalNetworks, power = b.powerNetworks; steps(b, 10); assert.equal(b.signalNetworks, signals); assert.equal(b.powerNetworks, power);
    b.erase(3, 2, "power"); b.refresh(); assert.equal(b.powerNetworks.length, 2); assert.equal(b.signalNetworks.length, 1);
});
test("跨线桥横竖隔离，普通十字合网", () => {
    const b = create(); line(b, "sim:signal", 1, 3, 5, 3); line(b, "sim:signal", 3, 1, 3, 5); b.paint("sim:cross", 3, 3); b.refresh();
    assert.equal(b.signalNetworks.length, 2); assert.notEqual(b.signalIndex.get("3,3:h"), b.signalIndex.get("3,3:v"));
    b.paint("sim:signal", 3, 3); b.refresh(); assert.equal(b.signalNetworks.length, 1);
});
test("同一信号网上多个输出采用 OR", () => {
    const b = create(), a = b.add("sim:switch", 1, 3, 0, { on: false }), other = b.add("sim:switch", 5, 3, 2), probe = b.add("sim:probe", 3, 6, 1);
    line(b, "sim:signal", 2, 3, 4, 3); line(b, "sim:signal", 3, 3, 3, 5); b.step(); assert.equal(probe.runtime.output, true);
    b.configure(other.id, { on: false }); steps(b, 2); assert.equal(probe.runtime.output, false); assert.equal(a.runtime.output, false);
});
test("电池放电遵守焦耳守恒，刷新不消耗能量", () => {
    const { b, storage, consumer } = powered({ generation: 0 }); const initial = storage.runtime.energy;
    for (let i = 0; i < 20; i++) b.refresh(); close(storage.runtime.energy, initial);
    steps(b, 200); close(storage.runtime.energy, initial - 600); close(consumer.runtime.watts, 60);
});
test("余电充电，满电不溢出；空电池不产生能量", () => {
    const { b, storage } = powered(); const initial = storage.runtime.energy; steps(b, 200); close(storage.runtime.energy, initial + 400);
    b.configure(storage.id, { capacity: 100, initialPercent: 99 }); b.reset(); steps(b, 200); close(storage.runtime.energy, 100);
    const empty = powered({ generation: 0, batteryParams: { initialPercent: 0 } }); steps(empty.b, 200); close(empty.storage.runtime.energy, 0); close(empty.consumer.runtime.watts, 0);
});
test("最后不足一拍的电量被精确分配，能量不变负数", () => {
    const { b, storage, consumer } = powered({ generation: 0, batteryParams: { capacity: 1, initialPercent: 100 } }); b.step(); close(storage.runtime.energy, 0); close(consumer.runtime.watts, 20); b.step(); close(consumer.runtime.watts, 0);
});
test("负载按功率比例分配，而非按放置顺序抢占", () => {
    const { b, consumer } = powered({ generation: 100, load: 100, battery: false }); const other = b.add("sim:load", 6, 4, 0, { watts: 300 }); b.step(); close(consumer.runtime.watts, 25); close(other.runtime.watts, 75);
});
test("异电压源冲突停网，兼容性和线路耐压有效", () => {
    const { b, storage, consumer } = powered(); b.configure(storage.id, { voltage: 240 }); const initial = storage.runtime.energy; b.step(); assert.equal(b.powerNetworks[0].status, "电压冲突"); close(consumer.runtime.watts, 0); close(storage.runtime.energy, initial);
    const other = powered({ battery: false }); other.b.configure(other.source.id, { voltage: 1000 }); other.b.step(); assert.equal(other.b.powerNetworks[0].status, "线路电压不兼容"); close(other.consumer.runtime.watts, 0);
});
test("电线过载仅报警，不私自改成自动断电", () => {
    const { b, consumer } = powered({ generation: 2000, load: 1500, battery: false }); b.step(); assert.equal(b.powerNetworks[0].status, "过载"); close(consumer.runtime.watts, 1500); close(b.powerNetworks[0].current, 12.5);
});
test("创造电源按真实需求发电，多源不产生 Infinity", () => {
    const { b, storage, consumer } = powered({ generation: 0 }); b.add("Creative_Power_Source", 3, 4); b.add("Creative_Power_Source", 5, 4); b.step();
    close(b.powerNetworks[0].generated, 660); close(consumer.runtime.watts, 60); close(storage.runtime.energy, 30030);
    assert.ok(Number.isFinite(b.powerNetworks[0].current));
});
test("切断电线后负载停止，不继续使用旧网络", () => {
    const { b, consumer } = powered({ battery: false }); b.step(); close(consumer.runtime.watts, 60); b.erase(5, 4, "power"); b.step(); close(consumer.runtime.watts, 0);
});
test("方向电力端口随旋转移动", () => {
    const b = create(), generator = b.add("Generator_Basic", 5, 5, 1), load = b.add("sim:load", 5, 2, 0, { watts: 100 }); line(b, "Wire_Copper", 5, 2, 5, 4); b.step(); close(load.runtime.watts, 100);
    generator.rotation = 0; b.markDirty(); b.step(); close(load.runtime.watts, 0);
});
test("继电器实际分断电网，禁止中心电线短接", () => {
    const b = create(); b.add("sim:source", 1, 4); const relay = b.add("sim:relay", 4, 4), load = b.add("sim:load", 7, 4), control = b.add("sim:switch", 4, 1, 1);
    line(b, "Wire_Copper", 1, 4, 3, 4); line(b, "Wire_Copper", 5, 4, 7, 4); line(b, "sim:signal", 4, 2, 4, 3);
    assert.equal(b.paint("Wire_Copper", relay.x, relay.y), false); b.step(); close(load.runtime.watts, 100);
    b.configure(control.id, { on: false }); steps(b, 2); close(load.runtime.watts, 0);
});
test("蓄水池示例真正经历启泵和停泵，传感器失去目标输出 0", () => {
    const b = M.createExample(registry, "tank"), pump = [...b.devices.values()].find(d => d.type === "LiquidPump_Electric"), tank = [...b.devices.values()].find(d => d.type === "sim:tank"), sensor = [...b.devices.values()].find(d => d.type === "sim:level");
    let on = 0, off = 0; for (let i = 0; i < 2400; i++) { b.step(); if (pump.runtime.ratio > 0) on++; else off++; assert.ok(tank.runtime.water >= 0 && tank.runtime.water <= 100); }
    assert.ok(on > 100 && off > 100); b.erase(tank.x, tank.y, "device"); steps(b, 2); assert.equal(sensor.runtime.output, false);
});
test("方案往返、版本、越界、重复、参数污染均有校验", () => {
    const b = M.createExample(registry, "blink"), data = b.serialize(); assert.deepEqual(M.CircuitBoard.restore(registry, data).serialize(), data);
    const bad = mutation => { const d = structuredClone(data); mutation(d); assert.throws(() => M.CircuitBoard.restore(registry, d)); assert.deepEqual(b.serialize(), data); };
    bad(d => d.version = 2); bad(d => d.devices[0].x = -1); bad(d => d.devices.push(d.devices[0])); bad(d => d.devices[0].params.capacity = Infinity);
    bad(d => d.devices[0].params.enabled = "false"); bad(d => d.devices[0].type = "Unknown"); bad(d => d.power.push(d.power[0])); bad(d => d.devices[0].params.untrusted = 1);
    bad(d => d.environment.light = 101); bad(d => d.width = 5000); bad(d => d.signal.push({ cell: "12,3", type: "sim:signal" }));
});

test("64 位输入精确限制无符号位宽，支持三种输入进制", () => {
    const maxHex = "F".repeat(16);
    assert.equal(M.parseWord64("0x" + maxHex), (1n << 64n) - 1n);
    assert.equal(M.parseWord64("0b1010"), 10n);
    assert.equal(M.parseWord64("18446744073709551615"), 18446744073709551615n);
    assert.equal(M.parseWord64("0x00_ab_cd"), 43981n);
    for (const input of ["-1", "1.5", "0xZZ", "", "0b1201", "0x" + "F".repeat(17),
        "0b1" + "0".repeat(64), (1n << 64n).toString(10)]) {
        assert.throws(() => M.parseWord64(input), input.slice(0, 20));
    }
});

test("64 级逐位 XOR / AND / OR，最高位进位形成第 65 位", () => {
    const maxHex = "0x" + "F".repeat(16), answer = M.addWordsWithRippleCarry(maxHex, "0x1");
    assert.equal(answer.hex, "0x1" + "0".repeat(16)); assert.equal(answer.carry, 1); assert.equal(answer.stages.length, 64);
    assert.ok(answer.stages.every(s => s.carryOut === 1));
    assert.deepEqual(answer.stages[0], { index: 0, a: 1, b: 1, carryIn: 0, sum: 0, carryOut: 1 });
    assert.deepEqual(answer.stages[63], { index: 63, a: 1, b: 0, carryIn: 1, sum: 0, carryOut: 1 });
    const normal = M.addWordsWithRippleCarry("0xABCDEF", "0x123456");
    assert.equal(BigInt(normal.hex), 0xABCDEFn + 0x123456n); assert.equal(normal.carry, 0);
});

test("随机确定性 64 位数对比原生大整数求和，覆盖低位、高位、跨位进位", () => {
    let seed = 0x12345678;
    const randomHex = () => Array.from({ length: 16 }, () => {
        seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0 & 15).toString(16);
    }).join("");
    for (let n = 0; n < 40; n++) {
        const a = "0x" + randomHex(), b = "0x" + randomHex();
        const result = M.addWordsWithRippleCarry(a, b);
        assert.equal(BigInt(result.hex), BigInt(a) + BigInt(b));
        assert.equal(result.carry, Number((BigInt(a) + BigInt(b)) >> 64n));
        for (const s of result.stages) {
            assert.equal(s.sum, s.a ^ s.b ^ s.carryIn);
            assert.equal(s.carryOut, (s.a & s.b) | ((s.a ^ s.b) & s.carryIn));
        }
    }
});

test("64 位网格示例存在三条独立宽总线，电力与 0/1 信号互不串线", () => {
    const b = M.createExample(registry, "adder64");
    const adder = [...b.devices.values()].find(d => d.type === "sim:adder64");
    const monitor = [...b.devices.values()].find(d => d.type === "sim:worddisplay");
    assert.equal(b.busNetworks.length, 3);
    assert.ok(b.powerNetworks.length >= 1);
    assert.equal(b.busPinNetwork(adder, 2) !== b.busPinNetwork(adder, 3), true);
    assert.equal(b.busPinNetwork(adder, 0) !== b.busPinNetwork(adder, 3), true);
    assert.equal(b.signalIndex.has("10,10:a"), true);
    assert.equal(b.signalIndex.has("11,9:a"), false);
    assert.equal(adder.runtime.wordStatus.startsWith("正常"), true);
    assert.equal(adder.runtime.sumHex, "0x1" + "0".repeat(16));
    assert.equal(adder.runtime.carry, 1);
    assert.equal(monitor.runtime.sumHex, adder.runtime.sumHex);
    steps(b, 100); assert.equal(adder.runtime.sumHex, monitor.runtime.sumHex);
    assert.equal(b.powerNetworks[0].delivered, 60);
});

test("加数修改、断电、断宽总线会立刻清空旧结果，恢复连线后再计算", () => {
    const b = M.createExample(registry, "adder64");
    const adder = [...b.devices.values()].find(d => d.type === "sim:adder64");
    const sourceB = [...b.devices.values()].find(d => d.type === "sim:word64" && d.rotation === 1);
    const display = [...b.devices.values()].find(d => d.type === "sim:worddisplay");
    b.configure(sourceB.id, { wordHex: "0x0" }); b.refresh();
    assert.equal(adder.runtime.sumHex, "0x" + "F".repeat(16));
    b.erase(10, 12, "power"); b.refresh();
    assert.equal(adder.runtime.sumHex, null); assert.equal(display.runtime.sumHex, null);
    b.paint("Wire_Copper", 10, 12); b.refresh();
    assert.equal(display.runtime.sumHex, "0x" + "F".repeat(16));
    b.erase(10, 7, "signal"); b.refresh();
    assert.equal(adder.runtime.sumHex, null); assert.equal(display.runtime.sumHex, null);
    b.paint("sim:bus64", 10, 7); b.refresh();
    assert.equal(display.runtime.sumHex, "0x" + "F".repeat(16));
});

test("宽数据网络多个输出端发生冲突并阻止虚假结果", () => {
    const b = M.createExample(registry, "adder64");
    const adder = [...b.devices.values()].find(d => d.type === "sim:adder64");
    b.add("sim:word64", 6, 10, 3, { wordHex: "0x123" });
    // 新寄存器旋转向上，输出点 (6,9)，与 A 总线共用同一组线。
    b.refresh();
    assert.equal(b.readDataBus(adder, 2).status, "多驱动冲突");
    assert.equal(adder.runtime.sumHex, null);
    assert.equal(adder.runtime.output, false);
});

test("64 位元件与宽总线保存格式向后兼容并拒绝非法大整数", () => {
    const b = M.createExample(registry, "adder64"), data = b.serialize();
    assert.equal(data.version, 1);
    assert.deepEqual(M.CircuitBoard.restore(registry, data).serialize(), data);
    const invalid = structuredClone(data), source = invalid.devices.find(d => d.type === "sim:word64");
    source.params.wordHex = "0x" + "F".repeat(17);
    assert.throws(() => M.CircuitBoard.restore(registry, invalid), /64/);
    assert.deepEqual(b.serialize(), data);
});

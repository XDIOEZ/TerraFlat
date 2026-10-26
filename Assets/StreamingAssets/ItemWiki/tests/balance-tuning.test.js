"use strict";
const assert = require("node:assert/strict");
const { test } = require("node:test");
const tune = require("../balance-tuning.js");

test("自适应步长不会二分锁死，反向后缩小、同向多次后加速", () => {
    const target = { fieldPath: "modules.damage.parameters.DamageValues.Physical", value: 30 };
    let saved = { step: 2, direction: 0, streak: 0 };
    const first = tune.adjust(target, saved, -1);
    assert.equal(first.after, 28);
    assert.equal(first.stepAfter, 2);
    target.value = first.after;
    saved = { step: first.stepAfter, direction: -1, streak: first.streak };
    const second = tune.adjust(target, saved, -1);
    assert.equal(second.after, 26);
    target.value = second.after;
    saved = { step: second.stepAfter, direction: -1, streak: second.streak };
    const third = tune.adjust(target, saved, -1);
    assert.equal(third.stepAfter, 2.5);
    target.value = third.after;
    saved = { step: third.stepAfter, direction: -1, streak: third.streak };
    const fourth = tune.adjust(target, saved, -1);
    assert.equal(fourth.stepAfter, 3.125);
    target.value = fourth.after;
    const reverse = tune.adjust(target, { step: fourth.stepAfter, direction: -1, streak: 4 }, 1);
    assert.equal(reverse.stepAfter, 1.5625);
    assert.ok(reverse.after > target.value);
});

test("每项独立历史、允许手动输入和安全撤销", () => {
    const target = { fieldPath: "weight", value: 10 };
    const first = tune.manual(target, { step: 0.5 }, 12);
    assert.equal(first.kind, "manual");
    assert.equal(first.after, 12);
    const back = tune.undo({ ...target, value: 12 }, {
        step: 0.5, history: [{
            before: 10, after: 12, stepBefore: 0.5,
            directionBefore: 1, streakBefore: 3
        }]
    });
    assert.equal(back.after, 10);
    assert.equal(back.stepAfter, 0.5);
    assert.equal(back.direction, 1);
    assert.equal(tune.undo({ ...target, value: 11 }, { history: [{ before: 10, after: 12 }] }), null);
});

test("父级继承的装备数组必须完整克隆，不能只留下修改的下标", () => {
    const resolved = {
        modules: {
            Mod_EquipmentStore: {
                parameters: {
                    equipmentInstances: [
                        { DefenseBonus: { Physical: 8 }, name: "保护胸甲" },
                        { SpeedIncrease: -0.2, name: "重量" }
                    ]
                }
            }
        }
    };
    const source = { id: "Chestplate_Test", parent: "Equipment_Base" };
    const rows = tune.collect({ final: resolved, kind: "item" }, x => x);
    const target = rows.find(x => x.fieldPath.endsWith("equipmentInstances.0.DefenseBonus.Physical"));
    assert.ok(target);
    tune.patchInheritedField(source, resolved, target.parts, 12);
    assert.equal(resolved.modules.Mod_EquipmentStore.parameters.equipmentInstances[0].DefenseBonus.Physical, 8);
    assert.equal(source.modules.Mod_EquipmentStore.parameters.equipmentInstances[0].DefenseBonus.Physical, 12);
    assert.equal(source.modules.Mod_EquipmentStore.parameters.equipmentInstances[1].SpeedIncrease, -0.2);
    assert.equal(source.modules.Mod_EquipmentStore.parameters.equipmentInstances[0].name, "保护胸甲");
});

test("仅采集玩法数值而不是位置、ID、视觉颜色等字段", () => {
    const entry = { kind: "item", final: {
        id: "Knife_X", weight: 1.2, visual: { color: { r: 1 } },
        modules: {
            damage: { parameters: {
                DamageValues: { Physical: 5, ImpactKind: 3 },
                "$transform": { localPosition: { x: 20 } },
                DamageInterval: 0.2
            } }
        }
    }};
    const targets = tune.collect(entry);
    assert.ok(targets.some(x => x.fieldPath === "weight"));
    assert.ok(targets.some(x => x.fieldPath.endsWith("DamageValues.Physical")));
    assert.equal(targets.some(x => x.fieldPath.includes("ImpactKind")), false);
    assert.equal(targets.some(x => x.fieldPath.includes("transform")), false);
    assert.equal(targets.some(x => x.fieldPath.includes("visual")), false);
});

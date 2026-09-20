(function (root, factory) {
    "use strict";
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.BuffWiki = api;
})(typeof globalThis === "object" ? globalThis : this, function () {
    "use strict";

    // BUFF 只提供领域字段与说明，列表、详情卡片和保存控件继续使用 Item 页面。
    const fields = [
        ["持续时间（秒；null 为永久）", "durationSeconds", null, true],
        ["结算间隔（秒）", "tickIntervalSeconds", 0],
        ["叠加方式", "stackMode", "ignore"],
        ["最大层数", "maxStacks", 1],
        ["第一层特效倍率", "visualBaseScale", 1],
        ["每层特效倍率增量", "visualScalePerStack", 0],
        ["入水叠层周期（秒）", "waterStackIntervalSeconds", 0],
        ["每 1/10 水深的层数上限增量", "waterStacksPerDepthLevel", 0],
        ["饮水延长时间（秒）", "drinkDurationExtensionSeconds", 0],
        ["分类", "category", "general"]
    ];
    const effectNames = {
        "core:true_damage": "真实伤害",
        "core:heal": "治疗",
        "core:max_health_percent_heal": "最大生命比例治疗",
        "core:max_health_percent_true_damage": "最大生命比例伤害",
        "core:nutrition_change": "营养变化",
        "core:temperature_cooling_multiplier": "体温下降速度倍率",
        "core:temperature_warming": "临时增温",
        "core:move_speed_multiplier": "移动速度倍率",
        "core:damage_taken_multiplier": "承受伤害倍率",
        "core:body_durability_restore": "身体部位恢复"
    };

    function makeEntry(source, pkg) {
        const displayName = source.displayName || source.id;
        return {
            id: `buff:${source.id}`, kind: "buff", displayName, category: "BUFF",
            package: pkg, source, final: source, modifiedAt: null,
            searchBlob: `${displayName} ${pkg.id} ${JSON.stringify(source)}`.toLowerCase()
        };
    }

    function stats(source) {
        return fields.map(([label, path, fallback, nullableNumber]) => [label,
            source[path] === undefined ? fallback : source[path],
            { sourceType: "root", path, nullableNumber: nullableNumber === true }]);
    }

    function effectStats(effect, index) {
        return [
            ["阶段", "phase", "tick"], ["处理器", "typeId", ""], ["数值", "value", 0],
            ["按层数乘算", "scaleWithStacks", false], ["目标", "targetId", ""],
            ["所需标签", "requiredTag", ""], ["上限（null 为未设置）", "upperLimit", null, true]
        ].map(([label, key, fallback, nullableNumber]) => [label,
            effect[key] === undefined ? fallback : effect[key],
            { sourceType: "root", path: `effects.${index}.${key}`, nullableNumber: nullableNumber === true }]);
    }

    function describe(source) {
        const maximum = source.maxStacks || 1;
        const mode = { ignore: "重复施加不变", extend_duration: "重复施加延长时间",
            refresh_duration: "重复施加刷新时间", add_stacks: "重复施加增加层数并刷新时间" }[source.stackMode || "ignore"];
        const lines = [`${mode}；最多 ${maximum} 层。`, source.durationSeconds == null
            ? "持续时间：永久，直到玩法规则移除。" : `持续时间：${source.durationSeconds} 秒；叠层不会重置周期结算时钟。`];
        const tick = source.tickIntervalSeconds || 0;
        for (const effect of source.effects || []) {
            const phase = { start: "获得时", tick: "每次周期结算", stop: "移除时" }[effect.phase] || effect.phase;
            lines.push(`${phase}：${effectNames[effect.typeId] || effect.typeId} ${effect.value ?? 0}${effect.scaleWithStacks ? " × 当前层数" : ""}${effect.targetId ? `（${effect.targetId}）` : ""}。`);
        }
        const damage = (source.effects || []).filter(e => e.phase === "tick" && e.typeId === "core:true_damage");
        if (tick > 0 && damage.length) {
            const count = Math.min(maximum, 10);
            lines.push(Array.from({ length: count }, (_, i) => {
                const dps = damage.reduce((sum, e) => sum + e.value * (e.scaleWithStacks ? i + 1 : 1), 0) / tick;
                return `${i + 1}层：${Number(dps.toFixed(4))} HP/s`;
            }).join("；") + (maximum > count ? "；更高层按相同公式计算。" : "。"));
        }
        if (source.waterStackIntervalSeconds > 0) {
            lines.push(`在真实水体中每 ${source.waterStackIntervalSeconds} 秒增加 1 层；跨水格保留计时，转入浅水不删除已经获得的层数。`);
            lines.push(Array.from({ length: 10 }, (_, i) => `${i + 1}/10 水深：最多 ${Math.min(maximum, (i + 1) * source.waterStacksPerDepthLevel)} 层`).join("；") + "。");
        }
        if (source.id === "潮湿" || source.id === "燃烧") {
            lines.push("同层水胜：3 层潮湿熄灭 3 层燃烧；4 层燃烧可蒸发原有 3 层潮湿，但不能施加到已有 4 层潮湿的实体上。燃烧中持续浸水会重新累计潮湿，达到火焰层数时灭火。");
        }
        if ((source.visualScalePerStack || 0) > 0)
            lines.push(`附着火焰倍率 = ${source.visualBaseScale ?? 1} +（层数 − 1）× ${source.visualScalePerStack}。`);
        return lines;
    }

    return { makeEntry, stats, effectStats, describe, effectNames };
});

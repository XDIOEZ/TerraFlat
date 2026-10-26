/* FlatWorld ItemWiki 通用数值调节：算法与 UI 分离，复用 Wiki 原有 JSON 保存。 */
(function (root, factory) {
    "use strict";
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.ItemBalanceTuner = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const selections = new Map();
    const ROOT_FIELDS = ["weight", "volume", "durability", "maxDurability", "amount"];
    const IGNORED = new Set([
        "$transform", "transform", "localPosition", "localEulerAngles", "localScale",
        "position", "rotation", "scale", "collider", "color", "spriteStates",
        "BitData", "Type", "ImpactKind", "guid", "Guid", "ModuleId", "id", "itemId",
        "sortingOrder", "sortingLayerName", "x", "y", "z", "w"
    ]);
    const exact = (n) => Math.round(n * 1000000) / 1000000;
    const num = (n) => typeof n === "number" && Number.isFinite(n);

    // 让第一次反馈只改变约 5%，零起点区分整数属性与倍率/概率属性。
    function defaultStep(value, path) {
        const amount = Math.abs(value);
        if (amount >= 10) return Math.max(1, Math.round(amount * 0.5) / 10);
        if (amount >= 1) return Math.max(0.1, Math.round(amount * 5) / 100);
        if (amount >= 0.01) return Math.max(0.005, exact(amount * 0.05));
        return /chance|probability|multiplier|rate|increase|interval/i.test(path) ? 0.01 : 1;
    }

    // 由真实 Item 解析结果列出数值字段，不提供身份、表现参数或空数组的误编辑入口。
    function collect(entry, describe = path => path) {
        const final = entry && entry.final || {};
        const result = [];
        const add = (parts, value, group, label) => {
            if (!num(value) || parts.some(part => IGNORED.has(String(part)))) return;
            const fieldPath = parts.join(".");
            result.push({
                fieldPath, parts, value, group,
                label: label || describe(String(parts[parts.length - 1])),
                hint: fieldPath
            });
        };
        for (const field of ROOT_FIELDS)
            add([field], final[field], "通用", describe(field));
        if (final.health && typeof final.health === "object") {
            for (const field of ["hp", "maxHp"])
                add(["health", field], final.health[field], "战斗", describe(field));
            for (const [key, value] of Object.entries(final.health.defense || {}))
                add(["health", "defense", key], value, "护甲", "防御 · " + describe(key));
        }
        const visit = (value, parts, category, depth) => {
            if (depth > 14 || result.length > 900) return;
            if (num(value)) { add(parts, value, category, describe(String(parts[parts.length - 1]))); return; }
            if (!value || typeof value !== "object") return;
            for (const [name, child] of Object.entries(value)) {
                if (IGNORED.has(name) || name.startsWith("$")) continue;
                visit(child, parts.concat(name), category, depth + 1);
            }
        };
        for (const [stable, module] of Object.entries(final.modules || {})) {
            if (!module || typeof module !== "object") continue;
            let category = /food|nutrition|spoil/i.test(stable) ? "食物"
                : /equipment|shield|armor|defense/i.test(stable) ? "护甲"
                : /weapon|damage|attack/i.test(stable) ? "武器"
                : "通用";
            for (const section of ["parameters", "data"])
                if (module[section] && typeof module[section] === "object")
                    visit(module[section], ["modules", stable, section], category, 0);
        }
        // 物理伤害和护甲提升到类别，方便一个物品的多模块参数集中筛选。
        for (const row of result) {
            if (/DamageValues|AttackDamage|DamageInterval/.test(row.fieldPath)) row.group = "武器";
            if (/DefenseBonus|Armor/.test(row.fieldPath)) row.group = "护甲";
            if (/FoodData|nutrition|Spoilage/.test(row.fieldPath)) row.group = "食物";
        }
        return result;
    }

    function settings(current, entry) {
        const saved = current || {};
        const step = num(saved.step) && saved.step > 0
            ? saved.step : defaultStep(entry.value, entry.fieldPath);
        return {
            step,
            direction: [-1, 0, 1].includes(saved.direction) ? saved.direction : 0,
            streak: Number.isInteger(saved.streak) && saved.streak >= 0 ? saved.streak : 0,
            min: num(saved.min) ? saved.min : entry.value < 0 ? -1000000 : 0,
            max: num(saved.max) ? saved.max : 1000000,
            history: Array.isArray(saved.history) ? saved.history : []
        };
    }

    // 自适应反馈：方向反转将步长减半，连续三次同向后轻微提速，永不二分锁死。
    function adjust(target, saved, direction) {
        if (![-1, 1].includes(direction)) throw new Error("方向必须为 -1 或 1");
        const old = settings(saved, target);
        let step = old.step;
        if (old.direction && direction !== old.direction) step = Math.max(0.0001, step * 0.5);
        else if (old.direction === direction && old.streak >= 2) step = Math.min(1000000, step * 1.25);
        const next = exact(Math.min(old.max, Math.max(old.min, target.value + direction * step)));
        if (!num(next) || next === target.value) return null;
        const streak = old.direction === direction ? old.streak + 1 : 1;
        return {
            kind: "adjust", fieldPath: target.fieldPath, before: target.value, after: next,
            stepBefore: old.step, stepAfter: exact(step), direction, streak,
            feedback: direction > 0 ? "偏低 · 提高" : "偏高 · 降低"
        };
    }

    // 手动输入也记录为历史，后续依然可以使用智能步长调整。
    function manual(target, saved, next) {
        if (!num(next)) throw new Error("数值必须为有限数字");
        const old = settings(saved, target);
        const value = exact(Math.max(old.min, Math.min(old.max, next)));
        if (value === target.value) return null;
        return {
            kind: "manual", fieldPath: target.fieldPath, before: target.value, after: value,
            stepBefore: old.step, stepAfter: old.step, direction: 0, streak: 0,
            feedback: "手动输入"
        };
    }

    function undo(target, saved) {
        const old = settings(saved, target);
        const last = old.history[old.history.length - 1];
        if (!last || !num(last.before) || !num(last.after) ||
            Math.abs(last.after - target.value) > 0.00001) return null;
        return {
            kind: "undo", fieldPath: target.fieldPath, before: target.value, after: last.before,
            stepBefore: old.step, stepAfter: last.stepBefore,
            direction: last.directionBefore || 0, streak: last.streakBefore || 0,
            feedback: "撤销"
        };
    }

    // 正确处理父级继承的数组：只为被覆盖的路径建立最小对象层，数组完整克隆后局部修改。
    function patchInheritedField(source, resolved, parts, value) {
        if (!parts.length || !num(value)) throw new Error("无效数值路径");
        let dst = source;
        let inherited = resolved;
        for (let i = 0; i < parts.length - 1; i++) {
            const key = parts[i];
            const base = inherited && inherited[key];
            if (base === undefined || (typeof base !== "object" || base === null))
                throw new Error("继承的数值路径已变化");
            if (dst[key] === undefined || dst[key] === null) {
                dst[key] = Array.isArray(base) ? JSON.parse(JSON.stringify(base)) : {};
            }
            if (typeof dst[key] !== "object") throw new Error("已有配置类型不兼容");
            dst = dst[key];
            inherited = base;
        }
        if (inherited?.[parts[parts.length - 1]] === undefined)
            throw new Error("字段已不存在，请重新读取 JSON");
        dst[parts[parts.length - 1]] = value;
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function format(number) {
        return Number(number).toLocaleString("zh-CN", { maximumFractionDigits: 6 });
    }

    function drawCurve(container, current, history) {
        container.replaceChildren();
        const values = (history || []).slice(-17).map(item => item.before);
        values.push(current);
        if (values.length < 2) {
            container.append(el("div", "balance-curve-empty", "调整数值后，这里会显示变化曲线"));
            return;
        }
        const min = Math.min(...values), max = Math.max(...values);
        const span = Math.max(0.1, max - min);
        const width = 520, height = 110, padding = 12;
        const ns = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(ns, "svg");
        svg.setAttribute("viewBox", "0 0 520 110");
        svg.setAttribute("role", "img");
        svg.setAttribute("aria-label", "最近 " + values.length + " 次数值变化");
        const points = values.map((n, i) => [
            padding + i * (width - padding * 2) / (values.length - 1),
            height - padding - (n - min) / span * (height - padding * 2)
        ]);
        const guide = document.createElementNS(ns, "line");
        guide.setAttribute("x1", "0"); guide.setAttribute("x2", "520");
        guide.setAttribute("y1", "98"); guide.setAttribute("y2", "98");
        guide.setAttribute("class", "balance-curve-axis");
        svg.append(guide);
        const polyline = document.createElementNS(ns, "polyline");
        polyline.setAttribute("points", points.map(([x, y]) => x + "," + y).join(" "));
        polyline.setAttribute("class", "balance-curve-line");
        svg.append(polyline);
        const last = document.createElementNS(ns, "circle");
        last.setAttribute("cx", points[points.length - 1][0]);
        last.setAttribute("cy", points[points.length - 1][1]);
        last.setAttribute("r", "5");
        last.setAttribute("class", "balance-curve-tip");
        svg.append(last);
        container.append(svg);
        container.append(el("small", "balance-curve-caption", "最近 " + (values.length - 1) + " 次变化 · " + format(min) + " ~ " + format(max)));
    }

    // 在物品详情中创建紧凑调参区，不复制完整 Wiki 目录与已有原始参数表。
    function mount(container, entry, hooks) {
        const writable = !!hooks.writable && entry.kind === "item";
        const targets = collect(entry, hooks.describe);
        container.className = "balance-tuner";
        container.replaceChildren();
        const header = el("div", "balance-heading");
        const titleBox = el("div", "balance-heading-copy");
        titleBox.append(el("small", "balance-eyebrow", "ITEM LAB / PARAMETER TUNING"),
            el("h3", "", "数值调节"),
            el("p", "", "每个参数独立记忆 · 连续点击微调 · Play Mode 原位预览"));
        const badge = el("span", "balance-session", writable ? "● 本地可编辑" : "◌ 只读预览");
        header.append(titleBox, badge);
        container.append(header);
        if (!targets.length) {
            container.append(el("p", "balance-empty", "该物品暂时没有可调数值。"));
            return null;
        }

        const layout = el("div", "balance-layout");
        const left = el("div", "balance-selector");
        const search = el("input", "balance-search");
        search.type = "search";
        search.placeholder = "⌕ 搜索伤害、防御、营养、功耗…";
        search.autocomplete = "off";
        const tabs = el("div", "balance-tabs");
        const options = el("div", "balance-options");
        left.append(search, tabs, options);

        const right = el("div", "balance-panel");
        const sub = el("div", "balance-chosen");
        const currentLabel = el("span", "balance-current-caption", "CURRENT VALUE");
        const currentValue = el("strong", "balance-current-value");
        const fieldName = el("h4", "balance-field-name");
        const pathName = el("code", "balance-field-path");
        sub.append(fieldName, pathName, currentLabel, currentValue);
        const actions = el("div", "balance-action-row");
        const down = el("button", "balance-down", "−  偏强 · 减小");
        const up = el("button", "balance-up", "＋  偏弱 · 增大");
        actions.append(down, up);
        const numeric = el("div", "balance-numeric");
        const direct = el("label", "");
        direct.append(el("span", "", "直接输入"));
        const manualValue = el("input", "");
        manualValue.type = "number"; manualValue.step = "any";
        direct.append(manualValue);
        const stepLabel = el("label", "");
        stepLabel.append(el("span", "", "调整步长"));
        const step = el("input", "");
        step.type = "number"; step.step = "any"; step.min = "0.0001";
        stepLabel.append(step);
        numeric.append(direct, stepLabel);
        const info = el("div", "balance-hint", "连续同向自动加速；反馈反向自动减半。");
        const graph = el("div", "balance-chart");
        const footer = el("div", "balance-foot");
        const historyCount = el("span", "balance-count");
        const undoButton = el("button", "balance-undo", "↶ 撤销上次");
        footer.append(historyCount, undoButton);
        const historyDetails = el("details", "balance-history");
        historyDetails.append(el("summary", "", "展开最近调整记录"));
        const historyList = el("div", "balance-history-list");
        historyDetails.append(historyList);
        const status = el("div", "balance-status", "通过 ItemWiki 校验、备份并保存到真正的 JSON 配置");
        right.append(sub, actions, numeric, info, graph, footer, historyDetails, status);
        layout.append(left, right);
        container.append(layout);

        let records = {}, activeGroup = "全部", busy = false;
        let selected = selections.get(entry.id.toLowerCase()) || targets[0].fieldPath;
        let current = targets.find(x => x.fieldPath === selected) || targets[0];
        selected = current.fieldPath;

        const recordOf = () => settings(records[current.fieldPath], current);
        const renderActive = () => {
            const record = recordOf();
            currentValue.textContent = format(current.value);
            fieldName.textContent = current.label;
            pathName.textContent = current.fieldPath;
            manualValue.value = String(current.value);
            step.value = String(record.step);
            down.disabled = up.disabled = manualValue.disabled = step.disabled = !writable || busy;
            undoButton.disabled = !writable || busy || !record.history.length ||
                Math.abs(record.history[record.history.length - 1].after - current.value) > 0.00001;
            historyCount.textContent = record.history.length + " 次历史 · 方向 " +
                (record.direction === 1 ? "↑" : record.direction === -1 ? "↓" : "—");
            drawCurve(graph, current.value, record.history);
            historyList.replaceChildren();
            for (const item of record.history.slice(-12).reverse())
                historyList.append(el("div", "balance-history-entry",
                    (item.timestamp || "") + "　" + format(item.before) + " → " +
                    format(item.after) + " · " + (item.feedback || "调整")));
        };

        const listOptions = () => {
            options.replaceChildren();
            const word = search.value.trim().toLowerCase();
            let found = 0;
            for (const target of targets) {
                if (activeGroup !== "全部" && target.group !== activeGroup) continue;
                if (word && !(target.label + " " + target.fieldPath).toLowerCase().includes(word)) continue;
                found++;
                const btn = el("button", "balance-target", "");
                if (target.fieldPath === selected) btn.classList.add("active");
                btn.type = "button";
                const text = el("span", "balance-target-name");
                text.append(el("strong", "", target.label), el("small", "", target.fieldPath));
                btn.append(text, el("b", "", format(target.value)));
                btn.addEventListener("click", () => {
                    current = target; selected = target.fieldPath;
                    selections.set(entry.id.toLowerCase(), selected);
                    listOptions(); renderActive();
                });
                options.append(btn);
            }
            if (!found) options.append(el("div", "balance-empty", "此筛选条件没有参数"));
        };
        for (const name of ["全部", "武器", "护甲", "食物", "战斗", "通用"]) {
            if (name !== "全部" && !targets.some(t => t.group === name)) continue;
            const btn = el("button", "balance-tab", name);
            if (name === activeGroup) btn.classList.add("active");
            btn.addEventListener("click", () => {
                activeGroup = name;
                for (const b of tabs.children) b.classList.toggle("active", b.textContent === name);
                listOptions();
            });
            tabs.append(btn);
        }
        search.addEventListener("input", listOptions);

        const save = async (change) => {
            if (!change || busy || !writable) return;
            busy = true; renderActive();
            status.textContent = "正在校验并保存物品配置…";
            try {
                const response = await hooks.save(current, change);
                if (response?.tuningWarning) status.textContent = response.tuningWarning;
                else status.textContent = "✓ 数值已保存并备份" +
                    (response?.tuningRequestId ? "，Unity 热更新请求已发出" : "");
            } catch (error) {
                busy = false; renderActive();
                status.textContent = "保存失败：" + error.message;
                hooks.onError?.(error);
                return;
            }
            busy = false;
        };
        down.addEventListener("click", () => save(adjust(current, recordOf(), -1)));
        up.addEventListener("click", () => save(adjust(current, recordOf(), 1)));
        undoButton.addEventListener("click", () => save(undo(current, recordOf())));
        manualValue.addEventListener("change", () => {
            if (manualValue.value.trim() === "") { manualValue.value = String(current.value); return; }
            save(manual(current, recordOf(), Number(manualValue.value)));
        });
        step.addEventListener("change", async () => {
            const next = Number(step.value);
            if (!num(next) || next <= 0 || next > 1000000) {
                step.value = String(recordOf().step);
                return;
            }
            try {
                await hooks.saveStep(current, next);
                records[current.fieldPath] = { ...recordOf(), step: next };
                status.textContent = "调整步长已保存";
            } catch (error) {
                status.textContent = "保存步长失败：" + error.message;
                step.value = String(recordOf().step);
            }
        });

        listOptions();
        renderActive();
        if (writable) {
            hooks.loadState(entry.id).then(response => {
                if (!container.isConnected) return;
                records = response.fields || {};
                renderActive();
            }).catch(error => {
                if (container.isConnected) status.textContent = "历史暂不可用：" + error.message;
            });
        } else status.textContent = "公开分享或离线浏览时仅可查看，禁止修改游戏配置。";
        return {
            focus(fieldPath) {
                const match = targets.find(row => row.fieldPath === fieldPath);
                if (!match) return false;
                activeGroup = "全部";
                search.value = "";
                current = match; selected = match.fieldPath;
                selections.set(entry.id.toLowerCase(), selected);
                for (const b of tabs.children) b.classList.toggle("active", b.textContent === "全部");
                listOptions(); renderActive();
                container.scrollIntoView({ behavior: "smooth", block: "start" });
                return true;
            }
        };
    }

    return { collect, defaultStep, settings, adjust, manual, undo, patchInheritedField, mount };
});

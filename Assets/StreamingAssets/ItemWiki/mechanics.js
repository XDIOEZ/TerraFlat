(function (root) {
    "use strict";
    const rules = new Map();
    const keyOf = value => String(value ?? "").trim().toLowerCase();
    const clone = value => JSON.parse(JSON.stringify(value));
    const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
    const valueText = value => value === undefined ? "按实际情况决定" : typeof value === "object" ? JSON.stringify(value) : String(value);
    const activeModules = definition => Object.entries(definition.modules || {}).filter(([, body]) => object(body) && body.enabled !== false);
    const playerNameOverrides = new Map([
        ["chicken", "鸡"],
        ["chicken_state", "鸡"],
        ["mortar", "石臼"],
        ["wildboar", "野猪"],
        ["wolf", "狼"]
    ]);
    const playerTagNames = new Map([
        ["axe", "斧头"],
        ["knife", "刀具"]
    ]);
    const playerName = value => {
        const raw = String(value || "").replace(/[-－]建筑$/, "");
        return playerNameOverrides.get(keyOf(raw)) || raw.replace(/_State$/i, "").replace(/_/g, " ");
    };
    const playerTagName = value => playerTagNames.get(keyOf(value)) || String(value || "").replace(/_/g, " ");
    function merge(base, override) {
        if (!object(override)) return clone(override);
        const result = object(base) ? clone(base) : {};
        for (const [key, value] of Object.entries(override)) {
            Object.defineProperty(result, key, { value: object(value) ? merge(result[key], value) : clone(value), enumerable: true, writable: true, configurable: true });
        }
        return result;
    }

    function resolveDefinitions(sources) {
        const result = new Map();
        const visiting = new Set();
        function resolve(key) {
            if (result.has(key)) return result.get(key);
            if (visiting.has(key)) throw new Error(`定义继承循环：${key}`);
            const source = sources.get(key);
            if (!source) throw new Error(`找不到父定义：${key}`);
            visiting.add(key);
            let inherited = {};
            if (source.parent) {
                inherited = clone(resolve(keyOf(source.parent)));
                delete inherited.gameName;
                delete inherited.labelKey;
                delete inherited.descriptionKey;
            }
            for (const [name, body] of Object.entries(source.modules || {})) {
                if (body?.prefab && inherited.modules?.[name] && keyOf(body.prefab) !== keyOf(inherited.modules[name].prefab)) delete inherited.modules[name];
            }
            const final = merge(inherited, source);
            final.abstract = source.abstract ?? false;
            delete final.parent;
            visiting.delete(key);
            result.set(key, final);
            return final;
        }
        for (const key of sources.keys()) resolve(key);
        return result;
    }

    async function loadManifest(fetchJson, base, manifestName, collection) {
        const manifest = await fetchJson(base + manifestName);
        if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.packages)) throw new Error(`无效目录：${manifestName}`);
        const sources = new Map();
        const packages = new Map();
        for (const pkg of manifest.packages.filter(entry => entry.enabled !== false)) {
            if (!/^[\w\-/]+\.json$/i.test(pkg.path) || pkg.path.startsWith("/")) throw new Error(`无效分包路径：${pkg.path}`);
            const data = await fetchJson(base + pkg.path);
            if (data.schemaVersion !== 1 || !Array.isArray(data[collection])) throw new Error(`无效分包：${pkg.path}`);
            for (const source of data[collection]) {
                const key = keyOf(source.id);
                if (!key || sources.has(key)) throw new Error(`重复或空 ID：${source.id}`);
                sources.set(key, source);
                packages.set(key, pkg);
            }
        }
        return { sources, packages, resolved: collection === "recipes" ? sources : resolveDefinitions(sources) };
    }

    function matches(input, item) {
        if (input.match === "tag") return Boolean(keyOf(input.tag)) && (item.tags || []).some(tag => keyOf(tag) === keyOf(input.tag));
        return input.match === "exact_item" && Boolean(keyOf(input.itemId)) && keyOf(input.itemId) === keyOf(item.id);
    }

    function aggregateInputs(inputs) {
        const groups = new Map();
        for (const input of inputs || []) {
            const identity = input.match === "tag" ? input.tag : input.itemId;
            if (!keyOf(identity)) continue;
            const key = `${input.match}:${keyOf(identity)}:${input.amount === 0 ? "tool" : "consume"}`;
            if (!groups.has(key)) groups.set(key, { ...input, amount: 0, slots: [] });
            const group = groups.get(key);
            group.amount += input.amount;
            group.slots.push(input.slot);
        }
        return Array.from(groups.values());
    }

    function register(prefabs, rule) {
        for (const prefab of prefabs) rules.set(prefab, rule);
    }

    function createContext(entries, recipes, lootTables) {
        const items = entries.filter(entry => entry.kind !== "actor" && !entry.final.abstract);
        const entryName = entry => playerNameOverrides.get(keyOf(entry?.final?.id)) || playerName(entry?.displayName);
        const names = new Map(items.map(entry => [keyOf(entry.final.id), entryName(entry)]));
        const relations = new Map(items.map(entry => [keyOf(entry.final.id), { inputs: [], outputs: [], loot: [] }]));
        const origins = new Map();
        for (const recipe of recipes) {
            for (const entry of items) {
                const relation = relations.get(keyOf(entry.final.id));
                if ((recipe.inputs || []).some(input => matches(input, entry.final))) relation.inputs.push(recipe);
                if ((recipe.outputs || []).some(output => keyOf(output.itemId) === keyOf(entry.final.id))) relation.outputs.push(recipe);
            }
        }
        for (const entry of entries.filter(value => !value.final.abstract)) {
            for (const [, body] of activeModules(entry.final)) {
                for (const output of rules.get(body.prefab)?.outputs?.(body) || []) {
                    if (!keyOf(output.id)) continue;
                    const key = keyOf(output.id);
                    if (!origins.has(key)) origins.set(key, []);
                    origins.get(key).push(`${entryName(entry)}${output.detail}`);
                }
            }
            const table = lootTables.get(keyOf(entry.final.lootTableId));
            for (const drop of table?.entries || []) relations.get(keyOf(drop.itemId))?.loot.push({ entry, drop, tableId: table.id });
        }
        return { relations, origins, lootTables, entryName, name: id => names.get(keyOf(id)) || playerName(id) };
    }

    function amountText(min, max) {
        if (min === undefined && max === undefined) return "";
        if (min === max || max === undefined) return ` ×${valueText(min)}`;
        return ` ×${valueText(min)}～${valueText(max)}`;
    }

    function probabilityText(value) {
        if (typeof value !== "number" || value >= 1) return "";
        if (value <= 0) return "，但当前概率为 0";
        return `，有 ${Math.round(value * 1000) / 10}% 的概率`;
    }

    function recipeText(recipe, context) {
        const inputs = aggregateInputs(recipe.inputs).map(input => {
            const name = input.match === "tag" ? `任意${playerTagName(input.tag)}类工具` : context.name(input.itemId);
            return input.amount === 0 ? `${name}（作为工具，不消耗）` : `${name} ×${valueText(input.amount)}`;
        }).join("、");
        const outputs = (recipe.outputs || []).map(output => `${context.name(output.itemId)} ×${valueText(output.amount)}`).join("、");
        const station = recipe.requiredStation ? `，需要在${context.name(recipe.requiredStation)}完成制作` : "";
        const temperature = recipe.recipeType === "smelting" && recipe.temperature !== undefined
            ? `，温度需达到 ${valueText(recipe.temperature)}${recipe.maxTemperature !== undefined ? `～${valueText(recipe.maxTemperature)}` : ""}℃`
            : "";
        return `使用${inputs || "对应材料"}${station}${temperature}，可以得到${outputs || "对应产物"}。`;
    }

    function describe(entry, context) {
        const item = entry.final;
        const customMechanics = typeof item.wiki?.mechanics === "string" ? item.wiki.mechanics.trim() : "";
        if (customMechanics) {
            return customMechanics.split(/\r?\n+/).map(line => line.trim()).filter(Boolean);
        }
        const paragraphs = [];
        for (const [slot, body] of activeModules(item)) {
            const rule = rules.get(body.prefab);
            if (rule) paragraphs.push(...rule.explain(body, context, entry).filter(Boolean));
        }
        const relation = entry.kind === "actor" ? null : context.relations.get(keyOf(item.id));
        const origins = context.origins.get(keyOf(item.id));
        if (origins?.length) paragraphs.push(`它还可以通过这些玩法获得：${origins.join("；")}。`);
        if (relation?.outputs.length) paragraphs.push(`它可以通过制作获得。${relation.outputs.map(recipe => recipeText(recipe, context)).join(" ")}`);
        if (relation?.inputs.length) paragraphs.push(`它也能作为材料或工具参与制作。${relation.inputs.map(recipe => recipeText(recipe, context)).join(" ")}`);
        if (relation?.loot.length) {
            paragraphs.push(...relation.loot.map(({ entry: owner, drop }) => `击败或破坏${context.entryName(owner)}后${probabilityText(drop.probability)}掉落${context.name(drop.itemId)}${amountText(drop.minAmount, drop.maxAmount)}。`));
        }
        if (item.lootTableId) {
            const table = context.lootTables.get(keyOf(item.lootTableId));
            if (table?.entries?.length) paragraphs.push(`它在死亡或被破坏后可能掉落：${table.entries.map(drop => dropText(drop, context)).join("；")}。`);
        }
        if (!paragraphs.length) paragraphs.push("目前没有额外用法，主要作为普通物品或制作材料使用。");
        return paragraphs;
    }

    function dropText(drop, context) {
        return `${context.name(drop.itemId)}${amountText(drop.minAmount, drop.maxAmount)}${probabilityText(drop.probability)}`;
    }

    function render(entry, context, document) {
        const container = document.createElement("section");
        container.className = "mechanics-card";
        for (const line of describe(entry, context)) {
            const paragraph = document.createElement("p");
            paragraph.textContent = line;
            container.appendChild(paragraph);
        }
        return container;
    }

    const api = { keyOf, valueText, activeModules, resolveDefinitions, loadManifest, matches, aggregateInputs, register, rules, createContext, recipeText, describe, render };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.ItemMechanics = api;
})(globalThis);

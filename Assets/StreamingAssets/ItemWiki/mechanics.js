(function (root) {
    "use strict";
    const rules = new Map();
    const keyOf = value => String(value ?? "").trim().toLowerCase();
    const clone = value => JSON.parse(JSON.stringify(value));
    const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
    const valueText = value => value === undefined ? "未声明（由模块默认/运行态决定）" : typeof value === "object" ? JSON.stringify(value) : String(value);
    const activeModules = definition => Object.entries(definition.modules || {}).filter(([, body]) => object(body) && body.enabled !== false);
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
        const names = new Map(items.map(entry => [keyOf(entry.final.id), entry.displayName]));
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
                    origins.get(key).push(`${entry.displayName} [${entry.final.id}]：${output.detail}`);
                }
            }
            const table = lootTables.get(keyOf(entry.final.lootTableId));
            for (const drop of table?.entries || []) relations.get(keyOf(drop.itemId))?.loot.push({ entry, drop, tableId: table.id });
        }
        return { relations, origins, lootTables, name: id => names.has(keyOf(id)) ? `${names.get(keyOf(id))} [${id}]` : String(id) };
    }

    function recipeText(recipe, context) {
        const inputs = aggregateInputs(recipe.inputs).map(input => `${input.match === "tag" ? `任一带标签「${input.tag}」的物品` : context.name(input.itemId)} ${input.amount === 0 ? "（需要但不消耗）" : `×${input.amount}`}${recipe.inputRule !== "unordered" ? `（槽位 ${input.slots.join("、")}）` : ""}`).join(" + ");
        const outputs = (recipe.outputs || []).map(output => `${context.name(output.itemId)} ×${valueText(output.amount)}`).join(" + ");
        const constraints = [`制作类型 ${valueText(recipe.recipeType)}`, `输入规则 ${valueText(recipe.inputRule)}`, `工作站 ${recipe.requiredStation || "未限定工作站"}`];
        if (recipe.recipeType === "smelting") constraints.push(`温度 ${valueText(recipe.temperature)} ～ ${valueText(recipe.maxTemperature)} ℃`);
        if (recipe.inputRule !== "unordered") constraints.push(`网格 ${valueText(recipe.gridWidth)}×${valueText(recipe.gridHeight)}`, `允许镜像 ${valueText(recipe.allowMirror)}`, `原始槽位 ${JSON.stringify(recipe.inputs)}`);
        if (recipe.actions?.length) constraints.push(`成功后动作 ${JSON.stringify(recipe.actions)}`);
        return `${recipe.displayName || recipe.id} [${recipe.id}]：${inputs} → ${outputs}。${constraints.join("；")}。`;
    }

    function describe(entry, context) {
        const item = entry.final;
        const sections = [];
        const add = (title, lines, source, raw) => sections.push({ title, lines, source, raw });
        add("阅读范围", ["以下是继承合并后的基础配置，不是当前存档或最终实战值；装备、难度、状态和环境会影响实际结果。未声明值由模块默认/运行态决定，不从 Name 或短描述推测数值。", entry.kind === "actor" ? "这是生物目录定义，只读展示，不进入 Item 保存接口。" : "机制说明与游戏内短描述独立；修改配置后重新读取即可更新。"], "ItemDefinitionCatalogLoader / ActorDefinitionCatalogLoader");
        add("携带与堆叠", [`允许拾取：${valueText(item.canBePickedUp)}；单件重量：${valueText(item.weight)} kg；单件体积：${valueText(item.volume)}；初始数量：${valueText(item.amount)}。`, `允许堆叠：${valueText(item.stackable)}。堆叠还需身份及特殊状态一致；不是同名就能合并。携带总重量/体积随数量累计，并受目标库存容量约束。`], "docs/systems/inventory.md；ItemData.CanStackWith");
        if (item.health) add("生命基础配置", [`生命与防御输入：${JSON.stringify(item.health)}。受击与死亡由伤害接收系统统一结算；配置生命不代表当前剩余生命。`], "docs/systems/combat.md");
        for (const [slot, body] of activeModules(item)) {
            const rule = rules.get(body.prefab);
            if (rule) add(rule.title, rule.explain(body, context, entry), rule.source, body);
            else add(`未登记解释：${body.prefab || slot}`, ["此模块尚无经过核对的机制规则；仅保留原始配置，不根据标签、名称或未知 MOD 字段推断用途。"], "当前定义 modules", body);
        }
        const relation = entry.kind === "actor" ? null : context.relations.get(keyOf(item.id));
        if (context.origins.get(keyOf(item.id))?.length) add("采收、生产与转化来源", context.origins.get(keyOf(item.id)), "当前启用具体定义的已登记模块；需满足来源模块条件");
        add("制作获得", relation?.outputs.length ? relation.outputs.map(recipe => recipeText(recipe, context)) : ["当前启用配方未声明直接制作此对象的产出；这不代表游戏中无法获得。"], "Recipes/recipe-manifest.json；CraftingRecipeMatcher");
        add("作为材料或工具", relation?.inputs.length ? [...relation.inputs.map(recipe => recipeText(recipe, context)), "标签输入列出的是可选匹配关系，不代表单件物品能同时满足全部需求；混合材料按全局分配结算。产物全部能放下才扣料。"] : ["当前启用配方未将此对象匹配为输入；标签本身不会自动赋予额外功能。"], "Recipes/recipe-manifest.json；CraftingMaterialAllocator / CraftingTransaction");
        if (relation?.loot.length) add("掉落来源", relation.loot.map(({ entry: owner, drop, tableId }) => `${owner.displayName} [${owner.final.id}] 的死亡战利品 ${tableId}：${dropText(drop, context)}；实际掉落按概率及运行态结算。`), "LootTables/loot-tables.json；docs/systems/combat.md");
        if (item.lootTableId) {
            const table = context.lootTables.get(keyOf(item.lootTableId));
            add("死亡/破坏产出", table ? (table.entries || []).map(drop => dropText(drop, context)) : [`未找到战利品表 ${item.lootTableId}`], "LootTables/loot-tables.json；实际产出可能受资源产出修饰影响");
        }
        return sections;
    }

    function dropText(drop, context) {
        return `${context.name(drop.itemId)}：基础概率 ${valueText(drop.probability)}（0～1）；数量 ${valueText(drop.minAmount)}～${valueText(drop.maxAmount)}`;
    }

    function render(entry, context, document) {
        const container = document.createElement("section");
        container.className = "mechanics-card";
        for (const section of describe(entry, context)) {
            const heading = document.createElement("h3");
            heading.textContent = section.title;
            container.appendChild(heading);
            for (const line of section.lines) {
                const paragraph = document.createElement("p");
                paragraph.textContent = line;
                container.appendChild(paragraph);
            }
            const source = document.createElement("small");
            source.textContent = `依据：${section.source}`;
            container.appendChild(source);
            if (section.raw) {
                const details = document.createElement("details");
                const summary = document.createElement("summary");
                summary.textContent = "本模块合并后的完整配置（只读）";
                const pre = document.createElement("pre");
                pre.textContent = JSON.stringify(section.raw, null, 2);
                details.append(summary, pre);
                container.appendChild(details);
            }
        }
        return container;
    }

    const api = { keyOf, valueText, activeModules, resolveDefinitions, loadManifest, matches, aggregateInputs, register, rules, createContext, recipeText, describe, render };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.ItemMechanics = api;
})(globalThis);

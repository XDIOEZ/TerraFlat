(function (root, factory) {
    "use strict";
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else root.ItemBehaviorTreeModel = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    // 按正式 Actor 定义与子树调用路径生成可独立展开的只读目录。
    const TYPE_LABELS = {
        selector: "优先选择", sequence: "顺序执行", condition: "条件", action: "动作",
        inverter: "取反", service: "伴随服务", subtree: "子树"
    };
    const isObject = value => value !== null && typeof value === "object" && !Array.isArray(value);
    const clone = value => value === undefined ? undefined : JSON.parse(JSON.stringify(value));
    const labelOf = (type, name) => [TYPE_LABELS[type] || "未知节点", name || type].filter(Boolean).join(" · ");
    const subtreeEntries = library => {
        const value = library?.subtrees || library || {};
        return isObject(value) ? Object.entries(value).filter(([, definition]) => isObject(definition)) : [];
    };

    function createCatalog(entries, library) {
        const shared = subtreeEntries(library);
        const actors = [];
        const localRecords = [];
        for (const entry of entries || []) {
            if (entry?.kind !== "actor" || !entry.final || entry.final.abstract) continue;
            const definition = entry.final.modules?.ai?.parameters?.behaviorTree;
            if (!definition?.root) continue;
            const actor = {
                kind: "actor", id: entry.final.id, name: entry.displayName || entry.final.gameName || entry.final.id,
                root: definition.root, capabilities: clone(definition.capabilities || {}), parameters: {},
                sourcePath: entry.package?.path || entry.sourcePath || "", package: entry.package || null,
                definition, entry, usedBy: []
            };
            actors.push(actor);
            for (const [name, localDefinition] of Object.entries(definition.subtrees || {})) {
                if (!isObject(localDefinition)) continue;
                localRecords.push({
                    kind: "subtree", id: `subtree:${actor.id}:${name}`, name, ownerId: actor.id, scope: "actor",
                    root: localDefinition.root, capabilities: {}, parameters: clone(localDefinition.parameters || {}),
                    sourcePath: actor.sourcePath, definition: localDefinition, usedBy: []
                });
            }
        }
        const subtrees = shared.map(([name, definition]) => ({
            kind: "subtree", id: `subtree:${name}`, name, scope: "shared", root: definition.root,
            capabilities: {}, parameters: clone(definition.parameters || {}),
            sourcePath: library?.subtreePaths?.[name] || library?.sourcePath || library?.path || "behavior-subtrees.json", definition, usedBy: []
        })).concat(localRecords);
        const catalog = { actors, subtrees };
        const resolveSubtree = (name, actorId) => {
            if (actorId) {
                const local = subtrees.find(record => record.scope === "actor" && record.ownerId === actorId && record.name === name);
                if (local) return local;
            }
            return subtrees.find(record => record.scope === "shared" && record.name === name) || null;
        };
        for (const actor of actors) {
            const used = new Set();
            const visit = (node, ancestors) => {
                if (!isObject(node)) return;
                if (node.type === "subtree") {
                    const record = resolveSubtree(node.name, actor.id);
                    if (!record) return;
                    used.add(record.id);
                    if (ancestors.has(node.name)) return;
                    const nested = new Set(ancestors);
                    nested.add(node.name);
                    visit(record.root, nested);
                    return;
                }
                for (const child of Array.isArray(node.children) ? node.children : []) visit(child, ancestors);
            };
            visit(actor.root, new Set());
            for (const record of subtrees) if (used.has(record.id)) record.usedBy.push(actor.id);
        }
        return catalog;
    }

    function buildTree(record, catalog) {
        const subtreeRecords = catalog?.subtrees || [];
        const resolveSubtree = (name, actorId) => {
            if (actorId) {
                const local = subtreeRecords.find(item => item.scope === "actor" && item.ownerId === actorId && item.name === name);
                if (local) return local;
            }
            return subtreeRecords.find(item => item.scope === "shared" && item.name === name) || null;
        };
        const actorId = record?.kind === "actor" ? record.id : record?.ownerId || null;
        let nodeCount = 0;
        let referenceCount = 0;
        const nodeKey = `${record?.kind || "tree"}:${record?.id || "unknown"}/root`;

        function substitute(value, args) {
            if (typeof value === "string" && value.startsWith("$")) {
                const name = value.slice(1);
                if (!Object.prototype.hasOwnProperty.call(args, name)) throw new Error(`缺少子树参数：${value}`);
                return clone(args[name]);
            }
            if (Array.isArray(value)) return value.map(item => substitute(item, args));
            if (isObject(value)) return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, substitute(item, args)]));
            return value;
        }

        function createNode(node, key, args, ancestors) {
            nodeCount++;
            if (!isObject(node)) return { key, type: "unknown", name: "", label: "未知节点", parameters: {}, children: [], error: "节点定义无效。" };
            const type = String(node.type || "unknown");
            const name = typeof node.name === "string" ? node.name : "";
            let parameters = {};
            let parameterError = "";
            try { parameters = substitute(node.parameters || {}, args); }
            catch (error) { parameterError = error.message; }
            const view = { key, type, name, label: labelOf(type, name), parameters, children: [] };
            if (node.id !== undefined) {
                try { view.id = substitute(node.id, args); }
                catch (error) { parameterError ||= error.message; view.id = node.id; }
            }
            if (parameterError) view.error = parameterError;
            if (type === "subtree") {
                referenceCount++;
                view.subtree = name;
                const target = resolveSubtree(name, actorId);
                if (!target) {
                    view.error = `找不到子树：${name || "<未命名>"}`;
                    return view;
                }
                if (ancestors.has(name)) {
                    view.error = `子树循环引用：${name}`;
                    return view;
                }
                if (parameterError) return view;
                let defaults;
                let supplied;
                try {
                    defaults = clone(target.parameters || {});
                    supplied = substitute(node.parameters || {}, args);
                } catch (error) {
                    view.error = error.message;
                    return view;
                }
                const effective = { ...(isObject(defaults) ? defaults : {}), ...(isObject(supplied) ? supplied : {}) };
                view.parameters = effective;
                const nextAncestors = new Set(ancestors);
                nextAncestors.add(name);
                view.children.push(createNode(target.root, `${key}/expanded`, effective, nextAncestors));
                return view;
            }
            if (Array.isArray(node.children)) {
                view.children = node.children.map((child, index) => createNode(child, `${key}/${index}`, args, ancestors));
            }
            if (!(type in TYPE_LABELS)) view.error ||= `未知行为树节点：${type}`;
            return view;
        }

        let root;
        if (!record?.root) {
            nodeCount++;
            root = { key: nodeKey, type: "unknown", name: "", label: "未知节点", parameters: {}, children: [], error: "行为树缺少根节点。" };
        } else {
            const ancestors = new Set();
            if (record.kind === "subtree") ancestors.add(record.name);
            root = createNode(record.root, nodeKey, clone(record.parameters || {}), ancestors);
        }
        return { root, nodeCount, referenceCount };
    }

    return { createCatalog, buildTree };
});

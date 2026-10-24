(function (root) {
    "use strict";

    //#region 数据读取与展示分期
    const keyOf = value => String(value ?? "").trim().toLowerCase();
    const asArray = value => Array.isArray(value) ? value : [];
    const activeModules = item => Object.values(item.modules || {}).filter(body => body && body.enabled !== false);
    const finite = (value, fallback) => Number.isFinite(Number(value)) ? Number(value) : fallback;
    const capabilityNames = { grind: "研磨", cut: "切削", weave: "纺织" };

    function parseData(value) {
        if (value && typeof value === "object" && !Array.isArray(value)) return value;
        if (typeof value !== "string" || !value.trim().startsWith("{")) return {};
        try { return JSON.parse(value); } catch { return {}; }
    }

    function makeCatalog(entries, config, warnings) {
        const eras = asArray(config.eras).map(era => ({ ...era })).sort((a, b) => a.order - b.order);
        const stages = asArray(config.stages).map(stage => ({ ...stage })).sort((a, b) => a.order - b.order);
        const stageById = new Map(stages.map(stage => [stage.id, stage]));
        const assignments = new Map();
        const specialItems = new Map();
        for (const group of asArray(config.assignments)) {
            if (!stageById.has(group.stageId)) {
                warnings.push({ code: "unknown-stage", message: `分期配置引用了不存在的阶段：${group.stageId}` });
                continue;
            }
            for (const id of asArray(group.itemIds)) {
                if (assignments.has(keyOf(id))) warnings.push({ code: "duplicate-assignment", message: `${id} 有重复分期，采用最后一项。`, itemIds: [id] });
                assignments.set(keyOf(id), { stageId: group.stageId, reason: group.reason || "人工展示分期", source: "tech-tree-eras.json", confidence: "curated" });
            }
        }
        for (const group of asArray(config.specialItems)) for (const id of asArray(group.itemIds)) specialItems.set(keyOf(id), group);
        const nodes = [];
        const nodeByKey = new Map();
        for (const entry of entries || []) {
            if ((entry.kind && entry.kind !== "item") || !entry.final || entry.final.abstract) continue;
            const item = entry.final;
            const key = keyOf(item.id);
            if (!key || nodeByKey.has(key)) continue;
            let classification = assignments.get(key);
            if (!classification) {
                classification = { stageId: "unclassified", reason: "尚未配置时代，请在 tech-tree-eras.json 中确认；不会自动归到原始时代。", source: "未配置", confidence: "unknown" };
                if (!stageById.has("unclassified")) {
                    eras.push({ id: "unclassified", name: "待分期", order: 999, color: "#8d99a4" });
                    const stage = { id: "unclassified", eraId: "unclassified", name: "待确认时代", description: "新物品或尚未明确分期的物品。", order: 999 };
                    stages.push(stage);
                    stageById.set(stage.id, stage);
                }
            }
            const stage = stageById.get(classification.stageId);
            const special = specialItems.get(key);
            const node = {
                id: item.id, key, entry, name: entry.displayName || item.gameName || item.id,
                eraId: stage.eraId, stageId: stage.id, eraIndex: eras.findIndex(era => era.id === stage.eraId),
                stageIndex: stages.indexOf(stage), depth: 0, classification: { ...classification },
                recipes: [], incoming: [], outgoing: [], externalTargets: [], sourceStatus: "no-known-route",
                sourceNote: "当前未配置可解析的生产路线；展示分期不代表自然生成或科技解锁。",
                special: Boolean(special), specialLabel: special?.label || "", specialReason: special?.reason || ""
            };
            nodes.push(node);
            nodeByKey.set(key, node);
        }
        stages.forEach((stage, index) => { stage.index = index; });
        eras.forEach((era, index) => { era.index = index; });
        return { nodes, nodeByKey, eras, stages };
    }
    //#endregion

    //#region 工作站与加工能力
    function collectProviders(nodes, config, resources) {
        const stationProviders = new Map();
        const capabilityProviders = new Map();
        const mechanical = new Map(asArray(resources.mechanicalCatalog?.Nodes).map(node => [keyOf(node.Id), node]));
        const addStation = (station, node, evidence) => {
            if (!keyOf(station)) return;
            const key = keyOf(station);
            if (!stationProviders.has(key)) stationProviders.set(key, new Map());
            stationProviders.get(key).set(node.id, { id: node.id, evidence });
        };
        const addCapability = (capability, node, level, evidence) => {
            const key = keyOf(capability);
            if (!key) return;
            if (!capabilityProviders.has(key)) capabilityProviders.set(key, []);
            if (!capabilityProviders.get(key).some(provider => provider.id === node.id && provider.level === level))
                capabilityProviders.get(key).push({ id: node.id, level, evidence });
        };
        for (const node of nodes) {
            for (const [capability, body] of Object.entries(node.entry.final.processingCapabilities || {})) {
                if (finite(body?.level, 0) > 0) addCapability(capability, node, Number(body.level), `${node.id}.processingCapabilities.${capability}`);
            }
            for (const body of activeModules(node.entry.final)) {
                const parameters = body.parameters || {};
                for (const rule of asArray(config.moduleStations).filter(rule => rule.prefab === body.prefab))
                    for (const station of asArray(rule.stations)) addStation(station, node, rule.evidence);
                if (body.prefab === "Module_Mortar") {
                    const station = parameters.StationId || "mortar";
                    addStation(station, node, `${node.id}.modules.${body.prefab}.StationId`);
                    // 坩埚关闭敲击手势，不把未声明的默认研磨能力伪造成研磨路线。
                    if (keyOf(station) !== "crucible" && parameters.EnableStrikeGesture !== false)
                        addCapability(parameters.ProcessCapability || "grind", node, parameters.ProcessCapabilityLevel, `${node.id}.modules.${body.prefab}`);
                }
                if (body.prefab === "Module_ManualProcessor") addStation(parameters.Station || "hand_forge", node, `${node.id}.modules.${body.prefab}.Station`);
                if (body.prefab === "Module_MechanicalNode") {
                    const definition = mechanical.get(keyOf(parameters.DefinitionId));
                    if (!definition) continue;
                    addStation(definition.Station, node, `Mechanical.Nodes.${definition.Id}.Station`);
                    addCapability(definition.ProcessCapability, node, definition.ProcessCapabilityLevel, `Mechanical.Nodes.${definition.Id}.ProcessCapability`);
                }
            }
        }
        return { stationProviders, capabilityProviders };
    }
    //#endregion

    //#region 真实生产路线归一化
    function collectRecipes(entries, recipes, resources) {
        const result = new Map();
        const add = recipe => { if (recipe?.id && !result.has(keyOf(recipe.id))) result.set(keyOf(recipe.id), recipe); };
        for (const recipe of recipes || []) {
            const owner = recipe.sourceKind === "processing" ? (entries || []).find(entry => keyOf(entry.final?.id) === keyOf(recipe.sourceItemId)) : null;
            const evidence = owner ? `Items/${owner.package?.path || owner.final.id}#${owner.final.id}.processing.${recipe.capability}` : `Recipes/${recipe.id}`;
            add({ ...recipe, evidence: recipe.evidence || evidence });
        }
        const liquids = new Map(asArray(resources.liquids?.liquids || resources.liquids).map(liquid => [keyOf(liquid.id), liquid]));
        for (const entry of entries || []) {
            if ((entry.kind && entry.kind !== "item") || !entry.final || entry.final.abstract) continue;
            const item = entry.final;
            // 即使调用方尚未升级继承解析，单料加工也只读取当前物品自己的声明。
            const processing = entry.source ? entry.source.processing : item.processing;
            for (const [capability, body] of Object.entries(processing || {})) {
                if (!body || body.enabled === false) continue;
                add({ id: `processing:${item.id}:${capability}`, sourceKind: "processing", sourceItemId: item.id,
                    capability, work: body.work, minLevel: body.minLevel, maxLevel: body.maxLevel,
                    inputs: [{ match: "exact_item", itemId: item.id, amount: body.inputAmount }], outputs: body.outputs,
                    evidence: `Items/${entry.package?.path || item.id}#${item.id}.processing.${capability}` });
            }
            for (const reaction of asArray(item.reactions)) {
                const recipe = { ...reaction, id: `reaction:${reaction.id || item.id}`, sourceKind: "reaction", sourceItemId: item.id,
                    evidence: `Items/${entry.package?.path || item.id}#${item.id}.reactions/${reaction.id}` };
                if (reaction.liquidOutput) {
                    const liquid = liquids.get(keyOf(reaction.liquidOutput.liquidId));
                    if (liquid?.solidification?.outputItemId) {
                        recipe.outputs = [...asArray(reaction.outputs), { itemId: liquid.solidification.outputItemId,
                            amount: finite(reaction.liquidOutput.amount, 1) * finite(liquid.solidification.outputAmount, 1) }];
                        recipe.intermediateLiquid = { id: liquid.id, name: liquid.displayName || liquid.id, amount: reaction.liquidOutput.amount,
                            coolingBelow: liquid.solidification.meltingPoint };
                        recipe.evidence += ` + Liquids/liquids.json#${liquid.id}.solidification`;
                    } else {
                        recipe.unresolvedLiquid = reaction.liquidOutput.liquidId;
                    }
                }
                add(recipe);
            }
            for (const [index, transition] of asArray(item.matter?.transitions).entries()) {
                if (!transition.outputItemId) continue;
                add({ ...transition, id: `transition:${item.id}:${transition.id || index}`, sourceKind: "transition",
                    inputs: [{ match: "exact_item", itemId: item.id, amount: 1 }],
                    outputs: [{ itemId: transition.outputItemId, amount: finite(transition.outputAmountMultiplier, 1) }],
                    evidence: `Items/${entry.package?.path || item.id}#${item.id}.matter.transitions/${transition.id || index}` });
            }
            for (const body of activeModules(item)) {
                const parameters = body.parameters || {};
                if (body.prefab === "Module_Building") {
                    const data = parseData(body.data?.BitData);
                    if (data.BuildingPrefabId && data.SummonerPrefabId && keyOf(data.BuildingPrefabId) !== keyOf(data.SummonerPrefabId))
                        add({ id: `placement:${data.SummonerPrefabId}:${data.BuildingPrefabId}`, sourceKind: "placement",
                            displayName: "放置为建筑", inputs: [{ match: "exact_item", itemId: data.SummonerPrefabId, amount: 1 }],
                            outputs: [{ itemId: data.BuildingPrefabId, amount: 1 }],
                            evidence: `Items/${entry.package?.path || item.id}#${item.id}.Module_Building.BitData` });
                }
                if (body.prefab === "Module_Plantable" && parameters.cropItemId)
                    add({ id: `planting:${item.id}`, sourceKind: "planting", displayName: "播种与生长",
                        inputs: [{ match: "exact_item", itemId: item.id, amount: 1 }], outputs: [{ itemId: parameters.cropItemId, amount: 1 }],
                        conditions: { environment: "需要可种植地块与实际生长条件" }, evidence: `${item.id}.Module_Plantable.cropItemId` });
                if (body.prefab === "Module_CropYield")
                    add({ id: `harvest:${item.id}`, sourceKind: "harvest", displayName: "成熟后采收",
                        inputs: [{ match: "exact_item", itemId: item.id, amount: 0 }], outputs: parameters.outputs,
                        conditions: { mature: true }, evidence: `${item.id}.Module_CropYield.outputs` });
                if (body.prefab === "Module_Growth" && parameters.allowCultivatedHarvest) {
                    const outputs = [];
                    if (parameters.harvestFoodItemId) outputs.push({ itemId: parameters.harvestFoodItemId, minAmount: parameters.harvestFoodMin, maxAmount: parameters.harvestFoodMax });
                    if (parameters.harvestSeedItemId) outputs.push({ itemId: parameters.harvestSeedItemId, minAmount: parameters.harvestSeedMin, maxAmount: parameters.harvestSeedMax });
                    add({ id: `growth:${item.id}`, sourceKind: "harvest", displayName: "成熟后采收", inputs: [{ match: "exact_item", itemId: item.id, amount: 0 }], outputs,
                        conditions: { mature: true }, evidence: `${item.id}.Module_Growth` });
                }
            }
        }
        for (const [index, process] of asArray(resources.mechanicalCatalog?.Processes).entries()) {
            add({ id: `mechanical:${process.Station}:${process.Input}:${index}`, sourceKind: "mechanical", requiredStation: process.Station,
                work: process.WorkSeconds, inputs: [{ match: "exact_item", itemId: process.Input, amount: finite(process.InputAmount, 1) }],
                outputs: asArray(process.Outputs).map(output => ({ ...output, itemId: output.ItemName })),
                evidence: `Assets/Resources/Config/Mechanical/mechanical-catalog.json#Processes[${index}]` });
        }
        const lootTables = resources.lootTables;
        for (const entry of entries || []) {
            if (!entry.final || entry.final.abstract || entry.kind === "buff") continue;
            const sourceInputs = entry.kind === "actor" ? [] : [{ match: "exact_item", itemId: entry.final.id, amount: 0 }];
            const externalSource = entry.kind === "actor" ? (entry.displayName || entry.final.id) : undefined;
            const table = lootTables instanceof Map ? lootTables.get(keyOf(entry.final.lootTableId)) : lootTables?.[keyOf(entry.final.lootTableId)];
            if (table?.entries?.length)
                add({ id: `loot:${entry.kind || "item"}:${entry.final.id}`, sourceKind: "loot", displayName: "采集或击破掉落",
                    inputs: sourceInputs, externalSource, outputs: table.entries,
                    evidence: `LootTables/${table.id || entry.final.lootTableId} ← ${entry.final.id}` });
            for (const [index, body] of activeModules(entry.final).entries()) {
                const parameters = body.parameters || {};
                if (body.prefab === "Module_Production") for (const [productionIndex, production] of asArray(parameters.ProductionList).entries())
                    add({ id: `production:${entry.kind || "item"}:${entry.final.id}:${index}:${productionIndex}`, sourceKind: "harvest", displayName: "周期生产",
                        inputs: sourceInputs, externalSource,
                        outputs: [{ itemId: production.itemName, minAmount: production.itemCountMin, maxAmount: production.itemCountMax, probability: production.SpawnProbability }],
                        conditions: { periodic: true, maxProductionTime: production.MaxProductionTime, randomProductionTime: production.Random_ProductionTime, productionSpeed: parameters.ProductionSpeed },
                        evidence: `${entry.kind === "actor" ? "Actors" : "Items"}/${entry.package?.path || entry.final.id}#${entry.final.id}.Module_Production.ProductionList[${productionIndex}]` });
                if (body.prefab === "Module_Collectable" && parameters.CollectItemId)
                    add({ id: `collect:${entry.kind || "item"}:${entry.final.id}:${index}`, sourceKind: "harvest", displayName: "交互采集",
                        inputs: sourceInputs, externalSource, outputs: [{ itemId: parameters.CollectItemId, amount: 1 }],
                        conditions: { stockRequired: true, maxStock: parameters.MaxStock },
                        evidence: `${entry.kind === "actor" ? "Actors" : "Items"}/${entry.package?.path || entry.final.id}#${entry.final.id}.Module_Collectable.CollectItemId` });
            }
        }
        return Array.from(result.values());
    }
    //#endregion

    //#region 依赖图与条件分组
    function build(entries, recipes, config = {}, resources = {}) {
        const warnings = [];
        const { nodes, nodeByKey, eras, stages } = makeCatalog(entries, config, warnings);
        const externalDefinitions = new Map((entries || []).filter(entry => entry.kind === "actor" && !entry.final?.abstract).map(entry => [keyOf(entry.final.id), entry]));
        const { stationProviders, capabilityProviders } = collectProviders(nodes, config, resources);
        const edges = [];
        const routes = [];
        const upgrades = [];
        const tagIndex = new Map();
        for (const node of nodes) for (const tag of node.entry.final.tags || []) {
            const key = keyOf(tag);
            if (!tagIndex.has(key)) tagIndex.set(key, []);
            tagIndex.get(key).push(node);
        }
        const name = id => nodeByKey.get(keyOf(id))?.name || String(id);
        const conditionsOf = source => Object.fromEntries(Object.entries(source || {}).filter(([key]) =>
            ["minTemperature", "maxTemperature", "temperature", "minMoisture", "maxMoisture", "minLevel", "maxLevel", "work", "actions", "inputRule", "intermediateLiquid", "unresolvedLiquid", "externalSource", "conditions"].includes(key)));
        function appendEdge(source, target, group, route, kind) {
            const from = nodeByKey.get(keyOf(source));
            const to = nodeByKey.get(keyOf(target));
            if (!from || !to) return;
            const edge = { id: `edge:${edges.length}`, source: from.id, target: to.id, kind, routeId: route.id,
                label: group.label, amount: group.amount, optional: Boolean(group.optional), groupId: group.groupId,
                conditions: group.conditions || {}, evidence: route.evidence, feedback: false };
            edges.push(edge);
            from.outgoing.push(edge.id);
            to.incoming.push(edge.id);
        }
        for (const recipe of collectRecipes(entries, recipes, resources)) {
            const route = { id: recipe.id, type: recipe.sourceKind || recipe.recipeType || "crafting", label: recipe.displayName || recipe.id,
                inputs: [], outputs: [], requirements: [], conditions: conditionsOf(recipe), evidence: recipe.evidence || recipe.id,
                inputRelation: "AND", alternativeRelation: "OR" };
            for (const [index, input] of asArray(recipe.inputs).entries()) {
                let candidates = input.match === "tag" ? (tagIndex.get(keyOf(input.tag)) || []) : [nodeByKey.get(keyOf(input.itemId))].filter(Boolean);
                if (input.excludeTag) candidates = candidates.filter(node => !(node.entry.final.tags || []).some(tag => keyOf(tag) === keyOf(input.excludeTag)));
                const label = input.match === "tag" ? `任一 ${input.tag} 类物品` : name(input.itemId);
                const sourceObject = ["loot", "harvest"].includes(route.type);
                const group = { ...input, groupId: `${route.id}:input:${index}`, candidates: candidates.map(node => node.id),
                    tool: input.amount === 0 && !sourceObject, optional: input.match === "tag", conditions: conditionsOf(input),
                    label: `${label}${sourceObject ? "（来源对象）" : input.amount === 0 ? "（工具，不消耗）" : ` ×${input.amount ?? 1}`}` };
                route.inputs.push(group);
                if (!candidates.length) warnings.push({ code: "unresolved-input", routeId: route.id, message: `${route.label} 找不到输入：${label}` });
            }
            for (const output of asArray(recipe.outputs)) {
                if (output.copyInputTag || output.copyInputItemId) {
                    const itemIds = output.copyInputItemId ? [nodeByKey.get(keyOf(output.copyInputItemId))].filter(Boolean).map(node => node.id)
                        : (tagIndex.get(keyOf(output.copyInputTag)) || []).map(node => node.id);
                    upgrades.push({ ...route, output, label: recipe.displayName || "现有物品强化", itemIds });
                    continue;
                }
                if (!output.itemId) continue;
                const target = nodeByKey.get(keyOf(output.itemId));
                if (!target && (route.type === "placement" || externalDefinitions.has(keyOf(output.itemId)))) {
                    const external = externalDefinitions.get(keyOf(output.itemId));
                    const label = external ? `产出${external.displayName || output.itemId}（生物，未加入物品节点）` : `放置目标：${output.itemId}（物品目录外的世界对象）`;
                    for (const input of route.inputs) for (const source of input.candidates)
                        nodeByKey.get(keyOf(source)).externalTargets.push({ id: output.itemId, label, evidence: route.evidence });
                    continue;
                }
                if (!target) {
                    warnings.push({ code: "unresolved-output", routeId: route.id, message: `${route.label} 的产物 ${output.itemId} 不在具体物品目录中。` });
                    continue;
                }
                route.outputs.push({ ...output, itemId: target.id, name: target.name });
            }
            if (recipe.requiredStation) {
                const station = keyOf(recipe.requiredStation);
                const alias = asArray(config.stationAliases).find(rule => keyOf(rule.station) === station);
                const candidates = Array.from(stationProviders.get(station)?.keys() || []);
                const direct = nodeByKey.get(station);
                if (direct && !candidates.includes(direct.id)) candidates.push(direct.id);
                route.requirements.push({ kind: "station", station: recipe.requiredStation, groupId: `${route.id}:station`,
                    candidates, optional: Boolean(alias?.portable) || candidates.length > 1, portable: Boolean(alias?.portable),
                    label: alias?.label || `工作站：${recipe.requiredStation}`, conditions: {}, evidence: alias?.evidence || "物品工作站模块与 Mechanical.Nodes" });
                if (!candidates.length && !alias?.portable) warnings.push({ code: "unresolved-station", routeId: route.id, message: `${route.label} 的工作站 ${recipe.requiredStation} 尚未匹配到具体物品。` });
            }
            if (recipe.capability) {
                const providers = (capabilityProviders.get(keyOf(recipe.capability)) || []).filter(provider =>
                    (recipe.minLevel === undefined && recipe.maxLevel === undefined) ||
                    (provider.level > 0 && (recipe.minLevel === undefined || provider.level >= recipe.minLevel) && (recipe.maxLevel === undefined || provider.level <= recipe.maxLevel)));
                const candidates = [...new Set(providers.map(provider => provider.id))];
                const level = recipe.minLevel !== undefined || recipe.maxLevel !== undefined ? `，等级 ${recipe.minLevel ?? "不限"}～${recipe.maxLevel ?? "不限"}` : "";
                route.requirements.push({ kind: "capability", capability: recipe.capability, groupId: `${route.id}:capability`, candidates,
                    optional: candidates.length > 1, label: `需要${capabilityNames[recipe.capability] || recipe.capability}能力${level}`,
                    conditions: { minLevel: recipe.minLevel, maxLevel: recipe.maxLevel }, evidence: providers.map(provider => provider.evidence).join("；") });
                if (!candidates.length) warnings.push({ code: "unresolved-capability", routeId: route.id, message: `${route.label} 的加工能力 ${recipe.capability} 尚未匹配到具体工具或机器。` });
            }
            const conditionLabels = [];
            if (recipe.minTemperature !== undefined || (recipe.recipeType === "smelting" && recipe.temperature !== undefined)) conditionLabels.push(`温度至少 ${recipe.minTemperature ?? recipe.temperature}℃`);
            if (recipe.maxTemperature !== undefined) conditionLabels.push(`温度至多 ${recipe.maxTemperature}℃`);
            if (recipe.minMoisture !== undefined) conditionLabels.push(`含水率至少 ${recipe.minMoisture}`);
            if (recipe.maxMoisture !== undefined) conditionLabels.push(`含水率至多 ${recipe.maxMoisture}`);
            if (recipe.intermediateLiquid) conditionLabels.push(`先得到${recipe.intermediateLiquid.name}，再冷却到 ${recipe.intermediateLiquid.coolingBelow}℃ 以下凝固`);
            if (recipe.externalSource) conditionLabels.push(`外部来源：${recipe.externalSource}`);
            if (recipe.conditions?.mature) conditionLabels.push("需要成熟后采收");
            if (recipe.conditions?.periodic) conditionLabels.push("受生产周期与产出概率控制");
            if (recipe.conditions?.stockRequired) conditionLabels.push("需要来源对象有可采集存量");
            if (recipe.conditions?.environment) conditionLabels.push(recipe.conditions.environment);
            if (conditionLabels.length) route.requirements.push({ kind: "condition", label: conditionLabels.join("；"), candidates: [], conditions: route.conditions });
            if (recipe.unresolvedLiquid) warnings.push({ code: "liquid-intermediate", routeId: route.id, message: `${route.label} 产出液体 ${recipe.unresolvedLiquid}，尚无可解析的固体终点。` });
            if (!route.outputs.length) continue;
            routes.push(route);
            for (const output of route.outputs) {
                const node = nodeByKey.get(keyOf(output.itemId));
                node.recipes.push(route.id);
                const nonSelfInput = route.inputs.some(group => group.candidates.some(id => keyOf(id) !== node.key));
                if (recipe.externalSource || nonSelfInput || !route.inputs.length) {
                    node.sourceStatus = "configured";
                    node.sourceNote = "已解析到下列生产、加工或获取路线；各路线为备选，仍需满足其全部条件。";
                }
                for (const input of route.inputs) {
                    const kind = ["placement", "transition", "harvest", "planting", "loot"].includes(route.type) ? route.type : route.type === "reaction" ? "transition" : route.type === "processing" || route.type === "mechanical" ? "processing" : input.tool ? "tool" : "ingredient";
                    for (const source of input.candidates) appendEdge(source, output.itemId, input, route, kind);
                }
                for (const requirement of route.requirements) if (requirement.kind !== "condition")
                    for (const source of requirement.candidates) appendEdge(source, output.itemId, requirement, route, "station");
            }
        }
        assignDepth(nodes, edges, nodeByKey);
        assignNaturalResources(nodes, routes, stages, nodeByKey);
        for (const node of nodes) {
            node.upgrades = upgrades.filter(upgrade => upgrade.itemIds.includes(node.id));
            if (node.special) node.sourceNote = `${node.specialLabel}：${node.specialReason} ${node.sourceNote}`;
        }
        const stats = { nodeCount: nodes.length, edgeCount: edges.length, routeCount: routes.length,
            eraCount: eras.filter(era => era.id !== "unclassified").length, stageCount: stages.filter(stage => stage.id !== "unclassified").length,
            unclassifiedCount: nodes.filter(node => node.stageId === "unclassified").length,
            noKnownRouteCount: nodes.filter(node => node.sourceStatus === "no-known-route").length,
            specialCount: nodes.filter(node => node.special).length, cycleCount: new Set(nodes.filter(node => node.cyclic).map(node => node.component)).size };
        return { nodes, edges, routes, eras, stages, warnings, stats,
            description: config.description || "时代是人工展示分期，连线表示配置中的真实关系。" };
    }
    //#endregion

    //#region 自然来源与循环安全的层级排序
    // 采集来源独立于展示时代，明确自然来源及其直接采集产物进入资源区。
    function assignNaturalResources(nodes, routes, stages, nodeByKey) {
        const naturalStages = new Set(stages.filter(stage => stage.layoutBand === "natural").map(stage => stage.id));
        const routeById = new Map(routes.map(route => [route.id, route]));
        const gatheringRoute = route => ["loot", "harvest"].includes(route.type);
        for (const node of nodes) {
            const item = node.entry.final;
            const modules = activeModules(item);
            const source = modules.some(body => body.prefab === "Module_ResourceHarvest") || (item.tags || []).some(tag => keyOf(tag) === "tree");
            const raw = naturalStages.has(node.stageId) && !modules.some(body => /DimensionPortal/.test(body.prefab))
                && node.recipes.every(id => gatheringRoute(routeById.get(id)));
            node.naturalResource = !node.special && (source || raw);
        }
        let changed = true;
        while (changed) {
            changed = false;
            for (const route of routes) {
                if (!gatheringRoute(route)) continue;
                const fromNature = route.inputs.length
                    ? route.inputs.every(group => group.candidates.length && group.candidates.every(id => nodeByKey.get(keyOf(id))?.naturalResource))
                    : Boolean(route.conditions.externalSource);
                if (!fromNature) continue;
                for (const output of route.outputs) {
                    const node = nodeByKey.get(keyOf(output.itemId));
                    if (!node || node.special || node.naturalResource) continue;
                    node.naturalResource = true;
                    changed = true;
                }
            }
        }
    }

    function assignDepth(nodes, edges, nodeByKey) {
        // 强连通分量收拢回收和种植循环，仅用于布局，不把所有备选路线当作必需材料。
        const adjacency = new Map(nodes.map(node => [node.id, []]));
        for (const edge of edges) if (edge.kind !== "station" && edge.kind !== "tool") adjacency.get(edge.source).push(edge.target);
        const indices = new Map(), low = new Map(), stack = [], onStack = new Set(), components = [];
        let counter = 0;
        function visit(id) {
            indices.set(id, counter); low.set(id, counter++); stack.push(id); onStack.add(id);
            for (const target of adjacency.get(id)) {
                if (!indices.has(target)) { visit(target); low.set(id, Math.min(low.get(id), low.get(target))); }
                else if (onStack.has(target)) low.set(id, Math.min(low.get(id), indices.get(target)));
            }
            if (low.get(id) !== indices.get(id)) return;
            const component = [];
            let next;
            do { next = stack.pop(); onStack.delete(next); component.push(next); } while (next !== id);
            const componentId = components.length;
            for (const itemId of component) { const node = nodeByKey.get(keyOf(itemId)); node.component = componentId; node.cyclic = component.length > 1 || adjacency.get(itemId).includes(itemId); }
            components.push(component);
        }
        for (const node of nodes) if (!indices.has(node.id)) visit(node.id);
        const outgoing = components.map(() => new Set()), indegree = components.map(() => 0), depths = components.map(() => 0);
        for (const edge of edges) {
            const source = nodeByKey.get(keyOf(edge.source)), target = nodeByKey.get(keyOf(edge.target));
            edge.feedback = source.component === target.component || source.stageIndex > target.stageIndex;
            if (edge.kind === "station" || edge.kind === "tool" || source.component === target.component) continue;
            if (!outgoing[source.component].has(target.component)) { outgoing[source.component].add(target.component); indegree[target.component]++; }
        }
        const queue = indegree.map((degree, index) => degree === 0 ? index : -1).filter(index => index >= 0);
        for (let index = 0; index < queue.length; index++) for (const target of outgoing[queue[index]]) {
            depths[target] = Math.max(depths[target], depths[queue[index]] + 1);
            if (--indegree[target] === 0) queue.push(target);
        }
        for (const node of nodes) node.depth = depths[node.component];
    }
    //#endregion

    const api = { build };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.ItemTechTreeModel = api;
})(globalThis);

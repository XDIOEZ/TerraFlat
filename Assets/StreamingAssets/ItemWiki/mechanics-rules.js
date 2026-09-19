(function () {
    "use strict";
    const mechanics = typeof module !== "undefined" && module.exports ? require("./mechanics.js") : globalThis.ItemMechanics;
    const text = mechanics.valueText;
    const parameters = body => body.parameters || {};
    const get = (body, path) => path.split(".").reduce((value, key) => value?.[key], parameters(body));
    const field = (body, path) => text(get(body, path));
    const percent = value => typeof value === "number" ? `${Math.round(value * 1000) / 10}%` : "";
    const range = (min, max) => min === max || max === undefined ? text(min) : `${text(min)}～${text(max)}`;
    const named = (context, id) => context?.name?.(id) || text(id);
    const buffName = id => ({
        "core:herbal_recovery": "草药恢复",
        "燃烧": "燃烧"
    })[id] || String(id || "对应状态");
    const resourceToolName = value => ({
        1: "镐",
        2: "铲子",
        3: "斧头",
        Pickaxe: "镐",
        Shovel: "铲子",
        Axe: "斧头"
    })[value] || "对应工具";
    const dimensionName = id => id === "cave" ? "地下洞穴" : id === "surface" ? "地表" : String(id || "目标区域");
    const bodyPartName = id => ({
        Head: "头部",
        Chest: "胸部",
        Abdomen: "腹部",
        Pelvis: "骨盆",
        LeftLeg: "左腿",
        RightLeg: "右腿",
        LeftHand: "左手",
        RightHand: "右手"
    })[id] || id;
    function register(prefabs, title, source, explain) {
        mechanics.register(prefabs, { title, source, explain });
    }

    register(["Mod_Damage", "Mod_Damage_AI"], "攻击与伤害", "docs/systems/combat.md；Mod_Damage", body => [
        "命中目标时可以造成伤害，实际伤害还会受到目标防御和使用者状态影响。"
    ]);
    register(["Module_Weapon_AnimationAction", "Mod_Weapon_AnimationAction"], "近战/工具使用", "docs/systems/combat.md；Mod_Weapon_AnimationAction.StartAttack", body => [
        `挥动它进行攻击时，每次基础消耗 ${field(body, "staminaCostPerAttack")} 点体力；${get(body, "loopComboOnHold") === true ? "按住攻击键可以连续挥动。" : "每次攻击需要重新按下攻击键。"}`
    ]);
    register(["Mod_Bow"], "弓的使用", "docs/systems/combat.md；Mod_Bow", body => {
        const usesSelfAsAmmo = get(body, "UseHeldItemAsAmmo") === true;
        const multiplier = get(body, "ProjectileDamageMultiplier");
        const lines = [
            `按住攻击键可以蓄力，完整蓄力约需 ${field(body, "FullChargeSeconds")} 秒，期间每秒消耗 ${field(body, "StaminaConsumePerSecond")} 点体力；松开后${usesSelfAsAmmo ? "会把手里的这件物品直接投掷出去" : "会消耗一支可用箭矢并射出"}。`,
            `最远瞄准距离约为 ${field(body, "MaxAimDistance")}。`
        ];
        if (!usesSelfAsAmmo) {
            lines[1] += `${typeof multiplier === "number" && multiplier !== 1 ? `这把弓还会把箭矢伤害调整为 ${text(multiplier)} 倍；` : ""}箭矢本身决定飞行、命中和回收等效果。`;
        }
        return lines;
    });
    register(["Mod_Projectile"], "箭矢飞行与回收", "docs/systems/combat.md；Mod_Projectile", body => {
        const recovery = get(body, "RecoveryChance");
        const salvage = get(body, "BrokenSalvageChance");
        const lines = [`作为投射物飞出后，它会按蓄力在 ${field(body, "MinSpeed")}～${field(body, "MaxSpeed")} 格/秒的速度范围内飞行，最长持续 ${field(body, "MaxFlightSeconds")} 秒。`];
        if (recovery === 1) lines.push("落地或命中后可以完整回收。");
        else if (typeof recovery === "number" && recovery > 0) lines.push(`落地或命中后有 ${percent(recovery)} 的概率可以完整回收。`);
        if (typeof salvage === "number" && salvage > 0) lines.push(`没有完整回收时，还有 ${percent(salvage)} 的概率留下破损残料。`);
        return lines;
    });
    register(["Module_DamageReciver"], "受伤、生命与死亡", "docs/systems/combat.md；DamageReceiver", body => [
        "它有自己的生命值，可以受到伤害；生命归零后会死亡或被破坏，并结算对应掉落物。"
    ]);
    register(["Module_ResourceHarvest"], "资源采集门槛", "flatworld-item-module Skill；Mod_ResourceHarvest", body => [
        `采集这个资源需要至少 ${field(body, "minimumTier")} 级${resourceToolName(get(body, "requiredTool"))}；使用其他武器攻击无法绕过这个要求。`
    ]);
    register(["Module_Food"], "营养与食物机制", "Mod_Food；FoodSpoilageObserver.cs；docs/systems/survival.md", (body, context, entry) => {
        const data = body.data || {};
        const food = data.FoodData || {};
        const nutrition = food.nutrition || {};
        if (entry.kind === "actor") return ["这个生物拥有自己的营养状态，营养会参与体力、健康和恢复等生存机制。"];
        const lines = [`可以食用，食用后会补充碳水 ${text(nutrition.Carbohydrates)}、脂肪 ${text(nutrition.Fat)}、蛋白质 ${text(nutrition.Protein)}、水分 ${text(nutrition.Water)} 和维生素 ${text(nutrition.Vitamins)}；完整吃完大约需要 ${text(food.Max_EatingProgress)} 秒。`];
        const spoilage = (data.MechanicStates || []).find(state => state.StateKey === "food.spoilage")?.Data;
        if (spoilage?.EnableSpoilage === true) lines.push(`放在库存中会逐渐腐败，大约 ${text(spoilage.SpoilageIntervalSeconds)} 秒后变成${named(context, spoilage.SpoilageTargetItemID)}。`);
        return lines;
    });
    register(["Module_HeldFood"], "手持食物操作", "Mod_HeldFood；docs/systems/survival.md", () => ["拿在手上时可以直接进行食用操作。"]);
    register(["Module_Equipment_Store"], "装备后效果", "docs/systems/equipment.md；EquipmentInstance_Speed / Defense / Bag", body => {
        const instances = parameters(body).equipmentInstances;
        if (!Array.isArray(instances)) return ["需要装备到角色对应的装备槽后才会生效，卸下后效果消失。"];
        return ["需要装备到角色对应的装备槽后才会生效，卸下后效果消失。", ...instances.map(instance => {
            const type = instance.$concreteType;
            if (type === "EquipmentInstance_Speed") return `装备后会增加 ${text(instance.SpeedIncrease)} 点移动速度。`;
            if (type === "EquipmentInstance_Defense") return "装备后会提高角色防御能力。";
            if (type === "EquipmentInstance_Bag") return `装备后会为行囊增加 ${text(instance.BagData?.itemSlots?.length)} 个额外槽位。`;
            return "";
        })];
    });
    register(["Module_Plantable"], "播种方式", "docs/systems/agriculture.md；Mod_Plantable", (body, context) => [
        `可以作为种子使用，在合适的地块上种成 ${named(context, get(body, "cropItemId"))}；最大播种距离约为 ${field(body, "maxPlantingDistance")}。`
    ]);
    register(["Module_Hoe"], "锄地方式", "docs/systems/agriculture.md；Mod_Hoe", body => [
        `可以用来开垦土地，每个地块需要锄 ${field(body, "usesPerTile")} 次才能完成；最远作用距离约为 ${field(body, "maxTillingDistance")}。`
    ]);
    register(["Module_Crop"], "作物成长与收获", "docs/systems/agriculture.md；Mod_Crop", body => [
        `这是一株会生长的作物，基础成熟时间约为 ${field(body, "growthDurationSeconds")} 秒；生长过程中会持续消耗地块中的水分和肥力，缺水或缺肥会减慢成长，降雨则能帮助生长。`,
        "成熟后可以进行收获；一次性作物收获后会消失，想继续种植需要重新播种。"
    ]);
    register(["Module_CropYield"], "成熟采收产物", "Mod_CropYield.cs；docs/systems/agriculture.md", (body, context) => [
        ...(parameters(body).outputs || []).map(output => `成熟收获时会获得${named(context, output.itemId)} ×${range(output.minAmount, output.maxAmount)}${output.probability < 1 ? `，出现概率约为 ${percent(output.probability)}` : ""}。`)
    ]);
    register(["Module_Growth"], "分阶段生长", "Mod_Grow；docs/systems/agriculture.md", (body, context) => {
        const lines = ["这株植物会经历不同生长阶段，随着成长推进改变外观和状态。"];
        if (get(body, "allowCultivatedHarvest") === true) {
            lines.push(`人工栽培并成熟后，可以收获 ${named(context, get(body, "harvestFoodItemId"))} ×${range(get(body, "harvestFoodMin"), get(body, "harvestFoodMax"))}，并获得对应种子。`);
        }
        return lines;
    });
    register(["Module_PlantClimate"], "植物气候限制", "Mod_PlantClimate；docs/systems/agriculture.md", body => {
        const minGrowth = get(body, "minimumGrowthTemperature");
        const maxGrowth = get(body, "maximumGrowthTemperature");
        const minSurvival = get(body, "minimumSurvivalTemperature");
        const maxSurvival = get(body, "maximumSurvivalTemperature");
        if ([minGrowth, maxGrowth, minSurvival, maxSurvival].some(value => value === undefined)) {
            return ["生长会受到环境温度影响，过冷或过热都会抑制成长；长期处于无法生存的极端温度中会逐渐死亡。"];
        }
        return [
            `适合在 ${text(minGrowth)}～${text(maxGrowth)}℃ 生长；超出这个范围会停止或减缓成长。`,
            `它能承受的温度范围约为 ${text(minSurvival)}～${text(maxSurvival)}℃，长期暴露在更极端的温度中会逐渐死亡。`
        ];
    });
    register(["Module_Production"], "周期生产", "Entities/Item/Modules/World/Mod_ItemMaker.cs", (body, context) => [
        ...(parameters(body).ProductionList || []).map(production => `它会周期性产出 ${named(context, production.itemName)} ×${range(production.itemCountMin, production.itemCountMax)}${production.SpawnProbability < 1 ? `，每次成功概率约为 ${percent(production.SpawnProbability)}` : ""}。`)
    ]);
    register(["Module_Collectable"], "交互采集库存", "Items/Food/Mod_Collectable.cs；docs/systems/agriculture.md", (body, context) => [
        `可以直接交互采集 ${named(context, get(body, "CollectItemId"))}，每次取出 1 份；最多能暂存 ${field(body, "MaxStock")} 份。`,
        "采集不会直接破坏这个对象；存量耗尽后，需要等待它通过自身生产机制重新补充。"
    ]);
    register(["Module_TemperatureYield"], "温度对产量的影响", "ResourceYieldUtility；Mod_TemperatureYield", (body, context) => [
        `环境温度会影响 ${named(context, get(body, "OutputItemId"))} 的产量：在约 ${field(body, "ColdTemperatureCelsius")}℃ 时为 ${field(body, "ColdMultiplier")} 倍，在约 ${field(body, "WarmTemperatureCelsius")}℃ 时为 ${field(body, "WarmMultiplier")} 倍。`
    ]);
    register(["Module_WaterVessel"], "液体容器", "Items/Food/Mod_WaterVessel.cs", body => {
        const capacity = get(body, "capacity");
        return [`可以盛装、转移、饮用或倒出液体${capacity !== undefined ? `，容量为 ${text(capacity)} 份` : ""}。液体的水质和其他状态会跟随容器保存，不同液体会产生各自的效果。`];
    });
    register(["Module_VesselHeating"], "容器加热", "Mod_VesselHeating；Mod_WaterVessel", () => ["容器中的液体可以被加热处理；具体会变成饮用水、盐或其他结果，取决于装入的液体和加热条件。"]);
    register(["Module_Fuel"], "燃料使用", "Mod_Fuel；docs/systems/building.md", (body, context, entry) => {
        const isHeatingBuilding = Object.values(entry?.final?.modules || {}).some(module =>
            String(module?.prefab || "").includes("Mod_Furnace"));
        return [
            isHeatingBuilding
                ? `点燃后会持续消耗燃料，燃烧时最高可支持约 ${field(body, "Data.MaxTemperature")}℃ 的温度；实际消耗速度会根据自身特性变化。`
                : `可以作为燃料使用，燃烧时最高可支持约 ${field(body, "Data.MaxTemperature")}℃ 的温度；实际燃烧速度会根据自身特性变化。`,
            get(body, "igniteWhileHeld") === true ? "拿在手上时也可以被点燃。" : ""
        ];
    });
    register(["BuildingFeature_Chest_Wood_Mod_Inventory"], "容器收纳", "docs/systems/inventory.md；Mod_Inventory", body => {
        const slotCount = body.data?.Data?.itemSlots?.length;
        return [`可以作为储物容器使用${slotCount !== undefined ? `，提供 ${text(slotCount)} 个物品槽位` : ""}；打开后可以存入、取出和整理物品。`];
    });
    register(["Module_Building"], "建筑放置与实体", "docs/systems/building.md；Mod_Building", (body, context) => {
        const serialized = body.data?.BitData;
        let data;
        if (typeof serialized === "string" && serialized.trim().startsWith("{")) data = JSON.parse(serialized);
        return [`可以放置到世界中成为${data?.BuildingPrefabId ? named(context, data.BuildingPrefabId) : "对应建筑"}；放置时需要满足占地与地形条件，拆除后会重新变回可携带物品，并保留原有状态。`];
    });
    register(["Module_Mortar", "BuildingFeature_WorkBench_Mod_MakeTable"], "工作站加工", "docs/systems/crafting.md；CraftingStationController", body => [
        "可以作为加工工作站使用。把材料放进去后会显示能够完成的配方，确认制作时才会消耗材料并生成产物。"
    ]);
    register(["BuildingFeature_BlastFurnace_Mod_Furnace", "BuildingFeature_Bonfire_Mod_Furnace", "BuildingFeature_Smelter_Mod_Furnace"], "热加工", "docs/systems/crafting.md；Mod_Furnace", () => ["可以进行需要温度的加工或冶炼。放入正确材料并把炉温提升到配方要求后，就能完成对应产物。"]);
    register(["BuildingFeature_Bonfire_Mod_LightSource", "BuildingFeature_Torch_Building_Mod_LightSource"], "照明", "Mod_LightSource", body => {
        const lightRange = get(body, "Data.Range");
        return [`会作为光源照亮周围${lightRange !== undefined ? `，有效范围约为 ${text(lightRange)} 格` : ""}，同时也会影响附近地块的光照环境。`];
    });
    register(["BuildingFeature_Door_Stone_Mod_Door", "BuildingFeature_Door_Wood_Mod_Door"], "门", "Mod_Door", () => [
        "可以直接交互开门或关门。关闭时会挡住角色移动，打开后解除阻挡并允许通过。"
    ]);
    register(["BuildingFeature_CompostBin_Mod_CompostBin"], "堆肥", "Mod_CompostBin", () => [
        "可以放入树叶、腐败物和其他可堆肥材料。每个槽位会独立计时，经过一段时间后把材料转化成肥料。"
    ]);
    register(["BuildingFeature_Meatrack_Meatrack"], "晾晒肉类", "Meatrack", () => [
        "可以挂上肉类进行风干或熏制，最多同时处理三份。不同肉类有自己的处理时间和结果，附近的篝火或熔炉等热源会加快加工速度。"
    ]);
    register(["BuildingFeature_MineEntrance_DimensionPortal"], "维度入口", "DimensionPortal", (body, context, entry) => [
        `交互后可以前往${dimensionName(get(body, "targetDimensionId") || (String(entry?.final?.id).toLowerCase().includes("caveexit") ? "surface" : "cave"))}。切换时会保存当前世界状态，并在目标区域对应的入口位置继续游戏。`
    ]);
    register(["BuildingFeature_Scarecrow_Mod_Equipment"], "装备展示", "Mod_Equipment", () => [
        "可以打开装备栏，把可装备物品放到稻草人身上保存和展示；这些装备属于稻草人本体，不会直接给玩家提供效果。"
    ]);
    register(["BuildingFeature_SparkMaker_Mod_FireDrill"], "钻木取火", "Mod_FireDrill", () => [
        "可以放入火绒并反复摩擦生火。摩擦进度会随时间衰减，连续操作把进度填满后会消耗火绒并产出火种。"
    ]);
    register(["BuildingFeature_Tent_Mod_Tent"], "睡眠", "Mod_Tent", () => [
        "可以在帐篷里睡觉。睡眠会快速推进世界时间，同时消耗饥饿和水分并恢复生命；营养或水分不足时更容易提前醒来。"
    ]);
    register(["Module_BodyPartTreatment"], "部位治疗", "Mod_BodyPartTreatment", body => {
        const parts = (get(body, "treatableParts") || []).map(bodyPartName).filter(Boolean);
        const duration = get(body, "channelDurationSeconds");
        const restored = get(body, "durabilityRestored");
        return [`拿在手上使用时可以选择受伤部位进行治疗${parts.length ? `，可处理${parts.join("、")}` : ""}。治疗完成后消耗一份物品${restored !== undefined ? `，立即恢复 ${text(restored)} 点部位耐久` : ""}${duration !== undefined ? `，完整处理约需 ${text(duration)} 秒` : ""}。`];
    });
    register(["Module_CanopyFruit"], "树冠结果", "Mod_CanopyFruit", (body, context) => {
        const settings = get(body, "Settings") || {};
        const fruit = named(context, get(body, "FruitItemId"));
        return [
            `成熟后会在树冠上周期性长出 ${fruit}，一次通常保留 ${text(settings.MinimumCount)}～${text(settings.MaximumCount)} 个果实；果实成熟后会在一段随机等待后自然掉落。`,
            `落下的果实可能砸伤经过的生物，命中后还有 ${percent(get(body, "SplitChance"))} 的概率裂成${named(context, get(body, "SplitItemId"))}；砍倒树时，已经成熟的果实也会一起结算掉落。`
        ];
    });
    register(["Module_Carrier"], "载具", "Mod_Carrier", body => [
        `可以交互乘坐并直接控制移动，最高速度约为 ${field(body, "MaxSpeed")} 格/秒。${get(body, "AllowsWater") === true ? "它可以在水面移动，" : ""}在陆地上的速度约为正常速度的 ${Math.round((get(body, "LandSpeedMultiplier") || 1) * 100)}%。再次交互会尝试在附近安全陆地靠岸下船。`
    ]);
    register(["Module_ConsumableBuff"], "食用效果", "Mod_ConsumableBuff", body => {
        const effects = (get(body, "buffIds") || []).map(buffName).filter(Boolean);
        return [`完整食用后会获得${effects.length ? effects.join("、") : "对应"}状态效果；效果的持续时间和叠加方式由该状态本身决定。`];
    });
    register(["Module_CropVisual"], "作物表现", "Mod_CropVisual", () => []);
    register(["Module_DamageOnHitBuff"], "命中状态", "DamageOnHitBuffApplier", body => [
        `成功命中生物时，有 ${percent(get(body, "applicationChance"))} 的概率给目标附加「${buffName(get(body, "buffId"))}」状态。`
    ]);
    register(["Module_FarmlandSupply"], "耕地补给", "Mod_FarmlandSupply", () => [
        "拿在手上对耕地使用时，会补充该地块的水分和肥力；只有地块确实得到补充时才会消耗一份。"
    ]);
    register(["Module_GroundCoverHarvest"], "地表植被采集", "Mod_GroundCoverHarvest", (body, context) => {
        const yieldId = get(body, "grassYieldItemId");
        const yieldAmount = get(body, "grassYieldAmount");
        return [`拿在手上可以采集附近的草层和地表小型植被，最远距离约为 ${field(body, "reach")} 格${yieldId ? `；割草时会掉落 ${named(context, yieldId)} ×${text(yieldAmount)}` : ""}。`];
    });
    register(["Module_ReadableBook"], "阅读", "Mod_ReadableBook", body => [
        `拿在手上使用时会打开阅读界面，可以翻阅其中的 ${(get(body, "pages") || []).length} 页内容。`
    ]);
    mechanics.rules.get("Module_Production").outputs = body => (parameters(body).ProductionList || []).map(output => ({ id: output.itemName, detail: `会周期性产出它，数量约为 ${range(output.itemCountMin, output.itemCountMax)}${output.SpawnProbability < 1 ? `，成功概率约为 ${percent(output.SpawnProbability)}` : ""}` }));
    mechanics.rules.get("Module_CropYield").outputs = body => (parameters(body).outputs || []).map(output => ({ id: output.itemId, detail: `成熟收获时会产出它，数量约为 ${range(output.minAmount, output.maxAmount)}${output.probability < 1 ? `，出现概率约为 ${percent(output.probability)}` : ""}` }));
    mechanics.rules.get("Module_Collectable").outputs = body => [{ id: parameters(body).CollectItemId, detail: "可以通过交互采集得到它" }];
    mechanics.rules.get("Module_Plantable").outputs = body => [{ id: parameters(body).cropItemId, detail: "可以通过播种生长成它" }];
    mechanics.rules.get("Module_Growth").outputs = body => parameters(body).allowCultivatedHarvest === true ? [
        { id: parameters(body).harvestFoodItemId, detail: `人工栽培成熟后可以收获它，数量约为 ${range(parameters(body).harvestFoodMin, parameters(body).harvestFoodMax)}` },
        { id: parameters(body).harvestSeedItemId, detail: "人工栽培成熟后可以收获它的种子" }
    ] : [];
    mechanics.rules.get("Module_Food").outputs = body => (body.data?.MechanicStates || []).filter(state => state.StateKey === "food.spoilage" && state.Data?.EnableSpoilage === true).map(state => ({ id: state.Data.SpoilageTargetItemID, detail: `腐败后会变成它，时间约为 ${text(state.Data.SpoilageIntervalSeconds)} 秒` }));
    mechanics.rules.get("BuildingFeature_CompostBin_Mod_CompostBin").outputs = () => [
        { id: "Fertilizer", detail: "可以把合适的有机材料堆肥后转化成它" }
    ];
    mechanics.rules.get("BuildingFeature_SparkMaker_Mod_FireDrill").outputs = () => [
        { id: "FireSeed", detail: "可以通过钻木取火制作出它" }
    ];
    mechanics.rules.get("Module_CanopyFruit").outputs = body => [
        { id: get(body, "FruitItemId"), detail: "会在树冠成熟后自然长出并掉落它" }
    ];
    mechanics.rules.get("Module_GroundCoverHarvest").outputs = body => {
        const id = get(body, "grassYieldItemId");
        return id ? [{ id, detail: "可以用对应采集工具割取地表草层得到它" }] : [];
    };
})();

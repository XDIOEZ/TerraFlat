(function () {
    "use strict";
    const mechanics = typeof module !== "undefined" && module.exports ? require("./mechanics.js") : globalThis.ItemMechanics;
    const text = mechanics.valueText;
    const parameters = body => body.parameters || {};
    const field = (body, path) => text(path.split(".").reduce((value, key) => value?.[key], parameters(body)));
    function register(prefabs, title, source, explain) {
        mechanics.register(prefabs, { title, source, explain });
    }

    register(["Mod_Damage", "Mod_Damage_AI"], "攻击与伤害", "docs/systems/combat.md；Mod_Damage", body => [
        `伤害盒生效期间负责命中结算，而不是播放攻击动画就必然造成伤害。四类基础伤害：${field(body, "DamageValues")}；同目标伤害间隔 ${field(body, "DamageInterval")} 秒。`,
        `是否仅手持时伤害：${field(body, "OnlyDealDamageWhenInHand")}；触碰进入时伤害：${field(body, "EnableOnTriggerEnterDamage")}。间隔为负时只做进入伤害，0 表示可逐帧，正值控制持续接触冷却；实际仍受攻击窗口限制。最终伤害由目标防御、攻击者状态等共同结算，不能把基础值直接当作扣血量。`
    ]);
    register(["Module_Weapon_AnimationAction", "Mod_Weapon_AnimationAction"], "近战/工具使用", "docs/systems/combat.md；Mod_Weapon_AnimationAction.StartAttack", body => [
        `通过攻击动作打开已有伤害盒；每段攻击基础体力消耗 ${field(body, "staminaCostPerAttack")}，攻速倍率 ${field(body, "attackSpeedMultiplier")}。体力成本还受难度影响。`,
        `持续按住时循环连击：${field(body, "loopComboOnHold")}。动画控制与伤害模块是两个职责；是否能伤害目标还要满足伤害盒和目标的受击条件。`
    ]);
    register(["Mod_Bow"], "弓的使用", "docs/systems/combat.md；Mod_Bow", body => [
        `以标签 ${field(body, "AmmoTag")} 选择弹药；蓄力到满需 ${field(body, "FullChargeSeconds")} 秒，拉弓每秒体力消耗 ${field(body, "StaminaConsumePerSecond")}。`,
        `发射伤害倍率 ${field(body, "ProjectileDamageMultiplier")}，最大瞄准距离 ${field(body, "MaxAimDistance")}；弹药自己的投射物与伤害配置继续参与结算。`
    ]);
    register(["Mod_Projectile"], "箭矢飞行与回收", "docs/systems/combat.md；Mod_Projectile", body => [
        `投射物基础速度范围 ${field(body, "MinSpeed")}～${field(body, "MaxSpeed")}；伤害倍率范围 ${field(body, "MinDamageMultiplier")}～${field(body, "MaxDamageMultiplier")}；最长飞行 ${field(body, "MaxFlightSeconds")} 秒。`,
        `命中后的回收概率 ${field(body, "RecoveryChance")}；破损残料概率 ${field(body, "BrokenSalvageChance")}（0～1）。回收与造成伤害不是同一个判定，不保证每次取回。`
    ]);
    register(["Module_DamageReciver"], "受伤、生命与死亡", "docs/systems/combat.md；DamageReceiver", body => [
        `基础生命 ${field(body, "Data.Hp")} / ${field(body, "Data.MaxHp")}；四类防御 ${field(body, "Data.DefenseValues")}。生命、受伤与死亡统一由接收系统处理，死亡战利品来自顶层 lootTableId。`
    ]);
    register(["Module_ResourceHarvest"], "资源采集门槛", "flatworld-item-module Skill；Mod_ResourceHarvest", body => [
        `采集此资源需要工具类型 ${field(body, "requiredTool")}，最低等级 ${field(body, "minimumTier")}。满足对应工具门槛后才可按资源采集规则处理，并非任何武器都能取得产物；破坏产物见死亡战利品。`
    ]);
    register(["Module_Food"], "营养与食物机制", "Mod_Food；FoodSpoilageObserver.cs；docs/systems/survival.md", (body, context, entry) => {
        const data = body.data || {};
        const food = data.FoodData || {};
        const nutrition = food.nutrition || {};
        if (entry.kind === "actor") return [`此模块维护生物自身营养，不表示生物可直接食用。基础营养 ${text(food.nutrition)}；健康恢复配置 ${field(body, "HealthState")}；体力配置 ${field(body, "StaminaState")}。动物被动回血依据营养/健康状态处理，实际肉类等产物以战利品为准。`];
        const lines = [`食用将食物营养转移给食用者；碳水 ${text(nutrition.Carbohydrates)}、脂肪 ${text(nutrition.Fat)}、蛋白质 ${text(nutrition.Protein)}、水分 ${text(nutrition.Water)}、维生素 ${text(nutrition.Vitamins)}。这是配置中的营养量，不是瞬间恢复同等生命。`, `完成食用所需进度 ${text(food.Max_EatingProgress)}；实际消费与营养/体力/健康状态由食物机制共同处理。`];
        const spoilage = (data.MechanicStates || []).find(state => state.StateKey === "food.spoilage")?.Data;
        if (!spoilage) lines.push("未声明腐败观察者参数，是否腐败和时间由模块默认/运行态决定。");
        else if (spoilage.EnableSpoilage === false) lines.push("本定义明确关闭食物腐败计时。");
        else lines.push(`腐败启用 ${text(spoilage.EnableSpoilage)}；库存 Tick 累计 ${text(spoilage.SpoilageIntervalSeconds)} 秒后请求原槽位替换为 ${text(spoilage.SpoilageTargetItemID)}；已有进度 ${text(spoilage.SpoilageElapsedSeconds)} 秒。不是经过现实时间就必然完成替换。`);
        return lines;
    });
    register(["Module_HeldFood"], "手持食物操作", "Mod_HeldFood；docs/systems/survival.md", () => ["手持使用入口调用食物消费机制；能否使用、消费进度及完成后的效果由食物模块和消费观察者决定，不能把手持入口当作额外一份营养。"]);
    register(["Module_Equipment_Store"], "装备后效果", "docs/systems/equipment.md；EquipmentInstance_Speed / Defense / Bag", body => {
        const instances = parameters(body).equipmentInstances;
        if (!Array.isArray(instances)) return ["装备效果实例未在此定义声明，由模块默认/运行态决定；不是放在普通背包里就会自动生效。"];
        return ["放入角色允许的装备槽后，由装备实例应用效果；卸下时撤销。实例 Name 仅是标注，不作为数值依据。", ...instances.map(instance => {
            const type = instance.$concreteType;
            if (type === "EquipmentInstance_Speed") return `移动速度的加法修饰项增加 ${text(instance.SpeedIncrease)}；这不是直接将最终速度乘以该数值。`;
            if (type === "EquipmentInstance_Defense") return `四类防御配置 ${text(instance.DefenseBonus)}；单值防御配置 ${text(instance.DefenseBonusIncrease)}。运行时优先使用有效四类防御；否则正单值按源码转换到四类防御。`;
            if (type === "EquipmentInstance_Bag") return `收纳袋向角色行囊挂接扩展槽，声明槽数 ${text(instance.BagData?.itemSlots?.length)}；物品仍遵守库存约束，不是无限容量。`;
            return `装备效果 ${text(type)} 尚未登记详细解释，原始实例：${JSON.stringify(instance)}。`;
        })];
    });
    register(["Module_Plantable"], "播种方式", "docs/systems/agriculture.md；Mod_Plantable", body => [
        `通过播种预览在符合种植条件的位置种下 ${field(body, "cropItemId")}；最大种植距离 ${field(body, "maxPlantingDistance")}。种子不等于成熟作物，后续生长由目标作物及地块水肥/环境处理。`
    ]);
    register(["Module_Hoe"], "锄地方式", "docs/systems/agriculture.md；Mod_Hoe", body => [
        `对可耕作地块累计锄地进度，每格所需使用次数 ${field(body, "usesPerTile")}，最大距离 ${field(body, "maxTillingDistance")}，使用间隔 ${field(body, "useInterval")} 秒。进度归地格而非锄头实例；完成耕地后再使用种子播种。`
    ]);
    register(["Module_Crop"], "作物成长与收获", "docs/systems/agriculture.md；Mod_Crop", body => [
        `作物由生长状态模块推进，基础生长时长 ${field(body, "growthDurationSeconds")} 秒。成熟交互依次执行收获动作，一次性作物收获后结束植株生命周期；产物由独立产出模块定义。`,
        `每秒基础耗水 ${field(body, "waterConsumePerSecond")}、耗肥 ${field(body, "fertilityConsumePerSecond")}；缺水/缺肥最低生长倍率 ${field(body, "minimumWaterGrowthMultiplier")} / ${field(body, "minimumFertilityGrowthMultiplier")}；雨中生长加成 ${field(body, "rainGrowthBonus")}。实际成熟时间受环境和难度影响。`
    ]);
    register(["Module_CropYield"], "成熟采收产物", "Mod_CropYield.cs；docs/systems/agriculture.md", body => [
        "本模块在成熟收获动作中逐项判定并生成世界产物；不是每次攻击都触发收获。",
        ...(parameters(body).outputs || []).map(output => `${text(output.itemId)}：数量 ${text(output.minAmount)}～${text(output.maxAmount)}，基础概率 ${text(output.probability)}（0～1）。`)
    ]);
    register(["Module_Growth"], "分阶段生长", "Mod_Grow；docs/systems/agriculture.md", body => [
        `以成长进度推进阶段，基础速度 ${field(body, "Data.GrowSpeed")}，最大进度 ${field(body, "Data.MaxGrowProgress")}，阶段阈值 ${field(body, "Data.growState_Value")}；阶段会改变植株外观及对应生命配置。`,
        `允许栽培收获 ${field(body, "allowCultivatedHarvest")}；栽培食物产物 ${field(body, "harvestFoodItemId")}，数量 ${field(body, "harvestFoodMin")}～${field(body, "harvestFoodMax")}；种子产物 ${field(body, "harvestSeedItemId")}。需满足成熟及栽培收获条件，不代表自然植株每次交互都有这些产物。`
    ]);
    register(["Module_PlantClimate"], "植物气候限制", "Mod_PlantClimate；docs/systems/agriculture.md", body => [
        `可生长温度 ${field(body, "minimumGrowthTemperature")}～${field(body, "maximumGrowthTemperature")} ℃；生存温度 ${field(body, "minimumSurvivalTemperature")}～${field(body, "maximumSurvivalTemperature")} ℃。`,
        `致死暴露时长 ${field(body, "fatalExposureHours")} 小时；超出生长区间不等于瞬间死亡，生存限制与成长限制分开计算。`
    ]);
    register(["Module_Production"], "周期生产", "Entities/Item/Modules/World/Mod_ItemMaker.cs", body => [
        `按生产进度触发产出；基础生产速度倍率 ${field(body, "ProductionSpeed")}。随机初始进度和环境/运行态会影响首次产出时间，不能把周期直接当作精确等待时间。`,
        ...(parameters(body).ProductionList || []).map(production => `产物 ${text(production.itemName)}，数量 ${text(production.itemCountMin)}～${text(production.itemCountMax)}；进度阈值 ${text(production.MaxProductionTime)}，概率 ${text(production.SpawnProbability)}（0～1）；生产次数上限 ${text(production.MaxProductionCount)}（-1 表示无限）；写入库存接收模块 ${text(production.StoreInModule)}；抛出 ${text(production.ThrowItem)}；达到上限销毁自身 ${text(production.DestroySelf)}。`)
    ]);
    register(["Module_Collectable"], "交互采集库存", "Items/Food/Mod_Collectable.cs；docs/systems/agriculture.md", body => [
        `交互从现有资源库存取出 ${field(body, "CollectItemId")}，每次取 1 份；库存上限 ${field(body, "MaxStock")}，自然初始库存 ${field(body, "NaturalInitialStockMin")}～${field(body, "NaturalInitialStockMax")}。`,
        "采集保留植株，但库存不凭空补充；周期补充需要生产模块配合。没有库存时不产出。"
    ]);
    register(["Module_TemperatureYield"], "温度对产量的影响", "ResourceYieldUtility；Mod_TemperatureYield", body => [
        `修饰产物 ${field(body, "OutputItemId")} 的资源数量；冷端 ${field(body, "ColdTemperatureCelsius")} ℃ 对应倍率 ${field(body, "ColdMultiplier")}，暖端 ${field(body, "WarmTemperatureCelsius")} ℃ 对应倍率 ${field(body, "WarmMultiplier")}。基础掉落数量不是最终保证产量。`
    ]);
    register(["Module_WaterVessel"], "液体容器", "Items/Food/Mod_WaterVessel.cs", body => [
        `容器身份与液体分开保存，可盛装/转移液体；容量 ${field(body, "capacity")} 份。液体 ID ${field(body, "Data.LiquidId")}，初始量 ${field(body, "Data.Amount")}。`,
        "空容器不等于饮用水；能否饮用与液体效果来自液体定义。倾倒会消耗实际液量，扶正不会恢复已倒出的内容；加热处理需要相应的加热机制。"
    ]);
    register(["Module_VesselHeating"], "容器加热", "Mod_VesselHeating；Mod_WaterVessel", () => ["此模块负责容器内液体的加热处理；结果取决于实际液体、温度和加工进度，不能仅凭容器标签断言装入的水已可安全饮用。未声明的处理阈值由模块和液体定义决定。"]);
    register(["Module_Fuel"], "燃料使用", "Mod_Fuel；docs/systems/building.md", body => [
        `燃料可供燃烧系统消耗，配置燃料状态 ${field(body, "Data.Fuel")}，最高温度 ${field(body, "Data.MaxTemperature")} ℃，燃烧速度倍率 ${field(body, "burnSpeedMultiplier")}。最高温度不是放入后立即达到的环境温度。`,
        `手持时点燃 ${field(body, "igniteWhileHeld")}；实际点火/维持燃烧仍由燃料与热源系统处理。`
    ]);
    register(["BuildingFeature_Chest_Wood_Mod_Inventory"], "容器收纳", "docs/systems/inventory.md；Mod_Inventory", body => [
        `通过容器交互管理物品库存；配置槽数 ${text(body.data?.Data?.itemSlots?.length)}，库存配置 ${text(body.data?.Data)}。存入、取出、合并和交换经过库存事务校验；空槽也不代表能绕过物品堆叠或容量限制。`
    ]);
    register(["Module_Building"], "建筑放置与实体", "docs/systems/building.md；Mod_Building", body => {
        const serialized = body.data?.BitData;
        let data;
        if (typeof serialized === "string" && serialized.trim().startsWith("{")) data = JSON.parse(serialized);
        return [`建筑由召唤物、放置预览和世界实体分工，预览通过占地/放置条件后才能安装；不能把可携带召唤物当作已经运行的建筑。角色配置 ${text(data?.Role)}；世界建筑 ID ${text(data?.BuildingPrefabId)}；召唤物 ID ${text(data?.SummonerPrefabId)}；共享状态模块 ${text(data?.SharedModuleIds)}。`];
    });
    register(["Module_Mortar", "BuildingFeature_WorkBench_Mod_MakeTable"], "工作站加工", "docs/systems/crafting.md；CraftingStationController", body => [
        `把材料放入工作站，按可匹配候选选择配方；当前显式站点 ID ${field(body, "StationId")}。需满足配方类型、工作站和输入规则，所有产物可放下后才原子扣料与产出。具体材料和配方见各物品的双向配方关系。`
    ]);
    register(["BuildingFeature_BlastFurnace_Mod_Furnace", "BuildingFeature_Bonfire_Mod_Furnace", "BuildingFeature_Smelter_Mod_Furnace"], "热加工", "docs/systems/crafting.md；Mod_Furnace", () => ["投入匹配的材料并满足配方温度区间后进行热加工；有序/网格配方还保留槽位和镜像限制。燃料最高温度不等于当前炉温，产出取决于实际配方和运行态。参数未声明时不推测炉子容量或加工速度。"]);
    mechanics.rules.get("Module_Production").outputs = body => (parameters(body).ProductionList || []).map(output => ({ id: output.itemName, detail: `周期生产，数量 ${text(output.itemCountMin)}～${text(output.itemCountMax)}，基础概率 ${text(output.SpawnProbability)}，进度阈值 ${text(output.MaxProductionTime)}；需满足生产次数、环境和库存接收条件。` }));
    mechanics.rules.get("Module_CropYield").outputs = body => (parameters(body).outputs || []).map(output => ({ id: output.itemId, detail: `成熟采收，数量 ${text(output.minAmount)}～${text(output.maxAmount)}，基础概率 ${text(output.probability)}。` }));
    mechanics.rules.get("Module_Collectable").outputs = body => [{ id: parameters(body).CollectItemId, detail: "交互从现有采集库存取 1 份，空库存不产出；补充由生产模块决定。" }];
    mechanics.rules.get("Module_Plantable").outputs = body => [{ id: parameters(body).cropItemId, detail: "通过播种生成植株，需满足地块及种植条件；不直接得到成熟产物。" }];
    mechanics.rules.get("Module_Growth").outputs = body => parameters(body).allowCultivatedHarvest === true ? [
        { id: parameters(body).harvestFoodItemId, detail: `栽培成熟收获，数量 ${field(body, "harvestFoodMin")}～${field(body, "harvestFoodMax")}。` },
        { id: parameters(body).harvestSeedItemId, detail: "栽培成熟收获种子，数量由对应运行时规则决定。" }
    ] : [];
    mechanics.rules.get("Module_Food").outputs = body => (body.data?.MechanicStates || []).filter(state => state.StateKey === "food.spoilage" && state.Data?.EnableSpoilage === true).map(state => ({ id: state.Data.SpoilageTargetItemID, detail: `库存食物腐败转化，基础间隔 ${text(state.Data.SpoilageIntervalSeconds)} 秒，需库存 Tick 与状态替换成功。` }));
})();

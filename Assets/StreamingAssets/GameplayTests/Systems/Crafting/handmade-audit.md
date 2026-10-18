# 手工制品配方审计（2026-10-18）

范围：启用的 `item-manifest.json` 全部 398 个定义，排除 21 个抽象基类，逐项核对 377 个实际定义。

核对来源：启用的普通/烹饪配方、物品 `processing`、`reactions` 与 `matter.transitions`、`mechanical-catalog.json` 专用加工、液体凝固/蒸发，以及建筑召唤器和落地本体配对。新配方见 `handmade_equipment.json` 和 `handmade_utilities.json`。

本次只补应由人工装配/加工的缺失制品；木炭、金属锭、玻璃、皮革、熟食等已有物态或温度路线保持原入口。现有织布、钻孔、手工锻打/机械锤加工不另加普通配方。自然材料、预设读物、特殊状态、GM/创造专用品和世界本体不补。

石坩埚按凿制容器处理：石板 1 + 打制石器 0（不消耗）在工作台制作；不把黏土直接当烧结容器。钉子需熟铁锭 1 + 石锤 0，产出 8。纸张是纤维 3 + 草木灰 1，产出 4。高炉和原熔炉分别保留独立配方，原 `core:高炉` 的显示名修正为“熔炉”。

这里只记录静态内容审计和设计归类，不代表实机制作验收。

| 类别 | 数量 |
| --- | ---: |
| 世界实体 | 106 |
| 自然与温度 | 77 |
| 已有配方 | 143 |
| 本次新增 | 20 |
| 内部或特殊 | 7 |
| 已有加工 | 24 |

## 逐包数量

| 物品包 | 实际定义 | 本次新增 | 已有配方/加工 | 其他来源/排除 |
| --- | ---: | ---: | ---: | ---: |
| `space_wrecks` | 1 | 0 | 0 | 1 |
| `snowball` | 1 | 0 | 0 | 1 |
| `vehicles` | 2 | 0 | 1 | 1 |
| `portable_buildings` | 2 | 0 | 0 | 2 |
| `mortar` | 1 | 0 | 1 | 0 |
| `crucible` | 3 | 1 | 0 | 2 |
| `weapons` | 17 | 5 | 11 | 1 |
| `basic_items` | 74 | 2 | 25 | 47 |
| `bird_egg` | 1 | 0 | 0 | 1 |
| `textiles` | 3 | 0 | 2 | 1 |
| `gold` | 4 | 0 | 1 | 3 |
| `diamond` | 2 | 0 | 0 | 2 |
| `obsidian` | 4 | 0 | 2 | 2 |
| `iron_bucket` | 2 | 0 | 1 | 1 |
| `sulfur` | 2 | 0 | 0 | 2 |
| `resource_nodes` | 13 | 0 | 0 | 13 |
| `equipment` | 3 | 0 | 3 | 0 |
| `tools` | 19 | 5 | 12 | 2 |
| `mechanical` | 28 | 0 | 14 | 14 |
| `electrical` | 12 | 2 | 2 | 8 |
| `hand_drill` | 2 | 0 | 1 | 1 |
| `seeds` | 5 | 0 | 2 | 3 |
| `crops` | 6 | 0 | 0 | 6 |
| `chili` | 2 | 0 | 0 | 2 |
| `cactus` | 2 | 0 | 0 | 2 |
| `building_summoners` | 17 | 5 | 12 | 0 |
| `building_bodies` | 14 | 0 | 0 | 14 |
| `grass_bed` | 2 | 0 | 1 | 1 |
| `dimension_portals` | 2 | 0 | 0 | 2 |
| `hoes` | 4 | 0 | 4 | 0 |
| `ancient_stage` | 36 | 0 | 20 | 16 |
| `command_manual` | 1 | 0 | 0 | 1 |
| `floors` | 5 | 0 | 5 | 0 |
| `medical` | 2 | 0 | 2 | 0 |
| `livestock_items` | 1 | 0 | 0 | 1 |
| `wooden_barrel` | 2 | 0 | 1 | 1 |
| `fishing` | 4 | 0 | 4 | 0 |
| `conveyor` | 4 | 0 | 2 | 2 |
| `chemical_machines` | 35 | 0 | 17 | 18 |
| `chemistry_equipment` | 7 | 0 | 7 | 0 |
| `water_trough` | 2 | 0 | 1 | 1 |
| `space_ships` | 26 | 0 | 12 | 14 |
| `space_devices` | 2 | 0 | 1 | 1 |

## 全量分类清单

下面按“物品包 + 来源类别”列出全部 377 个 ID，每个 ID 只出现一次。建筑本体通过对应载体放置；资源实体由世界生成，不添加本体制作配方。

| 物品包 | 类别 | 数量 | 定义 ID | 来源依据 |
| --- | --- | ---: | --- | --- |
| `space_wrecks` | 世界实体 | 1 | `ShipWreck` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `snowball` | 自然与温度 | 1 | `Snowball` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `vehicles` | 已有配方 | 1 | `Boat` | recipe-manifest.json 中已启用的配方产物 |
| `vehicles` | 世界实体 | 1 | `Boat_Building` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `portable_buildings` | 世界实体 | 2 | `ClayJar_Building`, `StoneMortar_Building` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `mortar` | 已有配方 | 1 | `StoneMortar` | recipe-manifest.json 中已启用的配方产物 |
| `crucible` | 自然与温度 | 2 | `Sand`, `Glass` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `crucible` | 本次新增 | 1 | `Crucible` | 两份 handmade 配方包 |
| `weapons` | 已有配方 | 11 | `Dagger_Stone`, `Dagger_Bone`, `ChippedTool`, `Bow_Wood`, `Bow_Recurve`, `Blowgun_Wood`, `StoneThrowingRope`, `Arrow_Wood`, `Arrow_Stone`, `Arrow_Copper`, `Arrow_Iron` | recipe-manifest.json 中已启用的配方产物 |
| `weapons` | 本次新增 | 5 | `Dagger_Copper`, `Knife_Flint`, `Spear_Stone`, `Spear_Copper`, `Spear_Iron` | 两份 handmade 配方包 |
| `weapons` | 内部或特殊 | 1 | `Spear_Stone_Animation` | 石矛内部动作变体 |
| `basic_items` | 自然与温度 | 45 | `Bone`, `CharredMatter`, `Earth`, `Leaf`, `Leather`, `RawHide`, `Twine`, `Fiber`, `Ingot_Bronze`, `Ingot_Copper`, `Ingot_RawIron`, `Ingot_Steel`, `Ingot_Tin`, `Ore_Coal`, `Ore_Copper`, `Ore_Flint`, `Ore_Iron`, `Ore_Tin`, `Apple`, `Peach`, `Orange`, `RiceGrain`, `CookedRice`, `Berry`, `Radish`, `Mushroom`, `Coconut_Green`, `Egg`, `Egg_Cooked`, `Fat`, `Meat`, `Meat_Cooked`, `Meat_Dehydrate`, `Meat_Rotten`, `Tea`, `Humus`, `IceBlock`, `Ore_Stone`, `StoneSlab`, `Resin`, `Charcoal`, `Peat`, `Cotton`, `WoodAsh`, `Beeswax` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `basic_items` | 已有配方 | 8 | `Plank`, `Rope`, `Coconut_Half`, `Coconut_Nude`, `Coconut_Shell`, `CoconutMeat`, `Glue`, `DrilledLog` | recipe-manifest.json 中已启用的配方产物 |
| `basic_items` | 已有加工 | 17 | `Ingot_WroughtIron`, `Rice`, `DrilledPlank`, `DrilledStoneSlab`, `DrilledStone`, `IronPlate`, `RawIronPlate`, `CopperPlate`, `BronzePlate`, `TinPlate`, `SteelPlate`, `OrePowder_Coal`, `OrePowder_Copper`, `OrePowder_Iron`, `OrePowder_Tin`, `OrePowder_Stone`, `OrePowder_Niter` | 物品 processing 或 mechanical-catalog.json 专用加工 |
| `basic_items` | 内部或特殊 | 2 | `WildDogMilk`, `WildDogMilk_Gray` | 特殊无限使用物，不补人工配方；狗奶休眠状态，不单独制作 |
| `basic_items` | 本次新增 | 2 | `Paper`, `Nail` | 两份 handmade 配方包 |
| `bird_egg` | 自然与温度 | 1 | `BirdEgg` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `textiles` | 已有加工 | 2 | `Cloth`, `Silk` | 物品 processing 或 mechanical-catalog.json 专用加工 |
| `textiles` | 自然与温度 | 1 | `SilkFiber` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `gold` | 自然与温度 | 2 | `Ingot_Gold`, `Ore_Gold` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `gold` | 已有加工 | 1 | `OrePowder_Gold` | 物品 processing 或 mechanical-catalog.json 专用加工 |
| `gold` | 世界实体 | 1 | `Mine_Gold` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `diamond` | 自然与温度 | 1 | `Ore_Diamond` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `diamond` | 世界实体 | 1 | `Mine_Diamond` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `obsidian` | 自然与温度 | 1 | `Obsidian` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `obsidian` | 世界实体 | 1 | `Mine_Obsidian` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `obsidian` | 已有配方 | 2 | `Dagger_Obsidian`, `Spear_Obsidian` | recipe-manifest.json 中已启用的配方产物 |
| `iron_bucket` | 已有配方 | 1 | `IronBucket` | recipe-manifest.json 中已启用的配方产物 |
| `iron_bucket` | 世界实体 | 1 | `IronBucket_Building` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `sulfur` | 自然与温度 | 1 | `Ore_Sulfur` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `sulfur` | 世界实体 | 1 | `Mine_Sulfur` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `resource_nodes` | 世界实体 | 12 | `Beehive`, `AppleTree`, `PeachTree`, `OrangeTree`, `Tree_Coconut`, `Mine_Coal`, `Mine_Copper`, `Mine_Iron`, `Mine_Stone`, `Mine_Tin`, `Iceberg`, `Tree_Pine` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `resource_nodes` | 自然与温度 | 1 | `Flower` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `equipment` | 已有配方 | 3 | `Chestplate_Iron`, `Chestplate_Twine`, `Chestplate_Wood` | recipe-manifest.json 中已启用的配方产物 |
| `tools` | 本次新增 | 5 | `Scissors`, `Axe_Copper`, `Axe_Bronze`, `Axe_RawIron`, `Axe_Iron` | 两份 handmade 配方包 |
| `tools` | 已有配方 | 11 | `Stick_Wood`, `Axe_Stone`, `Axe_Flint`, `Pickaxe_Stone`, `Pickaxe_Copper`, `Pickaxe_Bronze`, `Pickaxe_RawIron`, `Pickaxe_Iron`, `FlintStrike`, `WoodHammer`, `StoneHammer` | recipe-manifest.json 中已启用的配方产物 |
| `tools` | 已有加工 | 1 | `DrilledStick` | 物品 processing 或 mechanical-catalog.json 专用加工 |
| `tools` | 自然与温度 | 2 | `Log`, `FireSeed` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `mechanical` | 世界实体 | 14 | `HandCrank`, `WaterWheel`, `Windmill`, `Shaft_Wood`, `Gear_Wood`, `Gearbox_Wood`, `Clutch`, `CrossShaft`, `Millstone`, `MechanicalBellows`, `Sawmill`, `MechanicalLoom`, `MechanicalHammer`, `CopperAnvil` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `mechanical` | 已有配方 | 14 | `HandCrank_Summoner`, `WaterWheel_Summoner`, `Windmill_Summoner`, `Shaft_Wood_Summoner`, `Gear_Wood_Summoner`, `Gearbox_Wood_Summoner`, `Clutch_Summoner`, `CrossShaft_Summoner`, `Millstone_Summoner`, `MechanicalBellows_Summoner`, `Sawmill_Summoner`, `MechanicalLoom_Summoner`, `MechanicalHammer_Summoner`, `CopperAnvil_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `electrical` | 自然与温度 | 1 | `Rubber` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `electrical` | 世界实体 | 6 | `Generator_Basic`, `Wire_Copper`, `Battery_Basic`, `Creative_Power_Source`, `ElectricLightBulb`, `Tree_Oak` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `electrical` | 本次新增 | 2 | `Generator_Basic_Summoner`, `Battery_Basic_Summoner` | 两份 handmade 配方包 |
| `electrical` | 已有配方 | 2 | `Wire_Copper_Summoner`, `ElectricLightBulb_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `electrical` | 内部或特殊 | 1 | `Creative_Power_Source_Summoner` | 创造模式专用电源 |
| `hand_drill` | 世界实体 | 1 | `HandDrill` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `hand_drill` | 已有配方 | 1 | `HandDrill_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `seeds` | 自然与温度 | 3 | `Seed_Apple`, `Seed_Mushroom`, `Seed_Cotton` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `seeds` | 已有配方 | 2 | `Seed_Radish`, `Seed_Berry` | recipe-manifest.json 中已启用的配方产物 |
| `crops` | 世界实体 | 6 | `RadishCrop`, `MushroomCrop`, `TwineCrop`, `BerryCrop`, `RiceCrop`, `CottonCrop` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `chili` | 自然与温度 | 1 | `Chili` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `chili` | 世界实体 | 1 | `ChiliTree` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `cactus` | 自然与温度 | 1 | `CactusPiece` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `cactus` | 世界实体 | 1 | `Cactus` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `building_summoners` | 本次新增 | 5 | `BlastFurnace_Summoner`, `Door_Wood_Summoner`, `Meatrack_Summoner`, `Scarecrow_Summoner`, `CompostBin_Summoner` | 两份 handmade 配方包 |
| `building_summoners` | 已有配方 | 12 | `Bonfire_Summoner`, `Chest_Wood_Summoner`, `Door_Stone_Summoner`, `MineEntrance_Summoner`, `Smelter_Summoner`, `Tent_Summoner`, `Wall_Stone_Summoner`, `Wall_Wood_Summoner`, `WorkBench_Summoner`, `SparkMaker_Summoner`, `Torch_Summoner`, `WaterPlatform_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `building_bodies` | 世界实体 | 14 | `BlastFurnace`, `Bonfire`, `Chest_Wood`, `Door_Stone`, `Door_Wood`, `Mod_Meatrack`, `MineEntrance`, `Scarecrow`, `Smelter`, `Tent`, `WorkBench`, `SparkMaker`, `Torch_Building`, `CompostBin` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `grass_bed` | 已有配方 | 1 | `GrassBed_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `grass_bed` | 世界实体 | 1 | `GrassBed` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `dimension_portals` | 世界实体 | 2 | `NaturalMineEntrance`, `CaveExit` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `hoes` | 已有配方 | 4 | `Hoe_Wood`, `Hoe_Stone`, `Hoe_Copper`, `Hoe_Iron` | recipe-manifest.json 中已启用的配方产物 |
| `ancient_stage` | 自然与温度 | 11 | `Herb`, `Straw`, `FoxtailSpike`, `WillowBranch`, `WillowBark`, `Clay`, `Ore_Niter`, `Seed_Herb`, `Seed_Willow`, `ClayJar`, `Salt` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `ancient_stage` | 世界实体 | 4 | `HerbCrop`, `FoxtailCrop`, `Mine_Niter`, `Tree_Willow` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `ancient_stage` | 已有加工 | 3 | `Seed_Foxtail`, `Flour`, `Jam` | 物品 processing 或 mechanical-catalog.json 专用加工 |
| `ancient_stage` | 已有配方 | 17 | `HerbPoultice`, `Shovel_Wood`, `Shovel_Stone`, `Shovel_Copper`, `Shovel_RawIron`, `Shovel_Bronze`, `Shovel_Iron`, `Platform_Stone_Summoner`, `Platform_Copper_Summoner`, `Wall_Copper_Summoner`, `Platform_Iron_Summoner`, `Wall_Iron_Summoner`, `Platform_Bronze_Summoner`, `Wall_Bronze_Summoner`, `ClayJarBlank`, `Meat_Cured`, `Fertilizer` | recipe-manifest.json 中已启用的配方产物 |
| `ancient_stage` | 内部或特殊 | 1 | `MinersNote` | 带预设内容的矿工读物 |
| `command_manual` | 内部或特殊 | 1 | `CommandManual` | 聊天 /help 发放的命令手册 |
| `floors` | 已有配方 | 5 | `Floor_Wood_Summoner`, `Floor_Stone_Summoner`, `Floor_Copper_Summoner`, `Floor_Iron_Summoner`, `Floor_Bronze_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `medical` | 已有配方 | 2 | `Splint`, `Bandage_Plant` | recipe-manifest.json 中已启用的配方产物 |
| `livestock_items` | 自然与温度 | 1 | `Wool` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `wooden_barrel` | 已有配方 | 1 | `WoodenBarrel` | recipe-manifest.json 中已启用的配方产物 |
| `wooden_barrel` | 世界实体 | 1 | `WoodenBarrel_Building` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `fishing` | 已有配方 | 4 | `FishingRod`, `FishingHook_Bone`, `FishingHook_Iron`, `FishingHook_Gold` | recipe-manifest.json 中已启用的配方产物 |
| `conveyor` | 世界实体 | 2 | `Conveyor_Wood`, `Conveyor_Cloth` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `conveyor` | 已有配方 | 2 | `Conveyor_Wood_Summoner`, `Conveyor_Cloth_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `chemical_machines` | 世界实体 | 17 | `FluidPipe`, `FluidValve`, `FluidOutlet`, `GasSelector`, `GasTankBlock_Iron`, `GasTankBlock_Steel`, `LiquidStorageTank`, `GasPump_Mechanical`, `LiquidPump_Electric`, `PressureProbe_Mechanical`, `PressureProbe_Electronic`, `GasCompressor_Mechanical`, `GasCompressor_Electric`, `ElectricHeater`, `FluidFilter`, `WaterElectrolyzer`, `GasEngine` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `chemical_machines` | 已有配方 | 17 | `FluidPipe_Summoner`, `FluidValve_Summoner`, `FluidOutlet_Summoner`, `GasSelector_Summoner`, `GasTankBlock_Iron_Summoner`, `GasTankBlock_Steel_Summoner`, `LiquidStorageTank_Summoner`, `GasPump_Mechanical_Summoner`, `LiquidPump_Electric_Summoner`, `PressureProbe_Mechanical_Summoner`, `PressureProbe_Electronic_Summoner`, `GasCompressor_Mechanical_Summoner`, `GasCompressor_Electric_Summoner`, `ElectricHeater_Summoner`, `FluidFilter_Summoner`, `WaterElectrolyzer_Summoner`, `GasEngine_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `chemical_machines` | 自然与温度 | 1 | `FilterResidue` | 采集/掉落/收获或热湿度、腐败及液体相变路线 |
| `chemistry_equipment` | 已有配方 | 7 | `PortableGasTank_Iron`, `PortableGasTank_Steel`, `Spacesuit_Head`, `Spacesuit_Torso`, `Spacesuit_Hands`, `Spacesuit_Legs`, `Spacesuit_Feet` | recipe-manifest.json 中已启用的配方产物 |
| `water_trough` | 已有配方 | 1 | `WaterTrough` | recipe-manifest.json 中已启用的配方产物 |
| `water_trough` | 世界实体 | 1 | `WaterTrough_Building` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `space_ships` | 世界实体 | 13 | `ShipFloor_Iron`, `ShipFloor_Refractory`, `ShipWall_Iron`, `ShipWall_Refractory`, `ShipDoor`, `ShipDockingPort`, `ShipConsole`, `ShipChemicalEngine`, `ShipChemicalEngine_GM`, `ShipGravityGenerator`, `ShipNavigation`, `ShipAirSupply`, `ShipFiber` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `space_ships` | 已有配方 | 12 | `ShipFloor_Iron_Summoner`, `ShipFloor_Refractory_Summoner`, `ShipWall_Iron_Summoner`, `ShipWall_Refractory_Summoner`, `ShipDoor_Summoner`, `ShipDockingPort_Summoner`, `ShipConsole_Summoner`, `ShipChemicalEngine_Summoner`, `ShipGravityGenerator_Summoner`, `ShipNavigation_Summoner`, `ShipAirSupply_Summoner`, `ShipFiber_Summoner` | recipe-manifest.json 中已启用的配方产物 |
| `space_ships` | 内部或特殊 | 1 | `ShipChemicalEngine_GM_Summoner` | GM 飞船测试专用引擎 |
| `space_devices` | 世界实体 | 1 | `SpaceCultivator` | 建筑载体放置、资源/作物/传送入口生成；不是直接制作对象 |
| `space_devices` | 已有配方 | 1 | `SpaceCultivator_Summoner` | recipe-manifest.json 中已启用的配方产物 |

特殊来源补充：`RawHide.matter.transitions` 干燥产生皮革；橡胶树产量模块提供橡胶；`Cotton`/`SilkFiber` 的 `weave` 提供布/丝绸；五类钻孔材料和金属板沿用手钻、手工锻打或机械锤；`liquids.json` 提供玻璃、铜/锡/生铁凝固和海水制盐；`ClayJarBlank.reactions` 烧成陶罐；火种沿用取火/点燃流程。

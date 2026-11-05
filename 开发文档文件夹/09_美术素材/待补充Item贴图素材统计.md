# Item 临时贴图替换记录

## 当前状态

2026-11-05 复查启用 Manifest 的 500 个物品定义，本轮为 20 类物品制作独立 Sprite，替换 27 个物品/建筑定义以及沥青地板 Tile 的借图引用。上一轮 14 项已完成，继续保留。

## 本轮独立素材

| 物品 | 原借图 | 正式素材（Assets/6_Art/Generated/ 下） |
| --- | --- | --- |
| 棉籽 `Seed_Cotton` | `RadishSeed_Icon.png` | `Farming/CottonSeed/CottonSeed_Icon.png` |
| 钢钉 `SteelNail` | `Nail_Icon.png` | `InventoryCrafting/Nails/SteelNail_Icon.png` |
| 金矿粉 `OrePowder_Gold` | `OrePowder_Stone.png` | `InventoryCrafting/Powders/OrePowder_Gold.png` |
| 命令手册 `CommandManual` | `MinersNote_Icon.png` | `Quest/CommandManual/CommandManual_Icon.png` |
| 石油 `CrudeOil` | `Resin_Icon.png` | `InventoryCrafting/Petrochemicals/CrudeOil/CrudeOil_Icon.png` |
| 石脑油 `Naphtha` | `Resin_Icon.png` | `InventoryCrafting/Petrochemicals/Naphtha/Naphtha_Icon.png` |
| 燃油 `FuelOil` | `Resin_Icon.png` | `InventoryCrafting/Petrochemicals/FuelOil/FuelOil_Icon.png` |
| 沥青 `Bitumen` | `ResinGlue_Idle.png` | `InventoryCrafting/Petrochemicals/Bitumen/Bitumen_Icon.png` |
| 乙烯 `Ethylene` | `PortableGasTank_Steel_Icon.png` | `InventoryCrafting/Petrochemicals/Ethylene/Ethylene_Icon.png` |
| 丙烯 `Propylene` | `PortableGasTank_Steel_Icon.png` | `InventoryCrafting/Petrochemicals/Propylene/Propylene_Icon.png` |
| 聚合催化剂 `PolymerizationCatalyst` | `Ore_Sulfur_Icon.png` | `InventoryCrafting/Petrochemicals/PolymerizationCatalyst/PolymerizationCatalyst_Icon.png` |
| 木制水槽 `WaterTrough` | `WoodenBarrel_Icon.png` | `WaterVessel/WaterTrough/WaterTrough_Empty.png` |
| 电磁炉 `InductionStove` | `ElectricHeater_Idle.png` | `Chemistry/InductionStove/InductionStove_Idle.png` |
| 燃油发动机 `FuelEngine` | `GasEngine_Idle.png` | `Chemistry/FuelEngine/FuelEngine_Idle.png` |
| 气体灌装机 `GasFiller` | `GasCompressor_Electric_Idle.png` | `Chemistry/GasFiller/GasFiller_Idle.png` |
| 液体灌装机 `LiquidFiller` | `GasCompressor_Electric_Idle.png` | `Chemistry/LiquidFiller/LiquidFiller_Idle.png` |
| 液体排出口 `LiquidOutlet` | `FluidOutlet_Idle.png` | `Chemistry/LiquidOutlet/LiquidOutlet_Idle.png` |
| 太空电梯终端 `SpaceElevatorTerminal` | `ShipConsole_Idle.png` | `Space/SpaceElevatorTerminal/SpaceElevatorTerminal_Idle.png` |
| 飞船残骸 `ShipWreck` | `素材占位符.png[素材占位符]` | `Space/ShipWreck/ShipWreck_Icon.png` |
| 沥青地板 `Floor_Bitumen_Summoner` | `WaterPlatform_Stone_Tile.png` | `Building/Floor_Bitumen/Floor_Bitumen_Tile.png` |

## 接入边界

- 相同物品的召唤器和落地建筑同步换图；JSON 为正式视觉入口，不修改旧迁移 sourcePrefab。
- 水槽继续使用液体模块的程序重绘，独立图启用 Read/Write，并覆盖 liquidSurface.bounds，避免继承木桶口区域。
- 沥青地板同步修改 Floor_Bitumen_Summoner 和 Tile_BitumenFloor.asset，乘色统一白色。
- ShipWreck 的独立图用于定义/图鉴；世界残骸继续由真实船体部件 Sprite 拼合。
- 原素材仍供原物品使用，只解除本轮借用方引用，不删除原文件。

## 保留的有效复用

- 同一物品的图标/建筑、巨型矿脉、隐藏铜线、天然洞穴进出口。
- 相同材质的木/石/金属地板与水上平台、通用气液手轮阀、创造模式和 GM 派生设备。
- 共享机械连接零件及合法状态图；未发现额外的跨物品重复 PNG 内容。

## 生成与导入

内置 ImageGen；大色块、低饱和、硬像素簇，按物品用途分别绘制。乙烯/丙烯以分子结构区分，其余保留功能与材质轮廓。完整提示词、尺寸、GUID、原引用和 PPU 见 [生成清单](../../Tools/Art/IndependentItemSprites_20261105.json)。

19 张素材保留生成原图分辨率与 Alpha；水槽因液面按水量缓存整张纹理，使用独立 128×58 最近邻运行图，原稿保留在项目外的生成目录。Point、无 Mipmap、无压缩、maxTextureSize 2048。用 Alpha≥128 的主体最长边对齐原有世界尺寸；地板为 1×1 世界单位。

## 上一轮已完成的 14 项

Scissors、Seed_Foxtail、Flour、Honey、Shovel_Stone、MinersNote、StoneHammer、Seed_Radish、Seed_Mushroom、Seed_Berry、Seed_Herb、Seed_Willow、WillowBranch、Tree_Willow。

## 本轮验证

- Unity 成功导入 20/20 Sprite；启动二进制目录与用途图集重新烘焙完成。
- 523/523 配置地址命中用途目录，漏绑 0、地址冲突 0；20 个新地址和 ItemSprite 标签全部就绪。
- 修复 7 个既有源条目缺迁移来源的登记：GasPipe、CircuitBoard、GlassBottle、VacuumTube、VacuumTubeLogicModule、IntegratedCircuit、Transistor。先明确设置 Default 为源组，再由生成器迁至用途组；地址与用途不变。
- 全内容校验返回 21 条本轮范围外问题：FireSeed/动物音效模块别名、Coconut_Shell 生命配置和部分 UI/VFX Prefab 注册；本轮 Sprite 引用未报错，详情见生成清单。
- 其他 Agent 正在修改项目，按 AGENTS.md 未运行 PlayMode/Lua 回归。最终接入只做资源/配置校验，未声称实际游玩验收。
- flatworld-art 及 runtime-sprites 技能现有规则已覆盖本轮换图流程，无需扩展。

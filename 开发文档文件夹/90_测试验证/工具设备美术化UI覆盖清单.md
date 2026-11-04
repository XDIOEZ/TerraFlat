# 实体工具与设备 UI 美术化交付清单

2026-11-04。18个正式面板、71组外观；原有业务会话继续处理库存、加工和权威命令，器物 View 只读状态。没有新增存档或网络字段。

```mermaid
flowchart LR
    A[现有手持／落地交互] --> B[原业务会话]
    B --> C[类型化实际状态]
    C --> D[外观配置与器物图层]
    B --> E[原配方／真实槽位／参数]
```

## 实际入口与表现

| 正式入口 | 消费者 | 美术与状态 |
|---|---|---|
| UI_FireDrill | Mod_FireDrill / FireDrillMachinePanelSession | 弓架、弓弦、钻杆；实际受热烟雾、真实火种余烬 |
| UI_HandDrill | Mod_HandDrill / MechanicalPanelSession | 横梁升降、飞轮及钻杆、双侧牵绳 |
| UI_FishingRod | Mod_FishingRod | 鱼竿与原鱼线、挂点；真实鱼钩和鱼饵槽 |
| UI_Anvil | ManualProcessingItemPanelSession / ManualProcessingMachinePanelSession | 砧体与实际安装工具敲击；保留配方、库存 |
| UI_MakerTable | Inventory / MachinePanelSession | 工作台与点击工具动作；保留配方 |
| UI_Furnace | Inventory_Furnace / FurnaceMachinePanelSession / InductionStovePanelSession | 实际燃烧、余热、燃料，电磁炉热面 |
| UI_Bonfire | Inventory_Furnace / FurnaceMachinePanelSession | 篝火本体、真实燃烧和热烟 |
| UI_CompostBin | MachinePanelSession | 容器、前4格真实材料与转化后的物品、计时进度 |
| UI_MeatRack | MachinePanelSession | 挂架、前4格真实材料和干燥后的物品、加工进度 |
| UI_Cultivator | CultivatorPanelSession | 前4种植位置，复用TryGetPlotVisual的真实作物阶段与尺寸；保留全部原种植槽 |
| UI_Mechanical | MechanicalPanelSession / MachinePanelSession | 机身、转子、齿轮、风箱；真实转速、电量与开关 |
| UI_IndustrialFluid | FluidMachinePanelSession / ShipChemicalEnginePanelSession | 气泵曲柄连杆、抽液流动、阀门、液面、气量；真实交付决定泵运转 |
| UI_FluidTank | FluidMachinePanelSession / MechanicalPanelSession | 便携罐机身、真实气量与阀门 |
| UI_FuelInteraction | FuelInteractionPanel | 燃料器具、真实燃烧与剩余燃料；保留火种槽 |
| UI_Spacesuit | SpacesuitPanelSession | 装备本体、真实头盔开合、实际气罐余量；保留气罐安装槽 |
| UI_SteamEngine | SteamEnginePanel | 蒸汽机本体、启用阀与输出档位；仅设定值，不伪装运行 |
| UI_Minecart | MinecartPanel | 侧视矿车与独立车轮；实际沿轨道有符号速度驱动旋转，货仓保留 |
| UI_SpaceElevator | SpaceElevatorPanelSession | 终端本体、真实旅行进度与到站位置，仓库与命令保留 |

## 复用或保留

| 入口 | 处理与原因 |
|---|---|
| UI_FlintStrike | 复用协调聊天提交9e19afe97的独立燧石面板、敲击火花和正式引用；不切回钻木面板 |
| 陶罐／水袋／坩埚等容器、石臼、大气检测仪、可读书本 | 保留已有器物专用View与实际液面／内容／检测表现 |
| 背包、通用储物、装备管理、纯手工配方 | 管理和列表操作为主，保留概念布局 |
| 飞船导航／指令／起降、地图、设置、调试 | 命令和管理信息为主，保留概念布局；工业设备的独立面板用对应机身 |
| 无UI的斧镐、武器、纯机器部件 | 不新增窗口；部分共用机械资源有外观配置，只有已有UI入口会消费 |

## 外观身份清单

下表是实际catalog身份；召唤器与落地器具共用母项完整层栈。设备材质变体由同一View装配，不复制面板。

| 外观 | 接受的ItemId |
|---|---|
| Battery_Basic | Battery_Basic, Battery_Basic_Summoner |
| Battery_LithiumIronPhosphate | Battery_LithiumIronPhosphate, Battery_LithiumIronPhosphate_Summoner |
| BlastFurnace | BlastFurnace, BlastFurnace_Summoner |
| Bonfire | Bonfire, Bonfire_Summoner |
| Clutch | Clutch, Clutch_Summoner |
| CompostBin | CompostBin, CompostBin_Summoner |
| Conveyor_Cloth | Conveyor_Cloth, Conveyor_Cloth_Summoner |
| Conveyor_Wood | Conveyor_Wood, Conveyor_Wood_Summoner |
| CopperAnvil | CopperAnvil |
| CopperAnvil_Summoner | CopperAnvil_Summoner |
| Creative_Gas_Tank | Creative_Gas_Tank, Creative_Gas_Tank_Summoner |
| Creative_Power_Source | Creative_Power_Source, Creative_Power_Source_Summoner |
| Creative_Ship_Engine | Creative_Ship_Engine, Creative_Ship_Engine_Summoner |
| CrossShaft | CrossShaft, CrossShaft_Summoner |
| ElectricHeater | ElectricHeater, ElectricHeater_Summoner |
| ElectricLightBulb | ElectricLightBulb, ElectricLightBulb_Summoner |
| FishingRod | FishingRod |
| FluidOutlet | FluidOutlet, FluidOutlet_Summoner |
| FluidPipe | FluidPipe, FluidPipe_Summoner |
| FluidValve | FluidValve, FluidValve_Summoner |
| FuelEngine | FuelEngine, FuelEngine_Summoner |
| GasEngine | GasEngine, GasEngine_Summoner |
| GasFiller | GasFiller, GasFiller_Summoner |
| GasPump_Mechanical | GasPump_Mechanical, GasPump_Mechanical_Summoner |
| GasSelector | GasSelector, GasSelector_Summoner |
| GasTankBlock_Iron | GasTankBlock_Iron, GasTankBlock_Iron_Summoner |
| GasTankBlock_Steel | GasTankBlock_Steel, GasTankBlock_Steel_Summoner |
| Gear_Wood | Gear_Wood, Gear_Wood_Summoner |
| Gearbox_Wood | Gearbox_Wood, Gearbox_Wood_Summoner |
| Generator_Basic | Generator_Basic, Generator_Basic_Summoner |
| HandCrank | HandCrank, HandCrank_Summoner |
| HandDrill | HandDrill |
| HandDrill_Summoner | HandDrill_Summoner |
| InductionStove | InductionStove, InductionStove_Summoner |
| IronAnvil | IronAnvil |
| IronAnvil_Summoner | IronAnvil_Summoner |
| LiquidFiller | LiquidFiller, LiquidFiller_Summoner |
| LiquidOutlet | LiquidOutlet, LiquidOutlet_Summoner |
| LiquidPipe | LiquidPipe, LiquidPipe_Summoner |
| LiquidPump_Electric | LiquidPump_Electric, LiquidPump_Electric_Summoner |
| LiquidStorageTank | LiquidStorageTank, LiquidStorageTank_Summoner |
| LiquidTankBlock_Iron | LiquidTankBlock_Iron, LiquidTankBlock_Iron_Summoner |
| LiquidTankBlock_Steel | LiquidTankBlock_Steel, LiquidTankBlock_Steel_Summoner |
| LiquidValve | LiquidValve, LiquidValve_Summoner |
| MechanicalBellows | MechanicalBellows, MechanicalBellows_Summoner |
| MechanicalHammer | MechanicalHammer, MechanicalHammer_Summoner |
| MechanicalLoom | MechanicalLoom, MechanicalLoom_Summoner |
| Millstone | Millstone, Millstone_Summoner |
| Minecart | Minecart, Minecart_Summoner |
| Mod_Meatrack | Mod_Meatrack |
| PortableGasTank_Iron | PortableGasTank_Iron, PortableGasTank_Iron_Building |
| PortableGasTank_Steel | PortableGasTank_Steel, PortableGasTank_Steel_Building |
| Sawmill | Sawmill, Sawmill_Summoner |
| Shaft_Wood | Shaft_Wood, Shaft_Wood_Summoner |
| ShipAirSupply | ShipAirSupply, ShipAirSupply_Summoner |
| ShipChemicalEngine | ShipChemicalEngine, ShipChemicalEngine_Summoner |
| ShipChemicalEngine_GM | ShipChemicalEngine_GM, ShipChemicalEngine_GM_Summoner |
| ShipConsole | ShipConsole, ShipConsole_Summoner |
| ShipGravityGenerator | ShipGravityGenerator, ShipGravityGenerator_Summoner |
| ShipNavigation | ShipNavigation, ShipNavigation_Summoner |
| SpaceCultivator | SpaceCultivator, SpaceCultivator_Summoner |
| SpaceElevatorTerminal | SpaceElevatorTerminal, SpaceElevatorTerminal_Summoner |
| Spacesuit_Torso | Spacesuit_Torso |
| SparkMaker | SparkMaker, SparkMaker_Summoner |
| SteamEngine | SteamEngine |
| SteamEngine_Body | SteamEngine_Body |
| Torch_Building | Torch_Building |
| WaterWheel | WaterWheel, WaterWheel_Summoner |
| Windmill | Windmill, Windmill_Summoner |
| Wire_Copper | Wire_Copper, Wire_Copper_Summoner |
| WorkBench | WorkBench, WorkBench_Summoner |

## 复建与验证

- `PhysicalDeviceArtworkPrefabBuilder` 统一装配。原重建入口在重排前移除器物布局，再重新装配；`MechanicalPanelView.PreservePrefabLayout` 防止旧布局覆盖槽位。
- `Tools/UIArtwork/build_device_artwork.py` 仅生成外观配置和精灵导入，不再维护第二套Prefab写入器。素材生成来源分别记录在BowDrillUI、PumpDrillUI、MinecartUI目录。
- Unity原生隔离预览：`python Tools/UIArtwork/unity_artwork_preview.py preview`。产物在 `Library/FlatWorldDeviceArtwork/Previews`，含18个正式面板、全部外观、1920×1080、1366×768、全部18项1080×1920，以及钻木／手钻连续16帧。它们是正式渲染器的编辑态状态预览，不是实时游玩录像。
- 原生规则采样：`python Tools/UIArtwork/unity_artwork_preview.py rules`。验证全部面板、16层固定池、精灵引用、点击穿透、安全区、弓架位移、热烟、实际余烬、关闭复位、真实内容图与空槽清除、手持部件一致性；receipt保存在上述Library目录。
- Lua断言已扩展：UISettings的physical_device_artwork；Mechanical钻木单次点击加热与动画修订；IndustrialFluids气泵／液泵的断电、真实交付和输出满状态。
- 本轮其它Agent持续编辑同项目，按AGENTS跳过Gameplay／PlayMode自动测试；因此缺料、输出满、断电、拖放、关闭重开及所有手持／落地入口的完整运行时回归仍待正式Lua执行。原生规则采样不代表Lua整套已通过；内容图的规则通过直接注入类型化状态验证，库存／作物采样器的端到端现场尚待执行。
- 桌面两档与全部18项竖屏已生成并做原生视觉复核；尚未逐页实际触屏验收。

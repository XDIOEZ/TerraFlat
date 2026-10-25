---
name: flatworld-machines
description: "FlatWorld 数据机器的调度、库存加工、机械/电力/流体网络、交互表现及扩展接口。"
---

# 机器与工作设施

代码根为 `Assets/5_Scripts/5-3_GamePlay/`：

| 入口 | 职责 |
| --- | --- |
| `World/Machines/MachineWorld*`、MachineEntity、MachineLogic | 权威节点、作用域与调度 |
| MechanicalNetworkGraph、ElectricalNetwork、MachineWorld.Fluid* | 独立的机械/电力/流体拓扑 |
| RecipeProcessor、FurnaceLogic、FluidDeviceOperations、MachineInventoryCommands | 加工、供热与库存事务 |
| `Presentation/UI/*PanelSession` | 命令与只读面板 |
| ChunkTilemapRenderer.Mechanical、MechanicalDepthVisual* | 表现与交互代理 |

配置：`Resources/Config/Mechanical/mechanical-catalog.json`；网络：`NetworkItemStateCoordinator.Machines`；MOD 示例：`ModSDK/Examples/HarmonyMachines/`。

## 核心边界

- 召唤器/手持保留 Item；落地设施由 Place/SpawnGenerated/RestoreMachine 创建数据节点。Mod_MachineAuthoring 只存配置，ChunkView 不驱动业务。
- `Mod_Building.UseGameObjectBackend` 显式保留组合能力建筑的 Item 后端，自动机器编译返回空；坩埚同时复用固体热加工和液体模块，不强行装成普通石臼机器。
- 数据机器包含落地椰壳等容器，物品定义必须显式提供正最大生命及独立受击 Trigger，不能靠绕过校验补默认血量；回归 `building/portable_building_health`。
- 世界时间由调用方传入；网络恢复完才 Tick、快照后才休眠。船与后台地表按自身世界继续模拟，不借当前场景的玩家、环境或掉落物。
- 迁移以 TransferScopeNodes/TransferShipNodes 整批交接真实节点、加工器和共享库存；不要用会排放储罐的 Remove/Place 模拟迁移。
- 加工资格归设备，转化与温度归材料；RecipeProcessor 复用 CraftingService 原子事务。燃料炉只供热，不持有配方或加工进度。
- 面板与显示只读权威状态；库存命令校验操作者、距离与目标身份。拆回先成功交付全状态返还物，再移除节点。
- 三套拓扑共享节点、各自求解；扩展用注册策略与租约，保持设备规则、库存事务及表现分离。

## 按需资料

仅打开当前主题，不批量读取：

- [作用域、后台与归档](references/scopes-and-lifecycle.md)
- [加工、炉体与温度](references/processing-and-heat.md)
- [机械、电力与连接](references/power-networks.md)
- [工业气液与共享罐](references/industrial-fluids.md)
- [表现、面板与 C# MOD](references/presentation-and-mods.md)

机械/气液案例入口分别为 `GameplayTests/mechanical-suite.json`、`GameplayTests/fluid-suite.json`；执行与报告按 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)，细目见 GameplayTests 的 README 与 IndustrialFluids 覆盖矩阵。

## 铁路与库存引擎

- `World/Railways/` 的铁路仅复用 `ConveyorPath`/静态曲面，不接机械输送业务；`RailTrackNetwork` 按所属世界场景登记、双端口互认并按真实弧长推进。
- `EnginePowerService` 只识别注册的 `IEnginePowerProvider`，引擎位按能力收物；供给方自己管理冷 `ItemData` 的状态和资源，接收方不识别具体引擎。地面蒸汽机是 `FluidMachine`/机械动力源：管道从左侧输入纯水蒸汽，右侧机械轴输出动力，冷凝水留在设备内供容器取出。
- 矿车可放在地面或铁路，推动复用载具接触链；地面高阻、轨道约束和进出轨由矿车运动策略管理。`Mod_MinecartPlacement` 只检查实际吸轨位置的车辆净空，基础地形、支撑与占地资格仍归建造事务；两端定义以 `requiredGroundSupport=3` 拒绝承重2的液面。

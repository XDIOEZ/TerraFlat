---
name: flatworld-machines
description: "FlatWorld 数据机器的调度、库存加工、机械/电力/流体网络、交互表现及扩展接口。"
---

# 机器与工作设施

- 气液新批次在预留时筛选正式端口/设备路由及本轮冻结库存；相变、切相或选择器改选后只归还不合法的预留，合法批次保持原物质与随机态继续重试。工业液体对外份数向下取可回转的float，库存保留真实decimal尾量；整数残渣提交前检查整型范围。

## 代码入口

以 `Assets/5_Scripts/5-3_GamePlay/` 为根：

| 目录 | 入口与职责 |
| --- | --- |
| `World/Machines/` | MachineWorld*、MachineEntity、MachineLogic：唯一权威机器世界 |
| 同上 | MechanicalNetworkGraph、ElectricalNetwork、MachineWorld.Fluid*：三套独立拓扑 |
| 同上 | RecipeProcessor（非炉体）、FurnaceLogic、FluidDeviceOperations、MachineInventoryCommands：业务与事务 |
| `Presentation/UI/` | MachinePanelSession、MechanicalPanelSession、VesselMachinePanelSession：命令/读数 |
| `Extensibility/Mods/` | ModRuntimeManager.Managed、ModManagedAssemblyStore：C# 扩展 |

表现入口为 `ChunkTilemapRenderer.Mechanical`、`ChunkDepthMeshRenderer*`、`MechanicalDepthVisual*`；网络入口为 `NetworkItemStateCoordinator.Machines`。目录配置在 `Resources/Config/Mechanical/mechanical-catalog.json`，示例在 `ModSDK/Examples/HarmonyMachines/`。

## 实体与业务

- 召唤器/手持保留 Item，落地设施经 Place/SpawnGenerated/RestoreMachine 创建数据节点；Mod_MachineAuthoring 保存配置，不驱动落地 Load/Tick。普通 Ports=none 设施共用格索引，不产生扭矩网络。
- 模拟不依赖 ChunkView，网络整体恢复后才 Tick，快照后才休眠；无端口设施按玩家窗口休眠。世界时间由调用方传入，有界补算保留余量，手摇倒计时先于领域分派。
- 移动船使用 `UseScope(GetShipScopeKey(ShipId))/TickShip`，本地 Cell 不随地表切换；位置经 SpaceSession 投影。ScopeLease 为值类型，按单一 using 生命周期释放；面板/延迟回调通过 UseNodeScope 进入节点所在作用域。分裂以 TransferShipNodes 整批移交，真实库存先拆分，每个作用域整批结束才重建拓扑。
- 起飞/落地复用 TransferScopeNodes 在普通 worldKey 与 ship:key 间整批交接真实节点、加工器和共享罐份额，调用者给出最终 Cell/位置/绝对朝向；先全量校验，再提交归属与地表通知。不能用 Remove/Place 代替迁移，Remove 会执行储罐拆除排放；落地机器直接加入普通管电网络。
- 船上无机械端口的设施与机械网、流体网一样持续模拟，不用地表玩家区块决定休眠；`ship:` 作用域不是场景名，不能因场景匹配不到玩家而释放电池、培育仓等设备的交互对象和面板。
- SpaceSession 以游戏时钟调用 TickBackgroundWorlds，档案中的非活动地表作用域只恢复数据网络并持续推进加工/燃料/气液；活动地表由 ItemMgr 调度，避免重复。冷环境按自己的 WorldKey/冻结拓扑查询，不发布到当前场景 TemperatureMgr；炉吸燃料与带面交付经 DroppedItemService.ColdWorld 按世界查询实物、去重和原子扣量，不借用眼前世界掉落物。退出游戏后不补算。
- 后台炉吸燃料先用本世界冻结拓扑筛同格候选，再仅对候选计算距离、排序和补配置，禁止为同格查询排序全世界掉落物；记录身份优先核对原列表/下标，列表被回调挪动时才回退查找。Profiler 分 `ColdFuel.Collect/Sort/Transfer`。
- 后台掉落物每步从真实档案重填可复用的运动工作区，跨世界与异常结束均清空状态；水面同点采样合并，冷地表仅复用资源目录及有界查询键，不缓存实际差量。Profiler 分 `ColdWorld.Motion/Water`，规则覆盖见 Processing 的 `cold_motion_workspace`。
- 冷机器温度优先读已加载/冷缓存真实环境层，缺块时经 `SpaceSurfaceQuery.TryGetAmbientTemperature` 复用正式地理温度与星体覆盖公式，禁止为了取温度同步生成水文/生态整块；全局调温每次读取，资源/存档切换清理有界点样本。规则见 IndustrialFluids 的 `cold_temperature_path`。
- 炉内材料、罐体和停火炉体按 `ThermalRuntime.AdvanceAmbient` 每游戏秒消除剩余温差 1%，升降温至少 0.5℃/游戏秒，到达目标即停，跨速率下限的分步与累计结果一致；炉体先撤销自己的已熄火热源，共享罐内容按本轮启用成员的真实罐体平均温度只换热一次，转换回内能后仍走原有相变；主动燃烧升温与两库存换热保留设备规则。
- 加工条件归设备，产物归材料：ProcessCapability/Level 匹配 ItemDefinition.processing 的结果/work/等级闭区间；专用手钻/锻造仍用 MachineProcessDefinition。RecipeProcessor 复用 CraftingService 原子事务，输出满保留进度，SharedModuleIds 迁移便携载体状态。
- 燃料炉的 `heatingRatePercent` 只缩放炉内材料相对原速的升温（Smelter 50%、BlastFurnace/Bonfire 100%），炉体升温、真实干燥/加工计时及自然冷却不变；电炉与燃料炉共用 `FurnaceMaterialHeating`，Input/Output、容器液体和坩埚内材料沿同倍率受热。新冷载荷先取真实环境/容器原温；活动地表初温用气候基温避开自身炉心热源，已初始化温度不覆盖。
- `InductionStoveLogic` 为独立3输入2输出电磁炉，120V/1200W、最高2000℃、材料150%；实际供电比例只控制炉体主动升温，断电仍从真实余热换热，没有燃料、点火或配方。
- 炉输入区共用箱子总重量/总体积容量（Smelter 50 kg/50 L、BlastFurnace 150 kg/150 L、InductionStove 300 kg/300 L），仅Input限制，不限制Output/Fuel；模板通过 Inventory.StorageMaxWeightKg/StorageMaxVolumeCubicMeters（m³）交MachineInventory.Create/InitData恢复策略，库存内部体积为L，既有原子入库/转移事务统一判满，不另写炉专用限制。
- 燃料炉共用 FurnaceLogic，只管燃料、炉温、点火、供热与燃料副产物，不持有配方、RecipeProcessor 或加工进度；Input/Output 都按材料自身 MatterState 温度换热，熄火后继续余热传递。MaterialThermalProcessor 统一按材料 transitions/reactions 原子转化，不以炉温代替材料温度；先处理已有 Output，再处理 Input，避免新产物同轮重复加热。燃料资格读 Combustion.Fuel Tag，燃值由 Mod_Fuel 解析。
- 炉体点火后逐份吸收槽内燃料，`fuelAbsorptionSeconds` 默认3秒；`FuelAbsorptionState` 保存槽位/物品身份和进度，完成才事务扣一份、增加燃值并记录副产物。取走/换实例清零，满显示容量暂停，后台长步按吸收/燃烧边界分段；面板只读进度。
- CultivatorLogic 保存每槽作物、水、肥、电力和待收产量；断供/满输出保留状态。显示读取真实种植槽，空槽隐藏，不为客户端或休眠显示创建业务逻辑。
- 初始化库存/战利品只执行一次，随机空结果也算完成。最大生命由当前定义及 CraftedDurabilityMultiplier 派生，受损生命不重复叠乘；MachineCombatBridge 消费真实武器窗口并 PublishExternalDamage 回传实际损失。
- 放置失败不扣召唤器；拆回捕获全状态、成功生成返还物再移除。MachineArchive 持有独立归档，缺失 MOD 定义保留冷快照，显示卸载不删节点或库存；CaptureCurrentWorld 在捕获期间固定 scopeOverride，嵌套节点租约不得切回活动场景或把船库存写入地表档案。
- 面板拆回整笔事务进入 UseNodeScope；船上机器返还载体交 SpaceSession.TryDismantleMachinePiece，以太空掉落记录继承部件位置/点速度，节点删除成功后撤船部件、设备与视图并重建结构。联机准备拆除仍只支持地表，不能提前提交船回收。
- 权威库存命令校验玩家归属、距离、身份。客户端更新同格槽内容并保留槽/库存对象和本地布局。私有跨箱库存用 RegisterPrivateInventory 注册玩家/机器双键，不进入公开库存、箱快照或公共角色同步。

## 机械与电力

- 机械动力专项 Lua 入口为 `GameplayTests/mechanical-suite.json`，在独立真实世界组装全目录 20 种机械端口组件并核验动力、加工、发电、输送、测压及手推；编写、截图和执行转 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)，清单与 GM 参数见 `Assets/StreamingAssets/GameplayTests/README.md`。
- RPM/SourceRpm/SpeedRatio 有符号：正逆时针、负顺时针；方向由 RPM 派生，效率与功率读绝对值 SpeedRpm。轴、离合器、跨轴器保持方向，齿牙啮合/齿轮箱翻转；闭环转向不一致整网停转。
- 动力源分别提供扭矩与 RPM；同速有效源累加扭矩，异速冲突停转。用力器是输入终点，不转送扭矩，按最近可达源距离分配固定 TorqueLoad，同距按稳定 ID；不足完整需求才停该用力器。传动件不限制通过量，TorqueCapacity 不参与求解。
- 效率为 SpeedRpm/RequiredRpm，不封顶。齿轮箱按局部输入侧换算：左大轮输入 RPM×2/扭矩×0.5，右小轮反向；负载按侧倍率折算。水车由真实水流和半径算 RPM，供给强度由基础扭矩决定。
- CrossShaft 占 Layer1，要求同格 Layer0 下穿节点，但 A/B 只接相邻 Layer0，不把同格节点接入上层网。手持朝向是临时状态，世界端口/预览/动画共用 RotationQuarterTurns。
- `PlacementRotationSteps` 可显式声明 2/4 个放置朝向，普通与 GM 飞船引擎声明 4；轮换统一调用 MachineDefinition.NextPlacementQuarterTurns，不按具体物品身份加旋转特例。
- 手推设备声明 ManualDriveTorque/Rpm/SecondsPerPress；CanManualDrive 先排除手推检查整网外源，外源存在则禁手推。加工器仍按节点最终 RPM 工作。风箱朝向相邻炉体，通过 MachineAirflowEffect 提交：20 RPM 满风量、最多 500℃上限增量，温度只取最高风量；各来源的材料升温效率按百分点累加，机械风箱 AirflowHeatingRateBonusPercent=10（50%→60%），低转速按比例、单台满额封顶、停转/移除即撤。RegisterAirflow 扩展同一合并规则，FurnaceRuntimeState 同步实际 HeatingRatePercent 给面板。
- 电网与机械网共用节点但拓扑独立；线为独立覆盖层，cell 接同格，方向接旋转后的邻格。W=功率、J=储能，电压负责兼容、电流=P/V；当前无逐段压降求解。
- 发电节点可声明 `UnlimitedGeneration`，按兼容负载及电池充电缺口无限供电，不用巨额储能或无穷数；多个无限源分担本步出力，仍遵守端口、电压与线缆规则。`Creative_Power_Source` 为120V创造电源，通过创造背包召唤器放置，测试船只保留一台。
- 通电照明声明 `Lighting` 并复用 `electric-light` 逻辑，实供 `ElectricalSuppliedWatts` 决定亮度与 `powered` 贴图，断电/休眠撤光。地表和船体共用 `MachineLightPresentation`；玻璃保留源图 Alpha，专用自发光覆盖按脚点排序，不把底座一起提亮。灯泡默认 120V/60W、半径16格（火把8格）。
- converter 先排除电驱动力探测机械输入，从独立电源确定转换方向；机械输入优先，上游电网不作回流目标。电池探算不写能量，正式每轮结算一次；电驱扭矩向下取整，输出受真实功率预算限制。
- 机械路线缓存由可达节点、有效源、输入终点与实际图边变化失效，功率数值变化仍逐步结算；电力协调器拥有本步最终机械求解。只有显式声明 CanCacheElectricalDemandWithinStep 的策略才复用冻结需求，自定义来源默认动态采样。离线规则对照见 Tools/Tests/MachineNetworks。
- 电线 wire0..15、管线 pipe0..15 共用北1/东2/南4/西8掩码。电线读权威线格/方向口；管线读正式图边，端口相位/方向/启停参与掩码，流量不参与。地表格事件与船作用域事件分别刷新连接，两个世界使用各自节点索引。

## 工业气液

- 工业气液专项清单为 `GameplayTests/fluid-suite.json`，按功能组织 9 个 Lua 分组、保留 52 项检查；通用分组、截图和报告规则见 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)，覆盖矩阵见 `Assets/StreamingAssets/GameplayTests/Systems/IndustrialFluids/Gameplay/README.md`。子案独立清理，失败继续收集同组报告，整组仍失败，未运行不得报告通过。
- 固定步长冻结库存与吞吐预算；方向由端口输入/输出决定，不按压差自然输送，新料不在同轮穿透无限管格。管层2、机械/电子测压层4/5；测压探针切断真实动力边。
- 工业组分仍按 decimal 连续计量，地表边界一份对应一级整数水位。地表液泵把已供电的有效升数预算存在 `WorldLiquidPumpLiters`，攒够整份再抽；地表出口按端口保存 `WorldLiquidOutputLiters`，累计时先扣本轮共享吞吐，实际落地才扣真实一份，尾量留在工业库存。容器间和管内传输继续连续计量。
- StockCache 冻结工作区只复制组分数量，不复制端口预留；共享罐只在拓扑变化时绑定库存，Capture 同步目标组。FluidInventoryState 的组分原位改量通知 MassChanged，热量/预留只改状态版本；观察绑定与事件不进序列化/克隆。UsesObservedTickState 策略若改未观察字段，须主动通知或关闭该能力。
- FluidDeviceBehavior 经 RegisterFluidDevice 注册能力策略，具体事务在 FluidDeviceOperations；注册租约管理生灭，策略变更使拓扑/能力缓存失效。管网成员始终走正式图边，普通容器仅通过 IContainerPortProvider/ILiquidTransferPort 转移。
- `receiver` 为按需接收端，不接普通随机单物质推送；需求方经正式有向供给图检查全部所需物质，原子扣源库存与路径各段共享吞吐，同一共享罐去重。基础管道及铁/钢气罐显式配置1000 L/s，普通管道召唤器可在背包数量堆叠。
- 同材组合罐主成员保存共享 decimal 组分、内能、随机态与端口预留，其余引用主身份。拆分/跨船/回收先按真实容积分配并归还预留，再撤共享引用/预算和重建网络；超压固定受损成员，经 PressureExplosionQueue 有界处理。
- 气罐方块拆除只释放该格真实份额、返空方块：气相入所在环境，液相持久化溢流；便携罐继续保存内容。船上环境 Provider 读取真实舱温压/舱气，不借当前地表。
- 炉体接收 ICombustionSupportReceiver，供给侧 ICombustionSupportSource 随领域实例注册；氧气助燃只消费朝向邻炉的真实出气口预留和库存预算，已助燃气不再排环境，供氧/风箱加温取最大值。
- 电加热器只按实际 ElectricalSuppliedWatts 发布局部热源；断电、休眠、拆除撤来源。电解器一个液入/一个混气出口，液副产物用液腔与转液接口；氢能发动机双气输入按真实 H₂:O₂=2:1 mol 消耗，回收水无空间或缺气时停耗。

## 表现与扩展

- 主体/运动部件按脚点 Y 行合批，MechanicalDepthVisual 只负责交互/灯光，HP/库存不在代理。MechanicalShaft 连接层在机械主体下方；端口、铁环、连接短管由独立层生成，主体贴图只画设备本体。
- 飞船降落通过 `ChunkTilemapRenderer.MechanicalLandingPreparation` 分帧预建停用交互/灯光外壳、部件缓冲及共享 Sprite 几何，不复制运行机器；`BeginCommit` 提供候选，区块接管后归正常生命周期，未消费候选随准备器释放。`MachineWorld.BeginSurfaceChangeBatch` 只在同步交接内按世界/格去重；旧 `CellChanged` 保留，区块经 `SurfacePresentationCell(s)Changed` 按最终格和连接邻居各刷新一次，不延迟常规 Tick。
- Pivot/Renderer 局部变换供预览、落地与阴影共用，逻辑占格不随贴图偏移。旋转/往复部件保持同画布/PPU/Pivot，变速先积旧相位再改角速度；太阳/接触阴影独立 BRG 管理。交互目标经 SpatialInteractionRegistry 注册同一数据目标。
- `MachineDefinition.VisualLayer` 共用管道→电线→设备的显示与交互顺序；光标取完整逻辑格的可交互叠层，由 `GridInteractionSelector` 从上到下等分选择，不用贴图窄条或邻格半径决定线路命中。
- 电池世界三格灯由 `BatteryChargePresentation` 共用 33%/66%/99% 门槛；`BatteryIndicator.Cells` 为原图左上原点的像素矩形。地面 `MechanicalDepthVisual.Battery` 合入主体脚点行，船上 `ShipPieceView.Battery` 跟随主体变换；节点状态通知只在档位变化时更新，未亮格保留底图，精确数值仍看面板。
- 机器面板非模态；电池读数、电池形竖向填充和居中百分比使用 0.2 秒非缩放刷新，关闭停止，文本仅在变化时更新；比例读取节点 `ElectricalStoredJoules/CapacityJoules`，未接电网仍显示，UI 不修改储能。灯光跟随真实燃烧/燃料，不跟加工进度；声明 Mod_CombustionVisual 的设施由 MechanicalDepthVisual 读取同一燃烧状态并复用粒子，主体与粒子共享脚点行排序，停用/拆除清空。
- ConveyorPath 同时决定运输/推动/显示；ConveyorMode 0自动、1~12手动，自动传播优先手动段。DroppedItemRuntime/TransportItemBacked 按局部索引每轮搬一次，末端经 ContainerTransferService 按成功数量扣地面物。
- MachineLogicRegistry 返回可乱序释放租约，关键托管入口由 link.xml 保留。C# MOD 当前仅桌面 Mono，已启用 MOD 启动加载，程序集仍须清单声明；Initialize 注册、ContentReady 用内容、Dispose 撤本 MOD 资源，IL2CPP 拒绝托管 DLL。

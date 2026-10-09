---
name: flatworld-machines
description: "Use when: 定位或修改 FlatWorld 的机器世界、工作台、熔炉、箱子、堆肥、晾架、落地便携加工设施、机械/电力网络、机器交互/存档及 Harmony MOD 入口。关键词：MachineWorld、MachineEntity、MachineLogic、MechanicalNetworkGraph、ElectricalNetworkGraph、RecipeProcessor、FurnaceLogic。"
---

# FlatWorld 机器与工作方块

## 入口

- 权威实体和粗粒度领域逻辑：`Assets/5_Scripts/5-3_GamePlay/World/Machines/`。
- 调度/存档：`MachineWorld.cs`；无端口设施/命令/鼓风：`MachineWorld.Facilities.cs`。
- 电力：`ElectricalNetwork.cs` 负责电网拓扑与整网功率解算，`MachineWorld.Electrical.cs` 负责机械↔电力桥和扩展 Provider；不要再建第二套 ElectricalWorld。
- 工作台、熔炉、储物、堆肥、晾架、手动加工、手钻、取火、石臼、水容器各有 `MachineLogic`，机械扭矩仍由 `MechanicalNetworkGraph` 解算。
- 表现：`ChunkTilemapRenderer.Mechanical.cs`、`ChunkDepthMeshRenderer*.cs`、`MechanicalDepthVisual*.cs`；面板：`MachinePanelSession`、`MechanicalPanelSession`、`VesselMachinePanelSession`。
- 网络：`NetworkItemStateCoordinator.Machines.cs`；库存入口：`MachineInventoryCommands`。
- 代码 MOD：`ModRuntimeManager.Managed.cs`、`ModManagedAssemblyStore.cs`；作者示例：`ModSDK/Examples/HarmonyMachines/`。

## 约束

- 只维护一套 `MachineWorld`，不新建并列的 WorkBlockWorld；普通设施 `Ports=none` 共用格索引，但不能伪造扭矩网络。传动网络和设施业务都保持粗粒度内聚。
- 机械网与电网是同一批 `MachineEntity` 上的两张独立拓扑图；电线使用独立覆盖层。`Electrical.Connection=cell` 接同格线，方向名接旋转后的邻格；双向电机 `Role=converter` 的电气接口和唯一 `AxlePorts` 必须分开，默认左线右轴，不要求世界布线区分正负极。
- 双向电机每轮先排除电驱动力探测机械输入，再从独立电源向外确定方向；机械输入优先，转换链的上游电网不能成为发电回流目标。电池探算不写储能，正式结算每轮一次；输出按实际功率预算限制，电驱扭矩向下取整，不能将上一轮输出或四舍五入增量当作新能量。转换效率与游戏功率换算统一取目录配置。
- 电线 `visual.spriteStates` 使用 `wire0..wire15`，连接位为北1、东2、南4、西8；朝向只读取权威电线格与方向端口索引，同格设备不产生额外支路。邻格增删须刷新跨区块/循环边界的连接形状，连接未变化不重提网格，不能按召唤器旋转或每帧轮询选图。
- 混合电力/机械设备的接口属于独立表现层：机械轴口由 `MachineDefinition.AxisPortVisual` 配置 `Count=1/2`、`StartX`、`Y`，统一复用 `Shaft_Wood/axisPort` 标准贴图并始终绘制在机械主体下层；单端保留 `StartX` 正负决定左右，双端按 `±abs(StartX)` 镜像。电线口仍用 `electricalPort + ElectricalPortLocalPosition`；机身美术不得烘入机械连接杆或轴口。
- 电网首版按整网功率求解：W 表示功率、J 表示储能；电压参与兼容性，电流由 `P/V` 推导，电阻只保留正式数据接口，未实现逐段压降/基尔霍夫仿真。
- 电池面板由交互入口显式启用 `MechanicalPanelSession` 的 0.2 秒非缩放状态刷新：打开立即读数，关闭/销毁停协程，仅更新有变化的状态文本，不轮询库存或重排布局；其他面板默认仍走事件。储能读取节点当前 `ElectricalStoredJoules`，未接电网也要显示，不依赖加工 Tick、不在 UI 累加或预测电量。
- 召唤器、玩家库存和手持玩法保留 Item；落地设施走 `Place/SpawnGenerated/RestoreMachine`，`ItemMgr` 拒绝再实例化其完整 Item。`Mod_MachineAuthoring` 只保存配置，禁止重新启用其 Load/Tick 做运行时兜底。
- 模拟不依赖 ChunkView。网络整体先恢复再 Tick、先快照再休眠；无端口设施独立按玩家窗口休眠。显示卸载不能删除实体或撤销已保存库存。
- 业务时间使用传入的世界时间；有界补算保留剩余时间。手摇倒计时在领域逻辑分派前推进，定制领域逻辑不能绕过动力耗时。
- 工作台配方与进度属于 `RecipeProcessor`，面板只发命令；输入/输出预检与结算复用 `CraftingService`。只提供通用加工方式的设备使用 `MachineDefinition.ProcessCapability`：石磨提供 `grind`、锯木机提供 `cut`、织布机提供 `weave`（纺织），具体输入、产物和工作量读取物品自己的 `ItemDefinition.processing`，禁止再在 `mechanical-catalog.Processes` 为每种可加工物复制设备专用配方。扩展棉花、蚕丝或新纤维的纺织结果时，只在原料定义声明 `processing.weave`，不要在机器侧按原料 ID 或 Fiber 标签推断产物。需要加工尺度约束时，设备用 `ProcessCapabilityLevel` 提供单一等级，目标物品用 `processing.<capability>.minLevel/maxLevel` 声明可接受闭区间；等级过低或过高都不匹配。尚未迁移为物品能力的手钻、锻造等专用转换继续使用现有 `MachineProcessDefinition`。炉温、燃料、点火、副产物仍内聚在 `FurnaceLogic`，不要用“温度直接乘秒”替代实际热加工规则。
- 热加工同样遵循“设备给条件、材料定义结果”：晾架只提高空气暴露并读取 `TemperatureMgr` 的实际环境温度；炉体与坩埚优先解析物品 `reactions`，反应温度判断使用参与材料自身的 `MatterState.TemperatureCelsius`，不能把炉温直接当作材料温度。
- 钻木器手持/落地共用 `MaterialHeatingProcessor`，只配置热源上限与每次供热量；温度存于输入材料 `MatterState`，不存点击进度、固定产物或火绒白名单。单物料受热结果读取材料 `matter.transitions`，达到条件后经库存事务转入输出槽；新材料从实际环境温度初始化，禁止初始化成热源温度或冷却原本更热的材料。堆叠共用温度且平分单次供热，整槽转化必须先检查输出容量。
- 炉体先用 `Combustion.Fuel` Tag 判断物品是否允许作为燃料，再走 `Mod_Fuel.TryResolveItemData` 读取数值；冷 `ItemData` 先读保存态 BitData，缺失时回退当前物品定义的 `parameters.Data`。Tag 负责语义资格，FuelData 负责燃值与最高温度。
- 机械运行态 `Rpm/SourceRpm/SpeedRatio` 带符号：世界 XY 平面正值为正转（逆时针）、负值为反转（顺时针）、零为停止；`RotationDirection` 从 RPM 派生，不另存或另同步方向。配置 `Rpm/ManualDriveRpm` 仍是正数大小，内建动力源读取 `SourceRotationDirection=1/-1`，RPM Provider 直接返回最终有符号转速。
- 轴、离合器、跨轴器与齿轮轴口保持转向，齿轮齿牙啮合及穿过齿轮箱翻转转向；多动力源核对抵达自身后的有符号转速，闭环转向不一致必须整网停转。放置朝向、输入端与坐标奇偶不能代替转向；坐标奇偶只保留齿牙初相位，箱内动画复用 `GetGearboxRpm`。
- 工作效率、风箱与发电功率只读取 `SpeedRpm`（RPM 绝对值），扭矩仍为非负供给门槛；反转不能变成零效率、负功率或倒扣加工进度。联机沿用有符号 RPM 增量，方向翻转和停转即使低于速度阈值也必须刷新相位并同步；风箱通过 `Airflow` 输入影响炉体，炉体不遍历网络拓扑。
- 机器库存只能由权威端修改。拖放、快捷转移、排序/整理都走正式命令和现有库存事务；服务端校验玩家归属、距离与物品身份。客户端快照先校验候选，再更新同格 ItemSlot 的内容；保留库存和槽位身份、本地 UI 布局，并同步禁止放入状态，不能因定期同步使拖拽来源失效。
- 每名玩家独占、跨多个箱体共享的库存用 `MachineInventoryCommands.RegisterPrivateInventory` 注册角色与机器双键解析；不要放进公开的 `MachineLogic.Inventories` 或箱体快照。服务端用现有搬运/整理事务，私有库存只回给发起交互的连接；公共角色状态与联机全量存档快照也要剔除已注册的私有键。
- 放置提交失败不得消费召唤器；拆回先捕获全部状态、成功生成返还物再删除。便携设施继续按 SharedModuleIds 迁移同一状态，不能另存手持/落地两份进度。
- 砧台等重型手动加工设施的召唤器默认走放置，`RequiresPlacementRequest=false`；`Mod_ManualProcessor` 只保存和转移载体加工状态，不订阅手持使用动作，交互和加工必须检查已安装建筑。地面掉落的 Summoner 仍是待放置物，不能冒充已安装设施；手钻等便携工具继续使用独立模块。
- 箱子首次创建须接收当前配置/结构生成载荷中的库存及 InventoryInitName，后续只恢复 MachineStorageState；随机空结果也视为初始化完成，休眠、读档、F5 都不能重抽战利品。
- 机器最大生命按当前定义 MaxHp 与快照 CraftedDurabilityMultiplier 共用 CraftedDurabilityQuality 换算；只在未初始化时赋当前生命，读档/唤醒不得重复叠乘已有受损生命。受损表现阈值同样使用实例最大生命。
- 全部落地机器通过 `MachineCombatBridge` 消费真实武器窗口：目录必须校验有限正生命与启用的独立受击 Trigger，不能静默跳过关闭的 `health.collider`。候选格范围由当前全部节点的受击外延缓存派生，拓扑或资源变化后重建；偏移、朝向与转换器镜像和物理投影共用同一形状。提交耐久/拆除后必须调用 `PublishExternalDamage` 回传实际生命损失，防御抵消仍回传有效 0，不能只扣内部 HP 而漏掉武器命中反馈。
- `MachineArchive` 使用现有外层 `MechanicalNetworks` 载荷；保留缺失 MOD 的冷快照，避免卸载显示或暂缺资源导致存档丢失。不建立旧运行架构兼容层。
- 新增设施必须通过资源目录预检：稳定身份、主领域工厂、必要库存/燃料配置与正式面板。预检不创建 MachineLogic、面板或世界节点，不污染 F5 候选会话。
- 工业气液共用 `MachineWorld.Fluid*.cs` 的固定步长和库存预算；管层为 2，机械/电子测压层为 4/5。输送方向来自真实输入与独立出气口，不按压差自然流动；新料不能同轮穿过无限管格，测压探针必须切断真实动力边。
- 流体设备差异由 `MachineLogicRegistry.RegisterFluidDevice` 登记 `FluidDeviceBehavior`，具体事务放在 `FluidDeviceOperations`；Kind 只作策略键，调度、构图和动力层读取策略能力。持有并释放注册租约，替换策略必须让拓扑和能力缓存失效。
- 周围电加热由独立 `ElectricHeaterLogic` 消费电网实际 `ElectricalSuppliedWatts`，向现有局部温度场发布按供电比例缩放的热源；不挂气液腔体或伪造管口，断电、休眠、拆除和远端停机快照都撤销来源。
- 炉体只接收 `ICombustionSupportReceiver` 请求；供给侧实现 `ICombustionSupportSource` 并随领域实例生灭登记/注销。先扣真实供给，再完成一次请求，同时扣本轮库存预算；相邻液体容器按 `IContainerPortProvider/ILiquidTransferPort` 发现，不能要求具体 `VesselLogic`。
- 接口发现的工业管网成员必须继续走正式图边，不能用普通容器事务直抽，也不能回退抽地表；否则会绕过冻结库存与吞吐预算，让新液体同轮穿透多段。
- 同材组合罐只由稳定主成员保存共享库存、随机种子和各口预留，其余块引用主身份；成员变化同步主快照。超压固定本轮受损成员，破裂沿 `PressureExplosionQueue` 的有限预算结算，清空队列后才解锁重建。
- 机械图形代理不保存 HP、库存、炉温等权威数据；主体和运动部件合入所属区块的 Y 行网格，轻量 `MechanicalDepthVisual` 只负责交互与灯光，阴影继续走 BRG。
- 传动轴、齿轮输入短轴和标准 `axisPort` 连接件统一走 `MechanicalShaft` 下层合批，固定压在机械主体下面；它们不再因为所在格的 Y 行跑到相邻机械主体上方。
- 带 `Mod_Fuel` 的燃烧工作方块，其运行时灯光只跟随 `MachineLogic.IsBurning` 与燃料余量，不依赖加工进度；`MechanicalDepthVisual` 统一使用火把的橙红 Light2D 颜色，没有专用 `Mod_LightSource`/Light2D 模板时再补默认强度与范围。
- 普通设施本体的 `visual.rendererLocalPosition` 要同时用于放置预览、落地 Sprite 和阴影落点；格心仍是建造与动态排序锚点，不要用图片偏移改动权威占格。
- 工作设施库存/加工面板保持非模态，不主动获取玩法输入锁；交互发送器会在锁定时取消当前目标，面板自行加锁会形成“刚打开就关闭”的循环。距离失效、切换目标和关闭按钮继续走原清理链。
- 火堆与高炉/熔炉统一视为可交互炉类机械：`Mod_Furnace -> FurnaceLogic -> MachineWorld`；火堆是 `Ports=none` 的普通设施，不另建火堆专用交互运行时。炉体面板的输入/输出/燃料槽数量与命名必须和库存模板一致。
- dynamicY 视觉用 `SpatialInteractionRegistry.Register` 绑定 `MachineWorld.GetOrCreateInteractionTarget` 返回的同一数据目标，光标命中按实际图层范围，描边由 `IWorldInteractionPreview` 通知现有视觉；停用/卸载须注销和清理描边，不为预览逐帧查询机械图，也不把库存或面板搬回视觉代理。

- 水电解器仅一个液体输入口和一个混合气体输出口；各气体组分写入同一输出腔，按现有气包规则从共用出口抽取，不另造混合气身份。允许水源逐项声明：淡水产氢氧，盐水/海水产氢氯及独立碱液；碱液用 LiquidResidueFluidId/Chamber 保留真实流体，经 LiquidDefinition.sourceItemId 映射整份库存原料，保留不足一份的余量，沿原子事务取出，不增加管道口。
- 氢能发动机只有一个氢氧共用气体输入管道和一个回收水输出管道；SingleGas=false，氢氧反应输入均指向同一 main 腔，按 2:1 摩尔比例消费真实库存。机械轴口独立保留，动力仍按有效负载结算；缺氢、缺氧或回收水无空间时停止消费，不把其他气体当燃料。

## MOD

- `MechanicalShaft` 必须排在地表效果之上、`Player` 主体层之下，并加入太阳光和局部光的受光列表；新增排序层不能只改渲染器而漏掉 Global Light 的序列化范围。
- 输送带的 `ConveyorPath` 默认从相邻带格派生，互选带端连通运输与动力，余下两边为供能轴口；面板通过 `conveyor.orientation` 命令切换 `MachineState.ConveyorMode`（0 自动、1~12 固定直线/拐角输入输出），冷节点同步该值，自动方向传播必须从手动段优先开始且不得覆盖手动段。运输、玩家推动与安装显示共用该路径，不能只旋转图像；带面模式 6/7 按实际输送距离连续积分，GPU 只滚动中间带条。
- 地面输送用 `MachineDefinition.Transport` 声明额定速度和带宽，读取节点有符号 RPM；普通掉落由 `DroppedItemRuntime` 维护局部输送候选与空间索引，完整 Item 走 `DroppedItemService.TransportItemBacked`。跨带每轮只搬一次，移动后同步拾取、地形订阅、存档和联机位置，不改库存数量、不扫描全场景。
- 传送带末端向邻接输入端口调用 `ContainerTransferService`，仅按成功的实际数量扣除地面物品；拒收或满箱时保留原位置和数量。

- 关键规则保留普通托管、具名、禁止内联的入口；`link.xml` 保留推荐补丁类型。注册整类逻辑用 `MachineLogicRegistry.Register` 返回租约；多个 MOD 覆盖必须允许乱序卸载，不能恢复已经释放的工厂。
- C# MOD 只从 managed 清单声明的程序集加载，先核对用户授权的代码指纹；不自动信任包，不把 DLL 当沙箱。未声明可执行文件仍拒绝加载。
- `Initialize` 注册扩展，`ContentReady` 使用正式内容，`Dispose` 只撤销本 MOD 补丁和租约。世界切换不清除资源会话级注册；资源结束须释放配置模板缓存。
- 当前支持目标是桌面 Mono。IL2CPP 加载器明确拒绝托管 DLL，不擅自修改 Android 后端；不要承诺 Unity 原生/Burst 代码能按普通 Harmony IL 补丁处理。

## 检查

- 编辑模式 `FlatWorld/诊断/验证机器世界契约` 检查注册注销、库存快照、加工余量、分组、持久化与托管入口，不安装 MOD、不读写真实存档。
- 内容入口为 `FlatWorld/内容配置/校验全部本体内容`；编译和纯逻辑检查不等于放置/加工/读档/联机的真实游戏验收。

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
- 机械网与电网是同一批 `MachineEntity` 上的两张独立拓扑图；发电机/马达可同时入两网。电线使用独立覆盖层，与同格设备接入电网，不要求世界布线区分正负极。
- 电线 `visual.spriteStates` 使用 `wire0..wire15`，连接位为北1、东2、南4、西8；朝向只读取权威电线格索引，同格设备不产生额外支路。邻格增删须刷新跨区块/循环边界的连接形状，连接未变化不重提网格，不能按召唤器旋转或每帧轮询选图。
- 电网首版按整网功率求解：W 表示功率、J 表示储能；电压参与兼容性，电流由 `P/V` 推导，电阻只保留正式数据接口，未实现逐段压降/基尔霍夫仿真。
- 召唤器、玩家库存和手持玩法保留 Item；落地设施走 `Place/SpawnGenerated/RestoreMachine`，`ItemMgr` 拒绝再实例化其完整 Item。`MachineAuthoringModule` 只保存配置，禁止重新启用其 Load/Tick 做运行时兜底。
- 模拟不依赖 ChunkView。网络整体先恢复再 Tick、先快照再休眠；无端口设施独立按玩家窗口休眠。显示卸载不能删除实体或撤销已保存库存。
- 业务时间使用传入的世界时间；有界补算保留剩余时间。手摇倒计时在领域逻辑分派前推进，定制领域逻辑不能绕过动力耗时。
- 工作台配方与进度属于 `RecipeProcessor`，面板只发命令；输入/输出预检与结算复用 `CraftingService`。像石磨这种只提供通用加工方式的设备使用 `MachineDefinition.ProcessCapability`（例如 `grind`），具体输入、产物和工作量读取物品自己的 `ItemDefinition.processing`，禁止再在 `mechanical-catalog.Processes` 为每种可研磨物复制一条石磨配方；尚未迁移为物品能力的手钻、锯木、锻造等专用转换继续使用现有 `MachineProcessDefinition`。炉温、燃料、点火、副产物仍内聚在 `FurnaceLogic`，不要用“温度直接乘秒”替代实际热加工规则。
- 炉体先用 `Combustion.Fuel` Tag 判断物品是否允许作为燃料，再走 `Mod_Fuel.TryResolveItemData` 读取数值；冷 `ItemData` 先读保存态 BitData，缺失时回退当前物品定义的 `parameters.Data`。Tag 负责语义资格，FuelData 负责燃值与最高温度。
- 机械最终 RPM 决定工作效率，扭矩是供给门槛；风箱通过 `Airflow` 输入影响炉体，炉体不遍历网络拓扑。
- 机器库存只能由权威端修改。拖放、快捷转移、排序/整理都走正式命令和现有库存事务；服务端校验玩家归属、距离与物品身份。客户端快照先校验候选，再更新同格 ItemSlot 的内容；保留库存和槽位身份、本地 UI 布局，并同步禁止放入状态，不能因定期同步使拖拽来源失效。
- 每名玩家独占、跨多个箱体共享的库存用 `MachineInventoryCommands.RegisterPrivateInventory` 注册角色与机器双键解析；不要放进公开的 `MachineLogic.Inventories` 或箱体快照。服务端用现有搬运/整理事务，私有库存只回给发起交互的连接；公共角色状态与联机全量存档快照也要剔除已注册的私有键。
- 放置提交失败不得消费召唤器；拆回先捕获全部状态、成功生成返还物再删除。便携设施继续按 SharedModuleIds 迁移同一状态，不能另存手持/落地两份进度。
- 箱子首次创建须接收当前配置/结构生成载荷中的库存及 InventoryInitName，后续只恢复 MachineStorageState；随机空结果也视为初始化完成，休眠、读档、F5 都不能重抽战利品。
- 机器最大生命按当前定义 MaxHp 与快照 CraftedDurabilityMultiplier 共用 CraftedDurabilityQuality 换算；只在未初始化时赋当前生命，读档/唤醒不得重复叠乘已有受损生命。受损表现阈值同样使用实例最大生命。
- `MachineArchive` 使用现有外层 `MechanicalNetworks` 载荷；保留缺失 MOD 的冷快照，避免卸载显示或暂缺资源导致存档丢失。不建立旧运行架构兼容层。
- 新增设施必须通过资源目录预检：稳定身份、主领域工厂、必要库存/燃料配置与正式面板。预检不创建 MachineLogic、面板或世界节点，不污染 F5 候选会话。
- 机械图形代理不保存 HP、库存、炉温等权威数据；主体和运动部件合入所属区块的 Y 行网格，轻量 `MechanicalDepthVisual` 只负责交互与灯光，阴影继续走 BRG。
- 普通设施本体的 `visual.rendererLocalPosition` 要同时用于放置预览、落地 Sprite 和阴影落点；格心仍是建造与动态排序锚点，不要用图片偏移改动权威占格。
- 工作设施库存/加工面板保持非模态，不主动获取玩法输入锁；交互发送器会在锁定时取消当前目标，面板自行加锁会形成“刚打开就关闭”的循环。距离失效、切换目标和关闭按钮继续走原清理链。
- 火堆与高炉/熔炉统一视为可交互炉类机械：`Mod_Furnace -> FurnaceLogic -> MachineWorld`；火堆是 `Ports=none` 的普通设施，不另建火堆专用交互运行时。炉体面板的输入/输出/燃料槽数量与命名必须和库存模板一致。
- dynamicY 视觉用 `SpatialInteractionRegistry.Register` 绑定 `MachineWorld.GetOrCreateInteractionTarget` 返回的同一数据目标，光标命中按实际图层范围，描边由 `IWorldInteractionPreview` 通知现有视觉；停用/卸载须注销和清理描边，不为预览逐帧查询机械图，也不把库存或面板搬回视觉代理。

## MOD

- 关键规则保留普通托管、具名、禁止内联的入口；`link.xml` 保留推荐补丁类型。注册整类逻辑用 `MachineLogicRegistry.Register` 返回租约；多个 MOD 覆盖必须允许乱序卸载，不能恢复已经释放的工厂。
- C# MOD 只从 managed 清单声明的程序集加载，先核对用户授权的代码指纹；不自动信任包，不把 DLL 当沙箱。未声明可执行文件仍拒绝加载。
- `Initialize` 注册扩展，`ContentReady` 使用正式内容，`Dispose` 只撤销本 MOD 补丁和租约。世界切换不清除资源会话级注册；资源结束须释放配置模板缓存。
- 当前支持目标是桌面 Mono。IL2CPP 加载器明确拒绝托管 DLL，不擅自修改 Android 后端；不要承诺 Unity 原生/Burst 代码能按普通 Harmony IL 补丁处理。

## 检查

- 编辑模式 `FlatWorld/诊断/验证机器世界契约` 检查注册注销、库存快照、加工余量、分组、持久化与托管入口，不安装 MOD、不读写真实存档。
- 内容入口为 `FlatWorld/内容配置/校验全部本体内容`；编译和纯逻辑检查不等于放置/加工/读档/联机的真实游戏验收。

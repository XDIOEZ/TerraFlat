# 工业管道气液 Lua 功能回归

入口清单：`GameplayTests/fluid-suite.json`。保持 **9 个 Lua 分组**，保留既有业务边界，并把选择器、相态、组主、加工容量、液碱端口和地表抽排迁到原始采样加 Lua 断言。每系统最多10份Lua；历史失败见 [2026-10-14记录](../../../执行记录与失败待办_2026-10-14.md)，新一轮结果见 [2026-10-15记录](../../../执行记录与失败待办_2026-10-15.md)。所有系统仍是部分覆盖。

## 执行

2026-10-16：`fluid_tanks.lua` 增加 `ground_scope_roundtrip`，在真实隔离地表将两罐、管段和石磨整批移交船作用域再还原，检查节点/加工器身份、格子/绝对朝向、气液 mol 与内能守恒，以及目标同层占用时拒绝且不改变归属。本轮用户正在 PlayMode，仅补断言，未编译或运行。

Unity 顶部打开 **FlatWorld → 测试 → 测试中心（多选案例）**，选择 `fluid-suite.json`，勾选需要的案例或全选。测试中心会先执行 EditMode 预检，再只进入一次 PlayMode，共用一个新建的隔离测试世界。已有 PlayMode 会拒绝启动，避免打断正在进行的游戏。

也可在仓库根目录执行：

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/fluid-suite.json
python Tools/GameplayTesting/run_suite.py GameplayTests/fluid-suite.json --cases fluid_transport fluid_tanks fluid_environment
```

运行前让 Unity 完成脚本编译。默认测试场景执行机械动力专项；工业气液专项通过测试中心或上述命令选择。

测试中心显示 9 个分组，每组可以单独勾选。组内每项 `test.fluid` 仍使用独立夹具：某项失败后继续检查同组其余项，失败断言与子案例 ID 全部保留，整组最终仍为失败；世界、玩家或控制前提失效时立即停止。详细报告按分组 ID 写入 `Library/FlatWorldGameplayTests/Runs/<运行ID>/cases/<分组ID>/report.json`；流水线总报告在 `Library/FlatWorldGameplayTests/Pipeline/`。

## Lua 接口

```lua
-- 相关功能放进同一 Lua，各项检查仍读取生产系统的真实结果。
test.check("world_ready")
test.check("player_ready")
test.fluid("network_frozen_chain", "同轮新料不能穿透多个管段")
test.fluid("network_shared_throughput", "多出口共用吞吐预算")
test.fluid("network_blocked_retry", "堵塞后不丢料并继续原批次")
```

`test.fluid(id, label)` 只接受已登记的旧子案例；新现场使用 `test.runtime("industrial_fluids", operation, args, validator, label)`，由正式隔离存档边界保护。C#只准备自有夹具、调用生产API和返回原始状态，Lua回调必须实际执行assert。`runtime_sample`把前后数值写入报告，失败后能检查物质、预留、内能、液深、端口拒收和事务结果。两种接口都不开放C#、反射、文件或网络。

`test.runtime`断言失败会停止当前Lua；旧`test.fluid`仍收集独立子案错误。后续Lua是否继续由清单`continueAfterFailure`决定，未执行步骤不能算通过。

Lua编排沿用每脚本256步、1MB源码和指令预算；同主题参数组合并入现有文件，每个子检查有独立中文标签，不把“每系统最多10份Lua”误解成最多10个调用。整份清单仍受600秒总流程上限约束。无可见现场的独立气液作用域不拍空地截图；真实太空截图仍紧跟采样。

## 功能覆盖矩阵

| Lua 分组 ID | 子检查数 | 覆盖范围 |
| --- | ---: | --- |
| `fluid_inventory` | 6 | 单位换算、两相与混料、decimal 尾量、预留防超卖、转移及多原料原子事务 |
| `fluid_thermodynamics` | 3 | 热量压力、传热守恒、压缩做功、蒸发冷凝及曲线边界 |
| `fluid_state_save` | 4 | 起始快照、质量观察与缓存、JSON/MemoryPack/克隆隔离、机器气液恢复 |
| `fluid_topology` | 28 | 8个原子案；6个随机态×混气/单气的真实双路分流、无候选拒绝和原批次重试；4方向×首选/回退，按实际输送相和水两相总量断言 |
| `fluid_transport` | 4 | 冻结管链、共享吞吐、堵塞重试、完整机器固定步长调度 |
| `fluid_tanks` | 6 | 同材组合与异材隔离、跨船迁移、拆除真实份额与溢流、超压、有限破裂、便携气罐限压充装 |
| `fluid_pumps_compressors` | 7 | 原6子案及工业液泵原始采样；真实两相守恒、同轮不穿透、重复设备调用不旁路抽料、电池电线正式供能 |
| `fluid_reactions` | 19 | 原7子案及9组真实过滤容量边界、3组液碱小数尾量/部分转移/满腔回滚；大整数残渣拒绝不扣料，正式电解与发动机保留 |
| `fluid_environment` | 6 | 原4子案及地表泵/出口原始采样；断电、冻结预算、自然相变、部分接纳、解除堵塞、正式命令动态切相和对向旧预留释放 |

## 夹具与边界

大多数案例建立独立机器作用域、有限测试大气和真实物品快照，使用正式 `Place`、拓扑重建、`BeginFluidStep`/`CompleteFluidStep`、设备事务、`Tick`、保存和跨船迁移接口。测试调度阶段通过 `MachineWorld` 的测试专用 partial 夹具进入，没有复制输送、配方或热力学算法。

需要改变吞吐/端口条件的案例只克隆该节点的定义，不修改全局目录。相变边界使用局部材料目录，不替换 `FluidCatalog.Default`。设备事务专项可准备已供能读数；只有 `device_electrical_compressor` 明确运行真实电网供能。发动机实际负载案准备解算后的扭矩边界，不能据此宣称完整机械求解也已通过。

地表三案使用测试世界中玩家附近已加载的连续空格，只操作真实液体层。每案 `finally` 清理自己创建的节点、作用域及测试溢流/破裂记录，地表格恢复原有液体身份和深度；原有机器、存档目录和游戏世界不被整体清空。缺少连续空格或真实物品/液体定义时会明确失败。

这份矩阵覆盖**工业管道气液业务**及其地表抽排边界。纯库存以同物质Gas+Liquid总mol核对，不能要求室温纯水完全不蒸发；地表深度是float，世界与库存的换算比较允许一个液深存储刻度，不能把这个容差扩到库存内能或整数事务。画面、UI点击、联机Host/Client及地表自由流动/浮力的完整效果由用户验收。

## 扩展

核心旧子案保留在`GameplayTestFluidCore`；管网、设备和地表原始采样放在对应`GameplayTestFluidNetworks/Devices/Surface`，通过`GameplayTestRuntimeProbe`登记明确操作。新增业务判断写在同主题Lua，C#不重算规则、不只返回真假；通用预留筛选和数值边界仍在正式生产模块中实现。

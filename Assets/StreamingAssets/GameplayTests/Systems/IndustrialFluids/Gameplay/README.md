# 工业管道气液 Lua 功能回归

入口清单：`GameplayTests/fluid-suite.json`。按功能组织 **9 个 Lua 分组，保留原有 52 项检查**，每个系统的测试集控制在 10 个 Lua 以内；没有执行测试，也不声称达到 100% 行/分支覆盖率。

## 执行

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

`test.fluid(id, label)` 只接受已登记案例。拼错 ID 在 Lua 预检时失败；它不会向沙箱开放 C#、文件读写、反射或任意代码执行。一个功能案例内部的各项断言均写入原有 JSON 报告，没有断言的案例不能被记为成功。必要前提失败记录异常，防止后续空数据检查冒充通过。

Lua 编排沿用现有 256 步、1MB 源码和指令预算。每组通过 `ipairs` 顺序编排相关检查，最多 10 项功能调用加 2 项就绪检查；整份清单仍受测试场景的 600 秒总流程上限约束。顺序遍历参考 [Lua 官方手册](https://www.lua.org/manual/5.4/manual.html#pdf-ipairs)。

## 功能覆盖矩阵

| Lua 分组 ID | 子检查数 | 覆盖范围 |
| --- | ---: | --- |
| `fluid_inventory` | 6 | 单位换算、两相与混料、decimal 尾量、预留防超卖、转移及多原料原子事务 |
| `fluid_thermodynamics` | 3 | 热量压力、传热守恒、压缩做功、蒸发冷凝及曲线边界 |
| `fluid_state_save` | 4 | 起始快照、质量观察与缓存、JSON/MemoryPack/克隆隔离、机器气液恢复 |
| `fluid_topology` | 10 | 目录、16 种掩码、四向端口、压差规则、阀门、选择器、气液相偏好、物质筛选、receiver 路径、测压回差 |
| `fluid_transport` | 4 | 冻结管链、共享吞吐、堵塞重试、完整机器固定步长调度 |
| `fluid_tanks` | 6 | 同材组合与异材隔离、跨船迁移、拆除真实份额与溢流、超压、有限破裂、便携气罐限压充装 |
| `fluid_pumps_compressors` | 6 | 机械气泵、工业液泵、机械/电动压缩、压力控制、真实电池电线供能 |
| `fluid_reactions` | 7 | 滤材与残渣、满槽保护、全部实际电解配方、混气/液碱堵塞、液碱取液权限、发动机缺料与实际负载结算 |
| `fluid_environment` | 6 | 有限大气抽排、管段放气、容器事务、真实地表抽液倾倒、地表液泵与湿阻、排液入地表及满格回滚 |

## 夹具与边界

大多数案例建立独立机器作用域、有限测试大气和真实物品快照，使用正式 `Place`、拓扑重建、`BeginFluidStep`/`CompleteFluidStep`、设备事务、`Tick`、保存和跨船迁移接口。测试调度阶段通过 `MachineWorld` 的测试专用 partial 夹具进入，没有复制输送、配方或热力学算法。

需要改变吞吐/端口条件的案例只克隆该节点的定义，不修改全局目录。相变边界使用局部材料目录，不替换 `FluidCatalog.Default`。设备事务专项可准备已供能读数；只有 `device_electrical_compressor` 明确运行真实电网供能。发动机实际负载案准备解算后的扭矩边界，不能据此宣称完整机械求解也已通过。

地表三案使用测试世界中玩家附近已加载的连续空格，只操作真实液体层。每案 `finally` 清理自己创建的节点、作用域及测试溢流/破裂记录，地表格恢复原有液体身份和深度；原有机器、存档目录和游戏世界不被整体清空。缺少连续空格或真实物品/液体定义时会明确失败。

这份矩阵覆盖**工业管道气液业务**及其地表抽排边界。画面、UI 点击、联机 Host/Client 传输、地表自由流动/浮力的整套行为属于其他验收范围，不由这些业务断言代替。全部 Lua 与夹具均待你在 Unity 中编译、执行；当前没有 PASS/FAIL 运行结果。

## 扩展

核心、管网、设备和地表子案例分别登记在 `GameplayTestFluidCore`、`GameplayTestFluidNetworks`、`GameplayTestFluidDevices`、`GameplayTestFluidSurface`。新增规则时在对应职责内添加真实行为断言，并将 `test.fluid` 调用并入现有 Lua 分组；只有职责确实独立时才新增分组，整个系统保持 10 个 Lua 以内。不把测试特例写入生产输送或设备逻辑。

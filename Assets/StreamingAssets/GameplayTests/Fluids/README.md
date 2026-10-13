# 工业管道气液 Lua 功能回归

入口清单：`GameplayTests/fluid-suite.json`。共 **52 个独立 Lua 案例**，覆盖当前工业气液系统的功能分支；没有执行测试，也不声称达到 100% 行/分支覆盖率。

## 执行

Unity 顶部打开 **FlatWorld → 测试 → 测试中心（多选案例）**，选择 `fluid-suite.json`，勾选需要的案例或全选。测试中心会先执行 EditMode 预检，再只进入一次 PlayMode，共用一个新建的隔离测试世界。已有 PlayMode 会拒绝启动，避免打断正在进行的游戏。

也可在仓库根目录执行：

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/fluid-suite.json
python Tools/GameplayTesting/run_suite.py GameplayTests/fluid-suite.json --cases network_frozen_chain tank_scope_transfer surface_liquid_pump
```

运行前让 Unity 完成脚本编译。默认测试场景仍使用原清单；此专项通过测试中心或上述命令选择，不自动加入日常 quick-suite。

每个 Lua 可单独勾选，前置条件由自己的夹具准备，失败后按清单继续后续案例。详细断言记录在 `Library/FlatWorldGameplayTests/Reports/<运行ID>/<案例ID>.json`；流水线总报告在 `Library/FlatWorldGameplayTests/Pipeline/`。查阅首个失败断言和调用堆栈，不将脚本已保存等同于测试通过。

## Lua 接口

```lua
-- 选择白名单中的功能案例，所有检查读取生产系统的真实结果。
test.check("world_ready")
test.check("player_ready")
test.fluid("network_frozen_chain", "同轮新料不能穿透多个管段")
```

`test.fluid(id, label)` 只接受已登记案例。拼错 ID 在 Lua 预检时失败；它不会向沙箱开放 C#、文件读写、反射或任意代码执行。一个功能案例内部的各项断言均写入原有 JSON 报告，没有断言的案例不能被记为成功。必要前提失败记录异常，防止后续空数据检查冒充通过。

Lua 编排沿用现有 256 步、1MB 源码和指令预算。每个脚本只有就绪检查和一个功能调用；整份清单仍受测试场景的 600 秒总流程上限约束。沙箱与错误处理参考 [Lua 官方手册](https://www.lua.org/manual/5.4/manual.html#pdf-xpcall)。

## 功能覆盖矩阵

| 范围 | 案例数 | 已编写的检查 |
| --- | ---: | --- |
| 库存与热力学 `core_*` | 12 | 单位/份数换算、气液共存、单相混料限制、decimal 尾量、预留防超卖、部分转移和失败回滚、多原料原子扣量、冻结库存、质量事件、热量/压力/传热、压缩做功、蒸发冷凝及曲线边界、JSON/MemoryPack/克隆隔离 |
| 管网与作用域 | 23 | 20 种本体设备目录、16 种掩码、四向端口旋转、相态不兼容不建边、三轮管链、压差不自然流动、共享吞吐、堵塞原批次重试、阀门断边、选择器分流、方向相态偏好与回退、物质筛选、receiver 全需求路径、探针回差、同材罐组和异材隔离、唯一库存、拆分/跨船守恒、快照恢复、拆罐份额与液体溢流、固定超压受损对象、有限破裂队列、有限大气与逸散、液体容器事务、完整机器 Tick |
| 工业设备 `device_*` | 14 | 气泵供能/缓冲/湿阻/高压/空来源；液泵工业入口不能旁路取料；机械及电动压缩的供能、堵塞和压力合法性；真实滤材/残渣及满槽；遍历全部实际电解配方、断电/混气满腔/液碱满腔、液碱独立取液权限；发动机缺氢缺氧/无负载/无水空间与实际负载能量结算；便携罐限压及冻结充装；电池→电线→压缩机完整电网供能与断供 |
| 真实地表 `surface_*` | 3 | 独立 Liquid 层抽取/倾倒/异种拒收/满格拒收/空来源；真实地表液泵物质量换算、下一轮输出和气泵浸水阻塞；液体出口入世界及世界拒收时不丢料 |

## 夹具与边界

大多数案例建立独立机器作用域、有限测试大气和真实物品快照，使用正式 `Place`、拓扑重建、`BeginFluidStep`/`CompleteFluidStep`、设备事务、`Tick`、保存和跨船迁移接口。测试调度阶段通过 `MachineWorld` 的测试专用 partial 夹具进入，没有复制输送、配方或热力学算法。

需要改变吞吐/端口条件的案例只克隆该节点的定义，不修改全局目录。相变边界使用局部材料目录，不替换 `FluidCatalog.Default`。设备事务专项可准备已供能读数；只有 `device_electrical_compressor` 明确运行真实电网供能。发动机实际负载案准备解算后的扭矩边界，不能据此宣称完整机械求解也已通过。

地表三案使用测试世界中玩家附近已加载的连续空格，只操作真实液体层。每案 `finally` 清理自己创建的节点、作用域及测试溢流/破裂记录，地表格恢复原有液体身份和深度；原有机器、存档目录和游戏世界不被整体清空。缺少连续空格或真实物品/液体定义时会明确失败。

这份矩阵覆盖**工业管道气液业务**及其地表抽排边界。画面、UI 点击、联机 Host/Client 传输、地表自由流动/浮力的整套行为属于其他验收范围，不由这些业务断言代替。全部 Lua 与夹具均待你在 Unity 中编译、执行；当前没有 PASS/FAIL 运行结果。

## 扩展

核心、管网、设备和地表案例分别登记在 `GameplayTestFluidCore`、`GameplayTestFluidNetworks`、`GameplayTestFluidDevices`、`GameplayTestFluidSurface`。新增规则时在对应职责内添加真实行为断言，再增加独立 Lua 并登记专项清单；不把测试特例写入生产输送或设备逻辑。

# 气液独立结算验收记录

日期：2026-10-25。仅记录本轮实际执行结果；没有把源码审查或离线宿主冒充真实游戏测试。

## 已执行

| 检查 | 结果 | 范围 |
| --- | --- | --- |
| `dotnet run --project Tools/Tests/IndustrialGasNetworks/IndustrialGasNetworks.Tests.csproj --configuration Release` | 25 通过，0 失败 | 直接编译正式图、气相求解器、接收接口、库存、相变桥、拆分器和快照/副本代码 |
| `dotnet run --project Tools/Tests/FluidRuntime/FluidRuntime.Tests.csproj --configuration Release` | 46 通过，0 失败 | 原有库存、热力学、溢液、抽水与设备回归 |
| Unity Roslyn 编译器的隔离 GamePlay 编译 | 1372 个源文件，退出码 0 | 初始提交 `b1661acac596f851fe2745c5e621d088aa752895` 加本任务代码覆盖，包含新增正式世界层测试；输出在任务临时目录，不触发 Unity 重载 |
| 本任务白名单 `git diff --check` | 通过 | 无补丁空白错误 |
| 新资产 `.meta` | 已检查 | GUID 格式有效、任务内无重复 |

## CodeReviewFix

两轮独立源码复查，结合故障注入回归修复：实际气相空间分网、构造中途失败回滚、液体专用出口不可绕过、整网库存不能写进主管段液腔、非组主消息不能清掉客户端气网、组主回收不能携带全网库存、真实相变压力与潜热守恒、气液路由/预算分离。

第二轮指出的“缺失成员排放回调抛异常后重试可能重复排气”已经移除该执行路径：拓扑拆分只向仍存在的本地库存提交，全组成功后才清源；缺失成员整笔拒绝并保留原库存。正常拆装/移船先交还份额再删除成员；销毁物料由正式持久化释放队列接管。对应故障重试测试已加入并通过。

## 未执行与边界

- 开始时 Unity 处于用户正在运行的 PlayMode，本轮没有停止、暂停、强制刷新或重编译该 Editor。
- `gas_network_lifecycle` / `gas_network_restore_order` 已接入正式 Lua 测试并通过隔离源码编译，**尚未在 Unity 执行**。完整 IndustrialFluids Lua 套件、真实存档写盘/重启、船体迁移，以及真实 Host/Client 双端验收仍未执行。
- 收尾时 Editor MCP 只读状态/Console 连接被拒绝，未获取本轮最终 Unity Console。不能据此宣称运行时无异常。
- 隔离编译验证本任务覆盖层，不代表并行任务持续修改的整个工作区、所有程序集或打包已经通过。
- 离线宿主不模拟大气释放/燃烧系统。这些接入点仍需要正式世界层测试；未给出实际帧率/加载耗时提升百分比。

新网络线协议版本为 27；旧存档/旧客户端兼容不在本次目标内。地表 `LiquidId/LiquidLevel` 与固定 Tick 间隔未改变。

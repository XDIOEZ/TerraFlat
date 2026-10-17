# 流体库存与舱室查询离线回归

在仓库根目录运行：

```powershell
dotnet run --project Tools/Tests/FluidRuntime/FluidRuntime.Tests.csproj -c Release
```

需要 .NET 8 SDK，以及项目已有的 MemoryPack、FastCloner 和 Unity Newtonsoft DLL。源文件和依赖全部通过 `RepoRoot` 的相对路径引用；先让 Unity 完成依赖导入即可，不需要启动、暂停或刷新 Unity。编译产物放在已被忽略的 `Temp/Tests/FluidRuntime/`。

回归直接编译生产 `FluidState`、`FluidInventory`、`FluidThermodynamics`、`ThermalRuntime`、`FluidDefinition`、`ShipState` 和完整 `ShipAtmosphereService`。覆盖组分原位改量及替换、旧观察解绑、质量与温度/端口预留的版本隔离、可复用快照与回滚、冻结库存独立、压强试算、相变物质量与内能守恒。MemoryPack 使用实际生成器，JSON 和 FastCloner 使用实际库，确认保存与克隆不会带上订阅者或运行缓存。

舱室查询覆盖封闭/开放格、负坐标和旋转地板、直接增减舱气、气体状态引用更换、流体目录实例更换、结构版本与舱室集合引用变化后的索引失效。预热后断言稳定 `GetEnvironment` 连续查询的线程分配为零。

管道冻结用例使用真实库存和转移方法，检查起始库存快照不能读到同轮新收到的物料；它不运行完整 `MachineWorld` 调度，也不覆盖整个共享罐网络、跨船迁移或超压破坏。`TestDoubles.cs` 只提供测试材料目录、Unity 数学和环境常量等外部输入；星球大气分配和结构伤害替身一旦被调用就报错。2026-10-18 移除温度算法替身并直接引用生产百分比公式，本次按用户要求未编译、未运行。

末尾输出全快照克隆与复用冻结工作区、空库存相变、重复派生读数和环境查询各 10000 次的线程分配及耗时。这些数据用于检查核心热路径，不代表 Unity 整帧耗时或游戏 FPS；具体玩法、船舱环境与画面由实际场景验收。

项目禁用 .NET 8 的隐式框架符号，让 MemoryPack 生成器沿用仓库中 netstandard2.1 核心 DLL 的格式；测试编译使用 C# 11 以匹配该 DLL 的引用参数标注，不改变生产项目的语言版本。

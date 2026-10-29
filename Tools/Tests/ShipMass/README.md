# 飞船载荷质量读取离线回归

在仓库根目录运行：

```powershell
dotnet run --project Tools/Tests/ShipMass/ShipMass.Tests.csproj -c Release
```

需要 .NET 8 SDK。测试直接编译生产 `ShipMassReader.cs`、`MachinePersistence.cs` 和 `Mod_Building.Recovery.cs`，不启动、暂停或刷新 Unity。

覆盖 165 个建筑部件的重复 JSON 读取、JSON／二进制／机器特殊数据变化、船部件回收使用 FWI1 正式封包且保留库存、裸快照明确拒绝、冷库存只还原一次、Reset 保留缓存与 ClearCache 清理缓存、运行库存堆叠及流体变化、运行机器优先于冷载荷、嵌套库存和重复 GUID、共享气罐及液体、运行装备包优先、资源版本和资源实例更换，以及递归运行模块集合隔离。

增量来源回归直接编译 ShipMassReader.Incremental：检查真实数量/单重、嵌套库存、模块载荷、流体原位变化、来源去重及移交、运行机器库存/腔室/便携罐引用更换后的失效，以及取消监听。额外检查模块启停、加载、增删、替换和物品实例切换；165 个带运行模块的未变来源连续 1000 步不重复解析载荷，预热后线程分配为零。实际质量事件实现与流体序列化另由 Data 编译及 Tools/Tests/FluidRuntime 覆盖。

`TestDoubles.cs` 仅替代非测试目标的物品、库存、模块、流体定义及资源入口。二进制解码使用可计数的内存登记表，网络快照替身严格检查正式 FWI1 包头和大小，冷库存通过可计数的工厂还原；JSON 使用实际 Newtonsoft.Json。测试不验证建造材料复制、MemoryPack 编码格式、物品定义载入或 Unity 场景生命周期。

末尾输出热缓存下 165 个部件重复读取 1000 次的耗时、线程分配和解码次数。它能确认重复读取不会重新解析载荷，不代表 Unity 整场景帧率。实际起降、供燃料、货物移动、重心与碰撞表现由游戏场景验收。

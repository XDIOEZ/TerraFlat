# 工业气液拆分回归

```powershell
dotnet run --project Tools/Tests/IndustrialGasNetworks/IndustrialGasNetworks.Tests.csproj --configuration Release
```

直接编译正式 `FluidNetworkGraph`、`MachineWorld.GasTransport`、`IndustrialGasTransferPort`、`FluidInventory`、相变桥、守恒分配器、MemoryPack 气网快照和客户端副本代码。替身仅提供 Unity 坐标、机器宿主和设备行为入口；不启动、暂停或刷新 Unity。输出写入 `Temp/Tests/IndustrialGasNetworks`。

覆盖混合气与内能守恒、1/10/1000 管段无内部气相输送边且边界吞吐不随长度增长、液体独立路由、闭阀/单向口/选择器隔离、实际气相容积加权拆分、合并/拆分失败原子性、缺失成员重试不重复排放、满液体管段最小气相空间、快照序列化与乱序/重复/过期消息、客户端拒绝权威结算、被动气口禁止逆压泵气、凝结/蒸发潜热守恒及失败回滚。

此测试不替代完整 Unity 编译、真实存档恢复、跨作用域迁移或 Host/Client 联机验收。

正式世界层测试已接入 `GameplayTestGasNetworks.cs`，由 `fluid_state_save.lua` 的 `gas_network_lifecycle` / `gas_network_restore_order` 调用，检查非拓扑刷新不重建、独立组主载荷、组主回收和组主先加载的恢复顺序。运行前遵守项目 GameplayMCP 隔离要求，不能打断用户正在使用的 PlayMode。此离线宿主对大气释放/燃烧接口直接抛出未支持异常，不伪造这些系统的集成测试结果。

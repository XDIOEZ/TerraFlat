# 飞船燃料管路离线回归

运行 `dotnet run --project Tools/Tests/ShipFuel/ShipFuel.Tests.csproj -c Release`。

直接编译生产 `FluidNetworkGraph`、`MachineWorld.FluidSupply`、`ShipFuel` 与真实两相库存算法，覆盖有向气相来源、断管、旋转进气口、阀门、选择器、单气/液相口、原子 2H:1O 扣量、预留、冻结库存及共享路径吞吐。读取当前 GM 飞船和机器 Json，检查六台引擎都接入真实管道且可达气罐。

Unity 格坐标、机器作用域存取及领域能力采用最小替身；这些检查不会操作编辑器，也不证明游戏内操作、外观或起飞效果。中央通行空间和引擎喷口检查在 `SpaceCruise` 的 GM 模板回归中。

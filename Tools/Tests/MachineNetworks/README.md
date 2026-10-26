# 机械、电力与耦合调度离线回归

运行：`dotnet run --project Tools/Tests/MachineNetworks/MachineNetworks.Tests.csproj`

直接编译生产 `MechanicalNetwork`、`MechanicalNetworkGraph.Conveyors`、`ConveyorPath`、`ConveyorPresentation`、`ElectricalNetwork`、`MachineWorld.MechanicalSolver` 和 `MachineWorld.Electrical`；替身只提供 Unity 数学/贴图元数据、外部机器定义和环境入口。

覆盖最近用力器优先、完整扭矩门槛、有效源启停、手推输入终点通行、齿轮反转、多源异速冲突、直接撤边失效、电池探算不改储能与单次正式提交、发电条件单次采样、电动马达正式供能，以及两级转换链/回流环路的旧调度差分。转换差分覆盖五档独立出力和连续八步，逐节点比较转速、负载满足、转换方向、发电/供电功率与电池储能。

另测129节点/128用力器网络预热后的100轮求解分配和路线复用；这只证明离线算法结果及托管分配，不代替 Unity 运行性能和用户玩法验收。工业0.1秒节拍和Unity固定步配置未改变。

传送带回归覆盖双侧独立装拆、放置顺序、错向、离合器、层级、平行带/带端、四朝向与环绕邻格；正式 `RebuildGraphsIfDirty` 的事件检查确保无动力放轴/拆轴仍通知带格、稳定连接不重复通知。几何检查使用不同 PPU/Pivot，核对木芯厚度、轴线和格边接缝，不模拟或宣称完成 GPU 画面验收。

计算步边界回归直接调用正式重建入口，覆盖遍历设施时回调标脏不改列表、保留本轮流体预算、步外消费重建、液罐身份变化只更新流体图和稳态不重复重建；双泵与12格组合罐的完整Tick、通知合并及守恒由 `industrial_fluids/combined_tank_pump_tick` 现场Lua验证，离线不替代该现场。

# 连通水域抽液离线回归

执行：`dotnet run --project Tools/Tests/ConnectedLiquidPumping/ConnectedLiquidPumping.Tests.csproj --configuration Release`。

直接编译正式 `WorldLiquidBodySystem`、`DiffusionStreaming` 和 `PumpSearch`；宿主、区块存储、资源目录、两端交换入口和异步完成使用替身，不启动或刷新Unity。

33项检查覆盖原15项邻格扩散、守恒、边界数据租约、失败、取消和预算检查，以及18项共享寻源检查：多泵复用、干入口通过相邻湿格恢复、一级水桥越过扩散停稳、末级回退、余水重激活、筑坝与拆坝、干河床/异种液体隔断、未知边界等待、跨块共享加载、候选卸载后重读、拓扑复用、已加载水域合并、孤立池分组和失败不误抽末级。

正式Lua入口为工业 `fluid_pumps_compressors.lua` 中的 `connected_surface_pump`、`shared_surface_pump_search`。后者检查实际取水事务保留一级、过期候选不得重复扣量、末级耗尽和倒水后重新发现。单格速率用例封闭四邻，避免误读场景原有水域。实现期间未中断用户PlayMode；其后编辑态地表两份Lua规则通过，工业规则因工作区另一项储罐测试缺少 core:oxygen 定义而失败。泵现场专项及新增共享寻源Lua已通过，报告 `20261025T165442827Z_5bae84e8`；离线替身不能替代真实生成、存档、联机或帧耗时验证。

共享寻源面向当前流送世界，非活动星球冷查询仍用原有本地取水。水量仍为0～10整数，不处理细分尾水、真实河床水头或海淡水交换。未知区块只读地形和液体，不恢复画面、树或建筑。

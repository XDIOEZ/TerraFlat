# 连通水域抽液离线回归

执行：`dotnet run --project Tools/Tests/ConnectedLiquidPumping/ConnectedLiquidPumping.Tests.csproj --configuration Release`。

直接编译正式 `WorldLiquidBodySystem` 和 `WorldLiquidSupplyIndex`；宿主、区块存储、时间、资源目录和邻区异步完成使用替身，不启动或刷新Unity。

覆盖负坐标、跨块、空入口全量抽尽、一级尾水、再次注水、地形/登记水坝、异种液体、逐出恢复、小水域、干水渠、环形接缝、按需单邻区请求、恢复顺序和停泵释放租约。十万格热缓存测试只测独立CLR中供水查询/更新的分配和耗时，不代表Unity帧率或整条河峰值。

正式Lua入口：`surface_fluids` 的 `replenishment.lua` 与工业 `fluid_pumps_compressors.lua`；真实生成、后台区块提交、机器/储罐、存档、联机与水面表现仍需Unity验证。

# 连通水域抽液离线回归

执行：`dotnet run --project Tools/Tests/ConnectedLiquidPumping/ConnectedLiquidPumping.Tests.csproj --configuration Release`。

直接编译正式 `WorldLiquidBodySystem` 及 `DiffusionStreaming`；宿主、区块存储、资源目录、两端交换入口和异步完成使用替身，不启动或刷新Unity。

15项检查覆盖静水不加载、负坐标跨块扩散、停稳、地形水坝、异种液体、未知边界暂封、单请求去重、准备后邻格续流、不恢复建筑、休眠释放、取消晚到结果、失败不丢水、不无限重试、环形接缝及预算饱和释放。替身不替代真实生成、差量保存、区块逐出或Unity帧耗时验证。

正式Lua入口：`surface_fluids` 的 `replenishment.lua` 与工业 `fluid_pumps_compressors.lua`；前者新增真正 `WorldRuntime` 的数据租约规则，后者逐步推进正式世界的邻格扩散。仍使用0～10整数水量，差一级停稳；本次没有实现精细尾水、真实河床水头或海淡水交换。

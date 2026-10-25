# 工业数量与连接图离线回归

运行：`dotnet run --project Tools/Tests/IndustrialGasNetworks/IndustrialGasNetworks.Tests.csproj --configuration Release`。

直接编译生产 IndustrialFluidTransfer、FluidNetworkGraph 和共用 FluidInventory。替身只隔离 Unity 坐标与设备定义入口，不复制转移算法。

12组检查：固定数量容量、实际混气比例、过滤保留其余组分、无来源/0速率、部分预留比例、多次转移不超卖、液体冲突回滚、容量尾差、各罐独立序列化、拆管不搬料、气液分网、阀门/方向搜索。

边界：这不是实际气泵、机械解算、多引擎、三槽UI、双端联机或正式存档重启测试。设备流程由正式 GameplayTests/IndustrialFluids 采样覆盖，未执行时不能宣称通过。

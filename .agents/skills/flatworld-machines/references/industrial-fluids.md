# 工业气液与共享罐

- 气液分开结算：普通双向管段/开启阀门组成共享气网，连通组只保存一份混气组分、物质量、内能、总容积、液体占用后的气相空间和压力；组内没有逐段搬运。气网边界设备仍经正式有向端口及固定步长预算事务输送，新料本轮不能穿透多个设备。液体按独立相态边逐段搬运，保留本地容量/方向/液腔；地表继续使用 LiquidId/LiquidLevel，不随气网改动。
- 气网仅真实流体拓扑变化重建，电探针引起的其他图刷新不拆气网。拆网按真实可用气相空间分配，成员删除前必须交还份额；缺失成员整笔拒绝并保留源库存，拓扑事务内不做外部排放回调。回收携带本段份额；销毁把份额移交正式持久化释放队列。保持 decimal 物质量和 double 内能（浮点容差）。
- `FluidGasNetworkSnapshot` 由唯一组主保存独立气网载荷（组分/内能/成员/预留/随机态），不能把全网气体混进单管液腔。组主先加载时保留载荷，成员到齐才接管。客户端只按拓扑代次/状态序号替换副本，不本地聚合或结算；线协议版本随载荷变化提升。
- `MachineWorld.GasTransport` / `IIndustrialGasReceiver` 与液体求解分别维护冻结库存、吞吐预算、路由。普通管段数量不增加气体边界吞吐；被动气口只能顺压输气，压缩机通过真实做功升压。液体用自己的出口距离表，不能经过气口形成捷径。
- `FluidGasLiquidPhaseBridge` 显式读取整网分压进行一次蒸发/冷凝；液体仍归属各本地液腔，不跨段均压。候选库存/内能全部验证后才提交，凝液容量或相态不符就回滚。管层2、机械/电子测压层4/5；测压探针切断真实动力边。
- 新批次预留筛正式端口/设备路由与冻结库存；相变、切相、选择器改选只归还非法预留，合法批次保留物质/随机态重试。工业对外份数向下取可回转 float，库存留真实 decimal 尾量，整数残渣提交前判范围。
- 工业组分 decimal 连续计量，地表一份对应一级整数水位。泵以 WorldLiquidPumpLiters 累积已供电有效升数、够整份再抽；出口以 WorldLiquidOutputLiters 按端口累积，先扣本轮共享吞吐、实际落地才扣真实整份，尾量留工业库存。容器/管内仍连续传输。
- StockCache 只复制冻结组分数量、不复制预留。共享罐仅拓扑变动时绑定库存，Capture 同步目标组；FluidInventoryState 原位改组分通知 MassChanged，热量/预留仅改状态版本，观察/事件不序列化或克隆。UsesObservedTickState 改未观察字段时须主动通知或关闭能力。
- RegisterFluidDevice 注册 FluidDeviceBehavior，事务归 FluidDeviceOperations；租约管生灭，改策略失效拓扑/能力缓存。成员走正式图边，普通容器仅经 IContainerPortProvider/ILiquidTransferPort 转移。
- receiver 按需接收，不接普通随机单物质推送；需求方沿正式有向供给图检查全部物质，气体到相邻共享气网即停止遍历普通管段，原子扣源库存及各真实边界端口预算，共享库存去重。基础管道和铁/钢罐1000L/s，普通管道召唤器可堆叠。
- 同材组合罐主成员保存共享组分、内能、随机态和端口预留，其他引用主身份。拆分/跨船/回收先按真实容积分配、归还预留，再撤共享引用/预算、重建网络；超压固定受损成员，经 PressureExplosionQueue 有界处理。
- 方块罐拆除只释放该格真实份额、返空方块：气入当地环境，液持久化溢流；便携罐保留内容。船 Provider 读真实舱温压/舱气，不借当前地表。
- 炉体接收 ICombustionSupportReceiver，ICombustionSupportSource 随领域实例注册；助燃仅消费朝向邻炉的真实气口预留/库存预算，用过的氧不再排环境，供氧/风箱加温取最大。
- 电加热器只按实供 ElectricalSuppliedWatts 发布热源，断电/休眠/拆除撤销。电解器一液入一混气出口，液副产物走液腔/转液口；氢能发动机双气输入按 H₂:O₂=2:1 mol 消耗，缺气或回收水无空间即停耗。
- 离线生产源码回归：`dotnet run --project Tools/Tests/IndustrialGasNetworks/IndustrialGasNetworks.Tests.csproj --configuration Release`。正式世界层由 `fluid_state_save.lua` 的 `gas_network_lifecycle` / `gas_network_restore_order` 检查；离线通过不等于 Unity/Lua 或真实 Host/Client 已验收，不能为测试打断用户 PlayMode。

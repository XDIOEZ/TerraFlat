# 工业气液与共享罐

- 固定步长冻结库存与吞吐；端口输入/输出决定方向，不靠压差自然输送，新料同轮不穿透无限管格。管层2、机械/电子测压层4/5；测压探针切断真实动力边。
- 新批次预留筛正式端口/设备路由与冻结库存；相变、切相、选择器改选只归还非法预留，合法批次保留物质/随机态重试。工业对外份数向下取可回转 float，库存留真实 decimal 尾量，整数残渣提交前判范围。
- 工业组分 decimal 连续计量，地表一份对应一级整数水位。泵以 WorldLiquidPumpLiters 累积已供电有效升数、够整份再抽；出口以 WorldLiquidOutputLiters 按端口累积，先扣本轮共享吞吐、实际落地才扣真实整份，尾量留工业库存。容器/管内仍连续传输。
- StockCache 只复制冻结组分数量、不复制预留。共享罐仅拓扑变动时绑定库存，Capture 同步目标组；FluidInventoryState 原位改组分通知 MassChanged，热量/预留仅改状态版本，观察/事件不序列化或克隆。UsesObservedTickState 改未观察字段时须主动通知或关闭能力。
- RegisterFluidDevice 注册 FluidDeviceBehavior，事务归 FluidDeviceOperations；租约管生灭，改策略失效拓扑/能力缓存。成员走正式图边，普通容器仅经 IContainerPortProvider/ILiquidTransferPort 转移。
- receiver 按需接收，不接普通随机单物质推送；需求方沿正式有向供给图检查全部物质，原子扣源库存及路径各段共享吞吐、共享罐去重。基础管道和铁/钢罐1000L/s，普通管道召唤器可堆叠。
- 同材组合罐主成员保存共享组分、内能、随机态和端口预留，其他引用主身份。拆分/跨船/回收先按真实容积分配、归还预留，再撤共享引用/预算、重建网络；超压固定受损成员，经 PressureExplosionQueue 有界处理。
- 方块罐拆除只释放该格真实份额、返空方块：气入当地环境，液持久化溢流；便携罐保留内容。船 Provider 读真实舱温压/舱气，不借当前地表。
- 炉体接收 ICombustionSupportReceiver，ICombustionSupportSource 随领域实例注册；助燃仅消费朝向邻炉的真实气口预留/库存预算，用过的氧不再排环境，供氧/风箱加温取最大。
- 电加热器只按实供 ElectricalSuppliedWatts 发布热源，断电/休眠/拆除撤销。电解器一液入一混气出口，液副产物走液腔/转液口；氢能发动机双气输入按 H₂:O₂=2:1 mol 消耗，缺气或回收水无空间即停耗。

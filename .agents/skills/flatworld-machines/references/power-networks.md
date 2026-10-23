# 机械、电力与连接

## 机械

- RPM/SourceRpm/SpeedRatio 有符号：正逆时针、负顺时针；方向由 RPM 派生，功率与效率取绝对值 SpeedRpm。轴/离合器/跨轴器保持方向，啮合/齿轮箱翻转；闭环方向冲突停整网。
- 同速有效源累加扭矩，异速冲突停转。用力器是输入终点、不转送扭矩，按最近可达源距离分配固定 TorqueLoad，同距按稳定 ID；不足完整需求只停该用力器。传动件不限制通过量，TorqueCapacity 不参与求解。
- 效率 SpeedRpm/RequiredRpm 不封顶。齿轮箱左大轮输入 RPM×2/扭矩×0.5，右小轮反向，负载按侧倍率折算；水车 RPM 来自真实水流和半径，供给由基础扭矩决定。
- CrossShaft 在 Layer1，需要同格 Layer0 下穿节点，但 A/B 只连相邻 Layer0。世界端口/预览/动画共用 RotationQuarterTurns；手持朝向临时保存。PlacementRotationSteps 声明2/4朝向（普通/GM引擎为4），统一 NextPlacementQuarterTurns，不按物品身份加旋转分支。
- 手推声明 ManualDriveTorque/Rpm/SecondsPerPress；CanManualDrive 排除手推后查整网外源，存在则禁手推，加工仍读最终 RPM。
- 风箱朝向邻炉，经 MachineAirflowEffect 提交：20RPM满风、温度上限最多+500℃；温度取最高风量，材料升温百分点各源相加，AirflowHeatingRateBonusPercent=10（50%→60%），低速按比例、单台封顶、停转/移除即撤。RegisterAirflow 共用合并规则，FurnaceRuntimeState 同步真实 HeatingRatePercent。
- 拓扑记录 HasBellows；无风箱时跳过四邻查询，注册扩展气流仍叠加，装拆立即刷新。规则 `mechanical/bellows_effects`。

## 电力与缓存

- 电线为独立覆盖层，cell 接同格、方向口接旋转邻格；W为功率、J为储能，电压判兼容、电流=P/V，不逐段求压降。
- UnlimitedGeneration 按兼容负载/电池缺口供电，多源分担出力，仍遵守端口、电压、线缆；不用无穷数/巨额储能模拟。Creative_Power_Source 为120V创造召唤器，测试船仅一台。
- converter 排除电驱后探机械输入，从独立电源定转换方向，机械优先、上游电网不回流。电池探算不写能量、正式每轮一次，电驱扭矩向下取整且受真实功率预算限制。
- 机械路线按可达节点、有效源、终点与图边变化失效，功率值逐步结算；电力协调器拥有本步最终机械解。仅 CanCacheElectricalDemandWithinStep 策略复用冻结需求，自定义来源默认动态。
- MechanicalNetworkGraph.Rebuild 复用排序、BFS、X/Y边界工作区，finally 清引用；保持连接与环绕最短范围算法，纯设施预热重复重建零GC约束见 `Tools/Tests/MachineNetworks`。

## 连接与资格

- wire0..15、pipe0..15 共用北1/东2/南4/西8掩码。电线读权威线格/方向口，管道读正式图边及端口相位/方向/启停，不读流量；地表与船事件使用各自节点索引刷新。
- ElectricalDefinition.Hidden 仅隐藏显示，仍正常供电。EngineeringVision 只读手持、鼠标携带、头部装备同名 Tag；地表资格变化才重提线，船上同步可见性。交互、命令、工具命中均检查真实操作者，不把显示开关写回供电数据。

# 舱气、损伤、残骸与载荷

## 环境

- ShipGroundAtmosphere 按 WorldKey 独立保存普通地面船件的气密几何/舱气，不加入 Ships/Flights；结构/资源修订才刷新，复用 ShipAtmosphereService。ShipDeviceLogic 正常 Tick 供压，机器/生物读同房间；起落按格移交真气体及已围合历史，模板初气仅地面首次密闭落账。
- 闭合墙+完整内部地板决定密闭；停供保气，正常舱门不泄压，破口随外界，补洞不返旧气。密闭正常舱温，补舱只复温，气体仍需供给。
- 环境索引一次查地板/房间；敞舱 WithOutside 取真实外温压，真空源复用不可变单例。结构结算前更新外压，舱气库存包装仅在 Gas/材料目录改变时重绑；Cabin FluidInventory 下限用 MinimumCabinGasSpaceLiters。
- `PlanetShipAtmosphereSource.TakeGas` 先按 `AtmosphereService.MinimumMoles` 量化总请求，再按混合比例分配，末项接收量化后的余量，保持大气库存精确。
- 墙耐压比较船外压力，真空占位温度不造成气体传热伤害；密闭设备读舱温，有气外界按材料耐温。生物与结构伤害分责，可登陆性由材料耐受决定。

## 伤害与分裂

- 部件/残骸逐件持有 Health/PhysicalDefense，无共享血池。DisableHealth/MaxDisableHealth 独立功能损伤：归零停工但留结构，Health 零才剥离；动力/导航/生命支持共用 IsOperational/IsPowered。
- 普攻冻结首次名单，设备/墙优先再地板；SelectAtContact 将边线钳入候选真实占地，同一发毁上层不穿地板。冲击/燃爆先均分冻结沿线原始预算再逐件扣防御，不随件数放大。
- 压力沿持续断裂线损伤，断裂后重取线；触地、船间冲击、燃爆共用局部断裂线与真实能量预算、唯一伤害入口，真实燃料/压力只消费释放一次，实际扣血事件聚合红字。
- 四邻地板分裂后，有可用控制台/引擎的部分留船，其余 ShipWreck；先移交乘员、点火、导航、端口再冷封。每块以无原设备模块的 Item 保存部件耐久/库存，可再攻击拆分，WreckGuid 仅标外壳。
- 结构重建保留仍存活的 `IdentityAnchorPieceId`；普通 Item 死亡按所属船查已归零部件，离场清理不能使用只收活部件的索引。
- 失支撑部件按重叠/曼哈顿距离≤1 邻接组残骸，矩形 Footprint 用两轴最短间隙 O(1) 判断；HashSet 枚举后再移除邻接候选，保留分组/顺序。普通散物用 SpaceDropView 正式拾取。

## 真实质量与缓存

- ShipMassReader 按源 JSON/二进制身份复用解码，监听真实库存、堆叠、模块载荷、流体质量；未变来源不重解，动态乘员/散物位置仍逐步更新。
- ShipMassLedger 按来源增减质量、一阶矩、惯量，保持重心变化前后船体点位置/速度；资源/流体图身份变更或会话结束释放绑定，分裂移交前撤旧来源归属。
- FindPiece 权威索引随装载/放置/回滚/分裂维护，结构变才重绑视图。支撑查 ShipAtmosphereService.TryGetFloor 的结构修订索引，按格返回首个活地板，不遍历部件。
- 地面舱气回归 `Gameplay/ship_rules.lua:ground_atmosphere`；该领域样本不等于真实管电网/起降现场验收。

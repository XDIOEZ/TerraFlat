# 加工、炉体与温度

- `ProvidesWorkbenchInput` 的单格储物设施在工作台相邻八格提供真实共享输入；`NodeCellChanged` 按作用域和邻格触发重连，多柜不叠加。活动工作台保留柜子库存实例，避免柜子先休眠后使用旧库存；界面用 `InventorySlotMirrorBinding` 绑定原柜库存，联机仍按真实所有者及当前连接的工作台验证距离。

- ProcessCapability/Level 匹配材料 `ItemDefinition.processing` 的结果/work/等级闭区间；专用手钻用 MachineProcessDefinition，手动锻造在 `Recipes/crafting/forging.json` 选择金属板或针。RecipeProcessor 原子提交，输出满保留进度，SharedModuleIds 迁移便携载体状态。
- `MachineCatalog.TryGetProcess` 在正式配方目录上维护按 (Station, Input) 的无分配复合键索引；持久化/热重载仍保留原字符串键，配置重载、注册配方、保留或清退旧档配方必须使索引失效。机械总工作分为 `MachineWorld.Logic.Nodes` 和 `MachineWorld.Logic.Facilities` Profiler Marker，以区分首次 JIT 和真实工作；回归 `mechanical/process_lookup_index`。
- 手动砧共用 `ManualProcessingRuntime`，显式站点阻止混入普通手工配方，按成功产物件数消耗 `DurabilityPerProduct`；整批耐久不足不扣料。铜/铁砧500/1000耐久、10铜/熟铁锭制作；耗尽按唯一精确配方固定返50%，先交付返料和内部库存再移除世界节点，船上走正式部件事务；放置/拆回保留基础耐久比例。回归为 `crafting/{metal_tools_catalog,needle_sewing,manual_station_wear}`。
- 燃料炉共用 FurnaceLogic，只管燃料、炉温、点火、供热、副产物；燃料资格来自 Combustion.Fuel Tag，燃值由 Mod_Fuel 解析。MaterialThermalProcessor 按材料 transitions/reactions 原子转化，以材料自身 MatterState 温度换热；先已有 Output 后 Input，防止新产物同轮重复加热。
- 点火及持续燃烧经 MachineWorld.CanSustainCombustion，真实世界/舱气氧气占比至少5%；缺氧停火、清吸料进度、保留燃值，恢复后重新点火。助燃温度加成不能绕过门槛，熄火仍传余热。
- 炉内材料、罐体、停火炉体用 ThermalRuntime.AdvanceAmbient：每游戏秒消除剩余温差1%，升降温至少0.5℃/秒，到目标即停，分步/累计跨速率下限一致。炉体先撤自身熄火热源；共享罐按启用成员真实平均罐温仅换热一次，回写内能再走相变。主动升温与双库存换热保留设备规则。
- FurnaceMaterialHeating 共供燃料/电炉；heatingRatePercent 仅缩放材料升温，Smelter 50%、BlastFurnace/Bonfire 100%，不改炉体、干燥/加工计时和自然冷却。Input/Output、容器液体、坩埚材料同倍率；新载荷从真实环境/容器原温起步，活动地表用气候基温避开自身热源，已初始化温度不覆盖。
- 大步换热按材料反应/相变温度边界分段；输出拒绝只保留参与材料的温区，复用原子事务重试，不增加 pending 存档。
- InductionStoveLogic：独立3输入2输出、材料150%；目标温度默认2000℃，手动可设20～3500℃，120V、目标温度每℃需求0.6W（最高2100W）。目标随机器快照保存/同步，客户端走通用机器命令；电网原需求因子与MachineLogic.ElectricalDemandFactor相乘，升温按实供/需求比例，降目标/断电保留余热自然换热；无燃料/点火/配方，钨矿2→钨锭1由材料3422℃反应声明。
- 输入容量共用箱子重量/体积策略：Smelter 50kg/50L、BlastFurnace 150kg/150L、InductionStove 300kg/300L，仅限制 Input。模板 Inventory.StorageMaxWeightKg/StorageMaxVolumeCubicMeters（m³）由 MachineInventory.Create/InitData 恢复，库存内部体积为L，原子入库/转移统一判满。
- 点火后逐份吸槽内燃料，fuelAbsorptionSeconds 默认3秒；FuelAbsorptionState 记录槽位/实例/进度，完成才事务扣一份并增加燃值/副产物。取走或换实例清零，满显示容量暂停；后台长步按吸收/燃烧边界分段，面板只读。
- CultivatorLogic 保存每槽作物、水、肥、电力、待收量，断供/满输出保留状态；表现只读真实种植槽、空槽隐藏，不因客户端或休眠显示创建业务逻辑。

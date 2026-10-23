# 加工、炉体与温度

- ProcessCapability/Level 匹配材料 `ItemDefinition.processing` 的结果/work/等级闭区间；专用手钻/锻造仍用 MachineProcessDefinition。RecipeProcessor 原子提交，输出满保留进度，SharedModuleIds 迁移便携载体状态。
- 燃料炉共用 FurnaceLogic，只管燃料、炉温、点火、供热、副产物；燃料资格来自 Combustion.Fuel Tag，燃值由 Mod_Fuel 解析。MaterialThermalProcessor 按材料 transitions/reactions 原子转化，以材料自身 MatterState 温度换热；先已有 Output 后 Input，防止新产物同轮重复加热。
- 点火及持续燃烧经 MachineWorld.CanSustainCombustion，真实世界/舱气氧气占比至少5%；缺氧停火、清吸料进度、保留燃值，恢复后重新点火。助燃温度加成不能绕过门槛，熄火仍传余热。
- 炉内材料、罐体、停火炉体用 ThermalRuntime.AdvanceAmbient：每游戏秒消除剩余温差1%，升降温至少0.5℃/秒，到目标即停，分步/累计跨速率下限一致。炉体先撤自身熄火热源；共享罐按启用成员真实平均罐温仅换热一次，回写内能再走相变。主动升温与双库存换热保留设备规则。
- FurnaceMaterialHeating 共供燃料/电炉；heatingRatePercent 仅缩放材料升温，Smelter 50%、BlastFurnace/Bonfire 100%，不改炉体、干燥/加工计时和自然冷却。Input/Output、容器液体、坩埚材料同倍率；新载荷从真实环境/容器原温起步，活动地表用气候基温避开自身热源，已初始化温度不覆盖。
- InductionStoveLogic：独立3输入2输出，120V/1200W、最高2000℃、材料150%；实供比例只控制炉体主动升温，断电仍按真实余热换热，无燃料/点火/配方。
- 输入容量共用箱子重量/体积策略：Smelter 50kg/50L、BlastFurnace 150kg/150L、InductionStove 300kg/300L，仅限制 Input。模板 Inventory.StorageMaxWeightKg/StorageMaxVolumeCubicMeters（m³）由 MachineInventory.Create/InitData 恢复，库存内部体积为L，原子入库/转移统一判满。
- 点火后逐份吸槽内燃料，fuelAbsorptionSeconds 默认3秒；FuelAbsorptionState 记录槽位/实例/进度，完成才事务扣一份并增加燃值/副产物。取走或换实例清零，满显示容量暂停；后台长步按吸收/燃烧边界分段，面板只读。
- CultivatorLogic 保存每槽作物、水、肥、电力、待收量，断供/满输出保留状态；表现只读真实种植槽、空槽隐藏，不因客户端或休眠显示创建业务逻辑。

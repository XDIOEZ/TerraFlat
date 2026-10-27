# 温度、传热与生存

## 温度场与热源

- 地理 temperature.celsius 固定为纬度底温+局部噪声+一次海拔修正，blendRadius 固定顺序平滑，归一化同源；TryGetGeographicTemperature 不叠季节/天气/全局调温。
- 温室规则读取星球真实 CO₂ 分压（基准气压×CO₂ mol/基准 mol），相对 `AtmosphereState.InitialCarbonDioxidePartialPressureKPa` 算温差；初始气柱随大气创建、克隆及存档保存，不查来源目录。`GameConfig/Fluids/greenhouse-effect.json` 配有效光学量 `1+p/scale` 的对数温差，默认 scale=0.04kPa、每翻倍3℃、钳-30～60℃；初始大气零偏移，抽走其他气体不会因比例提高而增温。
- `PlanetData.ClimateTemperatureOffset` 合并全局基准差与 `AtmosphericGreenhouseTemperatureOffset`，当前环境、历史雪基温及冷世界查询共用，只叠一次；固定地理层不改。大气库存修改必须递增 `Revision`，抽排和飞船来源均使同帧温度缓存失效，地表队列仍保留入队温度快照。
- LocalTemperatureSource 发布目标环境温度（篝火默认40℃），ThermalInfluenceRuntime 权重 max(0,1-d²/R²)²，以当前天气环境为基准选最强升/降温，制冷独立、同类不累加。仅缓存几何距离权重，温度/功率/天气变化不重建；设备负责燃料/供电/撤销。
- 液体用 radiantHeatRadius/radiantTargetTemperature 同模型（岩浆环境目标180℃）；LiquidTemperatureField 仅索引加载液体，按目标温度缓存各格最大权重，依 Terrain/SurfaceLiquidRevision/目录失效。接触岩浆仍用1100℃物质温度/接触伤害，不强制环境到液体温度。
- 单格按格中心算当地源/邻区稀疏液体源，不重建全块权重；雨雪批采仍走全块。液体源只在 Terrain 身份/液体修订变时扫描，重载清源/权重，两路共用最强规则。
- 自然物同批 PointSamplingContext + LiquidTemperatureField.PointSamplingBatch 按目标块收已规范邻区，绑定 WorldRuntime/Epoch/Dimension；每格仍算真距离，下批重读地形身份/液体修订。跨程序集用 BeginAmbientPointSampling()。仅同步只读作用域，不跨帧缓存或中途改地形/源/目录；嵌套独立、退出释放、旧代际拒用。

## 自然传热与体温

- ThermalRuntime.AdvanceAmbient 统一体表、普通物、罐/工业内容、停火炉、落地容器/坩埚：每游戏秒保留99%温差，最低0.5℃/秒（MinimumAmbientTemperatureChangePerSecond），到目标停；跨下限先指数后匀速，分步/累计一致。流体按真实热容换内能，共享罐每轮一次；冷热同速，不读传热率或湿度/火源倍率。AdvanceTowards 仅入水/岩浆接触/主动传热，衣物改安全区。
- Mod_Temperature 持基础体温、来源增温、安全区、缓冲、避险；来源取最强有效值且上限仅限该源，保存基础、UI/伤害读最终；水冷保护多来源相加钳0～1。0.25秒体温步直接调 AdvanceAmbient 提交自然体温，保留增温/事件/疾病顺序。
- 玩家默认安全5～40℃、普通生物5～50℃，Actor可覆写、装备 SetSafeTemperatureRangeModifier。越界且共用缓冲耗尽才挂冻伤/热射病，安全移除，TemperatureMgr 不直接扣血。
- RemainingTemperatureBufferSeconds 冷热共用：危险扣1/秒，安全补1/秒，默认容量60；短暂安全/冷热切换/清Buff不重置，保存剩余量；RestoreOnRespawn 补满并清旧水冷目标。AI 记危险锚点、向外随机引导，普通陆移也保留短期方向偏置，不回退旧安全点。
- TemperatureWarningPresentationRules 共用体温条/TemperatureScreenRendererFeature，读最终体温与装备安全线；条提前3℃渐色（窄区取1/4宽），屏幕越线10℃满，以真实秒平滑、冷热有符号互斥；仅表现，不改缓冲/Buff。

## 氧气、压力与有限大气

- Mod_Oxygen 只管耗氧/缺氧，公开 CanReceiveOxygen/GetOxygenSupplyAmount/SupplyOxygen；Mod_AtmosphericOxygenSupply 每0.2秒按真 O₂/CO₂ 分压、水体阻塞和装备隔离供氧，呼吸阻塞比较实际供给/基础耗氧。
- SpacesuitSupport 查当前插槽气罐，先扣真氧后 Supply；IAmbientOxygenBlocker 报密封，空罐仍隔离。耐压按来源注册，Mod_Pressure 不识装备；低压/高压/缺氧各自伤害时钟，安全/死亡清债务。
- AtmosphereService.CanSustainCombustion 要真氧占比≥5%，真空/未知拒绝；ItemEnvironmentSources 燃烧点允许密闭舱气覆星球气，组分实时读。
- 大气按实际 Scene WorldKey 定星球，各维度共地表有限储量；创建/进世界完整校验，TryGetForWorld 仅读真状态，不逐生物全量校验；GetOrCreate/Validate 保留显式入口。
- 手持大气检测仪 `Mod_AtmosphereAnalyzer` 通过 `AtmosphereMeasurementService` 只读拥有者实际 WorldKey 对应的已有大气；快照按真实 mol 计算比例、标准 m³ 总量和气压，库存修订未变复用快照，不创建来源或读取其他星球。拟物窗口和真实手持生命周期回归为 `environment/atmosphere_measurement、atmosphere_analyzer_lifecycle`。
- 抽气 decimal mol 扣，排气超容量/真空记逸散，无自然回填。FluidThermodynamics 统一内能/两相 mol 温度、压缩/环境传热/有限潜热相变；罐体温独立，组合罐共享库存一次推进。

回归：`environment/greenhouse_temperature`、`environment/thermal_point_batch`、`environment/combustion_oxygen`、`survival/thermal`、`survival/temperature_presentation*`；最终画面由用户验收。

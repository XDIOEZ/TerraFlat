---
name: flatworld-environment
description: "FlatWorld 游戏时间、季节、区域天气、湿度、光照、局部温度、角色生存与环境接触。"
---

# 环境

## 入口

代码根目录 `Assets/5_Scripts/`：

| 目录/文件 | 职责 |
| --- | --- |
| `5-3_GamePlay/World/Time/` | `DayTimeSystem/TimeData/DayNightTimeManager` |
| `5-0_WorldModel/{ChunkWeatherProfile,RegionalWeatherEngine,RegionalWeatherSettings,RegionalWeatherState}.cs` | 纯区块天气 |
| `5-3_GamePlay/World/Environment/` | `WeatherMgr.Regional`、`LightLayerMgr`、`TemperatureMgr.Field`、`LocalTemperatureField/Source` |
| `5-0_WorldModel/{AirHumidityKernel,WorldAirHumidityField}.cs` | 静态/动态供湿 |
| `5-3_GamePlay/World/Chunk/Management/ChunkMgr.AirHumidity.cs` | 湿度桥 |
| `5-3_GamePlay/World/Environment/Contamination/` | 污染定义与修改入口 |
| `5-3_GamePlay/World/Fluids/` | `Atmosphere/AtmosphereService`、`Core/FluidThermodynamics` |

内容：`Assets/StreamingAssets/GameConfig/Time/time-system.json`、`Fluids/fluids.json`、`Contamination/`；天气参数：`Assets/Resources/Config/Weather/regional-weather.json`。Planet/时间状态位于 `World/Map/Data/PlanetData/PlanetTimeData`。

## 时间、天气与湿度

- 星球大气在创建/进入世界时完整校验；`TryGetForWorld` 只读已有真实状态，逐个生物采样不重复分配全量校验集合。显式 `GetOrCreate/Validate` 仍保留校验入口。

- DayTimeSystem 是跨场景/保存主入口，场景也可能引用 DayNightTimeManager。日长为 RotationPeriodSeconds、年长为 OrbitalPeriodSeconds，运行时 SetRotationPeriod/SetOrbitalPeriod；季节、年与轨道共用游戏秒轴。SeasonCalendar 历史快照冻结周期/倾角/离心率，不用当前日长重算过去。
- 活动场景名称按 handle 缓存，时刻/天气/维度实时读；GM 滑条按实际 DayLength 预览，松开由权威端 JumpToTime 一次。主菜单使用无报错 GetInstance 探测。
- 普通太空跨区保留玩家，不触发 WorldEnter；SpaceMgr 使用通用时间初始化/引用接口登记新星区，统一引用 `SpaceScene` 时钟并补采光率，不能把未登记场景的零光照当成太空环境。
- 区块生成时一次汇总固定 airHumidity、最终 precipitation、windX/Y、每格风模长和地理温度；`ChunkTerrainData.WeatherProfile` 冻结每种要素的 Probability/Intensity/DurationSeconds。自然要素为 rain/wind/thunder/fog，配置 Elements 按稳定字符串 ID 扩展；生成器与调度器经 `ChunkWeatherConfiguration` 共用冻结配置。
- 天气按共享区域推进，`RegionSizeInChunks` 默认 8（16 格区块对应 128×128 格），区域首次登记冻结代表区块档案，成员共用要素概率、强度和时限；每区每步只推进/复制一次，地表积分分发到所有已加载成员。环绕世界以 Min 分区，剩余窄边并入末区，雷击按实际区域边界选点。`CheckIntervalSeconds` 默认300游戏秒（5游戏分钟），每轮对全部空闲要素逐项独立抽签，风雨雷雾可同时命中组合，失败不阻止其余要素；运行时不重采气候，查询只读共享状态，普通 Client 只应用权威效果。
- 自然事件起始抽签由 `ResolveActivationProbability` 将冻结基础概率乘统一 `NaturalEventProbabilityMultiplier`（默认 0.3），再乘要素 `ProbabilityBands`；雨、雪、雷、普通风及扩展自然要素都减少至原概率的 30%，8/9/10 级风再乘 0.45/0.25/0.1，相对基础概率最终为 13.5%/7.5%/3%。标准日长1440游戏秒、每300秒检查及现有恢复规则下，同一区域八级自然风平均约7～8个游戏日一次，仅调概率、不设周次数上限，更强风更罕见。配置在 regional-weather.json，默认值同源；事件强度、持续时间与手动天气覆盖保留。
- 每区每种自然要素独立记 `NaturalRecoveryEndTime`；自然事件结束时再次触发概率乘 `RecoveryProbabilityMultiplier`（默认 0.2），随后按 `RecoverySeconds`（默认 300 游戏秒）线性恢复。长事件从计划结束后才恢复，重复触发刷新本要素历史；首次无惩罚，手动天气不产生或清除自然历史，检查每 300 游戏秒。恢复完成时刻随存档保留、回拨平移，过去时刻归零代表已恢复，客户端只消费权威效果。
- 本机雨雪用 `_precipitationTransitionSeconds` 完整 SmoothStep 过渡（WorldManager 默认 12 秒，低强度小雨也走完）；风/云光仍用 `_weatherTransitionSeconds` 零到满 3 秒。跨区块从当前表现续接，雨滴/水花由零按强度增多，雨声线性消费同一强度，停雨让现有粒子落完。程序入口 `SetPrecipitationIntensity(intensity, durationSeconds=60, transitionSeconds=-1)` 只改本块降水，0 停雨、0 过渡秒立即切换、-1 用默认值；普通客户端返回 false。`PresentedRainIntensity/PresentedSnowIntensity` 可读当前表现，权威浇水/积雪仍读真实当地天气。
- 雷束/电弧/屏幕闪光按环绕最短距离读取当前近/远雷声 Cue 的 MinDistance/MaxDistance，共用线性距离衰减；当前 8 格内完整、8～192 格渐弱、192 格处归零，修改雷声距离时雷光自动跟随。主雷束默认从落点向上 24 格，Shader 程序化计算折线/分叉/光晕，不采样整张闪电贴图；雷声仍按原传播延迟播放。
- 固定游戏秒格推进；跳时有界补已有事件，新区块不追补未加载的浇水/积雪，不补播历史雷声。回拨平移各独立时限，复制以 Revision 为主；`PlanetData.RegionalWeatherStates` 保存固定档案和事件状态。云量从雨/雷派生；自然雾使用地理温度和固定空气湿度，湿气消耗和雨冷反馈不参与；云雾仍可限时手动覆盖。
- 自然雾复用通用 `WeatherElementRule` 的 TemperatureWeight/温度区间/ParabolicEventVariation：默认温度 0～40℃ 归一化为 T，与空气湿度 H 等权得 x=(T+H)/2；每 300 游戏秒以 0.3×x 再乘统一自然倍率 0.3 抽一次。三个独立均匀样本取中位数 r，密度为 6r(1-r)，小雾/大雾少、中雾多；实际强度=0.1+0.9xr、时长=60+180xr 游戏秒，温湿越高越易出现强且持久的雾。限时手动雾优先，到期恢复仍在持续的自然雾。
- 静态 precipitation 是按区块水源距离和地理温度计算的长期降水：海洋第一层用于生成河湖，河湖第二层用于最终生态及天气。雨概率/强度/持续时间读 `PrecipitationWeight`，本体 rain=1、thunder=0.7+wind=0.3；固定 airHumidity、动态空气湿度与地表 moisture 保持独立，不再输送临时水汽。
- `TryGetGeographicAirHumidity` 读固定层；`TryGetAirHumiditySupply` 从恢复后的真实 Liquid/深度/距离得 B，背景仅 airHumidity.background；`TryGetAirHumidity` 返回 clamp(B-D)。缺邻区返回不可用，不生成地形，缓存只依 Terrain/LiquidRevision/Epoch。
- 雷电在共享天气区 thunder 事件期间按强度和最短间隔产生新序号，默认最短 20 游戏秒、384 格内跨区块共用该间隔，避免加载块叠加频率；雷事件开始先等待最短间隔，自然雷基础最大概率 0.015，统一乘 0.3 后上限为 0.0045/每 300 游戏秒。近期落雷缓存从存档真值恢复，按环绕最短距离判断；默认时间 1 倍时约为 20 真实秒，加速档 4 倍为约 5 秒。权威端在共享天气区选随机格落点并保存/复制最新落点和强度；`LightningStrikeSystem` 同事件只结算一次，表现/雷声共用落点。首次/读档只建基线，F12/StrikeLightningAt 调试允许立即雷击并共用真实伤害链。自然/手动天气雾均由 `DenseFogRendererFeature` 按玩家逻辑位置采样并合成本地主相机；最终清晰半径为 clearRadius/强度，柔边 opaqueRadius-clearRadius 默认从 10 加倍到 20 格，圈外完全遮挡。`DenseFogPresentationTransition` 从屏幕最远角外开始，以完整 30 真实秒 SmoothStep 收拢/散开；首次入雾和跨区浓度改变从当前雾边续接，不按强度缩短时长。0 强度散尽关闭，玩家/世界/场景变化重建，退出/抑制天气维度清除。
- `WeatherMgr.Audio` 通过 AudioService 播放当地雨/风循环并平滑更新句柄音量；雨声读实时雨雪分流。雷声只订阅新 LightningOccurred，沿用雷光事件落点，按环绕最短距离选近/远音色和传播延迟，以空间 Cue 衰减；LateUpdate 将落点映射到玩家最近副本并经 SetWorldPosition 更新方位，首次推进不补播，退出/抑制天气维度清空待播和句柄。
- 地表每块只接收一个降水积分，不再逐格查询或插值邻区天气；晴天无天气雪/玩家雪直接跳过。筛实际地面/待融雪格后 TrySampleAmbientChunk 同批冻结温度，按当地温度分雨雪，不跨帧借数组。
- 地表积分按原天气步长逐条排队，`MaintainSurfaceWork` 每渲染帧按 2ms 软预算消费完整区块（单块可超预算）；入队冻结温度标量及冷热来源数值并复用不可变权重，出队逐格合成，避免等待期间雨雪相态改变。短导航租约保护待结算数据，解绑前只冲刷该块；同步保存/退出/回拨结清旧账，自动保存最终载荷阶段通过短期天气推进屏障预算排空，再冻结核心与区块字节，取消/异常自动释放。屏障不停止游戏时间、输入或天气表现，释放后按真实时间有界补算；世界/epoch/星球不符只释放旧队列。积压到 MaximumCatchUpSteps 上限时暂缓新步而非集中冲刷，不降低雨量；极端时间跨度仍受原追赶上限约束。
- 土壤水量照常结算；`ChunkEnvironmentRenderer` 按每格最终显隐状态跳过重复 BRG 提交与清除，只在提交成功后缓存可见状态。重新绑定、Owner 表现重建或配置变化后重采权威数据，液体覆盖和抽干仍更新显隐。
- 雨水/融雪由 `SupplySurfaceWaterBatch` 按块复用目录与水肥只读视图，满水先跳过，规范格直接查占地；水量和湿度等层先批写、记录存档及权威差量、再通知，湿地实际显隐变化合并 BRG 上传。`SurfaceWater` 按区块计时，内部 Prepare/Compute/Write/Notify 分别定位准备、计算、写回和通知，不把调用次数变化当成性能验收。

## 地理温度、热源与生存

- 地理 temperature.celsius 在生成时固定：纬度底温 + 局部噪声 + 一次海拔修正，再按 blendRadius 固定顺序平滑，归一化温度同源；不含季节/天气。TryGetGeographicTemperature 也不叠全局调温。
- `TryGetClimateBaseline` 排除季节/天气/局部源，供历史生态/雪；`TryGetAmbientTemperature` 从已加载地理层叠 PlanetData 基准差、允许的天气/季节与局部源，供体温/产量/调试。未加载返回 false，出生格不写回全局温度。
- `LocalTemperatureSource` 发布目标环境温度（默认篝火 40℃）而非固定摄氏增量；`ThermalInfluenceRuntime` 用 `max(0,1-d²/R²)²` 衰减，按当前天气环境温差取最强升温/降温（独立制冷模式），同类热源不累加。区块只缓存每源距离权重，源温、功率与天气变化不重建几何；设备管理燃料/供电/撤销。
- 液体热辐射复用相同目标温度模型，配置 `radiantHeatRadius/radiantTargetTemperature`（岩浆目标环境 180℃）；`LiquidTemperatureField` 只索引已加载地表液体，按液体目标温度分组缓存各格最大权重，依 Terrain/SurfaceLiquidRevision/目录重建。岩浆接触仍走 1100℃物质温度、接触加热与伤害，环境不再直接强制成液体自身温度。
- 单格环境温度按格中心直接累计当地冷热源和邻区稀疏液体热源，不触发整块权重重建；整块雨雪采样仍走批量权重。液体源列表只随 Terrain 身份/SurfaceLiquidRevision 变化扫描，资源重载同时清除源列表和权重缓存；两条路径共用最强影响规则。
- 自然物同次环境冻结用 `TemperatureMgr.PointSamplingContext` 和 `LiquidTemperatureField.PointSamplingBatch`，按目标块一次收集已规范邻区，只读绑定 `WorldRuntime+Epoch+Dimension` 的当前地形；同批每格仍完整算热源距离，下批重读 Terrain 身份与 SurfaceLiquidRevision。作用域是同步只读阶段，期间调用方不得修改地形、热源或目录，不跟踪同批写入、不跨帧缓存结果；嵌套批次独立，退出清批次引用，目录重载/旧代际拒用，`environment/thermal_point_batch` 覆盖刷新与生命周期边界。
- 体温是最外层体表；角色、普通物品、罐体/工业内容、停火炉体、落地容器与坩埚的自然传热共用 `ThermalRuntime.AdvanceAmbient`，每游戏秒保留温差的 99%，速率最低 0.5℃/游戏秒，到达目标即停止；跨下限时先指数后匀速，保证分步与累计一致。默认下限在 `MinimumAmbientTemperatureChangePerSecond`。流体用同公式双精度结果按真实热容换回内能，共享罐库存每轮只推进一次；冷热不分速率，不读物品传热率或潮湿/附近火源倍率。`AdvanceTowards` 仅保留给入水、岩浆接触和主动设备传热，衣物只修安全范围。
- Mod_Temperature 管基础体温、按来源临时增温、安全范围、缓冲与避险。增温取最强有效来源，上限只限制该来源；保存基础副本，伤害/UI 读最终值。水冷保护 0～1、多来源相加钳制。
- 玩家默认安全 5～40℃、普通生物 5～50℃，Actor 可覆盖，装备经 SetSafeTemperatureRangeModifier 修正。越界且共用缓冲耗尽才持有冻伤/热射病 Buff；安全移除，TemperatureMgr 不直接扣血。
- `TemperatureWarningPresentationRules` 供 `FoodUIModule` 体温条与 `TemperatureScreenRendererFeature` 共用，读取最终体温和装备修正后的安全线；条在安全线前 3℃渐蓝/红（窄范围取四分之一宽度），屏幕越线后 10℃到满强，真实秒平滑渐入/退去，冷热有符号强度保证互斥。只改表现，不改危险缓冲/Buff；规则归 `survival/temperature_presentation*`，最终画面由用户验收。
- 冷热共用 RemainingTemperatureBufferSeconds：危险扣 1/s、安全默认补 1/s、容量默认 60；短暂安全/冷热切换/清 Buff 不重置，保存剩余量，RestoreOnRespawn 补满并清旧水冷目标。AI 记危险锚点并向远离方向随机引导，不再记录或回退安全点；普通陆地移动也保留短期方向偏置。
- Mod_Oxygen 只管耗氧/缺氧，公开 CanReceiveOxygen/GetOxygenSupplyAmount/SupplyOxygen；Mod_AtmosphericOxygenSupply 每 0.2s 查询实际 O₂/CO₂ 分压、水体呼吸阻塞和装备隔离后供氧。呼吸阻塞按实际供给与基础耗氧比较。
- SpacesuitSupport 解析当前插槽气罐，先扣真实氧量再供给；IAmbientOxygenBlocker 报密封，空罐仍隔离。耐压按来源注册，Mod_Pressure 不识别装备；低压/高压/缺氧各自伤害时钟，危险解除/死亡清债务。
- 大气按实际 Scene WorldKey 定位星球，各维度共享地表有限储量；抽气 decimal mol 扣量，排气超容量/真空记逸散，无自然回填。物质温度由内能/两相 mol 推导，压缩/环境传热/有限潜热相变由 FluidThermodynamics 统一结算；罐体温度独立，组合罐一次推进共享库存。

## 接触、积雪、污染与光照

- Mod_TileEffectReceiver 的 Ground/Liquid 各有角色运行器；Liquid 读真实深度，深水有体力时 LiquidFloating 只暂停 Ground，浮沉边界一次 Exit/Enter。Ground 身份比较 Terrain 引用/TopTileId，仅重进时创建实例 TileData。
- 天然雪 `snow.depth`、天气雪 `snow.weather.depth`、玩家编辑 `snow.player.edited/depth/season` 独立；天气雪随 SnowCells 保存，暖融后调用土壤补水。
- WorldSnowInteraction 铲整格雪，每层一 Snowball；铺回消耗真实物品加一层。编辑允许超十层，空格保留编辑标记，显示钳制不改厚度/产物；铲铺转为玩家厚度并清天气基线，取走的雪不再融水。
- `ContaminationSystem` 是污染权威写入口，指标存 `flatworld.contamination.<definitionId>`，含服务端权威/差量/通知；脏污度与具体病原负荷是不同指标。
- 全局光由 TimeSystem.prefab 的 DayTimeSystem+Light2D 拥有；FixedLighting 是上限，SuppressWeather 关闭天气。`GetLighting/GetSunLighting` 外层只衰减一次云量，不影响局部灯。
- 太阳/接触阴影透明度读纯 GetSunLighting，时间仅算轨迹；月相基于 TotalDays+CurrentTime/DayLength，月光作夜间下限后叠采光率/维度上限。MoonlightIntensity 与 MoonAppearance 分别表达亮度和出现进度，退出清 Shader 参数。
- LightLayerMgr 按查询直接读取 URP 活跃光源登记表，环绕代理分类与 Transform 用弱引用缓存；没有玩法查询时不再定时扫全场景，点光参数仍逐次实时读取，单次查询共用一份拓扑域。URP 适配失败才按需低频回退扫描；规则 `environment/light_registry、light_sampling` 覆盖登记生命周期、代理排除、动态值、环绕距离与无分配热路径。风玩法按自身格查询，PlanetData.WindStrength/GPU 风只是镜头投影。
- 风区边缘由 `WindTransitionWidth` 控制空间过渡总宽（默认 32 格，边界内外各 16 格）；`TryGetWindBlend` 按连续坐标 SmoothStep 混合最多四区，支持拐角、环绕和客户端副本，缺失区视为无风且不创建状态。风声/设施读混合风向量，移动先按各区原强度换算推速再混合，避免跌过 8 级门槛突然停住。
- `RegionalWindPushField` 每个规范天气区分别平滑三种起吹档位的推动向量，起风、停风、改档和反向都从当前推速用完整 `_weatherTransitionSeconds`（默认 3 秒）SmoothStep 续接；再按四区空间权重混合，跨界不重置时间。秒轴用 `Time.timeAsDouble`，不随游戏日历跳时，暂停冻结；普通客户端消费快照走同一力场，原始天气/设施/声音查询不被改写。每帧共用一批结果，同帧区域变更以 `WindQueryRevision` 补同步；世界/代际/拓扑重建或天气禁用清空，首次强风从零渐入，停风尾段仍保留 HasWindPush。规则 `environment/wind_push_transition` 覆盖连续性。
- 风推动共用 SampleWindVelocity：当前总质量乘天气所属星球重力/9.81 得等效质量；完整 ItemDefinitions 单件最轻 B、最重 A 的中点 C=(A+B)/2 为参考上限，B～C 均分三段，对应 8/9/10 级起吹，等效质量达到 C 不推动。风强向下取完整级别，未达起吹级目标速度为零，达到/高一级/高两级为 0.06/0.2/0.5 单位/s，同档目标不插值；邻风区各自分档并平滑响应后按空间距离混合。目录质量范围按 ItemDefinitionRevision/引用缓存；所有移动后端共用，抛掷未结束或完全沉没的散物不受风，不能以镜头区判断全世界。
- 大批散落物风推动使用 `WorldMotionSystem.TryCreateWindSamplingContext` 每批冻结一次目录质量范围和星球重力；每件仍按实际质量与当地四区风场采样。单件 `SampleWindVelocity` 与批量共用区域推动场，稳态目标和空间混合仍由 `WindEnvironmentRules` 统一裁定。
- 风采样直接读取区域风字段，与完整天气快照共用 `BuildWind`；风源同步只读风字段，不组装雨雷快照。每批风推动冻结活动拓扑域与 ChunkMgr，逐小步阻挡和移动后的环境检测仍实时执行。
- 单机散落物水态走 DroppedItemRuntime.Water；陆地静物订所在地格变化，未知格限量重试，水中低频查流。水态替换先备载荷/共享视觉再提交整组数量，水花归实际世界 Scene；浮力阈值与视觉浸没分开。

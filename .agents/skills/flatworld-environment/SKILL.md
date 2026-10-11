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

- DayTimeSystem 是跨场景/保存主入口，场景也可能引用 DayNightTimeManager。日长为 RotationPeriodSeconds、年长为 OrbitalPeriodSeconds，运行时 SetRotationPeriod/SetOrbitalPeriod；季节、年与轨道共用游戏秒轴。SeasonCalendar 历史快照冻结周期/倾角/离心率，不用当前日长重算过去。
- 活动场景名称按 handle 缓存，时刻/天气/维度实时读；GM 滑条按实际 DayLength 预览，松开由权威端 JumpToTime 一次。主菜单使用无报错 GetInstance 探测。
- 区块生成时一次汇总固定 airHumidity、windX/Y、每格风模长和地理温度；`ChunkTerrainData.WeatherProfile` 冻结每种要素的 Probability/Intensity/DurationSeconds。自然要素为 rain/wind/thunder，配置 Elements 按稳定字符串 ID 扩展；生成器与调度器经 `ChunkWeatherConfiguration` 共用冻结配置。
- 天气按真实区块原点独立推进，`CheckIntervalSeconds` 表示空闲要素每次概率检查的间隔，持续中的要素不重复抽取。运行时不再采五点气候、温差或动态供湿；查询只读本块，普通 Client 只应用权威效果。
- 本机雨雪、风与云光共用 `WeatherMgr._weatherTransitionSeconds`（WorldManager 默认零到满强度 3 秒）；跨区块从当前表现续接，雨风音消费过渡强度，停雨仅停发射并让粒子落完。权威天气、浇水/积雪和设施风力仍读真实当地状态；退出世界立即清理。
- 雷束/电弧/屏幕闪光共用环绕最短距离强度，默认 8 格内完整、8～128 格 SmoothStep 渐弱；雷声仍按声音服务的空间衰减与原传播延迟播放。
- 固定游戏秒格推进；跳时有界补已有事件，新区块不追补未加载的浇水/积雪，不补播历史雷声。回拨平移各独立时限，复制以 Revision 为主；`PlanetData.RegionalWeatherStates` 保存固定档案和事件状态。云量从雨/雷派生，自然雾、湿气消耗和雨冷反馈已移除；云雾仍可限时手动覆盖。
- 静态 precipitation 是长期风携水汽降水，影响径流/生态；输送 humidity、固定 airHumidity、动态空气湿度与地表 moisture 分开。
- `TryGetGeographicAirHumidity` 读固定层；`TryGetAirHumiditySupply` 从恢复后的真实 Liquid/深度/距离得 B，背景仅 airHumidity.background；`TryGetAirHumidity` 返回 clamp(B-D)。缺邻区返回不可用，不生成地形，缓存只依 Terrain/LiquidRevision/Epoch。
- 雷电在本块 thunder 事件期间按强度和最短间隔产生新序号，权威端选本块随机格落点并保存/复制最新落点和强度；`LightningStrikeSystem` 同事件仅结算一次线性衰减环境伤害，`LightningEffectController` 订阅同落点绘制闪电/落点电弧/屏幕闪光。首次/读档只建基线，F12 或 StrikeLightningAt 调试共用真实伤害链。手动雾仍由 `DenseFogRendererFeature` 在本地主相机最终画面合成 owned 玩家遮罩，进房恢复、退出/抑制天气维度清除。
- `WeatherMgr.Audio` 通过 AudioService 播放当地雨/风循环并平滑更新句柄音量；雨声读实时雨雪分流。雷声只订阅新 LightningOccurred，沿用雷光事件落点，按环绕最短距离选近/远音色和传播延迟，以空间 Cue 衰减；LateUpdate 将落点映射到玩家最近副本并经 SetWorldPosition 更新方位，首次推进不补播，退出/抑制天气维度清空待播和句柄。
- 地表每块只接收一个降水积分，不再逐格查询或插值邻区天气；晴天无天气雪/玩家雪直接跳过。筛实际地面/待融雪格后 TrySampleAmbientChunk 同批冻结温度，按当地温度分雨雪，不跨帧借数组。

## 地理温度、热源与生存

- 地理 temperature.celsius 在生成时固定：纬度底温 + 局部噪声 + 一次海拔修正，再按 blendRadius 固定顺序平滑，归一化温度同源；不含季节/天气。TryGetGeographicTemperature 也不叠全局调温。
- `TryGetClimateBaseline` 排除季节/天气/局部源，供历史生态/雪；`TryGetAmbientTemperature` 从已加载地理层叠 PlanetData 基准差、允许的天气/季节与局部源，供体温/产量/调试。未加载返回 false，出生格不写回全局温度。
- `LocalTemperatureSource` 是中心摄氏增量而非功率/目标温度，按来源注册/撤销，无热惯性；分区缓存依源快照失效，环绕键/距离规范化。来源设备拥有燃料/供电/保存，偏移不写生成层。
- 液体辐射来自 `radiantHeatRadius/radiantHeatOffset`；RadiantLiquidTemperatureField 只查加载块，以 Terrain/SurfaceLiquidRevision 缓存逐格最大贡献，液体/支撑面/目录改变重算，雪土光变化不重扫。
- 体温是最外层体表；环境趋近速度为目标绝对温度 1%/s，整数四舍五入、最低 1℃/s。各趋近过程统一 ThermalRuntime.AdvanceTowards；入水有独立过渡速度，衣物只修安全范围。
- Mod_Temperature 管基础体温、按来源临时增温、安全范围、缓冲与避险。增温取最强有效来源，上限只限制该来源；保存基础副本，伤害/UI 读最终值。水冷保护 0～1、多来源相加钳制。
- 玩家默认安全 5～40℃、普通生物 5～50℃，Actor 可覆盖，装备经 SetSafeTemperatureRangeModifier 修正。越界且共用缓冲耗尽才持有冻伤/热射病 Buff；安全移除，TemperatureMgr 不直接扣血。
- 冷热共用 RemainingTemperatureBufferSeconds：危险扣 1/s、安全默认补 1/s、容量默认 60；短暂安全/冷热切换/清 Buff 不重置，保存剩余量，RestoreOnRespawn 补满并清旧水冷目标。AI 记最近四个体温/环境均安全位置，避险取最近并消费记录。
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
- LightLayerMgr 热路径只读缓存 Light2D 成员并实时采强度/位置；成员集合低频刷新。风玩法按自身格查询，PlanetData.WindStrength/GPU 风只是镜头投影。
- 单机散落物水态走 DroppedItemRuntime.Water；陆地静物订所在地格变化，未知格限量重试，水中低频查流。水态替换先备载荷/共享视觉再提交整组数量，水花归实际世界 Scene；浮力阈值与视觉浸没分开。

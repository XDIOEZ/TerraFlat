---
name: flatworld-environment
description: "Use when: 定位或修改 FlatWorld 的世界时间、昼夜、天数、季节、光照层、天气、雨效、环境温度、角色体温或相关 Resources。关键词：DayTimeSystem、DayNightTimeManager、WeatherMgr、TemperatureMgr。"
---

# FlatWorld 环境

## 入口

- 时间：`Assets/5_Scripts/5-3_GamePlay/World/Time/{DayTimeSystem,TimeData,DayNightTimeManager}.cs`
- 天气与风力：`World/Environment/{WeatherMgr,WeatherMgr.Wind,WeatherEventScheduler,RainEffectController,RainGroundSplashController}.cs`
- 光照/温度：`World/Environment/{LightLayerMgr,TemperatureMgr}.cs`
- 逐格温度入口：`TemperatureMgr.Field.cs`；冷热源空间缓存与设备组件：`LocalTemperatureField.cs`、`LocalTemperatureSource.cs`。
- 存档：`World/Map/Data/{PlanetData,PlanetTimeData}.cs`

## 不变量

- 当前跨场景时间与存档主入口是 `DayTimeSystem`；季节改动前确认场景是否使用 `DayNightTimeManager`。
- 天气权威状态保存在 `PlanetData`；阶段边界使用绝对世界时间，跳时交给 Scheduler 跨越全部边界。
- `PlanetData.WindStrength` 是独立于降雨强度的星球级权威状态；修改必须经 `WeatherMgr.SetWindStrength` 发布天气快照，Client 只应用复制值，离开世界或 `SuppressWeather` 维度时清零 Shader 全局表现但不改存档值。
- 静态降水层影响地形/生态，不等于动态天气强度。
- 普通 Client 不调度天气或体温伤害，只应用服务器状态。
- 角色体温和调试温度层必须共用 `TemperatureMgr.TryGetAmbientTemperature`：读取已加载 `ChunkTerrainData` 的 `temperature.celsius`，叠加星球基准相对 `PlanetData.DefaultGlobalTemperature` 的差值、当前维度允许的天气修正和局部源；未加载返回 false，禁止为查询触发生成或复制整层数组。角色初始化只能更新自身 `AmbientTemperature`，不能把某个出生格温度写回星球全局值。
- 群系基础气温在 `DeterministicChunkGenerator.GenerateSurfaceCell` 完成群系分类后写入 `temperature.celsius`；不要为调整摄氏度改写归一化的 `temperature`，后者仍参与群系判定与生态分布。规则变化需递增纯生成器与地表 Profile 的生成签名，保持噪声布局版本不变。
- 局部冷热源是可重建的影响层，来源模块负责燃料/供电/保存并在停用、回池时撤销注册；不能把临时偏移写回生成气候，否则卸载后无法恢复并会污染地图差量。修改源快照只使覆盖分区失效，查询缓存不扫描全部来源；环形边界同时归一化分区键并使用最短距离，避免世界接缝出现断层或重复贡献。
- 设备组件 `LocalTemperatureSource` 的强度表示中心摄氏度增量（负值制冷），不是功率或绝对目标温度；恒温器应由设备控制器根据当前地块温度计算有效强度。当前影响层不保存热惯性，撤销源会立即撤销其环境增量；需要蓄热/热传导时应引入独立状态层，不能悄悄改变来源参数语义。
- 维度 `FixedLighting` 是光照上限；SuppressWeather 会关闭天气与雨效。
- 运行时全局光由 `TimeSystem.prefab` 中的 `DayTimeSystem + Light2D` 持有；`GameStartScene` 不得再注入独立 `DayTimeSystem`，否则会抢占单例并使带光源的运行时 Prefab 被销毁。
- 月相应基于 `TimeData.TotalDays + CurrentTime / DayLength` 计算，不能只使用日内时间；月光先作为昼夜曲线的夜间下限，再经过采光率与维度固定光照上限。
- 向 Shader 发布月光表现值时应保留 `GetLighting` 已应用的场景采光率与维度上限，并在系统禁用或退出世界时清零全局参数，避免关闭域重载后残留上一局状态。`_GlobalMoonlightIntensity` 只表达月相/场景后的最终亮度，黄昏到夜晚的出现进度由独立 `_GlobalMoonAppearance` 发布，避免把月相强度误当成尺寸动画进度。
- `LightLayerMgr.TryGetLightLevel` 属于怪物生成等高频查询热路径，只能读取已缓存的 Light2D 成员并实时采样其强度/位置；禁止在单次格子查询里调用 `FindObjectsOfType/FindObjectsByType`，光源成员集合统一由低频刷新维护。
- 新世界时间参数来自 `GameConfig/Time/time-system.json` 的 Profile；Profile ID、限时边界与日历随 `TimeData` 存档，只读取当前外层版本，不以缺失字段回退默认配置兼容旧档。
- 入水瞬时降温由 `Mod_Temperature` 自己维护平滑目标；装备等外部系统只能通过水体降温保护通道影响速度，禁止直接改河流过渡时间。保护值 0 表示无保护、1 表示完全阻止入水降温，多来源按加法叠加并由体温模块统一限制。
- 伤害语义联动 `flatworld-combat`，维度覆盖联动 `flatworld-dimension`，雨视觉联动 Effects Skill。

- 季节日历只从 `SeasonCalendar` 取快照；调整四季长度保留年、季、进度和绝对时钟，并记录 `SeasonHistory`。植物与积雪的历史补算使用 `SampleHistorical`，不能拿新季长重算过去的温害。
- `TemperatureMgr.TryGetClimateBaseline` 不含季节、动态天气和局部源；历史环境重建与积雪采样用它，角色体温仍用最终环境温度入口，避免重复叠加季节。
- 积雪是 `PlanetData.SeasonalSnow` 的独立基温分段状态，不是格子地形差量；`WeatherMgr.Snow` 在天气阶段边界与日内分段推进覆盖量，雪停保留覆盖，暖时融化。禁用天气的维度不修改星球覆雪状态。

## 验证

- 使用确定时间、种子与天气输入，验证跨阶段、保存恢复、Host/Client 权威和资源启停；不要靠真实等待。
- 默认不主动跑测试；需要时运行 `Environment.Smoke`。测试入口：`Assets/GameTest/Environment/EnvironmentSmokeTests.cs`。
- 真实世界链可用 Golden Path `environment.time-weather`，清理时恢复原环境。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

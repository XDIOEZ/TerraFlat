---
name: flatworld-environment
description: "Use when: 定位或修改 FlatWorld 的世界时间、昼夜、天数、季节、光照层、天气、雨效、环境温度、角色体温或相关 Resources。关键词：DayTimeSystem、DayNightTimeManager、WeatherMgr、TemperatureMgr。"
---

# FlatWorld 环境

## 入口

- 时间：`Assets/5_Scripts/5-3_GamePlay/World/Time/{DayTimeSystem,TimeData,DayNightTimeManager}.cs`
- 天气与风力：`World/Environment/{WeatherMgr,WeatherMgr.Wind,WeatherEventScheduler,RainEffectController,RainGroundSplashController}.cs`
- 光照/温度：`World/Environment/{LightLayerMgr,TemperatureMgr}.cs`
- 存档：`World/Map/Data/{PlanetData,PlanetTimeData}.cs`

## 不变量

- 当前跨场景时间与存档主入口是 `DayTimeSystem`；季节改动前确认场景是否使用 `DayNightTimeManager`。
- 天气权威状态保存在 `PlanetData`；阶段边界使用绝对世界时间，跳时交给 Scheduler 跨越全部边界。
- `PlanetData.WindStrength` 是独立于降雨强度的星球级权威状态；修改必须经 `WeatherMgr.SetWindStrength` 发布天气快照，Client 只应用复制值，离开世界或 `SuppressWeather` 维度时清零 Shader 全局表现但不改存档值。
- 静态降水层影响地形/生态，不等于动态天气强度。
- 普通 Client 不调度天气或体温伤害，只应用服务器状态。
- 维度 `FixedLighting` 是光照上限；SuppressWeather 会关闭天气与雨效。
- 运行时全局光由 `TimeSystem.prefab` 中的 `DayTimeSystem + Light2D` 持有；`GameStartScene` 不得再注入独立 `DayTimeSystem`，否则会抢占单例并使带光源的运行时 Prefab 被销毁。
- 月相应基于 `TimeData.TotalDays + CurrentTime / DayLength` 计算，不能只使用日内时间；月光先作为昼夜曲线的夜间下限，再经过采光率与维度固定光照上限。
- 向 Shader 发布月光表现值时应保留 `GetLighting` 已应用的场景采光率与维度上限，并在系统禁用或退出世界时清零全局参数，避免关闭域重载后残留上一局状态。
- 新世界时间参数来自 `GameConfig/Time/time-system.json` 的 Profile；Profile ID 与限时边界随 `TimeData` 存档，旧存档缺失时回退默认时间系统。
- 伤害语义联动 `flatworld-combat`，维度覆盖联动 `flatworld-dimension`，雨视觉联动 Effects Skill。

## 验证

- 使用确定时间、种子与天气输入，验证跨阶段、保存恢复、Host/Client 权威和资源启停；不要靠真实等待。
- 默认不主动跑测试；需要时运行 `Environment.Smoke`。测试入口：`Assets/GameTest/Environment/EnvironmentSmokeTests.cs`。
- 真实世界链可用 Golden Path `environment.time-weather`，清理时恢复原环境。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

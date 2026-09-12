# 环境系统

## 系统定位

负责时间、昼夜、季节、天气、风、光照、环境温度、局部冷热源、积雪和污染等星球/地块环境状态。

## 当前机制

- 跨场景时间和正式存档入口是 `DayTimeSystem`。
- 天气权威状态保存在 PlanetData；阶段边界使用绝对世界时间，跳时由 Scheduler 跨越所有必要阶段。
- 风力是独立于降雨强度的星球级状态。
- 环境温度统一通过 `TemperatureMgr.TryGetAmbientTemperature` 查询当前已加载地块，再叠加星球基准、天气与局部源。
- 局部冷热源是可重建影响层，不写回地形生成气候。
- 积雪是 PlanetData 中的独立季节状态，不等于地形 Tile 差量。
- 污染通过 ContaminationDefinition + ChunkTerrainData 环境层保存，不允许业务脚本直接绕过 ContaminationSystem 写值。

## 温度链

`生成气候基线 → 地块 temperature.celsius → 星球/季节/天气修正 → 局部冷热源 → 当前环境温度 → 玩家/植物/资源系统消费`

角色体温与环境温度是两个不同状态：HUD 显示环境温度时读取玩家所在地块环境值，不使用角色体温替代。

## 维度覆盖

- 维度可设置固定光照上限。
- `SuppressWeather` 维度关闭天气和雨效表现，但不能因此破坏地表星球持久化天气状态。

## 关键入口

- `World/Time/DayTimeSystem.cs`
- `World/Environment/WeatherMgr*.cs`
- `TemperatureMgr.cs` / `TemperatureMgr.Field.cs`
- `LocalTemperatureField.cs`
- `World/Environment/Contamination/`

## 修改时联动

- 玩家体温：生存系统。
- 植物成长：农业系统。
- 地形/群系温度：地图生成。
- 洞穴天气/光照：维度系统。

## 对应 Skill

`.agents/skills/flatworld-environment/SKILL.md`

---
name: flatworld-environment
description: "Use when: 定位或修改 FlatWorld 的世界时间、昼夜、天数、季节、光照层、天气、雨效、环境温度、角色体温或相关 Resources。关键词：DayTimeSystem、DayNightTimeManager、WeatherMgr、TemperatureMgr。"
argument-hint: "时间、光照、天气或温度问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 时间、天气与温度定位

> 最后核对：2026-07-27。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Time/DayTimeSystem.cs`：当前多场景时间、天数、全局光照和存档同步入口。
2. `Assets/5_Scripts/5-3_GamePlay/Time/TimeData.cs`：时间运行数据。
3. `Assets/5_Scripts/5-3_GamePlay/Manager/WeatherMgr.cs`：天气状态、天气温度修正和雨效入口。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/TemperatureMgr.cs`：环境温度与角色温度伤害计算。

## 时间与光照

- 世界时间：`Assets/5_Scripts/5-3_GamePlay/Time/DayTimeSystem.cs`。
- 时间存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetTimeData.cs`。
- 地块光照层：`Assets/5_Scripts/5-3_GamePlay/Manager/LightLayerMgr.cs`。
- 季节/特殊日并行实现：`Assets/5_Scripts/5-3_GamePlay/Time/DayNightTimeManager.cs`。
- 空主体旧入口：`Assets/5_Scripts/5-3_GamePlay/Time/GameTimeManager.cs`，不要作为主时间系统。

## 天气与温度

- 星球天气数据：`Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetData.cs`。
- 雨效控制：`Assets/5_Scripts/5-3_GamePlay/Manager/RainEffectController.cs`。
- 角色温度模块：`Assets/5_Scripts/5-3_GamePlay/Item/Mod_Temperature.cs`。
- 天气资源：`Assets/Resources/Weather/`。
- 雨效 Prefab：`Assets/Resources/Weather/RainEffect.prefab`。

## 运行链

```text
GameManager.Event_GameWorldEnter
→ DayTimeSystem 推进场景时间并更新 Light2D / LightLayerMgr
→ WeatherMgr 从 Active PlanetData 读取天气
→ TemperatureMgr 组合基础温度 + 天气修正
→ Mod_Temperature 更新角色体温并结算极端温度伤害
```

## 易误判点

- `DayTimeSystem` 与 `DayNightTimeManager` 都存在；当前跨场景时间与存档主入口优先看前者，修改季节功能前确认场景实际挂载。
- 天气状态保存在 `PlanetData`，不是 `WeatherMgr` 自身字段。
- 雨效通过 `Resources/Weather/RainEffect` 加载；移动 Prefab 后必须同步修改常量与本 Skill。

## 近期变更

- 2026-07-27：当前环境链明确为 `DayTimeSystem → LightLayerMgr` 与 `PlanetData → WeatherMgr/TemperatureMgr`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Environment/EnvironmentSmokeTests.cs`；当前基础覆盖时间、天气、温度与雨效 Resources 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；环境测试约定目录：`Assets/GameTest/Environment/`；场景目录：`Assets/GameTest/Scenes/Environment/`；冒烟分类：`Environment.Smoke`。
- 新增时间、昼夜、季节、天气、光照或温度行为时必须增加系统测试；修复 Bug 时先增加回归测试。时间推进到环境反馈主流程变化时同步更新环境冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；时间与天气测试必须注入确定值，不能依赖真实等待或随机天气。
- 完成修改后检查 Unity 编译和 Console，再运行 `Environment.Smoke`；涉及地图、角色状态、特效或存档时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变时间入口、场景时间引用、季节实现、光照同步、天气字段、雨效/材质位置、温度阈值或资源加载路径后，必须更新本 Skill；涉及伤害时同步更新 Combat Skill。

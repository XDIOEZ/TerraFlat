---
name: flatworld-environment
description: "Use when: 定位或修改 FlatWorld 的世界时间、昼夜、天数、季节、光照层、天气、雨效、环境温度、角色体温或相关 Resources。关键词：DayTimeSystem、DayNightTimeManager、WeatherMgr、TemperatureMgr。"
argument-hint: "时间、光照、天气或温度问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 时间、天气与温度定位

> 最后核对：2026-07-31。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs`：当前多场景时间、天数、全局光照和存档同步入口。
2. `Assets/5_Scripts/5-3_GamePlay/World/Time/TimeData.cs`：时间运行数据。
3. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/WeatherMgr.cs`：天气状态、天气温度修正和雨效入口。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/WeatherEventScheduler.cs`：纯数据、确定性、绝对时间边界的降雨事件状态机。
5. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/TemperatureMgr.cs`：环境温度与角色温度伤害计算。

## 时间与光照

- 世界时间：`Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs`。
- 时间存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/PlanetTimeData.cs`。
- 地块光照层：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/LightLayerMgr.cs`。
- 季节/特殊日并行实现：`Assets/5_Scripts/5-3_GamePlay/World/Time/DayNightTimeManager.cs`。
- 空主体旧入口：`Assets/5_Scripts/5-3_GamePlay/World/Time/GameTimeManager.cs`，不要作为主时间系统。
- `DayTimeSystem.AdvanceTime()` 统一结算跨日并发布 `TimeAdvanced` / `DayChanged`；`TryGetResolvedTimeData()` 解析场景时间引用并限制循环深度。
- `DayTimeSystem.TimeRun()` 在场景 `TimeScaleModifier` 基础上乘 `GameDifficultyService.Current.World.TimeSpeedMultiplier`；0% 可冻结自然昼夜推进，但显式调用 `AdvanceTime()` 的跳时逻辑不受影响。
- `SerializableTimeData` 必须往返保存 `TotalDays`，生成周期等跨日系统依赖 `TimeData.GetTotalGameTime()`。

## 天气与温度

- 星球天气数据：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/PlanetData.cs`。
- 地表静态降水层：`EnvironmentLayers.Precipitation` 由 `ChunkGenerator_Land` 根据最终高度图生成；它影响群系、资源和自然作物环境，不等同于 `WeatherMgr` 的动态降雨事件强度。
- 雨效控制：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/RainEffectController.cs`。
- 权威天气事件接线：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/WeatherMgr.AuthoritativeEvent.cs`。
- 角色温度模块：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Mod_Temperature.cs`。
- 玩家雨中暴露与火源恢复：`Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/WeatherExposureSpeechProvider.cs`。
- 天气资源：`Assets/Resources/Weather/`。
- 雨效 Prefab：`Assets/Resources/Weather/RainEffect.prefab`。

## 运行链

```text
GameManager.Event_GameWorldEnter
→ DayTimeSystem 推进场景时间并更新 Light2D / LightLayerMgr
→ WeatherMgr 在权威端消费 TimeAdvanced
→ WeatherEventScheduler 按绝对时间跨越 Forecast / RainStarting / RainSteady / RainHeavy / RainEnding / Recovery
→ PlanetData 保存天气、强度、阶段、边界、随机游标与事件序号
→ TemperatureMgr 组合基础温度 + 天气修正
→ WeatherExposureSpeechProvider 追加雨中暴露或火源恢复修正
→ Mod_Temperature 仅在离线或 Host/Server 更新体温并结算极端温度伤害
```

## 易误判点

- `DayTimeSystem` 与 `DayNightTimeManager` 都存在；当前跨场景时间与存档主入口优先看前者，修改季节功能前确认场景实际挂载。
- 天气状态保存在 `PlanetData`，不是 `WeatherMgr` 自身字段。
- 天气阶段边界保存绝对世界时间；禁止保存每帧递减计时器。跳时统一交给 `WeatherEventScheduler.Advance()` 循环跨越全部边界。
- 普通联机 Client 不运行天气或体温伤害调度，只应用服务器广播状态；网络入口见 Networking Skill。
- 雨效通过 `Resources/Weather/RainEffect` 加载；移动 Prefab 后必须同步修改常量与本 Skill。
- 雨效 Prefab 与控制器只在首次需要时加载；状态未变化时每帧只允许跟随相机，禁止重复 `Resources.Load`、`Instantiate` 或重新播放雨声。
- 温度、饥饿、流血等最终进入 `DamageReceiver.ForceHurt()` 的玩家伤害统一受 `EnvironmentalDamageMultiplier` 影响；不要在各环境发送端重复乘算。
- 维度环境覆盖从当前 `DimensionDefinition` 读取：`DayTimeSystem.GetLighting()` 可返回固定光照，`WeatherMgr` 可关闭天气与雨效；新增差异时扩展维度定义，不要在环境管理器硬编码 `cave`。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-04：地表静态降水取消独立噪声，改由最终高度图平滑反向映射；动态天气事件、雨效及权威端调度契约保持不变。
- 2026-07-31：环境链接入维度覆盖；地下矿洞使用固定 `0.08` 光照并抑制天气/雨效，地表行为保持不变。
- 2026-07-31：`WeatherMgr` 不再于开始菜单阶段提前解析 `DayTimeSystem`；仅在游戏世界激活后订阅时间推进事件，避免将正常的未进入世界状态误报为场景缺少时间系统。
- 2026-07-30：新增首个权威降雨事件闭环：预兆、起雨、稳定、增强、减弱、恢复；阶段使用绝对总时间、固定种子与持久化随机游标，时间跳跃可一次跨越多个阶段。
- 2026-07-30：雨中暴露降低玩家有效环境温度并加快降温，附近已点燃 `Mod_Fuel` 火源提供恢复；普通 Client 不重复结算体温伤害。
- 2026-07-30：动态天气接入权威农作物成长；雨量补水和天气成长倍率只在 `Mod_Grow` 单点结算，`Tile_Farmland.OnUpdate()` 不再二次修改成长进度或水分。
- 2026-07-29：自定义难度接入自然昼夜流逝倍率与玩家环境伤害倍率，时间发送端和伤害接收端各保持单一结算点。
- 2026-07-29：修复 `TotalDays` 未进入时间存档；增加统一时间推进、跨日事件和引用场景解析入口。
- 2026-07-27：当前环境链明确为 `DayTimeSystem → LightLayerMgr` 与 `PlanetData → WeatherMgr/TemperatureMgr`。

## 修改后验证

- 基础测试脚本：`Assets/GameTest/Environment/EnvironmentSmokeTests.cs`；当前覆盖时间、天气、温度、雨效 Resources、`TotalDays` 往返、固定种子确定性天气和跨阶段跳时不重复结算。
- 地表静态降水的高度映射与 MapCore 无独立降水噪声契约由 `Assets/GameTest/Map/MapSmokeTests.cs`（`Map.Smoke`）覆盖。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；环境测试约定目录：`Assets/GameTest/Environment/`；场景目录：`Assets/GameTest/Scenes/Environment/`；冒烟分类：`Environment.Smoke`。
- 新增时间、昼夜、季节、天气、光照或温度行为时必须增加系统测试；修复 Bug 时先增加回归测试。时间推进到环境反馈主流程变化时同步更新环境冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；时间与天气测试必须注入确定值，不能依赖真实等待或随机天气。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Environment.Smoke`；脚本不会产生 Unity 测试交互卡片，也无需视觉模型。涉及温度或天气细节时追加对应分类；只有光照、雨效等最终观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。
- 环境系统的专项行为位于 `Assets/GameTest/Environment/EnvironmentSmokeTests.cs`；维度固定光照与禁天气配置不再属于精简 Smoke 集合。

## 修改后维护本 Skill

改变时间入口、场景时间引用、季节实现、光照同步、天气字段、雨效/材质位置、温度阈值或资源加载路径后，必须更新本 Skill；涉及伤害时同步更新 Combat Skill。

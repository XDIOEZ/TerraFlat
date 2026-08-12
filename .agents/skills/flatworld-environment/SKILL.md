---
name: flatworld-environment
description: "Use when: 定位或修改 FlatWorld 的世界时间、昼夜、天数、季节、光照层、天气、雨效、环境温度、角色体温或相关 Resources。关键词：DayTimeSystem、DayNightTimeManager、WeatherMgr、TemperatureMgr。"
---

# FlatWorld 时间、天气与温度定位

> 最后核对：2026-08-09。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs`：当前多场景时间、天数、全局光照和存档同步入口。
2. `Assets/5_Scripts/5-3_GamePlay/World/Time/TimeData.cs`：时间运行数据。
3. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/WeatherMgr.cs`：天气状态、天气温度修正和雨效入口。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/WeatherEventScheduler.cs`：纯数据、确定性、绝对时间边界的降雨事件状态机。

## 时间与光照
- 世界时间：`Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs`。
- 时间存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/PlanetTimeData.cs`。
- 地块光照层：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/LightLayerMgr.cs`。
- 季节/特殊日并行实现：`Assets/5_Scripts/5-3_GamePlay/World/Time/DayNightTimeManager.cs`。

## 天气与温度
- 星球天气数据：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/PlanetData.cs`。
- 地表静态降水层：`EnvironmentLayers.Precipitation` 由 `ChunkGenerator_Land` 根据最终高度图生成；它影响群系、资源和自然作物环境，不等同于 `WeatherMgr` 的动态降雨事件强度。
- 雨效控制：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/RainEffectController.cs`。
- 雨滴落地水花：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/RainGroundSplashController.cs`；由 `WeatherMgr` 独立按需加载 `Resources/Weather/RainGroundSplash`，只同步启停与强度；原雨层定位仍由 `RainEffectController` 负责。

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

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：维度 `FixedLighting` 改作光照上限而非恒定值；引用地表时间的矿洞使用 `min(地表昼夜亮度, FixedLighting)`，光照颜色也解析引用时间，因此白天维持洞穴上限、夜晚继续随太阳变暗。
- 2026-08-09：动态雨效由 `RainEffectController` 独占相机顶部定位，`WeatherMgr` 不再覆盖其 Transform；`RainEffect` 保留单边顶部发射，生命周期按正交相机高度、初始下落速度和 `1.12` 倍余量动态补足到下边缘。独立地面水花层按天气强度优先采样已加载非水非阻挡地形，区块未就绪时在画面内降级发射；小雨/暴雨发射频率分别为 `12/s` / `48/s`，确保屏幕内持续有足够落地反馈。
- 2026-08-04：地表静态降水取消独立噪声，改由最终高度图平滑反向映射；动态天气事件、雨效及权威端调度契约保持不变。
- 2026-07-31：环境链接入维度覆盖；地下矿洞使用固定 `0.08` 光照并抑制天气/雨效，地表行为保持不变。
- 2026-07-31：`WeatherMgr` 不再于开始菜单阶段提前解析 `DayTimeSystem`；仅在游戏世界激活后订阅时间推进事件，避免将正常的未进入世界状态误报为场景缺少时间系统。

## 修改后验证
- 基础测试脚本：`Assets/GameTest/Environment/EnvironmentSmokeTests.cs`；当前覆盖时间、天气、温度、雨效 Resources、`TotalDays` 往返、固定种子确定性天气和跨阶段跳时不重复结算。
- 真实单机环境入口由 Golden Path 操作 `environment.time-weather` 覆盖：推进当前动态场景时间、切换确定强度降雨并在 `finally` 恢复原时间、天气与强度；水体效果和地表生态仍由 `environment.tile-effects/environment.ecology` 覆盖。
- 地表静态降水的高度映射与 MapCore 无独立降水噪声契约由 `Assets/GameTest/Map/MapSmokeTests.cs`（`Map.Smoke`）覆盖。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；环境测试约定目录：`Assets/GameTest/Environment/`；场景目录：`Assets/GameTest/Scenes/Environment/`；冒烟分类：`Environment.Smoke`。
- 新增时间、昼夜、季节、天气、光照或温度行为时必须增加系统测试；修复 Bug 时先增加回归测试。时间推进到环境反馈主流程变化时同步更新环境冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；时间与天气测试必须注入确定值，不能依赖真实等待或随机天气。

## 修改后维护本 Skill
改变时间入口、场景时间引用、季节实现、光照同步、天气字段、雨效/材质位置、温度阈值或资源加载路径后，必须更新本 Skill；涉及伤害时同步更新 Combat Skill。

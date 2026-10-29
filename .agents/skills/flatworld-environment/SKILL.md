---
name: flatworld-environment
description: "FlatWorld 时间、区域天气、温湿度、光风、角色生存与环境接触。"
---

# 环境

## 定位与按需读取

代码根 `Assets/5_Scripts/`：

| 入口 | 职责 |
| --- | --- |
| `5-3_GamePlay/World/Time/` | DayTimeSystem、TimeData、DayNightTimeManager |
| `5-0_WorldModel/Weather/ChunkWeather*`、`Weather/RegionalWeather*` | 冻结区块档案与纯区域引擎 |
| `5-3_GamePlay/World/Environment/` | WeatherMgr、LightLayerMgr、TemperatureMgr.Field、LocalTemperatureField/Source |
| `5-0_WorldModel/Weather/{AirHumidityKernel,WorldAirHumidityField}.cs` | 固定/动态供湿 |
| `5-3_GamePlay/World/Chunk/Management/ChunkMgr.AirHumidity.cs` | 湿度桥 |
| `5-3_GamePlay/World/Environment/Contamination/` | 污染定义与权威修改 |
| `5-3_GamePlay/World/Fluids/` | Atmosphere/AtmosphereService、Core/FluidThermodynamics |

配置：`Assets/StreamingAssets/GameConfig/Time/time-system.json`、同根 `Fluids/fluids.json`、`Contamination/`；天气 `Assets/Resources/Config/Weather/regional-weather.json`。星球/时间状态在 `World/Map/Data/PlanetData/PlanetTimeData`。

仅按当前改动读取，不批量加载：

- 天气生成/推进、湿度、雨雪雷雾、地表积分或保存：[weather-and-surface.md](references/weather-and-surface.md)。
- 地理/局部温度、传热、体温、氧气/压力或大气：[thermal-and-survival.md](references/thermal-and-survival.md)。
- 地块接触、积雪操作、污染、光照或风推动：[contact-light-wind.md](references/contact-light-wind.md)。

## 多世界所有权

- WeatherMgr.GetForWorld(WorldKey) 各持独立区域引擎、地表队列和雷击序号；Instance/ExistingInstance 仅路由 GameplayWorldRegistry.ExecutionWorldKey，太空/菜单可空，局部坐标世界不创建星球天气，不回退其他地表。后台世界继续结算，雨雪/音效/闪电/Shader 仅输出本机世界。
- WorldManager 预制体中的 WeatherMgr 子节点沿用父级生命周期；仅独立天气根节点自行 DontDestroyOnLoad，不拆离子节点或持久化无关父级。
- GameplayWorldRegistry.UseWorld 只用于同步事务，禁止跨 yield 持有。时间回调、网络请求及离场清理按拥有者 WorldKey 入上下文；天气/地表快照携真实 WorldKey，请求世界由连接绑定角色确定。
- TemperatureMgr 每世界独立场；热源按对象实际 Scene 发布，撤销按来源原绑定清理，晚回调不借当前镜头。雷击去重绑定世界/地形纪元。
- 保存用 BeginAllSurfaceSaveCapture/FlushAllSurfaceWorkForAutoSave 或 FlushAllSurfaceWork 覆盖所有世界；总退出先结清天气再释放区块，本机切洞穴不释放其他玩家地表天气。

## 数据与接口边界

- DayTimeSystem 为跨场景/保存时间主入口；日长 RotationPeriodSeconds、年长 OrbitalPeriodSeconds，经 SetRotationPeriod/SetOrbitalPeriod 修改。季节/年/轨道共用游戏秒轴；SeasonCalendar 历史冻结周期/倾角/离心率，不用新日长重算过去。
- GM 时间滑条按实际 DayLength 预览，松开由权威 JumpToTime 一次；场景名可按 handle 缓存，时间/天气/维度实时读。菜单用无报错 GetInstance 探测。
- 太空跨区不重建玩家/WorldEnter；SpaceMgr 必须登记新星区时间与采光并引用 SpaceScene 统一时钟，避免未登记场景的零光照。
- 固定地理 temperature/airHumidity/precipitation、动态空气湿度、地表 moisture 和天气事件分层；环境查询不生成缺失地形，不把镜头区天气当全世界状态。
- TryGetClimateBaseline 供历史生态/雪，排除季节/天气/局部源；TryGetAmbientTemperature 供体温/产量/调试，叠已加载地理层、PlanetData 基准差及允许的季节/天气/局部源，未加载返回 false。出生格温度不回写全局。
- 环境生产者发布通用供给/影响，生存模块只管自身状态；装备/设备走公开接口供氧或注册耐受，不向 Mod_Oxygen/Mod_Pressure 堆具体装备判断。
- 海洋 `ocean.currentX/Y` 保存带强度的近岸回推/外海离岸向量；`WaterEnvironmentRules.ResolveOceanCurrentVector` 统一混河口，运行时/冷区/海面共用。海洋 `RuntimeWaterCurrentSample.Flow` 为 0～1 强度，物品与角色推速均须乘它；共享格角包含零流样本且禁止归一化吞掉弱流带。
- 回归主题入口 `environment/*`、`survival/*`；按 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md) 和 `Assets/StreamingAssets/GameplayTests/systems.catalog.json` 选择，专项映射见参考。规则样本与最终场景/画面验收分开报告。

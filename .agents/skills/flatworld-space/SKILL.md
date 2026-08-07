---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、星球运行、公转自转、星体数据、飞行模块、SpaceMgr 或 Space Prefab。关键词：SpaceMgr、PlanetData、Module_Fly、SpaceScene。"
argument-hint: "太空或星球运行问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 太空与星球系统定位

> 最后核对：2026-07-31。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceMgr.cs`：运行时星球创建、索引、生命周期与保存入口。
2. `Assets/5_Scripts/5-3_GamePlay/World/Space/PlanetData.cs`：公转、自转与运行时轨迹 partial。
3. `Assets/5_Scripts/5-3_GamePlay/World/Map/Data/PlanetData.cs`：星球基础、地图、温度与天气 partial。
4. `Assets/5_Scripts/5-3_GamePlay/World/Space/Module_Fly.cs`：飞行模块。
5. 设计星球着陆或离开星球时同步读取 `flatworld-dimension`；星球与维度统一由 `WorldAddress` 表达。

## 资源与场景

- 太空场景：`Assets/3_Scenes/SpaceScene.unity`。
- 星球/太空 Prefab：`Assets/2_Prefabs/Space/`。
- 世界准备数据：`GameManager.ReadyPlanetData`。

## 核心链路

```text
GameManager.Event_GameWorldEnter
→ SpaceMgr.Load / AddPlanet
→ GameRes 按 PrefabName 实例化星球
→ 通过 BodyId / OrbitCenterBodyId 绑定轨道中心
→ PlanetData.RunPlanet 推进公转、自转和轨迹
→ SpaceMgr.Save 回写 ReadyPlanetData
```

## 易误判点

- `PlanetData` 是 partial class，跨 `Map/Data` 与 `Space` 两个目录；修改序列化字段必须检查两处。
- `RuntimeAngle` 与轨迹是非序列化运行时状态，初始化由 `InitializeRuntime()` 完成。
- Prefab 名称由 `RuntimePlanetName` / `PrefabName` 决定，并通过 `GameRes` 实例化；移动或重命名资源需检查 Addressables。
- 后续星球旅行不得把每颗星球实现为独立的一次性场景链：使用 `WorldAddress.PlanetId` 标识星球，`surface`/`cave` 等标识星球内维度，并复用 `DimensionManager` 动态世界切换。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：确立星球旅行的世界地址边界；当前地下矿洞作为首个维度切换切片，未来星球表面继续复用同一 `WorldAddress + DimensionManager` 架构。
- 2026-07-27：太空系统首版定位确认以 `SpaceMgr + PlanetData partial + SpaceScene` 为主链。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Space/SpaceSmokeTests.cs`；当前基础覆盖SpaceMgr、飞行模块、太空场景和星体 Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；太空系统测试约定目录：`Assets/GameTest/Space/`；场景目录：`Assets/GameTest/Scenes/Space/`；冒烟分类：`Space.Smoke`。
- 新增太空场景、星球运行、公转自转、飞行模块或场景切换行为时必须增加系统测试；修复 Bug 时先增加回归测试。进入太空到星体运行主流程变化时同步更新太空冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；轨道和时间测试必须使用确定数据与时间步长。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Space.Smoke`；无需视觉模型或测试工具卡片。涉及核心场景切换、星体存档、玩家或地图时追加对应分类；只有太空场景最终观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变星球数据字段、轨道关系、Prefab 命名/位置、场景、飞行模块、保存策略或 `GameRes` 注册方式后，必须更新本 Skill；天气温度字段变化同时更新 Environment/Data Skill。

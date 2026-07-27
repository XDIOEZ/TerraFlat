---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、星球运行、公转自转、星体数据、飞行模块、SpaceMgr 或 Space Prefab。关键词：SpaceMgr、PlanetData、Module_Fly、SpaceScene。"
argument-hint: "太空或星球运行问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 太空与星球系统定位

> 最后核对：2026-07-27。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Space/SpaceMgr.cs`：运行时星球创建、索引、生命周期与保存入口。
2. `Assets/5_Scripts/5-3_GamePlay/Space/PlanetData.cs`：公转、自转与运行时轨迹 partial。
3. `Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetData.cs`：星球基础、地图、温度与天气 partial。
4. `Assets/5_Scripts/5-3_GamePlay/Space/Module_Fly.cs`：飞行模块。

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

## 近期变更

- 2026-07-27：太空系统首版定位确认以 `SpaceMgr + PlanetData partial + SpaceScene` 为主链。

## 修改后维护本 Skill

改变星球数据字段、轨道关系、Prefab 命名/位置、场景、飞行模块、保存策略或 `GameRes` 注册方式后，必须更新本 Skill；天气温度字段变化同时更新 Environment/Data Skill。

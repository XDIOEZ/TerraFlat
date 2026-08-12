---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、星球运行、公转自转、星体数据、飞行模块、SpaceMgr 或 Space Prefab。关键词：SpaceMgr、PlanetData、Module_Fly、SpaceScene。"
---

# FlatWorld 太空与星球

## 入口

- 管理：`Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceMgr.cs`
- 轨道数据：同目录 `PlanetData.cs`；地图/天气 partial：`World/Map/Data/PlanetData.cs`
- 飞行：`World/Space/Module_Fly.cs`
- 场景/资源：`Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/Space/`

## 不变量

- 主链：世界进入 → SpaceMgr Load/AddPlanet → GameRes 按 PrefabName 实例化 → BodyId 绑定轨道中心 → RunPlanet → Save 回写。
- `PlanetData` 跨两个目录的 partial；序列化字段变化必须同时检查并联动 Data Skill。
- RuntimeAngle/轨迹为非序列化状态，由 `InitializeRuntime()` 重建。
- Prefab 名经 `RuntimePlanetName/PrefabName` 和 GameRes 解析；移动/改名同步检查 Addressables。
- 未来星球旅行使用 `WorldAddress.PlanetId + DimensionManager`，不为每颗星球造一次性 Scene 链。

## 验证

- 轨道与时间测试使用确定数据/步长；检查 Load/Save、中心绑定、Prefab 解析和场景清理。
- 默认不主动跑测试；需要时运行 `Space.Smoke`。入口：`Assets/GameTest/Space/SpaceSmokeTests.cs`。

星体字段、轨道、Prefab/Scene、飞行或保存策略变化时更新本 Skill；近期变更最多 5 条。

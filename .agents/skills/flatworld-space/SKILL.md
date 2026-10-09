---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、星球运行、公转自转、星体数据、飞行模块、SpaceMgr 或 Space Prefab。关键词：SpaceMgr、PlanetData、Mod_Fly、SpaceScene。"
---

# FlatWorld 太空与星球

## 入口

- 管理：`Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceMgr.cs`
- 轨道数据：同目录 `PlanetData.cs`；地图/天气 partial：`World/Map/Data/PlanetData.cs`
- 飞行：`World/Space/Mod_Fly.cs`
- 场景/资源：`Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`

## 不变量

- 主链：世界进入 → SpaceMgr Load/AddPlanet → GameRes 按 PrefabName 实例化 → BodyId 绑定轨道中心 → RunPlanet → Save 回写。
- `PlanetData` 跨两个目录的 partial；序列化字段变化必须同时检查并联动 Data Skill。
- RuntimeAngle/轨迹为非序列化状态，由 `InitializeRuntime()` 重建。
- Prefab 名经 `RuntimePlanetName/PrefabName` 和 GameRes 解析；移动/改名同步检查 Addressables。
- 未来星球旅行使用 `WorldAddress.PlanetId + DimensionManager`，不为每颗星球造一次性 Scene 链。

## 规划约束

- 新玩法以 `开发文档文件夹/01_世界生成与环境/太空场景开发待办.md` 为准；管道、气体与气压防护为前置依赖，不把待办视为现成功能。
- 恒星系生成流程与星球配置分离；首轮太阳系、玩家初始地球，各星球独立地表与环境配置。所有行星采用游戏化可降落设计，气态巨行星气压极高。
- 发射表现不改地表物理 Y；综合升空推力与太空方向推力分开。停推力后继续惯性升空及重力运动，满足条件仍可成功；进入太空只将实际速度转为切向，不补速或锁定圆轨道。
- 飞船地板四邻接，同船多控制台，独立船体双向确认后才连接；分裂部分符合控制台连接、供电、燃料和引擎条件即可继续驾驶，库存与网络按独立船体本地格子作用域处理。
- 非驾驶移动、冲刺和宇航服推进按屏幕方向操纵玩家，驾驶按船体轴向操纵飞船；玩家能力不替代引擎。承载地板毁坏释放乘员并保留速度，失去支撑的设备转掉落物。
- 只有完整包围的区域具备船内供气条件，太空开放或破口区按真空处理；隔离门正常开关不泄压。密闭判定与实际气体供给分开。
- 玩家随船降落有过渡选址，实际冲击可损毁已有建筑；陆地作为平台、海面浮为海上平台，内容与冲击状态保留。残骸通过攻击拆取材料或工作方块掉落物。
- 太空船与残骸运动不能因玩家回地表或视图卸载而冻结；船上必要网络保持活动，暂停和离线不补算。
- 结构冲击与燃爆复用唯一伤害／事件入口；燃料实际库存只释放或消费一次，不能复制气罐压力爆炸预算。
- 航空燃料是罐内接近 2:1 的氢氧气相库存用途，按 mol／统一标准气量判定，不新增替代组分的燃料物质；引擎按配方原子扣量，普通管口仍随机出气。宇航服推进与供氧分罐。

## 验证

- 轨道与时间测试使用确定数据/步长；检查 Load/Save、中心绑定、Prefab 解析和场景清理。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

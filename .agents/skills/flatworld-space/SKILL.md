---
name: flatworld-space
description: "Use when: 定位或修改 FlatWorld 的太空场景、星球运行、公转自转、星体数据、飞行模块、SpaceMgr 或 Space Prefab。关键词：SpaceMgr、PlanetData、Mod_Fly、SpaceScene。"
---

# FlatWorld 太空与星球

## 入口

- 权威会话：`Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceSession.cs`、`SpaceSessionState.cs`；`SpaceMgr.cs` 只投影星体与星空。
- 生成与轨道：同目录 `Universe/SpaceCatalog.cs`、`UniverseSimulation.cs`、`SpaceSurfaceQuery.cs`；`PlanetData.cs` 与 `World/Map/Data/PlanetData.cs` 保存星体地表身份。
- 船体：同目录 `Ships/`，领域结构、气密与对接在 `Ships/Domain/`；建筑后端、运动、乘员、环境、导航、爆炸与散落物按 partial 分工。
- 生命周期：`Core/Lifecycle/GameManager.Space.cs`、`World/Dimension/DimensionManager.cs`；正式面板在 `Presentation/UI/ShipPanelSession.cs`、`SpaceLandingPanelSession.cs`。
- 场景/资源：`Assets/3_Scenes/SpaceScene.unity`、`Assets/2_Prefabs/World/Space/`

## 不变量

- 主链：正式世界进入 → SpaceSession 恢复稳定星体、船体与船上机器作用域 → 唯一固定时钟推进 → 当前场景投影视图。SpaceMgr 不再另推宇宙时间。
- 宇宙坐标用 double 米，ViewOrigin 只影响表现；地表逻辑坐标与循环镜像通过 WorldLocalPresentation 换算，不把太空坐标交给 Chunk 生成、地表寻路或 NormalizePosition。
- 固定步须等待 IsGameplayReady，并按 CaptureShipContactPoses → 运动 → StepShipContacts → CommitSurfaceContacts 顺序结算。Kinematic 碰撞体负责表现，实际平动和旋转反作用由权威扫掠解算；碰撞、无人船和冷地表损毁不依赖可见性。
- 星体接触与船体接触比较同一步时间 Fraction，先撞船后须从真实碰撞时刻重积剩余轨迹；不能提前切地表作用域导致漏碰撞。
- 地表切换复用 DimensionManager 的黑屏、玩家快照与失败恢复；读档场景依据已保存飞行阶段，避免迁移中旧 CurrentSceneName 覆盖目标。未进入就绪状态不推进下落。
- `PlanetData` 跨两个目录的 partial；序列化字段变化必须同时检查并联动 Data Skill。
- 星体位置由 UniverseState.SimulationSeconds 和稳定轨道参数派生；自转含逆向周期，统一经 GetRotationRadians 计算。
- Prefab 名经 `RuntimePlanetName/PrefabName` 和 GameRes 解析；移动/改名同步检查 Addressables。
- 星球旅行使用 `WorldAddress.PlanetId + DimensionManager`，不为每颗星球造一次性 Scene 链。冷地表查询与落地损毁使用冻结生成 Profile 加最新 Chunk 差量，禁止读取当前活动星球替代目标星球。
- 船体部件能力 ID 为 `船体部件模块`，Prefab 为 `Module_ShipPart`，MonoBehaviour 文件必须同名 `Mod_ShipPart.cs`；配置位于 `space_ships.json`。设备、太阳系和飞行参数分别在 `space_devices.json`、`GameConfig/Space/solar-system.json`、`Resources/Config/Space/space-gameplay.json`。

## 规划约束

- 新玩法以 `开发文档文件夹/01_世界生成与环境/太空场景开发待办.md` 为准；管道、气体与气压防护为前置依赖，不把待办视为现成功能。
- 恒星系生成与各星体地表配置分离；首轮太阳系、玩家初始地球，行星和恒星均可游戏化登陆。气态巨行星气压极高，太阳“核聚变中”地块极热、强减速、高伤害；通过材料耐受表达普通船爆炸与后期存活，不硬编码恒星登陆必死。
- 发射表现不改地表物理 Y；综合升空推力与太空方向推力分开。停机仅撤推力，按惯性判断进入太空或下落；资源与引擎条件满足时允许重新点火救船，保持高度和速度，按实际运动更新阶段。进入太空只将实际速度转为切向，不补速或锁定圆轨道。
- 飞船地板四邻接，同船多控制台；独立船体必须通过墙壁类对接接口及双方控制台确认形成整体，普通接触、补地板和光纤不替代接口。任一方操作接口可解除该接口对并清除双方确认，重连须重新确认；按剩余有效连接路径重算整体，保留成员与接口身份，不复制库存或清零分离速度。分裂部分符合控制台连接、供电、燃料和引擎条件即可继续驾驶，库存与网络按船体本地格子作用域处理。
- 非驾驶移动、冲刺和宇航服推进按屏幕方向操纵玩家，驾驶按船体轴向操纵飞船；玩家能力不替代引擎。承载地板毁坏释放乘员并保留速度，失去支撑的设备转掉落物。
- 生物获得地面支撑后恢复直接移动；有效重力工作方块覆盖的舱内优先用地球重力，飞船落地后仍生效且不与当地重力叠加，出舱后用当地环境规则。内部移动重力与外部轨道受力分开。
- 墙体边界闭合且内部地板完整才密闭，停供保留舱气，隔离门正常开关不泄压；破口跟随外界，修复不还原旧气，太空补舱仍真空须重新供气。墙体（含隔墙）只比较船体外部气压耐受范围；船体、墙壁、地板另配置外部温度耐受范围，越界距离乘各自系数计算伤害，归零统一摧毁并刷新密闭。结构损伤与生物冷热规则分开。
- 密闭舱自动维持正常舱温，无需温控工作方块；破舱随外界温度，补舱恢复正常舱温但不生成气体。完整承载地板阻隔危险地块对乘员的直接接触伤害，按支撑关系判断；船体仍承受外部温压损伤，撞击与爆炸照常结算，不在角色核心增加船型或地块特判。
- 飞船及独自玩家复用下落选址，独自降落不计算宇航服推进；倒计时随预计剩余时间刷新，触地立即结束，未选或无人船附近随机并保存，按最终速度结算。飞船允许重新点火救船；落点中心地块决定整船陆海状态，海面浮船禁驾，再起飞复用现有条件，残骸攻击回收。
- 太空船与残骸运动不能因玩家回地表或视图卸载而冻结；船上必要网络保持活动，暂停和离线不补算。
- 结构冲击与燃爆复用唯一伤害／事件入口；燃料实际库存只释放或消费一次，不能复制气罐压力爆炸预算。
- 航空燃料是罐内接近 2:1 的氢氧气相库存用途，按 mol／统一标准气量判定，不新增替代组分的燃料物质；引擎按配方原子扣量，普通管口仍随机出气。宇航服推进与供氧分罐。

## 验证

- 轨道与时间测试使用确定数据/步长；检查 Load/Save、中心绑定、Prefab 解析和场景清理。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

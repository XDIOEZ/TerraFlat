---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
argument-hint: "生命周期、场景或启动问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 核心生命周期定位

> 最后核对：2026-08-03。路径相对仓库根目录。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs`：世界生命周期、新建/继续/退出、出生点、核心事件。
2. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板绑定与控件命名契约。
3. `Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs`：Addressables 本体资源加载完成后接入 MOD。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/SceneMgr.cs`：通用同步/异步场景服务。
5. `Assets/5_Scripts/5-3_GamePlay/Manager/ItemMgr.cs`：单机/联机 Player 加载、创建与本地档案上下文建立。
6. 涉及星球表面、矿洞或跨世界旅行时同步读取 `flatworld-dimension`，权威入口为 `DimensionManager` 与 `GameManager.Dimension.cs`。

## 关键入口与路径

- 世界事件：`GameManager.Event_GameWorldEnter`、`Event_GameWorldExit`、`Event_PlayerEnterWorld`。
- 自动保存：`Assets/5_Scripts/5-3_GamePlay/Manager/AutoSaveController.cs`。
- 玩家控制入口：`Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs`。
- 玩家档案上下文：`ItemMgr.LoadOrCreatePlayerData(..., out wasCreated)`；创建、加载、网络提升与远程副本配置都必须显式调用 `Player.SetProfileContext()`。
- 输入重绑定：`Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs`。
- 管理/调试控制：`Assets/5_Scripts/5-3_GamePlay/Controller/PlayerAdminController.cs`。
- 主菜单场景：`Assets/3_Scenes/GameStartScene.unity`。
- 管理器场景：`Assets/3_Scenes/Manager.unity`。
- 开发场景：`Assets/3_Scenes/Develop.unity`。

## 数据与调用链

```text
GameStartScene
→ GameRes 加载 Addressables 与 MOD
→ GameManager.CreateNewWorld / ContinueGame
→ 显示持久化 UI_WorldLoading Prefab
→ SaveDataMgr 准备存档
→ 进入世界并触发 Event_GameWorldEnter
→ ItemMgr 创建玩家并触发 Event_PlayerEnterWorld
→ 等待玩家周围 ChunkMgr 队列完成后关闭加载面板
```

- `CreateNewWorld()` 创建新的 `GameSaveData` 后，会先通过 `ApplyPendingNewWorldDifficulty()` 写入玩家选择的官方预设或完整自定义规则值对象，再生成种子与首个磁盘存档；不得在首存档之后补写难度。
- 新建世界和进入已有存档必须先调用 `BeginWorldEntryLoading()`，让 `UI_WorldLoading.prefab` 至少渲染一帧后再执行同步准备；加载面板持续到 `Event_PlayerEnterWorld` 且首批 `ChunkMgr.HasPendingChunkLoads` 清空。
- 加载失败必须调用 `FailWorldEntryLoading()`，避免遮罩永久阻塞；重复进入请求由 `isWorldEntryLoading` 拦截。

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Manager/GameWorldSceneManager.cs` 仅保留简单切场景逻辑，不是世界生命周期权威入口。
- 维度运行使用以 `WorldKey` 命名的动态空 Scene，不进入 Build Settings；`GameManager.RunWorld()` 仍是进入目标世界的权威入口，维度切换通过 partial 桥复用加载 UI 和世界事件。
- 无资源引用且未实现的旧 `Manager/SceneChange.cs` 已删除；场景切换应使用 `SceneMgr` 或明确的世界生命周期入口，不要恢复旧 `IInteract` 传送组件。
- UI 绑定已从 `GameManager.cs` 拆到 `GameManager.UI.cs`；修改主菜单控件名时两处职责不要重新混合。
- 主菜单、新建世界、存档选择和上下文菜单创建后必须调用 `BasePanel.PrepareForGamepadNavigation()`；根主菜单禁止用手柄取消关闭，子面板关闭时恢复父面板焦点。
- 世界逻辑应受 `GameManager.IsInGameWorld` 或世界事件控制，避免在主菜单场景提前运行。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；先检查，确认契约被破坏后才修改下游代码。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `GameRes` 的 Addressables 加载阶段、Prefab/Item/Module 注册或资源键 | `flatworld-item-module`、`flatworld-modding` | 本体先于 MOD、运行时字典键与 Prefab/Module 数据仍一致 | `ItemModule.Smoke`、`Modding.Smoke` |
| `GameRes` 的 Buff、Recipe 或 Skill 字典 | 只加载实际被改注册表对应的 `flatworld-buff`、`flatworld-inventory-crafting` 或 `flatworld-combat` | ID、加载顺序和冲突规则不变 | 对应领域 Smoke |
| 世界创建/进入/退出、存档准备、动态世界 Scene 或全局事件顺序 | `flatworld-dimension`、`flatworld-data-save` | 加载遮罩、首存档、世界事件和失败恢复顺序 | `Dimension.Smoke`、`DataSave.Smoke` |
| 玩家创建/释放、出生点或 `SetProfileContext()` | `flatworld-player-interaction`；涉及远程副本时再加载 `flatworld-networking` | 本地档案身份、远程副本隔离和玩家事件只触发一次 | `PlayerInteraction.Smoke`；联机时追加 `Networking.Smoke` |
| `GameManager.UI.cs` 控件键、加载面板或主菜单绑定 | `flatworld-ui` | Prefab 节点名、焦点恢复和世界输入锁 | `UI.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：主菜单、新建世界、存档与上下文菜单接入通用手柄焦点导航；根主菜单保持不可被取消键误关。
- 2026-07-31：新增维度世界切换链；动态 Scene、玩家释放/重建和失败恢复复用现有世界生命周期与加载遮罩，未建立第二套权威入口。
- 2026-07-30：删除无代码或资源引用的旧 `SceneChange` 交互式切场景组件，正式场景入口继续由 `SceneMgr` 与世界生命周期链承担。
- 2026-07-29：新建世界和进入已有存档接入持久化加载面板；覆盖存档准备、场景卸载、玩家创建、出生点重试和首批周围区块加载，完成或失败后自动关闭。
- 2026-07-29：新世界创建链改为在首存档前写入自定义难度的死亡开关与 16 个倍率字段。
- 2026-07-29：新世界创建链在首个存档落盘前应用创建面板选择的难度类型与自定义死亡掉落规则。
- 2026-07-28：`ItemMgr` 在单机和联机 Player 创建链显式区分本地档案、新建档案与远程副本，为新玩家教程和本地系统隔离提供权威运行时上下文。
- 2026-07-27：`GameManager` 使用 partial 分离世界生命周期与主菜单/存档 UI 绑定；领域控制器直接组合 `BasePanel`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Core/CoreSmokeTests.cs`；当前覆盖 GameManager、GameRes、SceneMgr、启动/管理器场景入口，以及新建/进入存档必须使用 Prefab 加载界面、先等待渲染帧并持续到区块队列完成的源码契约。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；核心流程测试约定目录：`Assets/GameTest/Core/`；场景目录：`Assets/GameTest/Scenes/Core/`；冒烟分类：`Core.Smoke`。
- 新增启动、世界创建、继续游戏、场景切换或退出行为时必须增加系统测试；修复 Bug 时先增加回归测试。全局生命周期变化时同步更新最小启动冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试必须使用临时世界和临时存档，并在结束时清理全局对象与事件订阅。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Core.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有场景或界面最终观感变化才做定向截图。
- 维度生命周期契约由 `Assets/GameTest/Dimension/DimensionSmokeTests.cs`（`Dimension.Smoke`）补充覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

若改变生命周期事件、入口脚本、场景名、UI partial、资源加载顺序或管理器 Prefab/场景位置，必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。近期变更最多保留 8 条，先写日期、再写影响与新约束。

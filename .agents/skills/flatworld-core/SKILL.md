---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
argument-hint: "生命周期、场景或启动问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 核心生命周期定位

> 最后核对：2026-08-08。路径相对仓库根目录。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.cs`：世界生命周期、新建/继续/退出、出生点、核心事件。
2. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板绑定与控件命名契约。
3. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs`：Addressables 本体资源加载完成后接入 MOD。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/SceneMgr.cs`：通用同步/异步场景服务。
5. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ItemMgr.Players.cs`：单机/联机 Player 加载、创建与本地档案上下文建立。
6. 涉及星球表面、矿洞或跨世界旅行时同步读取 `flatworld-dimension`，权威入口为 `DimensionManager` 与 `GameManager.Dimension.cs`。

## 关键入口与路径

- 世界事件：`GameManager.Event_GameWorldEnter`、`Event_GameWorldExit`、`Event_PlayerEnterWorld`。
- 自动保存：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/AutoSaveController.cs`。
- 玩家控制入口：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs`。
- 玩家档案上下文：`ItemMgr.LoadOrCreatePlayerData(..., out wasCreated)`；创建、加载、网络提升与远程副本配置都必须显式调用 `Player.SetProfileContext()`。
- 输入重绑定：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/InputBindingService.cs`。
- 管理/调试控制：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/PlayerAdminController.cs`。
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

- `CreateNewWorld()` 创建新的 `GameSaveData` 后，会先通过 `ApplyNewWorldDifficulty()` 写入玩家选择的官方预设或完整自定义规则值对象，再生成种子与首个磁盘存档；不得在首存档之后补写难度。
- 新世界种子由 `NewWorldCreationRequest.Seed` 提交：非空数字或文字原样保存到 `GameSaveData.SaveSeed` 并稳定映射为整数 `Seed`；仅空白输入随机生成，文本 `0` 也是合法手动种子。
- 新建世界和进入已有存档必须先调用 `BeginWorldEntryLoading()`，让 `UI_WorldLoading.prefab` 至少渲染一帧后再执行同步准备；加载面板持续到 `Event_PlayerEnterWorld` 且首批 `ChunkMgr.HasPendingChunkLoads` 清空。
- 加载失败必须调用 `FailWorldEntryLoading()`，避免遮罩永久阻塞；重复进入请求由 `isWorldEntryLoading` 拦截。

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameWorldSceneManager.cs` 仅保留简单切场景逻辑，不是世界生命周期权威入口。
- 维度运行使用以 `WorldKey` 命名的动态空 Scene，不进入 Build Settings；`GameManager.RunWorld()` 仍是进入目标世界的权威入口，维度切换通过 partial 桥复用加载 UI 和世界事件。
- 旧 `Manager/SceneChange.cs` 与 `GeneralWorldEdge.prefab` 上残留的 Missing Script 已删除；场景切换应使用 `SceneMgr` 或明确的世界生命周期入口，不要恢复旧 `IInteract` 传送组件。
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

- 2026-08-08：新玩家出生点纯采样与 `ChunkMgr.RefreshRuntimeWindow()` 统一使用 `DimensionManager.GetActiveGenerationSeed()`；禁止一个使用维度派生种子、另一个使用基础存档种子，否则预测陆地会在真实区块加载后变成水面。
- 2026-08-08：新玩家纯地形出生搜索改为“锚点附近密集方环 + 剩余预算覆盖完整配置半径”；避免 4096 次采样被近处连续海面耗尽而实际只检查约 32 格，最终候选仍以正式区块结果确认非水且可走后再触发 `Event_PlayerEnterWorld`。
- 2026-08-08：新世界 UI 接入手动世界种子；空白输入随机生成，数字、文字及 `0` 都作为可复现种子保存，`ReadyGameSaveData` 同步记录最终解析后的字符串与整数种子。
- 2026-08-08：新世界 `PlanetData.NoiseScale` 在 `ChunkMgr.RefreshRuntimeWindow()` 创建纯生成快照时写入 `world.coordinateScale`，确保玩家选择的世界坐标缩放同时作用于 WorldModel 地形、气候和河流水文尺度；后台任务继续只读取不可变 Profile，不直接访问 Unity 单例。
- 2026-08-08：`NewWorldCreationRequest` 接受空玩家名和空存档名；任一留空时统一补同一个八位纯数字名称，确保无需命名即可创建并进入新世界。
- 2026-08-08：编辑器 Addressables Play Mode 固定使用 `BuildScriptFastMode`（索引 0），避免 `GameRes` 从旧 Bundle 读取已迁移前的 Prefab；清除 `GeneralWorldEdge.prefab` 上已删除 `SceneChange` 的残留组件，并为 `GameStartScene/Main Camera` 补齐主菜单阶段的 `AudioListener`；正式 Player 构建仍使用独立的 Packed Build 流程。
- 2026-08-06：新世界 UI 默认提交 `WorldTopologyMode.Wrapped`；`PlanetData` 本身仍默认 Infinite，避免旧存档或非 UI 构造被自动转换。Wrapped 创建请求必须有正半径、正 Chunk 尺寸和可构造对齐边界。
- 2026-08-04：新玩家进入存档时，出生点必须以当前维度的纯生成 Profile、当前 `PlanetData` 和派生种子计算；先写玩家坐标再触发 `Event_PlayerEnterWorld`，由 `Mod_ChunkLoader` 流送周围 Chunk。定位阶段不得注册运行时 Chunk，河流格同样必须排除；区块存档统一由 `SaveDataMgr` 扫描。
- 2026-08-04：`GameStartIndex` 启动主菜单时，若自动单例已抢占为无 UI 配置的 `GameManager`，场景中的 `WorldManager` 配置实例必须接管；主菜单 Prefab 缺失必须输出明确错误。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Core/CoreSmokeTests.cs`；当前覆盖 GameManager、GameRes、SceneMgr、启动/管理器场景入口、空玩家/存档名称自动生成八位数字，以及新建/进入存档必须使用 Prefab 加载界面、先等待渲染帧并持续到区块队列完成、出生点必须纯种子定位后再交给玩家流送模块加载 Chunk、禁止 GameManager 重复扫描保存区块的源码契约。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；核心流程测试约定目录：`Assets/GameTest/Core/`；场景目录：`Assets/GameTest/Scenes/Core/`；冒烟分类：`Core.Smoke`。
- 新增启动、世界创建、继续游戏、场景切换或退出行为时必须增加系统测试；修复 Bug 时先增加回归测试。全局生命周期变化时同步更新最小启动冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试必须使用临时世界和临时存档，并在结束时清理全局对象与事件订阅。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Core.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有场景或界面最终观感变化才做定向截图。
- 维度管理器的基础 PlayMode 生命周期由 `Assets/GameTest/Dimension/DimensionLifecycleTests.cs`（`Dimension.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

若改变生命周期事件、入口脚本、场景名、UI partial、资源加载顺序或管理器 Prefab/场景位置，必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。近期变更最多保留 8 条，先写日期、再写影响与新约束。

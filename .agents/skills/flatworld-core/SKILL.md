---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
---

# FlatWorld 核心生命周期定位

> 最后核对：2026-08-09。路径相对仓库根目录。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.cs`：世界生命周期、新建/继续/退出、出生点、核心事件。
2. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板绑定与控件命名契约。
3. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs`：Addressables 本体资源加载完成后接入 MOD。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/SceneMgr.cs`：通用同步/异步场景服务。

## 关键入口与路径
- 世界事件：`GameManager.Event_GameWorldEnter`、`Event_GameWorldExit`、`Event_PlayerEnterWorld`。
- 自动与手动保存：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/{AutoSaveController,GameManager,SaveDataMgr}.cs`。
- 玩家控制入口：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs`。
- 玩家档案上下文：`ItemMgr.LoadOrCreatePlayerData(..., out wasCreated)`；创建、加载、网络提升与远程副本配置都必须显式调用 `Player.SetProfileContext()`。
- 输入重绑定：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/InputBindingService.cs`。
- 管理/调试控制：`Assets/5_Scripts/5-3_GamePlay/Player/Controller/PlayerAdminController.cs`。

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

## 易误判点
- `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameWorldSceneManager.cs` 仅保留简单切场景逻辑，不是世界生命周期权威入口。
- 维度运行使用以 `WorldKey` 命名的动态空 Scene，不进入 Build Settings；`GameManager.RunWorld()` 仍是进入目标世界的权威入口，维度切换通过 partial 桥复用加载 UI 和世界事件。
- 旧 `Manager/SceneChange.cs` 与 `GeneralWorldEdge.prefab` 上残留的 Missing Script 已删除；场景切换应使用 `SceneMgr` 或明确的世界生命周期入口，不要恢复旧 `IInteract` 传送组件。
- UI 绑定已从 `GameManager.cs` 拆到 `GameManager.UI.cs`；修改主菜单控件名时两处职责不要重新混合。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；先检查，确认契约被破坏后才修改下游代码。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `GameRes` 的 Addressables 加载阶段、Prefab/Item/Module 注册或资源键 | `flatworld-item-module`、`flatworld-modding` | 本体先于 MOD、运行时字典键与 Prefab/Module 数据仍一致 | `ItemModule.Smoke`、`Modding.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：`BackToHelloScene_Coroutine` 新增默认开启的 `saveCurrentGame` 开关；“不保存直接退出”只跳过玩家、区块与退出时间写盘，仍执行事件、对象/区块清理及场景卸载后退出应用。
- 2026-08-09：游戏内设置的时间暂停由 `SettingCanvas` 按 `GameNetwork.IsOnline` 与 `GameManager.IsInGameWorld` 判定；单机保存并恢复原 `Time.timeScale`，联机不修改全局时间流速。
- 2026-08-09：手动保存入口改为分帧快照与后台原子写盘，`GameManager` 通过保存状态 HUD 反馈进度；退出保存路径仍保持生命周期专用的记录退出时间流程。
- 2026-08-09：`GameRes` 启动阶段的 JSON Item Sprite 加载已避开含方括号的 Addressables 内部路径；迁移工具阻断此类路径并保留 GUID 引用，启动日志确认 21 个分包、78 个本体物品可完整注册。
- 2026-08-09：维度切换加载目标玩家后必须按其相机视距主动刷新完整 Chunk 窗口，并在解锁输入前等待活动视野绑定完成；不能只等待玩家脚下区块。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/Core/CoreSmokeTests.cs`；当前覆盖 GameManager、GameRes、SceneMgr、启动/管理器场景入口、空玩家/存档名称自动生成八位数字，以及新建/进入存档必须使用 Prefab 加载界面、先等待渲染帧并持续到区块队列完成、出生点必须纯种子定位后再交给玩家流送模块加载 Chunk、禁止 GameManager 重复扫描保存区块的源码契约。
- `Runtime.GoldenPath` 在完整退出后会从刚写入的隔离存档重新进入同一玩家/世界键，断言旧动态 Scene、`Player_DIC` 与 Item 注册表均已清理，再执行第二次退出。
- `Runtime.GoldenPath` 的 `FlatWorldGoldenPathScenarios.AutoSave.cs` 在 `OnWorldReady` 启动正式自动保存链，并继续调用 `GameManager.SaveGame()` 验证手动异步入口；移动阶段断言后台写入完成且输入锁、Mover/Rigidbody2D 与时间缩放保持可用。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；核心流程测试约定目录：`Assets/GameTest/Core/`；场景目录：`Assets/GameTest/Scenes/Core/`；冒烟分类：`Core.Smoke`。
- 新增启动、世界创建、继续游戏、场景切换或退出行为时必须增加系统测试；修复 Bug 时先增加回归测试。全局生命周期变化时同步更新最小启动冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试必须使用临时世界和临时存档，并在结束时清理全局对象与事件订阅。

## 修改后维护本 Skill
若改变生命周期事件、入口脚本、场景名、UI partial、资源加载顺序或管理器 Prefab/场景位置，必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。近期变更最多保留 8 条，先写日期、再写影响与新约束。

---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
argument-hint: "生命周期、场景或启动问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 核心生命周期定位

> 最后核对：2026-07-27。路径相对仓库根目录。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.cs`：世界生命周期、新建/继续/退出、出生点、核心事件。
2. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板绑定与控件命名契约。
3. `Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs`：Addressables 本体资源加载完成后接入 MOD。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/SceneMgr.cs`：通用同步/异步场景服务。
5. `Assets/5_Scripts/5-3_GamePlay/Manager/ItemMgr.cs`：单机/联机 Player 加载、创建与本地档案上下文建立。

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
→ SaveDataMgr 准备存档
→ 进入世界并触发 Event_GameWorldEnter
→ ChunkMgr / ItemMgr / 时间天气 / 导航等订阅者启动
```

## 易误判点

- `Assets/5_Scripts/5-3_GamePlay/Manager/GameWorldSceneManager.cs` 仅保留简单切场景逻辑，不是世界生命周期权威入口。
- UI 绑定已从 `GameManager.cs` 拆到 `GameManager.UI.cs`；修改主菜单控件名时两处职责不要重新混合。
- 世界逻辑应受 `GameManager.IsInGameWorld` 或世界事件控制，避免在主菜单场景提前运行。

## 近期变更

- 2026-07-28：`ItemMgr` 在单机和联机 Player 创建链显式区分本地档案、新建档案与远程副本，为新玩家教程和本地系统隔离提供权威运行时上下文。
- 2026-07-27：`GameManager` 使用 partial 分离世界生命周期与主菜单/存档 UI 绑定；领域控制器直接组合 `BasePanel`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Core/CoreSmokeTests.cs`；当前基础覆盖GameManager、GameRes、SceneMgr 和启动/管理器场景入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；核心流程测试约定目录：`Assets/GameTest/Core/`；场景目录：`Assets/GameTest/Scenes/Core/`；冒烟分类：`Core.Smoke`。
- 新增启动、世界创建、继续游戏、场景切换或退出行为时必须增加系统测试；修复 Bug 时先增加回归测试。全局生命周期变化时同步更新最小启动冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试必须使用临时世界和临时存档，并在结束时清理全局对象与事件订阅。
- 完成修改后检查 Unity 编译和 Console，再运行 `Core.Smoke`；涉及资源、地图、玩家、存档或 UI 初始化时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

若改变生命周期事件、入口脚本、场景名、UI partial、资源加载顺序或管理器 Prefab/场景位置，必须同步更新本 Skill；跨到存档、UI、MOD 时也更新对应 Skill。近期变更最多保留 8 条，先写日期、再写影响与新约束。

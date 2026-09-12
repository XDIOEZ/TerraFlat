# 核心生命周期

## 系统定位

负责启动资源、创建/继续世界、玩家进入世界、场景切换、退出世界和资源会话清理。

## 当前机制

- `GameManager` 是新建世界、继续游戏、运行和退出世界的权威入口。
- `GameRes` 统一加载 Prefab、Item/Actor/Recipe/Buff/Quest/MOD 等目录，并通过加载阶段与依赖关系发布 Ready。
- 世界进入流程必须等待玩家创建、活动 ChunkView 完整绑定和物理同步完成后才解除输入锁。
- 动态维度场景不依赖 Build Settings，使用 WorldKey 创建并复用同一世界运行链。

## 主链

`GameStartScene → GameRes 资源加载 → CreateNewWorld/ContinueGame → UI_WorldLoading → SaveDataMgr → Event_GameWorldEnter → 创建玩家 → Event_PlayerEnterWorld → ChunkView 就绪 → 解锁输入`

## 权威来源

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs`
- UI 绑定：同目录 `GameManager.UI.cs`
- 资源门面：`GameRes.cs`、`GameRes.Loading.cs`、`GameRes.LoadPlan.cs`
- 场景：`SceneMgr.cs`
- 保存：`AutoSaveController.cs`、`SaveDataMgr.cs`

## 关键边界

- 资源加载只有一个会话入口，加载过程中不能另起第二套目录初始化。
- 世界运行中或仍存在运行时 Item 时不能随意重载资源目录。
- `Event_PlayerEnterWorld` 只代表玩家实例已创建，不等于世界视觉已经可展示。
- UI 生命周期逻辑留在 `GameManager.UI.cs`，不要把主菜单/加载页业务堆回通用资源管理器。

## 修改时联动

- 资源目录：Item / Buff / Recipe / Quest 等对应领域文档。
- 世界地址或切换：维度系统。
- Chunk 进入完成条件：WorldModel。
- 存档版本：存档与数据系统。

## 对应 Skill

`.agents/skills/flatworld-core/SKILL.md`

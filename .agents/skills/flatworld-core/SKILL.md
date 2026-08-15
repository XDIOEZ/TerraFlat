---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
---

# FlatWorld 核心生命周期

## 入口

- 世界生命周期：`Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs`
- UI 绑定：同目录 `GameManager.UI.cs`
- 资源启动：同目录 `GameRes.cs`
- 场景服务：同目录 `SceneMgr.cs`
- 保存：同目录 `{AutoSaveController,SaveDataMgr}.cs`

## 主链

`GameStartScene → GameRes 加载 Prefab/Item JSON/Actor JSON/Recipe/Buff/Quest/MOD → CreateNewWorld 或 ContinueGame → UI_WorldLoading → SaveDataMgr → Event_GameWorldEnter → ItemMgr 创建玩家 → Event_PlayerEnterWorld → 等待活动 ChunkView → 解锁输入`

## 不变量

- `GameManager` 是新建、继续、运行、退出世界的权威；`GameWorldSceneManager` 不是。
- 动态维度 Scene 不进 Build Settings，以 `WorldKey` 命名并复用 `RunWorld()`。
- 资源加载保持本体先于 MOD；注册失败不得留下半初始化字典。
- `GameRes` 会随 `WorldManager` Prefab 再次出现在 `GameStartScene`；跨场景存活实例已存在时，重复实例不得启动资源加载协程，否则会先清空目录、再随重复对象销毁而中断加载。
- 创建/网络提升/远程副本都显式设置 Player ProfileContext，玩家事件只触发一次。
- UI 逻辑留在 `GameManager.UI.cs`；加载视觉来自 Prefab，不在运行时拼装。
- 标准新建/继续游戏的加载页只能在玩家脚下区块与完整可见 `ChunkView` 窗口完成表现绑定、物理同步收尾后发布 `Completed` 并淡出；后台生成队列清空不等于可展示，诊断超时只能告警，不能提前放行。
- 世界/资源/玩家/UI 契约变化时只加载实际命中的 Data、Dimension、Item、Networking 或 UI Skill。

## 验证

- 检查成功、取消、失败、无保存退出均能释放事件、输入锁、玩家、Chunk 和 Scene。
- 默认只做静态诊断、编译和 Console；系统级生命周期变化按 `flatworld-test-automation` 选择相关 Smoke，并扩展 Golden Path。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

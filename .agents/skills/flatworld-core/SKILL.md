---
name: flatworld-core
description: "Use when: 定位或修改 FlatWorld 的游戏启动、新建世界、继续游戏、退出世界、出生点、场景切换、资源初始化与全局生命周期。关键词：GameManager、GameRes、SceneMgr、GameStartScene、Manager scene。"
---

# FlatWorld 核心生命周期

## 入口

- 世界生命周期：`Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs`
- UI 绑定：同目录 `GameManager.UI.cs`
- 资源门面：同目录 `GameRes.cs`；会话：`GameRes.Loading.cs`；阶段组合：`GameRes.LoadPlan.cs`
- 阶段执行/所有权/校验：同目录 `ResourceLoadPipeline.cs`、`ResourceAssetScope.cs`、`ResourceCatalogValidation.cs`
- 世界内原位更新：同目录 `GameRes.HotReload.cs`、`ResourceReloadContext.cs`
- 场景服务：同目录 `SceneMgr.cs`
- 保存：同目录 `{AutoSaveController,SaveDataMgr}.cs`

## 主链

`GameStartScene → GameRes 加载 Prefab/Item JSON/Actor JSON/Recipe/Buff/Quest/MOD → CreateNewWorld 或 ContinueGame → UI_WorldLoading → SaveDataMgr → Event_GameWorldEnter → ItemMgr 创建玩家 → Event_PlayerEnterWorld → 等待活动 ChunkView → 解锁输入`

## 不变量

- `GameManager` 是新建、继续、运行、退出世界的权威；`GameWorldSceneManager` 不是。
- 动态维度 Scene 不进 Build Settings，以 `WorldKey` 命名并复用 `RunWorld()`。
- F5/调试资源刷新统一走 `RequestResourceReload`：主菜单使用 `TryReloadResources` 完整替换会话；单机世界使用 `GameRes.HotReload` 准备候选目录，校验后同帧发布，不保存、不退出、不重新加载存档。候选加载期间正式 `LoadState` 保持 Ready，以 `IsResourceReloadInProgress` 防重入；失败保留原目录。联机世界仍禁止单边热更新。
- `ResourceReloadContext` 只交换资源目录和会话标量，每次加载器 `MoveNext` 返回 Unity 前恢复正式引用；禁止登记玩家、区块、库存等持续变化的运行态。新增静态目录由所有者提供 `ConfigureResourceReload`，加载阶段不能用异步回调绕过上下文写入全局目录，也不能提前触发世界生命周期事件。
- 原位更新成功后发布 `ResourcesReloaded`，活跃 Item 和 Chunk 不重建；旧代 Addressables、MOD 模板与 Lua 保留到正式退出完成后释放。运行中不能删除原有物品/地块/MOD 身份、改变地块数字编号或增删/重排液体身份，在用 MOD Bundle 二进制变更同样拒绝。`TryReloadResources` 只允许无世界和活跃 Item 时调用。
- 新目录在 `GameRes.LoadPlan.cs` 注册阶段及依赖；阶段内返回嵌套 `IEnumerator`，禁止 `StartCoroutine` 脱离 `ResourceLoadPipeline` 的异常、超时与取消管理。
- 本体资源句柄发出时即交给 `ResourceAssetScope` 持有，成功保留到目录卸载，失败/取消统一释放；卸载必须先处理 MOD 与物品池，再清空派生目录、释放资源，禁止用自动创建单例的查询入口做销毁清理。
- 本体先校验再加载 MOD，合并后再次通过 `ResourceCatalogValidation` 才发布 Ready。新系统通过 `IResourceCatalogValidator` 接入引用校验，不把玩法资源约束塞进通用加载器；静态目录检查入口为 `FlatWorld/诊断/检查 Addressables 目录`。
- 完整资源会话在 `validate-final` 后执行 `brg-sprite-mesh-prewarm`，合并最终 TileBase/JSON/MOD 目录与 Palette，完成后才允许 Ready。原位 F5 候选跳过共享缓存预热，发布后经 `GetOrCreate` 按需构建，失败候选不得清理正式 BRG/Mesh。完整卸载及旧代回收必须先解绑 BRG、清理共享 Mesh，再释放源资源；停止播放和域重载也必须显式释放隐藏 Mesh。
- 编辑器普通 Play 与完整流程入口统一启用 Domain Reload 和 Scene Reload（`m_EnterPlayModeOptionsEnabled: 0`），由 Unity 一次性重建 Addressables、单例与静态事件；禁止反射替换 Addressables 私有实例来模拟局部重置。通用 Prefab 标签查询为 0 时必须在 `GameRes` 入口失败；排查时区分静态目录缺失与运行时 Locator 状态，不能仅凭空查询断言根因。
- `GameRes` 会随 `WorldManager` Prefab 再次出现在 `GameStartScene`；跨场景存活实例已存在时，重复实例不得启动资源加载协程，否则会先清空目录、再随重复对象销毁而中断加载。时间系统 JSON 必须在 `GameRes` 允许创建新世界前完成加载，玩家覆盖文件无效时保留内建配置。
- 启动资源采用双闸门：`GameRes.IsStartupReady` 只表示主菜单必要 UI 与基础配置已经就绪，此时启动遮罩关闭、完整内容继续在同一资源会话后台加载；`isLoadFinish/LoadState.Ready` 仍是进入世界的硬门槛。玩家在后台加载完成前点击新建或继续时，必须先显示 `UI_WorldLoading` 并等待完整 Ready，禁止先创建世界再补资源。
- 基于 `SingletonMono<T>` 的跨场景管理器必须按 Unity null 语义恢复已销毁的静态引用，且场景副本不得覆盖有效实例，否则返回主菜单再进入时会把运行时回调发送给已销毁对象。
- 停止播放/关闭程序的对象销毁顺序不能承担业务依赖：清理使用绑定时保存的管理器和事件源引用，禁止重新查找单例或创建场景/池根节点；整个 Chunk 窗口关闭时直接销毁 View，正常流送才入池。表现清理必须可重复调用，终止时取消后台生成并保证纯运行时最终释放；应用退出不能记成自然物被采集。
- 创建/网络提升/远程副本都显式设置 Player ProfileContext，玩家事件只触发一次。
- 新玩家由 `ItemMgr` 在 `Player.Load()` 前应用 `PlayerCreationTemplateCatalogService` 解析的 JSON 模板；内建目录位于 `GameConfig/Players`，MOD 在同一资源就绪阶段注册，已有存档和跨维度重建不重新覆盖创建参数。
- UI 逻辑留在 `GameManager.UI.cs`；加载视觉来自 Prefab，不在运行时拼装。
- 标准新建/继续游戏的加载页只能在玩家脚下区块与完整可见 `ChunkView` 窗口完成表现绑定、物理同步收尾后发布 `Completed` 并淡出；后台生成队列清空不等于可展示，诊断超时只能告警，不能提前放行。
- 世界/资源/玩家/UI 契约变化时只加载实际命中的 Data、Dimension、Item、Networking 或 UI Skill。

## 验证

- 检查成功、取消、失败、无保存退出均能释放事件、输入锁、玩家、Chunk 和 Scene。
- 系统级生命周期变化必须在真实 Play Mode 中实际覆盖启动、进世界、退出/重进和资源释放；编译与 Console 只作为运行门禁和故障定位。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

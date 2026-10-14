---
name: flatworld-core
description: "FlatWorld 启动、世界进入退出、资源加载/原位更新、场景与玩家生命周期。"
---

# 核心生命周期

## 代码入口

根目录：`Assets/5_Scripts/5-3_GamePlay/Core/`。

| 目录 | 入口与职责 |
| --- | --- |
| `Lifecycle/` | GameManager、GameManager.UI、SceneMgr：世界与场景 |
| 同上 | GameRes、GameRes.Loading/LoadPlan/HotReload：资源会话 |
| 同上 | ResourceLoadPipeline、ResourceAssetScope、ResourceCatalogValidation、ResourceReloadContext：阶段/所有权/发布 |
| `Save/` | SaveDataMgr、AutoSaveController：持久化 |

`启动场景 → 主菜单必需资源 → 完整内容 Ready → 新建/继续 → 加载页 → 世界存档 → WorldEnter → 玩家创建/Load → PlayerEnter → 可见 ChunkView 绑定/物理收尾 → 解锁`

## 世界生命周期

- GameManager 是新建、继续、运行、退出的权威。动态维度以 WorldKey 创建 Scene 并复用 RunWorld，不入 Build Settings；离线太空恢复使用静态 SpaceScene 引导，再按 SpaceSector__X__Y 创建星区场景。
- LoadSceneSingleAndInvokeWhenReady 完成 Single 加载后调用 SpaceSession.ResetSpacePhysics，只释放已失效的计算场景与代理，保留权威位置/速度；下个固定步重建，避免失重移动继续引用已卸载场景。
- 联机本机 realm 切换走 GameManager.Space 的观察者接口，只改变本机显示与动态场景归属；不全局迁移、释放地表或重锁输入。换星区保留浮动原点，退出世界清本机显示缓存；地表恢复必须对应已保留的真实 ChunkMgr 运行时，缺目标时拒绝并提示。太空玩家关闭默认 Rigidbody2D 模拟，运动和跨界接触用纯数据，回地表恢复原值。
- IsStartupReady 只开放主菜单，完整内容仍在同会话后台加载；isLoadFinish/LoadState.Ready 才允许进入世界。加载页 Completed 等待脚下区块及完整可见窗口绑定/物理完成，生成队列清空不等于完成。
- 新角色由 ItemMgr 在 Player.Load 前注入 GameConfig/Players 模板，已有存档/跨维度不重复注入。创建、网络提升与远程副本显式设置 ProfileContext，玩家事件一次发布。
- GameManager 确认安全陆地后写新角色出生点；后台预搜索使用冻结纯数据并共享正式生成器/水文缓存，退出/失败取消。
- 跨场景单例保留有效实例，重复 GameRes 不开启资源协程。停止/退出通过已绑定引用清理，不重新查单例创建对象；终止流送直接销毁 View、取消后台任务，正常流送才回池。

## 资源会话

- LoadPlan 声明阶段和依赖，嵌套 IEnumerator 受 ResourceLoadPipeline 统一异常/超时/取消管理。菜单后台阶段每帧协作预算1ms；PureDataAsync 仅处理私有字符串/DTO/ID，Unity 对象和目录发布仍在主线程。
- 本体先校验、MOD 合并后再 validate-final；领域引用通过 IResourceCatalogValidator/增量版本扩展。正式世界生成资源用 worldAuthoringResourcePaths，完整会话在 Ready 前预热最终 BRG Sprite Mesh。
- 具体资源请求立即归 ResourceAssetScope，失败/取消释放；Addressables 初始化句柄归 GameRes、跨会话保留。卸载顺序为 MOD/池、表现/共享 Mesh、目录、源资源；销毁清理使用现有引用。
- 编辑器 FullPlayModeReloadPolicy 保持 Domain/Scene Reload 开启；Prefab 标签空结果在 GameRes 入口失败。TimeSystem JSON 在允许新世界前完成，玩家覆盖配置无效时保留内建。
- Lua 测试入口可通过 `GameplayTestResourceCache` 恢复 Editor 持久源资产和物品 DTO；每轮新建 Scope/句柄、运行时模板和当前 C# 绑定，仅 Ready 后写缓存，失败/取消撤销暂存。缓存清理及诊断见 [Lua 测试](../flatworld-lua-testing/SKILL.md)。

## 原位更新

- RequestResourceReload 在主菜单完整替换会话，单机世界加载隔离候选、校验后同帧发布 ResourcesReloaded；不保存退出或重建活跃 Item/Chunk。联机不做单边更新；TryReloadResources 仅无世界/活跃 Item 时可用。
- 候选期间正式 Ready 保留，IsResourceReloadInProgress 防重入；ResourceReloadContext 只交换目录/会话标量，每次向 Unity 让出前还原引用。持续玩家、库存、区块运行态不属于候选。
- 结构错误丢弃候选；单个 Actor 错误隔离并保留已发布版本。物品/机器身份删除时暂留旧定义供现存实例与冷区块解析，退出保存/清实例后再回收旧代 Addressables、模板、Lua。
- 在用地块/MOD 身份、Tile 数字编号、液体身份布局及 Bundle 二进制变更不在世界内发布。候选跳过共享预热，发布后按需 GetOrCreate；失败候选不清正式 BRG/Mesh。

## 通用 Lua 测试场景

- `Assets/3_Scenes/Development/GameplayTestScene.unity` 复用正式 WorldManager/建世界/默认玩家模板；测试系统入口、Lua 编写与阶段截图统一见 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)。
- 可选陈列通过世界生成 Hook 配置平地空生态，按正式资源/机器/掉落/Item 后端遍历非 Actor 定义；保存隔离租约独立于场景组件，退出写盘并返回菜单后才恢复正式目录。报告在 `Library/FlatWorldGameplayTests/Runs/`，隔离存档在 `Saves/`。

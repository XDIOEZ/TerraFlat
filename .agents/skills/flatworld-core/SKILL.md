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
| `Rules/` | GameRuleConfiguration：独立基础配置、任意来源修正与不可变业务快照；GameRuleMath：通用数量取整 |

`启动场景 → 主菜单必需资源 → 完整内容 Ready → 新建/继续 → 加载页 → 世界存档 → WorldEnter → 玩家创建/Load → PlayerEnter → 可见 ChunkView 绑定/物理收尾 → 解锁`

## 世界生命周期

- 业务只读 `GameRuleConfiguration.Current` 或订阅 `Changed`，不查询难度；外部通过 `SetModifier(source, callback, priority)` / `RemoveModifier` 修改候选。每次从独立基础值重建，来源按优先级和首次注册顺序执行；失败不替换来源或快照，通知期不允许重入写入。难度为优先级 -1000 的来源，机制可由后续外部来源或业务自身公开开关覆盖。难度定义复用分域只读 RuleSet 复制倍率，草稿、规则候选和最终发布各自独立。`SaveDataMgr.SaveData` 换根时由 `GameWorldRuleSession` 一次装配，退出设空档撤销会话来源；新建难度写入完成后再发布。

- GameManager 是新建、继续、运行、退出的权威。动态维度以 WorldKey 创建 Scene 并复用 RunWorld，不入 Build Settings；离线太空恢复使用静态 SpaceScene 引导，再按 SpaceSector__X__Y 创建星区场景。
- `SaveAndRestart → BackToHelloScene_Coroutine(requireSuccessfulSave:true)` 在撤除世界/网络前同步写盘，失败保留会话；成功清理后由等待旧 PID 退出的助手重开 Windows/Linux 运行包。MOD 菜单复用 `TryRestartApplication`；Editor/其它平台明确不支持自动重启，不能把角色死亡当重启。
- LoadSceneSingleAndInvokeWhenReady 完成 Single 加载后调用 SpaceSession.ResetSpacePhysics，只释放已失效的计算场景与代理，保留权威位置/速度；下个固定步重建，避免失重移动继续引用已卸载场景。
- 联机本机 realm 切换走 GameManager.Space 的观察者接口，只改变本机显示与动态场景归属；不全局迁移、释放地表或重锁输入。换星区保留浮动原点，退出世界清本机显示缓存；地表恢复必须对应已保留的真实 ChunkMgr 运行时，缺目标时拒绝并提示。太空玩家关闭默认 Rigidbody2D 模拟，运动和跨界接触用纯数据，回地表恢复原值。
- IsStartupReady 只开放主菜单，完整内容仍在同会话后台加载；isLoadFinish/LoadState.Ready 才允许进入世界。加载页 Completed 等待脚下区块及完整可见窗口绑定/物理完成，生成队列清空不等于完成。
- 新角色由 ItemMgr 在 Player.Load 前注入 GameConfig/Players 模板，已有存档/跨维度不重复注入。创建、网络提升与远程副本显式设置 ProfileContext，玩家事件一次发布。
- GameManager 从种子锚点沿固定右上斜线逐格找新角色出生点，首个无水可走格即停，不扩圈/铺二维网格；只受采样预算限制，环绕回起点结束。后台与同步回退共用正式单格规则，预搜索使用冻结纯数据并共享生成器/水文缓存，退出/失败取消。
- 跨场景单例保留有效实例，重复 GameRes 不开启资源协程。停止/退出通过已绑定引用清理，不重新查单例创建对象；终止流送直接销毁 View、取消后台任务，正常流送才回池。
- 销毁回调借 `GameplayWorldRegistry.TryUseExistingWorldForOwnerRelease` 只用现有且 owner 引用相符的记录，允许卸载中释放，不调用 GetOrCreate；正常查询仍要求场景加载。回归 `GameplayWorldOwnerReleaseEditModeTests`。

## 资源会话

- LoadPlan 声明阶段和依赖，嵌套 IEnumerator 受 ResourceLoadPipeline 统一异常/超时/取消管理。菜单后台阶段每帧协作预算1ms；PureDataAsync 仅处理私有字符串/DTO/ID，Unity 对象和目录发布仍在主线程。
- 本地 Items/Recipes 分包先在主线程校验文件路径，再交给单次 PureDataAsync 批量读盘与解析；Android/WebGL 保留逐包 UnityWebRequest，避免小文件反复跨帧等待。
- `audio` 阶段通过 AudioService.PreloadAsync 在 startup-ui/startup-ready 前预热音库。通用资源和物品视觉由 ResourceLoadQueue 保持最多4个在途请求、完成即补槽，按原索引收结果，仅预算耗尽或请求未完成时让出。编辑器 Sprite 冷加载在每个槽位通过 AssetDatabase.LoadObjectAsync 读取 TextureImporter 主纹理，完成即严格校验子图并接续 ResourceAssetScope 请求，同图集共享主纹理操作；不能只延迟同步 AssetDatabase 调用就当成异步，也不能跨 yield 持有 ProfilerMarker 作用域。
- 本体先校验、MOD 合并后再 validate-final；领域引用通过 IResourceCatalogValidator/增量版本扩展。正式世界生成资源用 worldAuthoringResourcePaths，完整会话在 Ready 前预热最终 BRG Sprite Mesh。
- 具体资源请求立即归 ResourceAssetScope，失败/取消释放；Addressables 初始化句柄归 GameRes、跨会话保留。卸载顺序为 MOD/池、表现/共享 Mesh、目录、源资源；销毁清理使用现有引用。
- Editor Fast Mode 的默认 AssetDatabase 请求由 Scope 映射到独立零模拟延迟 Provider，保留子资源、依赖、数据与句柄所有权；不替换原 Provider/缓存，不接管自定义 Provider 或 Bundle。启动 UI 也复用四槽队列。目录复查复用模块严格编译结果，仍检查原型类型、碰撞体和模块自有约束；回归 `core_lifecycle/editor_asset_loading`、`item_runtime/compiled_module_parameters`。
- 启动 JSON 由共享程序集 `Shared/ContentCache/` 自动缓存到 `persistentDataPath/ContentCache/`，玩家仍编辑 `StreamingAssets/GameConfig/` 原始 JSON。Item/Actor/Tile/Recipes/Buffs 按 Manifest 与启用分包生成各自 MemoryPack 二进制包，逐文件 SHA-256 核对后命中跳过 JSON 解析；动物技能按来源摘要复用 DTO；模块能力参数及其他配置经 `JsonConfigurationCache.Deserialize/FromUnityJson/FromToken/Parse` 按原文/参数树摘要恢复 DTO。新增配置模型需支持 MemoryPack，派生值/Unity 引用/运行态排除，字典比较器须保留；配置校验、当前物品引用及资源绑定每轮重新执行，缓存损坏或写盘失败回退原解析器。Actor 保留原始继承来源，世界内逐个容错重载绕过整目录缓存。真实重建通过 BuildRevision 让加载页补充首次较慢提示，命中不提示；回归 `core_lifecycle/binary_content_cache、json_configuration_cache`。
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

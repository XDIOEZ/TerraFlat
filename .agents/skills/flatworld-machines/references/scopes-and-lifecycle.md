# 作用域、后台与归档

## 调度与迁移

- Ports=none 设施共用格索引，不生成扭矩网络；地表按玩家窗口休眠，船上无端口设备仍持续模拟。世界时间有界补算保留余量，手摇倒计时先于领域分派；退出游戏不补算。
- 船使用 `UseScope(GetShipScopeKey(ShipId))/TickShip`，Cell 保持船内坐标、位置由 SpaceSession 投影。值类型 ScopeLease 单一 using 释放，面板/延迟回调用 UseNodeScope；`ship:` 是作用域，不是场景名，不能按地表玩家匹配失败关闭船设备与面板。
- 分裂用 TransferShipNodes，起降用 TransferScopeNodes 在 worldKey 与 ship:key 间迁移；先拆分真实罐库存并全量校验，再提交最终 Cell/位置/绝对朝向、归属与地表通知，每个作用域整批结束才重建拓扑。落地直接加入普通管电网。
- `ScopeState.Key` 写入时冻结 `MachineScopeIdentity.ShipId`；本轮温压、排气、溢流共用该身份，切船/落地/释放不得复用旧船 ID。规则：`mechanical/ship_scope_identity`。

## 冷地表

- SpaceSession 用游戏时钟 TickBackgroundWorlds 推进档案中的非活动地表；活动地表由 ItemMgr 调度。冷作用域只恢复数据网络，不写当前 TemperatureMgr。
- 炉吸料/带面交付通过 DroppedItemService.ColdWorld 按 WorldKey 取实物、去重和原子扣量。冻结拓扑先筛同格候选，再算距离、排序、补配置；原列表/下标身份不符才回退查找，禁止排序全世界掉落物。Profiler：`ColdFuel.Collect/Sort/Transfer`。
- 掉落物每步从真实档案重填可复用运动区，跨世界/异常均清空；同点水面合并采样，仅复用资源目录和有界查询键，不缓存真实差量。Profiler：`ColdWorld.Motion/Water`；规则 `cold_motion_workspace`。
- 冷机器温度先读真实环境层/冷缓存，缺块用 SpaceSurfaceQuery.TryGetAmbientTemperature 的正式地理与星体公式，禁止为采温生成完整水文/生态。全局调温逐次读取，资源/存档切换清点样本；规则 `cold_temperature_path`。
- 冷风力机 windStrength 可保留生成时天气汇总（每星球最多128块），完整块逐出后仍复用；加载地表优先当前资料，真实水流/液体不复用此缓存。规则 `cold_wind_cache`。

## 状态与事务

- 机器 Place/恢复/网络合并/捕获/归档统一用 `ItemInstanceDataFactory.CloneRuntime` 复制快照，复用只读 `SharedConfiguration`，模块字节、堆叠、位姿和嵌套库存独立；不要重新用 FastCloner 扫描整份配置对象图。规则 `mechanical/runtime_snapshot_clone`。
- 完整目录的 `machine-prewarm` 分步预备状态编解码、空拓扑及独立熄火炉体的构造/捕获/零秒 Tick 和空库存材料流程；原位更新候选跳过。预备不经 MOD 工厂，不调用真实 Place/Spawn、非零机器 Tick、环境或存档接口；取消/异常释放已构造的私有炉体，不宣称覆盖全部首次 JIT。
- 初始化库存/战利品仅一次，随机空结果也完成；最大生命由当前定义和 CraftedDurabilityMultiplier 派生，受损值不重复叠乘。MachineCombatBridge 消费真实武器窗口，以 PublishExternalDamage 回传实损。
- 放置失败不扣召唤器。MachineArchive 独立保存，缺 MOD 定义留冷快照，卸载显示不删节点/库存；CaptureCurrentWorld 捕获期间固定 scopeOverride，嵌套租约不得把船库存写进地表。
- 整笔拆回事务进入 UseNodeScope；船机器经 SpaceSession.TryDismantleMachinePiece 交付太空掉落并继承位置/点速度，节点删除成功后撤部件、设备、视图并重建结构。联机准备拆除仅支持地表。
- 客户端更新同格槽内容时保留槽/库存对象及本地布局；RegisterPrivateInventory 以玩家/机器双键注册私有跨箱库存，不进入公开库存、箱快照或公共角色同步。

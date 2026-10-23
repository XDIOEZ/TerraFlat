# 调度、感知与环绕空间

- 覆写 ModUpdate 的模块显式选 EveryFrame/FixedInterval/Disabled；启停/结构/配置/池复用使调度失效，事件能力变用 MarkModuleScheduleDirty，投掷伤害可 SetIdleTickSuppressed，手持武器完整更新。通知只进已注册世界 Item，冷宿主/测试夹具卸载仅自身 dirty。
- Tick 捕注册令牌及 Item/Module 代际，回调后复核；重建保留未变模块时钟，休眠不补算。Profiler：FlatWorld.Module.<类型名>，Hive.SpawnResident 区分真生成，勿把实例化峰误作频率问题。
- 距离档以同 Scene 最近玩家及环绕最短距限制 ItemMgr Tick，不提高模块频率；玩家/地图/手持完整，Owner 不豁免。ItemSimulationRangeIndex 用近档集合、到期堆、区域判档；明显全近/全停区域用带余量平方距离快捷判，边界精算。
- RefreshMembership 对无限制实体，或同 XY/Scene/资格且本帧玩家上下文已刷新该区的实体复用；纯视觉 Z 不影响。BeginFrame 仍更新玩家移动影响的区域/边界成员，手持/放下、真 XY 移动、环绕拓扑必须失效。
- 休眠重时钟、停根刚体并通知 ISimulationRangeAware；重入先恢复原 simulated 再提目标。NotifyRuntimeItemMoved 先更新空间索引/距离档再发事件。
- Actor 感知几何读当前定义，Collider Bridge 仅旧对象；注册/结构变选后端增代际，移动只改位置，格访问 LastVisitedBatch 去重。ItemPhysicsProjection2D 把总重量投影质量、速度/接触写非持久 PhysicsState。
- 掉落拾取/吸燃料用 QueryItemCentersInCircleNonAlloc 查中心桶，不用扫描全世界倍率/几何的 Actor 感知查询；环绕接缝按真实中心半径去重。
- ItemData.transform/索引/存档为规范逻辑坐标，Transform/刚体可在客户端镜像；ReprojectRuntimeItemsToLocalAnchor 处理跨周，WrappedWorldPhysicsAdapter 管根刚体，命中经 GameplayPhysics2D.ResolveComponent<T> 返源。远程副本不进本地 Tick/感知/存档。
- Mod_DiscardItem 仅真丢弃需槽位时查悬停，手部→快捷栏→悬停优先级/长按时钟保留。Mod_TileEffectReceiver 本格/四邻只复用单轮寻址，样本读最新权威层；水生线路保留0.2米采样，仅单次合并连续相同规范液体格，不跨调用缓存水态。

回归：`item_runtime/simulation_region_shortcut`、`item_runtime/simulation_membership_cache`。

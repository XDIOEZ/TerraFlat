# 自然生态纯规则回归

`natural_snapshot_copy` 核对正式运行态工厂的身份、共享配置、独立可变状态、回池隔离和运动保存往返。离线 `python Tools/Tests/NaturalSnapshots/verify.py` 另比较旧深拷贝与工厂分配；不代表真实模块保存/卸载、画面或帧率验收。

2026-10-26 `spatial_geometry` 补充阻挡体中心排序：检查偏移、缩放、旋转、镜像与冷编译冻结/重编译；触发器、禁用或缺失碰撞体仍回退根点。巨矿正式配置另由 `item_runtime/giant_mines` 覆盖；用户正在 PlayMode，本轮未执行新增 Lua 或真实遮挡验收。

`shared_resources` 新增 `giant_generation/giant_clearance`：覆盖十一矿种选择、十七条规则合计 0.13 预算、同候选互斥、不同间距拒绝、概率零/一与倍率饱和、规则反序、岸带锚点与完整净空分工、洞穴出生安全区及缩短阻挡后的导航包含关系。本轮仅静态补充，未在用户运行中的 PlayMode 执行。

## 2026-10-23 大矿山与跨区块静态资源

`shared_resources` 已登记到本系统 `rules.lua`：检查确定性负 GUID、左右加载顺序、四区块交界、环绕世界接缝、负坐标所有权、小世界拒绝、取消、引用交接、规则保存与指纹、奖励落在阻挡体外沿。

本轮在用户 PlayMode 期间仅静态修改，未暂停或重新编译 Unity。外部 `GamePlay.csproj` 构建通过（0 错误）；`Tools/Tests/SharedNaturalResources/verify.py` 的 2057 项断言通过，含 311 次跨块身份比对、完整占地的冷邻区障碍拒绝、内部普通生态及伴生物过滤、临时地形释放、十种矿物 PPU、65 格占地和掉落配置。测试程序集在 Temp 副本中将四个原生 ProfilerMarker 初始化置为 default，生成逻辑不变，正式源文件未替换。

**尚未执行：**Unity 内 Lua、实际实体的 B→A→卸载 B→卸载 A 全链、真实挥镐/投射物/爆炸、完整存档重进和画面验收。纯引用计数断言不是上述实机流程的替代。

2026-10-20 性能补充：`ChunkNaturalItemRenderer` 初次宿主扫描同时记录伴生物候选，第二阶段只处理这些候选，不再为少量伴生物二次扫描整块放置表；维持宿主优先、候选次序、绑定预算、延后重试及取消清理。新增 `ecology/companion_bind_candidates` 检查混合、无伴生、连续复用及稳态 GC，未在 Unity 中运行。

`output_validation_cache` 验证资源 ECS 产物校验在同一编译配置中只运行一次，非法产物不缓存，修复或配置重编译后重新核验；不代表区块加载帧耗时已测量。

2026-10-18 新增但未执行：`body_read_batch` 在自有 Entities World 检查发布值批读、停用实体、批内生命/姿态写入、显式失效、新增/移除及跨批清理；`spatial_geometry` 检查碰撞冷编译的偏移/缩放、树干覆盖与重新编译；`soil_snapshot_reuse` 检查复用容器逐次读取权威水肥与耗水尾数。尚未覆盖真实九宫格泵水回调、完整发布通知顺序及改后平均帧耗时。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

- 在独立 4×4 `ChunkTerrainBuffer` 上调用 `ChunkEcologyGenerator.Generate`，检查稳定非零身份、位置唯一、气候边界、群系掩码、地块阻挡和零倍率。
- 规则夹具不物化 Item，不写生态差量。
- 邻区标签缓存规则经正式 `DeterministicChunkGenerator.HasHostTagInChunk` 和地形扩展计数，检查正负结果复用、标签大小写/任务版本复用，以及世界种子、纪元、区块地址、Profile 和环绕拓扑隔离。生成中取消必须不写缓存，重试后可复用；世界退出后旧纪元拒绝查询，新纪元重新生成。
- 独立 `WorldRuntime` 正式执行 `Generate→TryCommit`，检查现成地形能预填蜂巢所需花朵标签，冷暖生成的完整生态身份、位置、偏移、宿主和规则不变。2026-10-17 新增规则的相关 C# 已通过外部编译，按用户要求未执行测试。
- PlayMode 与地形共用 [Terrain/gameplay-suite.json](../Terrain/gameplay-suite.json)，不建立重复案例：在真实已加载空地上经 `NaturalEntityEcsService.TryRegister` 装配自有 `Tree_Pine` 资源实体，绑定正式区块表现，并采集真实身份/存档快照与导航占地。
- Lua 检查登记实体时该格从可走变阻挡、`TryTakeSnapshotAndRemove` 返回原身份且释放占地、同一快照重新登记后身份保持。地表覆盖栈与正式存档差量在同一条链检查，宿主自动定时截图。
- 只生成自有树木，经模拟租约和 Lua 级收尾清理，失败/中断同样恢复原地格，不删自然生成的既有资源。
- 缺口：完整自然生成装配、伴生关系、随机数量/斑块分布、年度更新、自然删除标记、砍伐掉落、冷区块整场保存恢复。当前现场检查资源 ECS 的运行态与快照往返，未覆盖生成器自动分布和保存文件重进。
- 新增现场已在本轮执行，验收由用户进行。
2026-10-18 新增但未执行：`ecology/capture_batch` 直接调用正式 `EcologyWorldSaveData.CaptureChangedItems`，Lua 检查初始顺序、已有重复 GUID 只覆盖首条、同批重复 GUID 最后输入生效、空/零 GUID 拒绝、删除标记逐次清除、其他区块不变及后续批次重建索引。区块解绑仍逐个执行原本 Item 保存/深拷贝或 ECS 快照移交，仅最后写差量时一次查区块和建立临时索引；不代表自然物模块捕获或卸载总体耗时已经下降到目标。

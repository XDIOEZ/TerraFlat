# 编辑器与调试

## 性能导出

- 一级菜单仅「测试」「性能分析」「其他工具」；其余归 `FlatWorld/其他工具/` 并同步验证函数/ExecuteMenuItem 路径。
- 顶栏「性能 JSON」/`FlatWorld/性能分析/导出选中或最新帧与控制台 JSON` 由 `Automation/ProfilerSnapshotToolbar.cs` 调 PuerTS `UnityMcpProfilerSnapshot`，复用 editor.profiler.analyze。输出到已忽略的 `Logs/ProfileData/<时间_frame-帧号_编号>/`，包含 profiler-analysis.json、console-logs.json、world-scene.json、screen.png；附加回调全部写 pending 后原子发布，忽略 `.pending-*`。手动导出成功后复制本次报告文件夹的完整路径，在当前编辑器窗口和 Console 提示；共用 `ExportFrame` 不操作剪贴板。
- 先读根共享说明（instructionsFile 用 `../`；模板在 PuerTS 包根 ProfilerAnalysisGuide.md）。schema 4 additionalFiles 关联现场，保留 Top100、完整线程树、附近帧耗时、>50ms最慢3帧树与 Registry 累计快照。改导出结构同步模板。
- `WorldSceneSnapshotExporter` 只读已加载 GO/自然ECS/掉落/机器/可选ECS居民与剩余生成基线，以 GUID 合并实体、dataRef 索引库存；不触发保存/扩展回调，二进制模块保留 Base64。现场/累计/日志/截图属于本地导出时刻，不能当作历史选中帧或远程 Player；非PlayMode明确不可用，不改Play/Pause/录制或主动刷新资源。
- 性能点用 Utilities 的 `PerformanceDebugPoint/Registry`，GM「设备数据」按需开统计/慢摘要/阈值。固定 Marker、struct 不跨 yield；后台只记有界数字，主线程合并日志。单位与 Total/Self 见 [点位说明](../../../../开发文档文件夹/07_网络存档与底层架构/关键性能DEBUG点位说明.md)。
- MCP 录制入口 `gameplay(operation=profiler, arguments={action:"start",seconds:10})` 非阻塞、自动导出；status/stop/cancel/export 见 [GamePlayMCP](../../flatworld-gameplay-mcp/SKILL.md)。`GameplayProfilerTool` 恢复原开关，与菜单共用 ExportFrame/pending 发布，不依赖弹窗/剪贴板；帧摘要仅本轮有效保留帧，累计统计附起始快照。

## 编辑器与运行工具

- 贴图世界尺寸检测：`FlatWorld/其他工具/美术/贴图世界尺寸检测` 异步调用只读 `Tools/Art/validate_sprite_world_size.py`，按可见 Alpha/PPU/缩放对照目标或参考图，提示建议 PPU 与导入降采样风险；说明 `Tools/Art/SpriteWorldSizeValidator.md`，离线回归 `Tools/Tests/SpriteWorldSize/test_validator.py`。这是源配置预测，不替代已加载 Sprite 或游戏观感验收，0.70单位仅钻木取火器样本目标。

- MCP 合成截图异步等待自然渲染帧，禁止通过 `EditorApplication.Step` 或修改暂停状态取图；暂停时明确拒绝，无渲染帧时 10 秒超时并清理待截图对象。截图成功须以实际文件和画面为准，传输断连不代表文件一定未生成。
- PuerTS MCP 调用接收 `System.Type` 的 Unity API 时，传 `puer.$typeof(CS.类型)`，不能直接传 JS 类；如 `CS.UnityEngine.Resources.FindObjectsOfTypeAll(puer.$typeof(CS.UnityEngine.GameObject))`。对象查询类型须继承 `UnityEngine.Object`，否则出现 `FindAllObjectsOfType: Invalid Type`；`GetComponentsInChildren` 的 Type 重载同样先转换。
- HTML 物品字典入口 `Assets/StreamingAssets/ItemWiki/打开物品Wiki.cmd`，直接读启用的 Items/Actors/Recipes/Buffs manifests；维护说明在同目录 README。启动器按项目和读写模式复用后台服务，不按端口批量杀 Python。
- 科技树 `tech-tree-eras.json` 配展示时代/细分阶段，`tech-tree-model.js` 从当前配方、物品加工和机器/温变数据建关系；processing 不继承。新增浏览器资源或配置入口同步 `wiki_server.py` 公开白名单。
- 科技树自然资源按采矿/树木与直接采集路线识别，`stages.layoutBand: "natural"` 标记基础自然原料组；下方资源区按用途居中布局，与展示时代分开，未知来源不自动视为自然资源。
- 外部刷新在 Windows 用 ReadDirectoryChangesW，禁 Mono DefaultWatcher 递归扫 Assets；Play/切换中留请求，编辑静默后合并，溢出补一次全量。Unity 保存独立于全库刷新；内容工坊只写 JSON 差异。
- CreateScene 不触发 sceneLoaded；Hierarchy 工具结合卸载/hierarchyChanged 定向解析记录，GlobalObjectId 仅解析已加载所属场景。
- `GameLogManager` 业务日志用 `[WORK]`；刷盘线程池最多一个请求，共享流锁、无 Unity API，暂停/退出/错误/显式 Flush 同步落盘。
- F3 `EnvironmentInfoDisplay` 读 WorldModel/EnvironmentLayerIds、FarmlandSystem 水肥、TemperatureMgr；隐藏停采样，拒非有限/视口外指针，无效清悬停，IMGUI 按 CalcHeight 整页滚动；F4 GM、F6 晴天。
- `GMReflectionConsole` 等 GameRes.StartupResourcesReady，偏好由 GMConsolePreferences 持有，页签为玩家/世界/生成与AI/调试工具/设备数据。RuntimeDebugOverlay 事件开GM，日志Canvas更高；复制/磁盘保原文，缺字只在显示转码。
- GM 两级页签复用公共 `UI_TabButton` 和 `ReusableUITabVisual`；二级行42高、按钮150×38、导航 flexibleHeight=0。页签在滚动区外，仅当前页激活，各页保滚动、分类记子页，搜索先切页再定位。规则 `ui_settings/gm_secondary_pages`，不启动正式 GM 或写偏好；完整UI约定见 [flatworld-ui](../../flatworld-ui/SKILL.md)。
- 地块传送 `GMReflectionConsole.TileTeleport` 搜名称/ID/searchAliases，兼容粘黏/“地块”后缀；`ChunkMgr.FindGroundTileAsync` 先当前地形再后台正式生成查找，只选裸露无水可走底材，取消/关页/换世界丢弃旧结果。
- 拾取倍增 `GMReflectionConsole.Pickup`（1～100整数，默认关闭/10倍）只设本地权威 `Mod_ItemPicker.PickupAmountMultiplier`，PickerLoaded 恢复；偏好不进存档。完整原份预检跨库存容量，仅放大可堆叠物、余物保原量。GM恢复只从 ItemMgr.Player_DIC 定向找本地档案玩家/管理模块，未入世低频等，不扫全场/创建单例。
- `GMWorldLayerOverlay` 温度/污染互斥预算纹理，格对齐、未加载透明、关闭停采，不租区块/改地形；导航一格一Point texel，读真实流场/Job依赖，借用 Native 表不跨后端发布。
- Render Streaming 用项目设置/WebApp，普通Play不自动串流；显式入口统一持有偏好/端口/URL/自启PID，回收核对缓存webserver.exe身份，编码按目标协商。
- 本地 Android：`Automation/CodexAndroidBuildRunner` 消费 Library/CodexRunAndroidBuild.request，Library与APK同名 .build.json 记录结果。wallDurationSeconds 含 Addressables、Player durationSeconds 不含前置、大小读 apkSizeBytes；Addressables 1.22.3 临时关报告弹窗、finally恢复，保留报告且严格拦截资源失败。

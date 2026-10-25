# 角色对话

2026-10-25：增加工作体力不足气泡，采集、工具挥动等一次性扣费失败后通过语义事件点名 `External` 配置台词，随机四句并复用8秒冷却。`rules.lua` 新增正式扣费拒绝/余额不变、恰好足够、成功/免费/持续消耗不误报、来源保留及默认来源断言；同时检查中英文台词、外部空条件合法、自动空条件与Debug触发拒绝。按用户要求仅静态修改，未编译、未运行Lua或玩法验收；本地玩家筛选、实际连续点击的冷却、优先级阻挡后重试及联机反馈仍需现场确认。

2026-10-20：气泡 UI 静止帧优化：`ScreenSpaceSpeechBubblePresenter` 保留原有每帧相机投影/视口裁剪与模态面板检查，仅在可见性实际变化时 `SetActive`，屏幕锚点变化时更新 `anchoredPosition`；重复 Hide 不再重新提交相同透明度。现有隔离场景用例覆盖发言、遮挡基础显示与主动隐藏，尚未自动比较静止、移动、镜头缩放及离屏回归的 Canvas 更新量；本轮未运行 Unity 编译或 PlayMode。

2026-10-20：`WeatherExposureSpeechProvider` 附近燃烧源扫描使用可复用的物理重叠数组代替每次 `OverlapCircleAll` 分配；满容量自动扩容并重新查询，避免密集碰撞体导致漏报，且及时清理对象池 Collider 引用。原天气、燃料、点燃判断与刷新时序不变。当前 Lua 尚未覆盖热源密集场景、动态启停及雨天 Fact，未运行完整 Unity 编译或 PlayMode；需现场回归与 GC 对比。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`dialogue/condition、config、builtin_locales、carry_weight_command、help_catalog、help_command、stamina_rejection`。

已编写的规则覆盖：CharacterSpeechConditionEvaluator 数值/文本/缺失Fact条件；CharacterSpeechConfigLoader 的 JSON/schema/Fact 校验与无效条目隔离；正式 Resources 自言自语资源的有效性与英文译文逐句齐全。

`carry_weight_command` 调用正式 `/carryweight`（kg）和 `/carryvolume`（L）处理器，共用数字/小数解析、缺参/多参/非正/非有限值拒绝、无玩家/无实际背包时拒绝且两种容量不变，以及不吞掉其它命令。新增体积命令断言尚未执行；实际成功设置并保留另一容量、入世/重建后的命令装配、超重 Buff/UI 更新和联网/创造状态拒绝尚未覆盖，用户自行在游戏中验收。

`help_catalog` 调用正式中立目录和书页生成：覆盖提供方替换/撤销、重复去除、稳定排序、只收聊天命令并排除 F4/调试类条目、三条聊天命令登记、简介及每条一页、完整保留用法与条件，并核对容量单位及边界说明。`help_command` 覆盖无玩家/多参数拒绝及其它命令透传。新增断言未执行；实际发放、背包容量拒绝、书内自带木炭后的双击编辑、保存重读与书页实际排版仍需用户在游戏中验收。

规则集未覆盖：本地玩家筛选、实际 Provider 冷却、一次性完成只能在成功显示后写入和语言切换；正式气泡与调度优先级见下方 PlayMode 共享入口。叶子程序集登记入口，GamePlay 不反向引用 Dialogue。

后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享入口

- 共用 `../Quests/gameplay-suite.json` 的 `narrative_event_dialogue` 案例，说明见 [叙事流程共享集](../Quests/README.md)。
- 当前隔离主角已经加载的 `CharacterSoliloquyController` 与 `ScreenSpaceSpeechBubblePresenter` 完成正式发言：高优先级显示、低优先级拒绝、空台词拒绝、同优先级替换，核对实际 TMP 文字/气泡活跃/透明度和非交互性；截图后正式隐藏。
- 等待其他来源的气泡自然结束后才持有自有发言租约；断言失败也通过租约收尾，不用替身 Presenter。实际发言最终显示一张现场截图。
- 同共享集的任务/教程案例还核对真实教程控制器贡献的对话 Facts。配置台词选择、冷却、一次性标记、语言切换、相机跟随和边缘裁切仍未覆盖；事件行动测试与气泡测试相邻编排，不宣称存在自动事件到气泡桥接。
- 新增现场入口已在本轮执行，玩法验收由用户进行。

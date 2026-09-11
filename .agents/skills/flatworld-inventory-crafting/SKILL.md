---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
---

# FlatWorld 背包、制作与农业

## 入口

- 库存：`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/{Inventory,Mod_Inventory,Inventory_UI,Inventory_HotBar,ItemSlot_UI}.cs`
- 制作：`Assets/5_Scripts/5-3_GamePlay/Items/Crafting/`
- 配方真源：`Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json` 及分包 JSON
- 配方可视化编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，Unity 菜单为 `FlatWorld/内容配置/内容工坊`
- 装备：`Items/Equipment/{Mod_Equipment,Equipment_SO,EquipmentInstance*,Module_Equipment_Store}.cs`
- 食物/农业：`Entities/Item/Mod_Food.cs`、种子/成长模块与 `Mod_Grow.AuthoritativeCrop.cs`
- 移动营养消耗：`Entities/Move/Mover.cs` 与 `Mod_Food` 分别维护营养、水分的移动倍率。
- Prefab：`Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`

## 不变量

- 配方 JSON 是唯一真源；旧 Recipe/CookRecipe SO 只作 MOD 兼容，不恢复双重维护。
- 内容工坊的普通合成使用不限长度的滚动材料清单，保存为连续的一维输入；载入旧网格配方时按材料身份归并数量，并保留 `amount=0` 的不消耗工具。只有热加工继续使用 3×3 位置画布。保存前必须使用运行时配方工厂校验整份启用目录，并保留已有配方的未知顶层字段。
- 所有制作入口调用 `CraftingService`；匹配由 `CraftingRecipeMatcher`，扣料/产出由 `CraftingTransaction` 原子提交。
- 玩家手工台 `Mod_HandCraftTable` 与世界工作台 `Mod_MakeTable` 均使用 `RecipeType.Crafting`；配方通过可选 `requiredStation` 区分制作入口：留空表示任意普通制作入口，`handcraft` 表示随身手工，`workbench` 表示世界工作台。匹配器用 `CraftingCapabilities.StationId` 做能力过滤，MOD 可复用字符串 ID 扩展新工作站，禁止按具体配方 ID 硬编码。当前手工台固定 4 输入/2 输出，当前工作台固定 5 输入/2 输出，运行时与 Prefab 序列化槽位必须严格一致；槽位数属于具体工作站能力，未来工作站可声明更多输入槽，配方、内容工坊和普通合成匹配器不得设置全局材料数量上限。
- 多产物必须全部放下才提交；失败不扣料、不部分产出。体积大于 1 的非堆叠产物每个单位必须独占一个容量足够的空槽，不能把 `amount > 1` 整组塞进单槽。`amount=0` 参与签名但不消耗。
- `RecipeType.Crafting` 必须配置 `inputRule: "unordered"` 且 `allowMirror: false`；普通合成只比较材料身份与总量，同类材料可以集中堆叠或分散在任意输入槽。配方需求按当前输入的可满足子集匹配，额外放入的无关材料不得屏蔽候选，也不得在制作所选配方时被扣除；因此候选扫描配方目录即可覆盖输入材料的全部可制作组合。加热加工才允许有序、镜像和网格规则，并继续保持严格输入语义。
- 普通合成输入必须通过 `CraftingRecipeMatcher.TryMatchAll` 保留全部材料候选，`CraftingStationController` 统一维护候选、选择、进度与双输出预览，最终使用所选 `RuntimeRecipe` 精确预检和原子提交，禁止重新回退到目录首个匹配项。Exact/Tag 候选重叠时扣料计划必须按全部需求做全局容量分配，禁止逐项贪心消耗。
- 配方产物合法性与 `ItemData` 创建必须读取 `GameRes.ItemDefinitions`；`AllPrefabs` 只保存表现壳和别名，禁止用它决定候选按钮或“开始制作”是否可用，否则 JSON 物品会出现候选已选中但提交按钮被错误禁用。
- `GameRes.recipeDict` 是尚未迁移 `CraftingService` 的熔炼流程专用旧输入签名索引；`RecipeType.Crafting` 允许相同输入对应多个候选，必须只注册到 `CraftingRecipeCatalog`，禁止写入这个单值字典或把同输入多候选误判为冲突。
- 配方动作在库存事务成功后执行；异常恢复快照。玩法进度信号只在最终成功后发布。
- 制作输入变化、事务扣料和面板初始化都会被动刷新预览；此时 `RecipeNotFound` 是合法的“当前无配方”状态，应清空预览且不输出 Warning。只有用户主动提交前检查失败，或库存、产物等结构性异常，才输出制作诊断。
- 制作模块的 `Save()` 只负责持久化，不能解绑输入、输出、按钮或交互监听；这些运行时事件统一在 `Unload()` 中成对清理，由 Item 退出、移除模块与回池生命周期调用，否则自动保存会让预览与制作按钮永久失效。
- 模态库存才获取输入锁；快捷栏和 `Inventory_Hand` 不锁玩家输入。
- 单个库存面板需要专属槽位皮肤时，在面板 Prefab 上配置 `InventorySlotVisualProfile`，由 `Inventory.InitUI` 在动态槽位创建完成后统一应用；不要改通用 `UI_Slot.prefab` 做面板特判。该 Profile 只允许改 Sprite、图标/文字尺寸等表现，不得接管库存事务、拖拽或选择状态。
- 槽位鼠标与触屏拖放必须复用 `ItemSlot_UI.OnMouseDragBegin` / `OnMouseDragDrop` 的来源事务：命中 `ItemSlot_UI` 时直接在起始槽与目标槽之间移动、合并或双向交换，异类交换必须同时校验双方库存接收规则与整堆容量，禁止把目标物品经 `Inventory_Hand` 中转；只有未命中槽位时才把整组转入 `Inventory_Hand`，后续手机点击按轻触方向处理：连续拿取方向下同类已有物品从槽位取一件，放置方向下空槽/同类槽向目标放一件，异类槽交换，长按空槽或同类槽则一次性放下手上整组；同类目标容量不足时余量留在起始槽，空槽起手才转交父级 `ScrollRect`。
- 手机快捷栏轻触必须走独立 `OnTouchTap` 语义，只切换当前选中格或按单件规则取放；普通触屏拖放与桌面键鼠共用直接槽位事务，长按更久后的半组拖拽才以 `Inventory_Hand` 为来源。
- 跟随指针的 `UI_Hand` 是纯视觉层：Canvas 排序固定占用全局顶层（32767），必须高于快捷栏、设置页和其它游戏 UI；CanvasGroup/子图形不得拦截目标槽位射线。直接槽位拖拽生成的 `InventoryDragGhost` 也必须使用独立顶层 Canvas，不能只靠 `SetAsLastSibling`，否则会被独立 Canvas 的快捷栏/模态页压住。世界手持物挂在快捷栏节点及其子节点末端。
- 快捷栏选中框属于当前槽位背景层，切换时必须重新挂到目标槽位并置为首个兄弟；数量文本和物品图标保持在其上方，不能依赖独立 Canvas 的任意 `sortingOrder`。
- 玩家行囊的键鼠点击和滚轮无条件使用 `Inventory_Hand`，不能因携带槽为空或上次手柄操作留下的目标而回退快捷栏；桌面指针抬起实际进入 `OnDesktopTap`，只修改 `OnLeftClick` 不会恢复鼠标点击。PC 左键整组取放：空手按携带槽容量拿取，有物品时整组放置、同类合并或异类交换；不得转入 `OnTouchTap` 的单件语义，滚轮才逐件取放。点击与拖放共用整组跨库存事务，校验双向接收规则及容量、通知双方并同步快捷栏手持物；创造背包允许超量堆叠，但取出仍按目标容量与非堆叠规则拆分，余量保留原槽。快捷栏选中槽只参与手柄确认与角色当前装备，不参与 PC 背包交换。
- 创造背包的无限格数由 `CreativeInventoryState` 存在玩家 `flatworld.creativeInventory` 命名空间，`Mod_Inventory.Load` 在初始化槽位前恢复到 `Inventory_Data` 的运行时策略，不改 MemoryPack 布局。库存事务通过 `NotifyItemDataChanged` 维护尾部空槽；容量预检必须纯只读并计入可动态扩容的空间。新增槽的 UI 只同步表现，不重新初始化库存业务事件；快捷栏部分拾取后的余量必须继续尝试主背包，最后统一发布拾取数量。
- 快捷栏收到 Mobile `RightClick` 时必须允许当前手持物执行 `Act`，不能因触点位于手机“使用”按钮上而被 `IsPointerOverUI()` 拦截；键鼠右键仍保留 UI 遮挡检查。
- 快捷栏生成的手持物只注册到玩家 `Mod_FocusPoint`；左右翻身角由该模块读取 `Mod_TurnBack.CurrentTurnAngleY` 后与 Z 轴瞄准一次性合成，不能再把手持物根节点注册进 `controlledTransforms_Direction`。
- 需要“只从物品所在库存取料”的玩法统一使用 `InventoryContextResolver` 按 `ItemData` 引用/Guid 解析真实所属 `Inventory`；快捷栏手持物会命中 `Inventory_HotBar.RuntimeInventory`，普通背包命中对应 `Mod_Inventory.InventoryInstances`。实际扣除使用 `Inventory_Data.TryConsumeFirstByTag/TryConsumeFromSlot` 事务入口，不能直接改 `Stack.Amount`，否则快捷栏 UI、数据事件和后续持久化会失步。
- 丢弃统一经过 `Module_DiscardItem.DropItemByCount`；扣减 `ItemSlot.Amount` 后除触发槽位事件外，还必须按快捷栏槽位索引显式刷新 UI，兼容手机入口没有 `ItemSlot_UI` 引用的情况。
- 快捷栏拖拽到非 UI 区域后的整组丢弃由 `ItemSlot_UI` 世界长按回调转发到 `Module_DiscardItem`，落点使用触点屏幕坐标；UI 槽位长按放置路径保持独立。
- 快捷栏物品拖入 `Inventory_Hand` 后，移动端摇杆必须让出当前触摸所有权，避免长按世界丢弃时浮动摇杆抢占操作。
- 与背包并行打开的专用制作面板创建后必须调用 `InventoryPanelLayout.ApplyDefaultCraftingPosition`；只让背包靠左会在窄屏、安全区或 UI 缩放后由置顶背包覆盖左侧输入槽射线。
- 手机端已经拿起物品后的轻点/长按丢弃由 `MobileHeldItemDropSurface` 统一转发到 `Module_DiscardItem.TryDropHeldItemAtScreenPosition`，仅操作手部携带槽，空手不得取快捷栏选中物；中间空白触控面只在 `Inventory_Hand` 有物品时参与射线，`ItemSlot_UI` 的拖拽射线必须继续把该组件视为世界落点。
- `Mod_Plantable` 只通过 `IPlantableCrop` 初始化幼苗并判断地块占用；作物定义只配置 `cropItemId`，统一 `PlantingSummoner` 负责预览，禁止写死依赖某个成长模块或复用 `Mod_Building` 链路。
- 同一物品同时挂 `Mod_Plantable` 与 `Mod_Food` 时，右键动作按目标上下文仲裁：有效耕地由种植优先，无效种植目标则静默让给食用，不能一次动作同时播种和进食，也不能在正常进食时刷种植警告。
- 新版农业统一通过 `FarmlandSystem` 查询 `ChunkTerrainData`，禁止返回旧 `Chunk.Map`；锄地进度属于地格而非锄头实例。水肥计算使用临时 `TileData_Farmland` 快照，成长或施肥后必须 `CommitSoil`，否则数据修改不会进入权威环境层。
- 玩家播种作物由 `ChunkAgricultureRenderer` 管理，保存到独立的 `ChunkSaveRecord.AgricultureCells`；不得登记为 `ChunkNaturalItemRenderer` 的临时掉落物，否则区块解绑会回收且不保存。`ChunkView` 的同步/分帧保存入口均须抓取农业状态，退出世界不能当成收获删除快照。
- 普通农作物使用 `CropShell + Mod_Crop + Mod_CropYield + Mod_CropVisual`：`Mod_Crop` 只保存两阶段权威状态并调度 `ICropHarvestAction`，产物表和其他收获副作用必须拆成独立动作模块。
- `BerryCrop` 统一走普通 `CropShell + Mod_Crop + Mod_CropYield + Mod_CropVisual` 链，野外生态与耕地播种复用同一 Item 定义；药草、狗尾草等小型作物可继承 `BerryCrop` 后只覆盖成长参数与产物，禁止重新维护 `SmallCrop_Base` 或 `Bush` 双轨模板。
- 作物需要多张成长图时，在物品 `visual.spriteStates` 同时声明 `seedling/growing/mature`，由 `Mod_CropVisual` 根据 `normalizedGrowth` 派生表现阶段；不得为了中间画面给 `CropStage` 增加持久化阶段。只要声明任一阶段图就必须三张齐全，对象池卸载时恢复外壳原 Sprite。
- 世界植株与收获物必须保留独立 Item ID；种下时把植株重置为幼苗，一次性作物成熟交互后由动作生成食物/种子并销毁植株，持续采果植株只扣果实库存；不能把世界植株直接改成食物实例。
- `Mod_Grow` 继续承担树木与自然植物成长，并实现 `IPlantableCrop` 接入同一播种入口；水肥、天气与 `CropGrowthMultiplier` 在权威成长模块中各结算一次。
- 苹果/桃/柑橘这类果树优先复用 `AppleTree` 与 `Apple` 的 JSON 继承链：果实只覆盖营养、腐败和视觉，果树只覆盖采收物、气候、战利品和视觉；若需要野生生成，再单独在地表 `ecologyRules` 注册稳定规则，禁止为每种果树复制一套成长代码。
- 使用 `_BodyClip` 裁剪作物精灵时，必须给 `Mod_CropVisual` 绑定支持该属性的 `Sprite-Lit-Master` 材质；通用 `Prop` 外壳默认材质不提供 BodyClip。
- `_BodyMinV/_BodyMaxV` 实际传入 `Sprite.bounds` 的本地 Y，不是 UV；主体和交互描边 Shader 都必须保留 `DisableBatching=True`，否则精灵合批预变换顶点后可能按世界位置误裁整株作物。不能通过提高 Sorting Order 修复，也不能只保护主体而遗漏描边 Pass。
- 废弃 `Module_Equipment.cs` 不再使用。
- `Mod_Food` 的被动生命联动必须读取 `Mod_PlayerDeathState`；玩家濒死或 `DamageReceiver.Hp <= 0` 时停止回血与生存伤害，避免死亡状态被抬成极低正数。
- `Mod_Food.HealthState` 的回血判定只看蛋白质；`HealInterval/HealAmount` 大于 0 时按间隔一次性回血，动物继续使用 `HealNeedRatio`，玩家创建模板通过 `proteinHealThreshold` 配置绝对蛋白质门槛。
- `Mod_Food` 仅在基础营养持续消耗或 `IFoodTickObserver` 规则要求时进入 `FixedInterval`；无角色模块的静态世界食物应休眠，库存腐败仍由 `IModuleDataTickObserver` 独立推进，可选角色模块必须静默查询。
- `Module_HeldFood` 的咬痕只读取 `EatingProgress` 与 `Max_EatingProgress`，按物品 GUID 确定性重建当前轮廓遮罩，不重复持久化随机点；实际口数取最大进度的向上整数，最后一口直接清空残余区域。

- `IInventoryHeatTreatment` 处理输入槽里的状态型容器，普通熔炼复用 `CraftingRecipeMatcher` 和 `CraftingTransaction`。制盐必须先确保输出事务成功再减海水，处理进度属于容器而非炉体；满输出时不清水、不重复发盐。
- 水罐靠物品体积大于 1 的既有规则禁止堆叠，水量／水质在模块状态中；不得只按同一物品 ID 合并不同状态的罐。部分转移保持水量守恒，归属、活体和距离在每次动作时检查。
- 食物机制除了目录注册，也组合消费物本身实现 `IFoodMechanic` 的模块；药品使用守卫与消费完成观察者应接入这条链，不在按钮响应时直接回血。
- `CraftingOutputRules` 在预检和真实提交共用；防腐加工的新鲜度必须来自匹配计划实际消耗的原料，保留最差剩余比例，不能读取整份输入库存或重建全新寿命。
- 植物环境通过 `IPlantEnvironmentCondition` 与 `PlantClimateTimeline` 结算；自主耐候树木只向成长器提供 `IPlantGrowthConstraint`，不能被两个模块重复推进。补算游标未追上当前时钟时禁止抢先收获。

## 验证

- 覆盖满包、回滚、普通合成多候选精确选择、Tag 全局分配、加热镜像/紧凑网格、快捷栏/手持同步、输入锁和作物存档往返。
- UI 契约联动 UI Skill；Item 生命周期联动 Item Skill；配方/农业存档联动 Data Skill。
- JSON 物品迁移时可以暂不填写 `visual` 图标；库存槽位的统一显示入口必须回退到 shell Prefab 的 `SpriteRenderer`，否则已有物品会在快捷栏中变成空槽。
- 默认不主动跑测试；需要时运行 `InventoryCrafting.Smoke`，专项用 `.Core`/`.Agriculture`。测试目录：`Assets/GameTest/InventoryCrafting/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

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

- 库存液体原料通过 `LiquidDefinition.sourceItemId` 唯一映射到液体；每个完整物品对应一份，容器拖入先校验同液体与整份容量，再从真实所属库存调用 `TryConsumeFromSlot`，数量不得超过 `InventoryDragTransaction.DraggedAmount`。不能把液体原料伪装成容器变体；已有进食进度的原料不能再按完整一份装液。浮点残余容量不足一份时不扣料，容器之间仍允许按原有规则部分转液。

- 配方 JSON 是唯一真源；旧 Recipe/CookRecipe SO 只作 MOD 兼容，不恢复双重维护。
- 内容工坊的普通合成使用不限长度的滚动材料清单，保存为连续的一维输入；载入旧网格配方时按材料身份归并数量，并保留 `amount=0` 的不消耗工具。只有热加工继续使用 3×3 位置画布。保存前必须使用运行时配方工厂校验整份启用目录，并保留已有配方的未知顶层字段。
- 所有制作入口调用 `CraftingService`；匹配由 `CraftingRecipeMatcher`，扣料/产出由 `CraftingTransaction` 原子提交。
- `Mod_Mortar` 每次有效加工手势执行一份单原料、多产物配方；加工手势包括“提起后下压到接触线”的捣击，以及石棒贴近碗底累计横移达到配置阈值的研磨步进，两者统一发布同一加工意图。输入与输出共享动态 `Inventory`，统一由 `CraftingService` 原子扣料和写入，禁止整堆改 ID。提交前按全部产物预留空槽；事务优先合并可堆叠产物，剩余原料独立保留。事务通知期间合并刷新，避免槽位变化取消正在拖动的石棒；动态容量策略在 Load 恢复，槽位和内容独立持久化。
- 石臼面板使用正式透明槽位模板动态克隆，数量增加不等于创建新槽；同类满堆或不同产物才占新格。空槽使用碗内轮廓投料，已有物品的整个槽位随重力移动，确保命中区与图标一致。可见物品分页，关闭面板只复位视觉，不清空库存；父节点失活期间 `OnDisable` 只能清理手势、协程和临时投料表现，禁止调用 `SetSiblingIndex/SetAsLastSibling` 等层级排序，完整槽位布局复位应由层级稳定时的显式开关流程执行。`ItemSlot_UI.ItemAddedAtPointer` 只在点击/拖放事务实际增加物品后发布位置反馈；石臼数量、种类或槽位扩容不能重排已有物品，合并投料只用无射线的图标表现下落，停稳保留落点。透明槽位通过 `ItemSlot_UI.selectionGraphic` 把选择/拖入描边指定到图标，不能对透明背景使用忽略 Alpha 的 Outline，否则会出现整块黄色方形。

- 玩家手工台 `Mod_HandCraftTable` 与世界工作台 `Mod_MakeTable` 均使用 `RecipeType.Crafting`；配方通过可选 `requiredStation` 区分制作入口：留空表示任意普通制作入口，`handcraft` 表示随身手工，`workbench` 表示世界工作台。匹配器用 `CraftingCapabilities.StationId` 做能力过滤，MOD 可复用字符串 ID 扩展新工作站，禁止按具体配方 ID 硬编码。当前手工台固定 4 输入/2 输出，当前工作台固定 5 输入/2 输出，运行时与 Prefab 序列化槽位必须严格一致；槽位数属于具体工作站能力，未来工作站可声明更多输入槽，配方、内容工坊和普通合成匹配器不得设置全局材料数量上限。
- 多产物必须全部放下才提交；失败不扣料、不部分产出。是否允许堆叠只读取物品 `Stackable`，不得再用重量或体积阈值推导；`Stackable=false` 的产物每个单位必须独占一个空槽，不能把 `amount > 1` 整组塞进单槽。`amount=0` 参与签名但不消耗。
- `RecipeType.Crafting` 必须配置 `inputRule: "unordered"` 且 `allowMirror: false`；普通合成只比较材料身份与总量，同类材料可以集中堆叠或分散在任意输入槽。配方需求按当前输入的可满足子集匹配，额外放入的无关材料不得屏蔽候选，也不得在制作所选配方时被扣除；因此候选扫描配方目录即可覆盖输入材料的全部可制作组合。加热加工才允许有序、镜像和网格规则，并继续保持严格输入语义。
- 普通合成输入必须通过 `CraftingRecipeMatcher.TryMatchAll` 保留全部材料候选，`CraftingStationController` 统一维护候选、选择、进度与双输出预览，最终使用所选 `RuntimeRecipe` 精确预检和原子提交，禁止重新回退到目录首个匹配项。Exact/Tag 候选重叠时扣料计划必须按全部需求做全局容量分配，禁止逐项贪心消耗。
- 配方产物合法性与 `ItemData` 创建必须读取 `GameRes.ItemDefinitions`；`AllPrefabs` 只保存表现壳和别名，禁止用它决定候选按钮或“开始制作”是否可用，否则 JSON 物品会出现候选已选中但提交按钮被错误禁用。
- `GameRes.recipeDict` 是尚未迁移 `CraftingService` 的熔炼流程专用旧输入签名索引；`RecipeType.Crafting` 允许相同输入对应多个候选，必须只注册到 `CraftingRecipeCatalog`，禁止写入这个单值字典或把同输入多候选误判为冲突。
- 配方动作在库存事务成功后执行；异常恢复快照。玩法进度信号只在最终成功后发布。
- 制作输入变化、事务扣料和面板初始化都会被动刷新预览；此时 `RecipeNotFound` 是合法的“当前无配方”状态，应清空预览且不输出 Warning。只有用户主动提交前检查失败，或库存、产物等结构性异常，才输出制作诊断。
- 制作模块的 `Save()` 只负责持久化，不能解绑输入、输出、按钮或交互监听；这些运行时事件统一在 `Unload()` 中成对清理，由 Item 退出、移除模块与回池生命周期调用，否则自动保存会让预览与制作按钮永久失效。
- 模态库存才获取输入锁；快捷栏和 `Inventory_Hand` 不锁玩家输入。
- 单个库存面板需要专属槽位皮肤时，在面板 Prefab 上配置 `InventorySlotVisualProfile`，由 `Inventory.InitUI` 在动态槽位创建完成后统一应用；不要改通用 `UI_Slot.prefab` 做面板特判。该 Profile 只允许改 Sprite、图标/文字尺寸等表现，不得接管库存事务、拖拽或选择状态。
- 槽位鼠标与触屏拖放必须复用 `ItemSlot_UI.OnMouseDragBegin` / `OnMouseDragDrop` 的来源事务：命中 `ItemSlot_UI` 时直接在起始槽与目标槽之间移动、合并或双向交换，异类交换必须同时校验双方库存接收规则与整堆容量，禁止把目标物品经 `Inventory_Hand` 中转；只有未命中槽位时才把整组转入 `Inventory_Hand`，后续手机点击按轻触方向处理：连续拿取方向下同类已有物品从槽位取一件，放置方向下空槽/同类槽向目标放一件，异类槽交换，长按空槽或同类槽则按统一长按时间把进度转发到唯一的 `Inventory_Hand` 手部插槽显示，并在进度走满时立即一次性放下手上整组，不等待松手；通用 `UI_Slot.prefab` 不承载这层进度视觉。长按进度视觉必须先经过短按/拖拽判定缓冲（当前 0.2 秒）再显示，缓冲期内转为拖拽则始终不出现进度条，且该视觉缓冲不得延长原有长按提交总时长。提交成功后该手势必须被消费，不能继续进入半组拖拽；同类目标容量不足时余量留在起始槽，空槽起手才转交父级 `ScrollRect`。
- 库存拖拽物需要触发“非槽位玩法目标”时统一实现 `IInventoryDragDropTarget`，并通过 `InventoryDragTransaction.TryConsumeSourceItem` 在保留来源槽位的前提下修改拖拽物状态；目标区域负责拦截有效落点，不能先把物品转入 `Inventory_Hand` 再回写。液体容器面板使用这条链把其它容器拖到罐体剖面进行守恒转液：当前手持来源优先走运行时 `Mod_WaterVessel.TransferTo`，非手持库存来源原地更新 `ItemData` 的液体状态并通知真实所属库存刷新。
- 手机快捷栏轻触必须走独立 `OnTouchTap` 语义，只切换当前选中格或按单件规则取放；普通触屏拖放与桌面键鼠共用直接槽位事务，长按更久后的半组拖拽才以 `Inventory_Hand` 为来源。
- 跟随指针的 `UI_Hand` 是纯视觉层：Canvas 排序固定占用全局顶层（32767），必须高于快捷栏、设置页和其它游戏 UI；CanvasGroup/子图形不得拦截目标槽位射线。直接槽位拖拽生成的 `InventoryDragGhost` 也必须使用独立顶层 Canvas，不能只靠 `SetAsLastSibling`，否则会被独立 Canvas 的快捷栏/模态页压住。世界手持物挂在快捷栏节点及其子节点末端。
- `UI_Hand` 的桌面跟随可以读取 Input System `Pointer.current`；触屏库存与世界丢弃必须以该次手势自己的 `PointerEventData.position` 为权威，并在触点抬起后保留最后位置，直到桌面库存指针明确接管。禁止用全局 `Pointer.current` 持续覆盖触屏位置：Device Simulator、多指或触点结束时它可能切到模拟鼠标/另一触点，把手持槽钉到错误坐标。所有进入表现层的坐标仍需过滤 NaN/Infinity。
- 快捷栏选中框属于当前槽位背景层，切换时必须重新挂到目标槽位并置为首个兄弟；数量文本和物品图标保持在其上方，不能依赖独立 Canvas 的任意 `sortingOrder`。
- 玩家行囊的键鼠点击和滚轮无条件使用 `Inventory_Hand`，不能因携带槽为空或上次手柄操作留下的目标而回退快捷栏；桌面指针抬起实际进入 `OnDesktopTap`，只修改 `OnLeftClick` 不会恢复鼠标点击。PC 左键整组取放：空手按携带槽容量拿取，有物品时整组放置、同类合并或异类交换；不得转入 `OnTouchTap` 的单件语义，滚轮才逐件取放。点击与拖放共用整组跨库存事务，校验双向接收规则及容量、通知双方并同步快捷栏手持物；创造背包允许超量堆叠，但取出仍按目标容量与非堆叠规则拆分，余量保留原槽。快捷栏选中槽只参与手柄确认与角色当前装备，不参与 PC 背包交换。
- 物品的 `weight`（kg）、`volume`（L）和 `stackable` 是三个独立维度；重量/体积只参与玩家携带容量，不能决定堆叠资格。普通玩家主背包默认就是动态容量：基础保持 27 格；总空余槽位 <= 2 时一次补足到 3 个空槽；总槽位 > 27 且存在多余空槽时按 `max(27, 已占用槽位 + 3)` 收缩，避免 27/28 格之间反复扩缩。格子数量不构成携带容量限制；玩家主背包、`Inventory_Hand` 与快捷栏对 `stackable=true` 物品统一使用单格无限堆叠，重量/体积上限继续按“主背包 + 快捷栏”统一携带统计执行，不能让手持/快捷栏的槽位上限成为第二套容量规则。世界拾取也使用同一统计口径，避免先塞快捷栏绕过上限。行囊 Footer 同样显示当前值/上限。
- 行囊“整理”按钮使用渐进语义：每轮第一次点击只合并可堆叠物并压紧空槽，不改变物品原有相对顺序；库存保持整齐后继续点击，按物品定义、数量、当前总重量、当前总体积依次循环排序。若中途再次出现空洞或可继续合并的堆叠，下一次点击先回到整理阶段并从首个排序规则重新开始。
- `CreativeInventoryState` 只保存创造模式的重量/体积容量豁免，不再控制主背包格子数量；普通背包与创造背包都使用同一套玩家主背包动态槽位策略。创造背包目录必须排除 `BuildingRole.PlacedBuilding` 的落地建筑本体，以及当前静态定义 `CanBePickedUp=false` 的树、矿点、作物、传送口等世界专用实体，只保留真正可持有的物品和建筑召唤器；重复召唤创造背包时也要清理历史遗留的世界实体槽位。创造背包会把已入包物品的运行态 `CanBePickedUp` 改成 false，因此清理旧槽位时必须回到当前 `RuntimeItemDefinition` 判断，禁止读取槽位里的该标志反推是否可持有。便携设施过滤时应优先用当前 `ItemData.IDName` 与 `BuildingPrefabId/SummonerPrefabId` 的载体身份对应关系判定，不能只信历史 `Role`，否则旧背包状态可能把落地本体误当成可持有召唤器。创造模式额外绕过重量/体积上限，但 `Stackable=false` 仍严格一件一格。库存事务通过 `NotifyItemDataChanged` 只负责及时补足预留空槽，周期容量自检再负责安全收缩多余空槽，避免在数据变更事件分发前移除刚变化的槽位引用；容量预检必须纯只读并计入可动态扩容的空间。动态增减槽位的 UI 只同步表现，不重新初始化库存业务事件；快捷栏部分拾取后的余量必须继续尝试主背包，最后统一发布拾取数量。
- 快捷栏收到 Mobile `RightClick` 时必须允许当前手持物执行 `Act`，不能因触点位于手机“使用”按钮上而被 `IsPointerOverUI()` 拦截；键鼠右键仍保留 UI 遮挡检查。
- 快捷栏生成的手持物只注册到玩家 `Mod_FocusPoint`；左右翻身角由该模块读取 `Mod_TurnBack.CurrentTurnAngleY` 后与 Z 轴瞄准一次性合成，不能再把手持物根节点注册进 `controlledTransforms_Direction`。
- 需要“只从物品所在库存取料”的玩法统一使用 `InventoryContextResolver` 按 `ItemData` 引用/Guid 解析真实所属 `Inventory`；快捷栏手持物会命中 `Inventory_HotBar.RuntimeInventory`，普通背包命中对应 `Mod_Inventory.InventoryInstances`。实际扣除使用 `Inventory_Data.TryConsumeFirstByTag/TryConsumeFromSlot` 事务入口，不能直接改 `Stack.Amount`，否则快捷栏 UI、数据事件和后续持久化会失步。
- 丢弃统一经过 `Module_DiscardItem.DropItemByCount`；扣减 `ItemSlot.Amount` 后除触发槽位事件外，还必须按快捷栏槽位索引显式刷新 UI，兼容手机入口没有 `ItemSlot_UI` 引用的情况。
- 快捷栏拖拽到非 UI 区域后的整组丢弃由 `ItemSlot_UI` 世界长按回调转发到 `Module_DiscardItem`，落点使用触点屏幕坐标；UI 槽位长按放置路径保持独立。
- 快捷栏物品拖入 `Inventory_Hand` 后，移动端摇杆必须让出当前触摸所有权，避免长按世界丢弃时浮动摇杆抢占操作。
- 与背包并行打开的专用制作面板创建后必须调用 `InventoryPanelLayout.ApplyDefaultCraftingPosition`；只让背包靠左会在窄屏、安全区或 UI 缩放后由置顶背包覆盖左侧输入槽射线。
- 手机端已经拿起物品后的轻点/长按丢弃由 `MobileHeldItemDropSurface` 统一转发到 `Module_DiscardItem.TryDropHeldItemAtScreenPosition`，仅操作手部携带槽，空手不得取快捷栏选中物；中间空白触控面只在 `Inventory_Hand` 有物品时参与射线，`ItemSlot_UI` 的拖拽射线必须继续把该组件视为世界落点。
- `Inventory` 持有的 `item` 是 `UnityEngine.Object`；生命周期边界禁止用 `item?.GetComponent...` 判断存活，因为 C# 空条件运算符不会触发 Unity 的“已销毁对象视为 null”语义。玩家/容器卸载时必须先解除库存输入监听并清空所属 `item`，`Mod_Hand.Unload` 同时清理 `Inventory_Hand.PlayerHand`，避免槽位 `OnDisable` 或延迟 UI 回调访问上一轮玩家。
- `Mod_Plantable` 只通过 `IPlantableCrop` 初始化幼苗并判断地块占用；作物定义只配置 `cropItemId`，统一 `PlantingSummoner` 负责预览，禁止写死依赖某个成长模块或复用 `Mod_Building` 链路。
- 同一物品同时挂 `Mod_Plantable` 与 `Mod_Food` 时，右键动作按目标上下文仲裁：有效耕地由种植优先，无效种植目标则静默让给食用，不能一次动作同时播种和进食，也不能在正常进食时刷种植警告。
- 新版农业统一通过 `FarmlandSystem` 查询 `ChunkTerrainData`，禁止返回旧 `Chunk.Map`；锄地进度属于地格而非锄头实例。水肥计算使用临时 `TileData_Farmland` 快照，成长或施肥后必须 `CommitSoil`，否则数据修改不会进入权威环境层。
- 玩家播种作物由 `ChunkAgricultureRenderer` 管理，保存到独立的 `ChunkSaveRecord.AgricultureCells`；不得登记为 `ChunkNaturalItemRenderer` 的临时掉落物，否则区块解绑会回收且不保存。`ChunkView` 的同步/分帧保存入口均须抓取农业状态，退出世界不能当成收获删除快照。
- 普通农作物使用 `CropShell + Mod_Crop + Mod_CropYield + Mod_CropVisual`：`Mod_Crop` 只保存两阶段权威状态并调度 `ICropHarvestAction`，产物表和其他收获副作用必须拆成独立动作模块。
- `BerryCrop` 继续让野外生态与耕地播种复用同一 `CropShell` Item 定义，但持续采果不能走一次性 `Mod_CropYield`：由 `Mod_Collectable` 同时实现 `ICropHarvestAction/ICropHarvestPolicy` 保存果实库存并保留成熟植株，单次交互严格消费 1 份库存并掉落 1 个果实，不使用全局掉落数量倍率放大单次采摘；`Mod_Production` 只在成熟且库存未满时周期补果，果实提示跟随库存显隐；继承得到的 `Mod_CropYield` 必须禁用，避免一次采摘销毁植株或额外掉落种子。普通一次性作物仍保持 `Mod_Crop + Mod_CropYield + Mod_CropVisual`。药草、狗尾草等一次性小型作物不要直接继承这套持续采果行为。
- 野外自然生成、允许玩家用武器清除的小型作物统一继承 `WildCrop_Base`；该抽象定义负责成熟自然初态、通用 `DamageReceiver`、植被受击材质和独立 DamageReceiver Trigger，具体作物只按外形/耐久覆盖 HP 与伤害碰撞尺寸。仅种植链使用的萝卜、水稻不因该规则自动获得生命模块；具体死亡掉落仍由各物品顶层 `lootTableId` 定义，禁止把通用掉落塞进 `WildCrop_Base`。
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

- `IInventoryHeatTreatment` 处理输入槽里的状态型液体容器，普通熔炼复用 `CraftingRecipeMatcher` 和 `CraftingTransaction`。具体液体的转化温度、时间、结果液体或副产物由 `LiquidDefinition.HeatProcess` 声明；需要产出物品时必须先确保输出事务成功再消耗液体，处理进度属于容器而非炉体。
- 通用液体容器由 `Mod_WaterVessel` 承载，但状态只保存稳定 `LiquidId + Amount + ProcessingSeconds`，其中 `Amount` 是可持久化的浮点“份数”，用于表达连续液体余量；液体语义统一来自 `GameRes.LiquidDefinitions`。容器通过显式 `Stackable=false` 禁止堆叠，不同液体不能自动混装，部分转移保持数量守恒。新增本体或 MOD 液体不得复制容器 Item 变体，应该注册新的 `LiquidDefinition` 并复用同一容器模块。
- 液体容器的“饮水”按钮只按 `LiquidDefinition.Drinkable` 与剩余量决定是否可点，不得用当前补水收益或角色水分已满来阻止玩家主动饮用；`Mod_Food.DrinkWater` 即使实际补水量为 0 也要发布一次完整饮水结果。感染、脱水等饮用后果统一声明在 `LiquidDefinition.drinkEffects`，世界水源与容器都通过 `LiquidDrinkEffectProcessor` 结算，禁止在水地块或具体容器里按液体 ID 再写一套后果逻辑。
- 世界液体来源通过地块的 `IWorldLiquidSourceData.LiquidId` 接入 `WorldLiquidSourceResolver`，再解析到同一 `LiquidDefinition` 目录；容器不得按水 Tile 名称或盐度自行猜液体 ID。准心高亮与实际装液必须复用同一个 WorldCell 解析入口，并通过容器 `AddLiquid` 等统一 API 提交状态。
- 液体容器在背包/快捷栏中的图标同样由容器模块状态驱动：优先使用液体 `visualState` 对应的 `visual.spriteStates`，缺少专用状态时统一回退容器的 `filled` 状态，空容器使用 `empty`；禁止按水种类或容器 Item ID 写死 UI 分支。容器模块原地修改 `ModuleData` 后必须通过 `Inventory_Data.NotifyItemStateChanged` 通知真实所属库存；该入口必须同时发布槽位刷新与库存级 `Event_RefreshUI`，否则快捷栏等专用库存可能保留旧图标。
- 快捷栏当前手持实例会把 `Item.OnUIRefresh` 绑定到 `Inventory_HotBar.RefreshUI`；手持物模块只修改内部状态而不替换 `ItemData` 引用时，除了通知真实所属库存，还必须发布 `Item.OnUIRefresh`，使当前快捷栏槽立即重画状态型图标，不能只刷新世界中的手持 Sprite。
- `UI_WaterVessel` 的拖拽倾倒只消费现有 `Mod_WaterVessel.RemoveLiquidAmount`，不保存独立手势状态。绝对倾角定义当前姿态的最大保液量：直立 0°=100%，水平 90°=50%，倒扣 180°=0%，按浮点份数连续结算；实际余量只允许下降，扶正不能恢复已倒出的液体。空容器仍允许完整拖动和自动回正，只提供交互反馈、不产生液体扣减。手势必须按独立 `pointerId` 持有触点，并在不随罐体旋转的父级坐标系计算：外圈拖拽优先按绕罐体中心的极角变化解释，因此左右、上下、斜向及半圆轨迹均可倾倒；中心起手才退化为二维线性位移。方向契约固定为屏幕右侧手势产生负 Z（顺时针、朝右倒），屏幕左侧手势产生正 Z（逆时针、朝左倒），圆弧与线性回退必须一致。来回改变倾角只驱动 `WaterVesselLiquidGraphic` 的短时波动，真实流失量仍由容器状态决定；罐口外液流由独立 `WaterVesselPourGraphic` 读取本次真实移除量做表现，禁止用视觉帧反向扣减玩法数据。
- 食物机制除了目录注册，也组合消费物本身实现 `IFoodMechanic` 的模块；药品使用守卫与消费完成观察者应接入这条链，不在按钮响应时直接回血。
- `CraftingOutputRules` 在预检和真实提交共用；防腐加工的新鲜度必须来自匹配计划实际消耗的原料，保留最差剩余比例，不能读取整份输入库存或重建全新寿命。
- 植物环境通过 `IPlantEnvironmentCondition` 与 `PlantClimateTimeline` 结算；自主耐候树木只向成长器提供 `IPlantGrowthConstraint`，不能被两个模块重复推进。补算游标未追上当前时钟时禁止抢先收获。

## 验证

- 覆盖满包、回滚、普通合成多候选精确选择、Tag 全局分配、加热镜像/紧凑网格、快捷栏/手持同步、输入锁和作物存档往返。
- UI 契约联动 UI Skill；Item 生命周期联动 Item Skill；配方/农业存档联动 Data Skill。
- JSON 物品迁移时可以暂不填写 `visual` 图标；库存槽位的统一显示入口必须回退到 shell Prefab 的 `SpriteRenderer`，否则已有物品会在快捷栏中变成空槽。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

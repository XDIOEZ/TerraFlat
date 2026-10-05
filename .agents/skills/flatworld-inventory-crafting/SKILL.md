---
name: flatworld-inventory-crafting
description: "Use when: 定位或修改 FlatWorld 的背包、槽位、快捷栏、手持、容器、工作台、制作配方、装备、食物、种子、植物生长、耕地或相关 Prefab/SO。关键词：Inventory、Mod_Inventory、Crafting、Mod_Equipment、Mod_Food。"
---

# FlatWorld 背包、制作与农业

## 入口

- 落地工作台、炉体、储物/堆肥/晾架及便携设施本体使用 `World/Machines`，先读 `flatworld-machines/SKILL.md`；原有部分 Mod 脚本仅作内容模板，不能重新增加落地 Item 模拟链。

- 库存：`Assets/5_Scripts/5-3_GamePlay/Items/Inventory/{Inventory,Mod_Inventory,Inventory_UI,Mod_HotBar,ItemSlot_UI}.cs`
- 制作：`Assets/5_Scripts/5-3_GamePlay/Items/Crafting/`
- 固定/多物料配方真源：`Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json` 及分包 JSON；单物料的通用加工响应（如 grind）内聚在输入物品的 `ItemDefinition.processing`。
- 配方可视化编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`，Unity 菜单为 `FlatWorld/内容配置/内容工坊`
- 装备：`Items/Equipment/{Mod_Equipment,Equipment_SO,EquipmentInstance*,Mod_EquipmentStore}.cs`
- 装备能力必须由宿主 Prefab 或 JSON 显式组合 `Mod_Equipment`；普通 Item/Actor 不得由 `Item` 基类自动补齐装备模块。AI/普通实体的装备逻辑按 `EquipmentSlot.*` 标签选槽，不能依赖玩家 UI 索引，也不能回退绑定全局 `Inventory_Hand.PlayerHand`。
- `Mod_Equipment` 只在宿主没有其它当前可用世界交互时响应 E；传送门、机器、作物等主玩法交互必须优先。
- 温度衣物采用耐受范围语义：`EquipmentInstance_ThermalInsulation` 只降低安全体表温度下限或提高上限，不修改当前体表温度、环境目标温度或传热速度；耐寒与耐热使用独立数值。
- 食物/农业：`Entities/Item/Mod_Food.cs`、种子/成长模块与 `Mod_Grow.AuthoritativeCrop.cs`
- 移动营养消耗：`Entities/Move/Mod_Mover.cs` 与 `Mod_Food` 分别维护营养、水分的移动倍率。
- Prefab：`Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`

## 不变量

- 多容器模块完成全部库存初始化后统一绑定输入；未配置 `ToggleActionName` 的常驻容器不依赖控制器输入资产。

- 体力恢复按 `Mod_Stamina.MaxValue` 的有效上限判断，不能用原始 `Data.MaxStamina`，避免缺盐等容量下降后反复消耗营养；体力模块卸载时释放自己创建的 HUD。

- 背包“排序”按钮依次循环稳定 ID、分类、数量、重量、体积五种独立规则；只有“分类”模式读取物品 `ItemData.Tags` 中的 `InventoryGroup.<类别>` 标签分组，无分组物品排在分组之后，同组内再按稳定 ID 排序。机械扭矩节点及其便携召唤器使用 `InventoryGroup.MechanicalPower`，MOD 物品可声明同一标签加入；纯手动锻造设备不属于该组。

- F5 原位应用模块参数不能重置生产运行态；`Mod_Production.ApplyResourceConfiguration` 复用按产物身份匹配的 `RestoreRuntimeProgress`，保留累计生产时间、次数和初始化标记，不重新 Load 或改写库存。未变化的参数集合不重新构造。

- 库存液体原料通过 `LiquidDefinition.sourceItemId` 唯一映射到液体；每个完整物品对应一份，容器拖入先校验同液体与整份容量，再从真实所属库存调用 `TryConsumeFromSlot`，数量不得超过 `InventoryDragTransaction.DraggedAmount`。不能把液体原料伪装成容器变体；已有进食进度的原料不能再按完整一份装液。容器液量只保存整数份，任何不足一份的转移都不结算。

- 固定/多物料配方继续以 Recipe JSON 为真源；单物料通用加工以物品 `processing` 为真源，由 `ItemProcessingResolver` 适配成临时 `RuntimeRecipe` 后继续走 `CraftingService`。通用加工可用 `minLevel/maxLevel` 声明发出者等级闭区间；有区间时缺少等级、低于下界或高于上界都拒绝。物品发出能力写在 `processingCapabilities.<capability>.level`，不要拿战斗伤害或资源采集 `HarvestTier` 代替加工等级。旧 CookRecipe/熔炼 Recipe SO 只作热加工 MOD 兼容，普通合成不再载入旧 SO。
- 配方输出可通过可选 `durabilityMultiplier` 为同一产物定义赋予实例耐久品质；倍率必须为大于 0 的有限数，由 `CraftedDurabilityQuality` 在预览与真实提交共用的产物创建阶段应用，并写入 `ItemData.CraftedDurabilityMultiplier`。禁止为单个配方另写按配方 ID 硬编码的输出规则，否则容易与通用倍率重复叠乘。
- 金属手钻的钻头质量读取实际扣除矿锭的 `DrillDurability:<正数>` Tag，并通过 `CraftingOutputRules` 写入产物实例；动态耐久必须同时持久化到共享“手钻模块”，放置/拆回及 ItemDefinition 读档重建后再恢复，不能只改临时 `ItemData.MaxDurability`。MOD 矿锭可通过同一 Tag 接入。
- 内容工坊的普通合成使用不限长度的滚动材料清单，按物品或标签身份填写总数量，`amount=0` 表示必须存在但不消耗的工具。只有热加工继续使用 3×3 位置画布。保存前必须使用运行时配方工厂校验整份启用目录，并保留已有配方的未知顶层字段。
- 所有制作入口调用 `CraftingService`；匹配由 `CraftingRecipeMatcher`，扣料/产出由 `CraftingTransaction` 原子提交。
- 普通石臼只提供 `ProcessCapability=grind`，禁止重新按 `requiredStation=mortar` 维护具体物品配方；投入物是否可研磨、输入量、产物和工作量由该物品自己的 `processing.grind` 声明，且 `processing` 不从 parent 自动继承，避免模板物品把具体加工产物泄漏给子定义。运行时由 `ItemProcessingResolver` 生成单原料、多产物 `RuntimeRecipe`，`work` 同时决定石臼需要累计的有效加工手势数；捣击和贴底研磨步进统一发布加工意图。累计进度与当前配方 ID 必须保存在 `MortarState`，配方切换或原料不再匹配时清零；达到工作量后仍由 `CraftingService` 原子扣料和写入，失败时保留已完成进度。坩埚继续使用独立的热加工工作站配方，不走 grind。输入与输出共享固定 `Inventory`，默认 6 格，可由 `Mod_Mortar.SlotCount`/JSON 配置；手持、落地及未物化坩埚热加工都用相同固定格数。禁止整堆改 ID 或按产物临时加格；事务优先合并可堆叠产物，剩余原料独立保留，放不下全部产物时不扣料。事务通知期间合并刷新，避免取消正在拖动的石棒；槽位和内容独立持久化。
- `Inventory.basePanel` 只表示由 `Inventory.InitUI` 管理且含 `UI_Content` 的槽位面板；石臼等自管槽位的组合面板只传给 `SyncQuickTransferTarget` 判断快捷转移窗口状态，不得写入 `basePanel`，否则动态扩容通知会误走通用槽位初始化。
- 储物库存可在序列化 `Inventory` 实例上配置 `StorageMaxWeightKg` 与 `StorageMaxVolumeCubicMeters` 两个正值；`Inventory.InitData` 将立方米按 `LitersPerCubicMeter` 转为库存内部 L 后应用容量策略。`Mod_Inventory.Load` 恢复 `Inventory_Data` 后须重新应用容量策略，固定槽位与物品内容仍由 `Inventory_Data.itemSlots` 持久化。受限非玩家库存的重量/体积 UI 读自身 `Inventory_Data`，玩家行囊继续通过 `PlayerCarryCapacityUtility` 合并统计快捷栏；体积在逻辑与存档中沿用 L，UI 转为 m³ 显示。
- 石臼面板使用正式透明槽位模板动态克隆，数量增加不等于创建新槽；同类满堆或不同产物才占新格。空槽使用碗内轮廓投料，已有物品的整个槽位随重力移动，确保命中区与图标一致。可见物品分页，关闭面板只复位视觉，不清空库存；父节点失活期间 `OnDisable` 只能清理手势、协程和临时投料表现，禁止调用 `SetSiblingIndex/SetAsLastSibling` 等层级排序，完整槽位布局复位应由层级稳定时的显式开关流程执行。`ItemSlot_UI.ItemAddedAtPointer` 只在点击/拖放事务实际增加物品后发布位置反馈；石臼数量、种类或槽位扩容不能重排已有物品，合并投料只用无射线的图标表现下落，停稳保留落点。透明槽位通过 `ItemSlot_UI.selectionGraphic` 把选择/拖入描边指定到图标，不能对透明背景使用忽略 Alpha 的 Outline，否则会出现整块黄色方形。

- 玩家手工台 `Mod_HandCraftTable` 与世界工作台 `Mod_MakeTable` 均使用 `RecipeType.Crafting`，并通过 `CraftingCapabilities.CompatibleStationIds` 互认 `handcraft`/`workbench` 配方；留空的 `requiredStation` 仍对普通制作入口开放，其它工作站 ID 保持精确匹配，MOD 可声明兼容标识而不按配方 ID 硬编码。世界工作台按相同手工基准的 70% 计算点击次数，取最近整数且至少一次。手工台固定 4 输入/2 输出，世界工作台固定 5 输入/2 输出；匹配器读取配置槽位内的材料，输出满时由事务拒绝整次制作，不扣材料也不补格。
- 多产物必须全部放下才提交；失败不扣料、不部分产出。是否允许堆叠只读取物品 `Stackable`，不得再用重量或体积阈值推导；`Stackable=false` 的产物每个单位必须独占一个空槽，不能把 `amount > 1` 整组塞进单槽。`amount=0` 参与签名但不消耗。
- `RecipeType.Crafting` 使用 `inputRule: "unordered"`，`inputs` 中同一物品或标签只写一条总数量；不得写 `slot/gridWidth/gridHeight/allowMirror`，动作按 `targetRole` 找工具，不得写 `slotIndex`。普通合成只比较材料身份与总量，同类材料可以集中堆叠或分散在任意输入槽。配方需求按当前输入的可满足子集匹配，额外放入的无关材料不得屏蔽候选，也不得在制作所选配方时被扣除；因此候选扫描配方目录即可覆盖输入材料的全部可制作组合。加热加工才允许有序、镜像和网格规则，并继续保持严格输入语义。
- 普通合成输入必须通过 `CraftingRecipeMatcher.TryMatchAll` 保留全部材料候选，`CraftingStationController` 统一维护候选、选择、进度与输出槽预览，最终使用所选 `RuntimeRecipe` 精确预检和原子提交，禁止重新回退到目录首个匹配项。Exact/Tag 候选重叠时扣料计划必须按全部需求做全局容量分配，禁止逐项贪心消耗。
- 配方产物合法性与 `ItemData` 创建必须读取 `GameRes.ItemDefinitions`；`AllPrefabs` 只保存表现壳和别名，禁止用它决定候选按钮或“开始制作”是否可用，否则 JSON 物品会出现候选已选中但提交按钮被错误禁用。
- `GameRes.recipeDict` 是尚未迁移 `CraftingService` 的熔炼流程专用旧输入签名索引；`RecipeType.Crafting` 允许相同输入对应多个候选，必须只注册到 `CraftingRecipeCatalog`，禁止写入这个单值字典或把同输入多候选误判为冲突。
- 配方动作在库存事务成功后执行；异常恢复快照。玩法进度信号只在最终成功后发布。
- 制作输入变化、事务扣料和面板初始化都会被动刷新预览；此时 `RecipeNotFound` 是合法的“当前无配方”状态，应清空预览且不输出 Warning。只有用户主动提交前检查失败，或库存、产物等结构性异常，才输出制作诊断。
- 制作模块的 `Save()` 只负责持久化，不能解绑输入、输出、按钮或交互监听；这些运行时事件统一在 `Unload()` 中成对清理，由 Item 退出、移除模块与回池生命周期调用，否则自动保存会让预览与制作按钮永久失效。
- 模态库存才获取输入锁；快捷栏和 `Inventory_Hand` 不锁玩家输入。
- 单个库存面板需要专属槽位皮肤时，在面板 Prefab 上配置 `InventorySlotVisualProfile`，由 `Inventory.InitUI` 在动态槽位创建完成后统一应用；不要改通用 `UI_Slot.prefab` 做面板特判。该 Profile 只允许改 Sprite、图标/文字尺寸等表现，不得接管库存事务、拖拽或选择状态。
- `UI_Bag` 的 `InventoryBagSearch` 平时只调整各槽位的 `CanvasGroup.alpha`，不删除或隐藏真实槽位；`Inventory.InitUI` 创建动态槽位后绑定，库存 `Event_RefreshUI` 触发单槽重算。匹配依据为物品定义的中文名、当前显示名、英文译名、稳定 ID，以及实例 Tag 的稳定 ID/多语言别名，不能读取存档名称缓存或把本地化显示文本当业务 Tag。搜索词有效时点击“排序”或“整理”，库存事务都先把命中项整体置顶；排序继续在命中/未命中分区内执行当前排序规则，整理则只做稳定分区、合并堆叠和压紧空槽。搜索组件只提供同一套命中判定，不能复制另一套搜索逻辑。
- 槽位鼠标与触屏拖放必须复用 `ItemSlot_UI.OnMouseDragBegin` / `OnMouseDragDrop` 的来源事务：命中 `ItemSlot_UI` 时直接在起始槽与目标槽之间移动、合并或双向交换，异类交换必须同时校验双方库存接收规则与整堆容量，禁止把目标物品经 `Inventory_Hand` 中转；只有未命中槽位时才把整组转入 `Inventory_Hand`，后续手机点击按轻触方向处理：连续拿取方向下同类已有物品从槽位取一件，放置方向下空槽/同类槽向目标放一件，异类槽交换，长按空槽或同类槽则按统一长按时间把进度转发到唯一的 `Inventory_Hand` 手部插槽显示，并在进度走满时立即一次性放下手上整组，不等待松手；通用 `UI_Slot.prefab` 不承载这层进度视觉。长按进度视觉必须先经过短按/拖拽判定缓冲（当前 0.2 秒）再显示，缓冲期内转为拖拽则始终不出现进度条，且该视觉缓冲不得延长原有长按提交总时长。提交成功后该手势必须被消费，不能继续进入半组拖拽；同类目标容量不足时余量留在起始槽，空槽起手才转交父级 `ScrollRect`。
- `CraftingTransaction` 提交会深拷贝并替换库存槽内的 `ItemData`；拖拽来源不能只按对象引用校验，应同时允许同槽、同非零 Guid、同定义 ID 的当前实例。持续加工器在输入或输出槽被拖动时暂缓推进，避免最后一件原料在松手前被消费；拖拽完成、转入手部或取消时必须释放该占用。
- 库存拖拽物需要触发“非槽位玩法目标”时统一实现 `IInventoryDragDropTarget`，并通过 `InventoryDragTransaction.TryConsumeSourceItem` 在保留来源槽位的前提下修改拖拽物状态；目标区域负责拦截有效落点，不能先把物品转入 `Inventory_Hand` 再回写。液体容器面板使用这条链把其它容器拖到罐体剖面进行守恒转液：当前手持来源优先走运行时 `Mod_WaterVessel.TransferTo`，非手持库存来源原地更新 `ItemData` 的液体状态并通知真实所属库存刷新。
- 手机快捷栏轻触必须走独立 `OnTouchTap` 语义，只切换当前选中格或按单件规则取放；普通触屏拖放与桌面键鼠共用直接槽位事务，长按更久后的半组拖拽才以 `Inventory_Hand` 为来源。
- 跟随指针的 `UI_Hand` 是纯视觉层：Canvas 排序固定占用全局顶层（32767），必须高于快捷栏、设置页和其它游戏 UI；CanvasGroup/子图形不得拦截目标槽位射线。直接槽位拖拽生成的 `InventoryDragGhost` 也必须使用独立顶层 Canvas，不能只靠 `SetAsLastSibling`，否则会被模态页的独立 Canvas 压住。世界手持物挂在快捷栏节点及其子节点末端；玩家根 `SortingGroup` 接收世界 Y 排序，`Player.prefab` 的 `Module_Hotbar` 子组用 `Default/1` 压过身体的 `Default/0`，不可只靠兄弟节点顺序解决手持遮挡。
- `UI_Hand` 的桌面跟随可以读取 Input System `Pointer.current`；触屏库存与世界丢弃必须以该次手势自己的 `PointerEventData.position` 为权威，并在触点抬起后保留最后位置，直到桌面库存指针明确接管。禁止用全局 `Pointer.current` 持续覆盖触屏位置：Device Simulator、多指或触点结束时它可能切到模拟鼠标/另一触点，把手持槽钉到错误坐标。所有进入表现层的坐标仍需过滤 NaN/Infinity。
- 通用槽位悬浮信息由 `ItemSlot_UI -> UI_ItemTooltip` 统一呈现：标题和说明读取当前 `RuntimeItemDefinition` 的本地化文本，实例参数读取槽位当前 `ItemData`；模块额外动态说明统一由模块 Prefab 实现 `IItemTooltipInfoProvider` 返回，UI 禁止按具体物品或模块写分支。虚拟化槽位重绑、停用或销毁时必须刷新或关闭悬浮面板。
- 快捷栏选中框属于当前槽位背景层，切换时必须重新挂到目标槽位并置为首个兄弟；数量文本和物品图标保持在其上方，不能依赖独立 Canvas 的任意 `sortingOrder`。
- `Mod_HotBar.RuntimeInventory` 在 `Player.prefab` 中以 Unity 托管引用保存，字段必须保留 `[SerializeReference]`；移除该标记会让 Prefab 中的 `rid` 数据无法恢复，连带丢失 `InventoryPanel_Prefab`，表现为整个快捷栏不创建。
- 玩家行囊的键鼠点击和滚轮无条件使用 `Inventory_Hand`，不能因携带槽为空或上次手柄操作留下的目标而回退快捷栏；桌面指针抬起实际进入 `OnDesktopTap`，只修改 `OnLeftClick` 不会恢复鼠标点击。PC 左键整组取放：空手按携带槽容量拿取，有物品时整组放置、同类合并或异类交换；不得转入 `OnTouchTap` 的单件语义，滚轮才逐件取放。点击与拖放共用整组跨库存事务，校验双向接收规则及容量、通知双方并同步快捷栏手持物；创造背包允许超量堆叠，但取出仍按目标容量与非堆叠规则拆分，余量保留原槽。快捷栏选中槽只参与手柄确认与角色当前装备，不参与 PC 背包交换。
- 主行囊 `UI_Bag` 的页面滚动由独立纵向 `ScrollRect` 处理；灵敏度应按格子行距除以 `InputSystemUIInputModule.scrollDeltaPerTick` 换算，使单个滚轮刻度约移动一行，不能和槽位滚轮逐件取放的逻辑混为一谈。
- 生存主行囊 `UI_Bag` 使用固定 20 个常驻槽位；只有创造背包启用 `InventoryVirtualizedSlotGrid`。虚拟化模式下 `Inventory.itemSlot_UI` 是按真实索引保存的稀疏映射，未渲染索引允许为 null；刷新、搜索和拖拽不能假设每个数据槽都有 GameObject，拖拽期间禁止重绑复用槽位。
- 物品的 `weight`（kg）、`volume`（L）和 `stackable` 独立；重量/体积不决定堆叠资格。普通库存（主背包、手部、快捷栏、储物、制作和机器）统一使用 `Inventory_Data.DefaultSlotVolume = int.MaxValue` 表示无限堆叠；不可堆叠物仍一格一件，装备/装配专用槽仍可显式限制为 1。数量与槽容量沿用 float，转整数前复用 `GetWholeStackAmount` 钳制，不能直接对最大容量调用 `FloorToInt`。生存主背包固定 20 格，储物和制作库存使用配置中的固定数量，满槽不能通过拾取、制作预检、显式扩容或装备附加槽绕过。携带重量/体积继续合并主背包和快捷栏计算；世界拾取与行囊 Footer 使用同一口径。
- 行囊“整理”按钮只负责合并可堆叠物并压紧空槽，不推进“排序”按钮的规则循环；无搜索时保持物品原有相对顺序，有搜索时仅把命中项稳定移动到前方，并分别保持命中/未命中分区内的原有相对顺序。
- `CreativeInventoryState` 同时控制创造背包动态槽位与重量/体积豁免，必须先恢复状态再初始化格数，批量填充前先启用。只有创造背包保持 20 格基线、空槽 <= 2 时补足 3 格的扩容和收缩策略。F2 创造背包以 `GameRes.ItemDefinitions` 为主目录，并额外合并 `ModRuntimeManager.DefinitionInfos` 中已经物化成功、且实际指向 `Item` 运行时模板的 MOD 道具，不能把 MOD 内部普通 Prefab/模块壳当成物品。创造背包目录必须排除 `BuildingRole.PlacedBuilding` 的落地建筑本体，以及当前静态定义 `CanBePickedUp=false` 的树、矿点、作物、传送口等世界专用实体，只保留真正可持有的物品和建筑召唤器；重复召唤创造背包时也要清理历史遗留的世界实体槽位。创造背包会把已入包物品的运行态 `CanBePickedUp` 改成 false，因此清理旧槽位时必须回到当前静态定义或当前 MOD 运行时模板判断，禁止读取槽位里的该标志反推是否可持有。便携设施过滤时应优先用当前 `ItemData.IDName` 与 `BuildingPrefabId/SummonerPrefabId` 的载体身份对应关系判定，不能只信历史 `Role`，否则旧背包状态可能把落地本体误当成可持有召唤器。创造模式额外绕过重量/体积上限，但 `Stackable=false` 仍严格一件一格。库存事务通过 `NotifyItemDataChanged` 只负责及时补足预留空槽，周期容量自检再负责安全收缩多余空槽，避免在数据变更事件分发前移除刚变化的槽位引用；容量预检必须纯只读并计入可动态扩容的空间。动态增减槽位的 UI 只同步表现，不重新初始化库存业务事件；快捷栏部分拾取后的余量必须继续尝试主背包，最后统一发布拾取数量。
- 快捷栏收到 Mobile `RightClick` 时必须允许当前手持物执行 `Act`，不能因触点位于手机“使用”按钮上而被 `IsPointerOverUI()` 拦截；键鼠右键仍保留 UI 遮挡检查。
- 快捷栏生成的手持物只注册到玩家 `Mod_FocusPoint`；左右翻身角由该模块读取 `Mod_TurnBack.CurrentTurnAngleY` 后与 Z 轴瞄准一次性合成，不能再把手持物根节点注册进 `controlledTransforms_Direction`。
- 快捷栏手持创建使用 `ItemMgr.InstantiateHeldItem`，直接传真实槽位 ItemData，并在注册前设置 Owner/inHand；禁止先按定义 ID 创建并注册随机 GUID，再 BindData 替换身份。库存事件只标脏，在快捷栏安全更新边界同步；同槽同数据不重建，但同 GUID 的新数据引用仍需重绑。InitData 前解除旧库存事件、完成后重绑，Unload 与 OnDestroy 共用完整清理。
- 快捷栏保存前先调用当前手持物 ModuleSave，再序列化库存，避免手持进度落后于玩家快照。手持 ECS 迁移属于统一实体架构待办，不能另建 World，也不能把仅有接口优化当作玩法模块全部迁移完成。
- 需要“只从物品所在库存取料”的玩法统一使用 `InventoryContextResolver` 按 `ItemData` 引用/Guid 解析真实所属 `Inventory`；快捷栏手持物会命中 `Mod_HotBar.RuntimeInventory`，普通背包命中对应 `Mod_Inventory.InventoryInstances`。实际扣除使用 `Inventory_Data.TryConsumeFirstByTag/TryConsumeFromSlot` 事务入口，不能直接改 `Stack.Amount`，否则快捷栏 UI、数据事件和后续持久化会失步。
- 丢弃统一经过 `Mod_DiscardItem.DropItemByCount`；扣减 `ItemSlot.Amount` 后除触发槽位事件外，还必须按快捷栏槽位索引显式刷新 UI，兼容手机入口没有 `ItemSlot_UI` 引用的情况。
- 快捷栏拖拽到非 UI 区域后的整组丢弃由 `ItemSlot_UI` 世界长按回调转发到 `Mod_DiscardItem`，落点使用触点屏幕坐标；UI 槽位长按放置路径保持独立。
- 快捷栏物品拖入 `Inventory_Hand` 后，移动端摇杆必须让出当前触摸所有权，避免长按世界丢弃时浮动摇杆抢占操作。
- 与背包并行打开的专用制作面板创建后必须调用 `InventoryPanelLayout.ApplyDefaultCraftingPosition`；只让背包靠左会在窄屏、安全区或 UI 缩放后由置顶背包覆盖左侧输入槽射线。
- 手机端已经拿起物品后的轻点/长按丢弃由 `MobileHeldItemDropSurface` 统一转发到 `Mod_DiscardItem.TryDropHeldItemAtScreenPosition`，仅操作手部携带槽，空手不得取快捷栏选中物；中间空白触控面只在 `Inventory_Hand` 有物品时参与射线，`ItemSlot_UI` 的拖拽射线必须继续把该组件视为世界落点。
- `Inventory` 持有的 `item` 是 `UnityEngine.Object`；生命周期边界禁止用 `item?.GetComponent...` 判断存活，因为 C# 空条件运算符不会触发 Unity 的“已销毁对象视为 null”语义。玩家/容器卸载时必须先解除库存输入监听并清空所属 `item`，`Mod_Hand.Unload` 同时清理 `Inventory_Hand.PlayerHand`，避免槽位 `OnDisable` 或延迟 UI 回调访问上一轮玩家。
- `Inventory.BindController/UnbindController` 只管理输入绑定，不能顺带解除 `Inventory_Data.Event_RefreshUI` 或槽位 `onSlotDataChanged`；这些数据/UI 监听只在库存真正退出运行时生命周期时通过 `UnbindRuntimeDataEvents` 清理。否则快捷栏在控制器重绑后会失去滚轮转移等事务的自动刷新，只在切换选中槽时才重画图标。
- `Mod_Plantable` 对 `entityRuntime: "resource"` 的作物调用 `ChunkAgricultureRenderer.CreateEntityCrop`，先装配 Entity 再扣真实库存种子，失败通过 `RollbackEntityCrop` 回滚，禁止临时创建 Item 提取状态。`PlantingSummoner` 仅是共享种植预览；未迁移定义仍有明确的 `IPlantableCrop` 入口，不得拿它给已声明资源后端的作物兜底。
- 同一物品同时挂 `Mod_Plantable` 与 `Mod_Food` 时，右键动作按目标上下文仲裁：有效耕地由种植优先，无效种植目标则静默让给食用，不能一次动作同时播种和进食，也不能在正常进食时刷种植警告。
- 新版农业统一通过 `FarmlandSystem` 查询 `ChunkTerrainData`，禁止返回旧 `Chunk.Map`；未完成锄地只保留当前会话的临时进度和覆盖层，不写 `ChunkTerrainData`/存档，停工 10 秒后逐渐恢复，只有完成时才把 Ground Tile 正式替换为 `Tile_Farmland`。水肥计算使用临时 `TileData_Farmland` 快照，成长或施肥后必须 `CommitSoil`，否则数据修改不会进入权威环境层。
- 锄头持续使用由 `Mod_GameController.IsRightClickHeld` 提供按住状态；`Mod_Hoe`、`Mod_GroundCoverHarvest` 等右键工具只依赖 `IWeaponActionAnimation.TryRequestAction(false)` 确认一段挥动真实开始后才结算，不直接绑定具体伤害模块。锄地间隔由动作动画长度和 AttackSpeed 决定，禁止再叠加独立的固定使用冷却。
- 玩家播种的 Entity 作物由 `ChunkAgricultureRenderer` 登记句柄，纯数据通过 `SaveDataMgr.RecordCultivatedCropData` 保存到 `ChunkSaveRecord.AgricultureCells`；天然植物仍使用生态 GUID 与差量，不能交叉登记或同时保存两份。保存前提交待结算土壤与真实死亡，解绑本身不删除农业快照。`FarmlandSystem.HasWorldPlant` 与 AI 采蜜均须包含资源实体查询，不能只看 ItemManager。
- 耕地植株保存已结算的绝对游戏秒，由 `IWorldTimePlant` 在区块恢复后按 `DayTimeSystem` 的时钟补算；退出游戏和暂停期间不补现实时间，历史段不能沿用重载时的短时天气。`Mod_Grow` 与 `Mod_PlantClimate` 组合时由成长模块统一推进气候，避免冷热暴露重复结算；`GrowData` 的 MemoryPack 成员只能在末尾追加，不能调换既有字段顺序。
- 普通作物的 JSON 仍以 crop/cropYield/cropVisual 描述组合，但资源后端编译为生命周期、产出和批量表现，不实例化这些 MonoBehaviour。一次性收获先生成全部产物再标记收获并清除农业或生态来源；持续采果只扣资源库存，不销毁植株。库存与掉落事务只在主线程提交，不能由多个 Job 直接写同一库存。
- `BerryCrop` 的野生与播种共用资源实体定义：采集模块编译成 `EntityResourceStock`，生产列表编译成 `EntityStockProduction`；按 `ProductionIntervalDays` 声明的日历批次使用当前世界 `DayLength`，不吃生产速度/难度倍率，库存已满也继续走批次时钟。可用 `YieldGeneVariants` 为每株按稳定 GUID 固定一个带权产量区间，区间内每批仍做整数均匀随机；一次交互严格扣 1 份库存并掉落 1 个果实，不叠加全局掉落倍率，不销毁植株。继承的 cropYield 必须禁用；药草、狗尾草等一次性植物不要继承持续采果规则。库存提示是 BRG 部件，不创建果实 GameObject。
- 野外自然生成、允许玩家用武器清除的小型作物统一继承 `WildCrop_Base`；该抽象定义负责成熟自然初态、通用 `Mod_DamageReceiver`、植被受击材质和独立 Mod_DamageReceiver Trigger，具体作物只按外形/耐久覆盖 HP 与伤害碰撞尺寸。仅种植链使用的萝卜、水稻不因该规则自动获得生命模块；具体死亡掉落仍由各物品顶层 `lootTableId` 定义，禁止把通用掉落塞进 `WildCrop_Base`。
- 作物需要多张成长图时，在物品 `visual.spriteStates` 同时声明 `seedling/growing/mature`，由 `Mod_CropVisual` 根据 `normalizedGrowth` 派生表现阶段；不得为了中间画面给 `CropStage` 增加持久化阶段。只要声明任一阶段图就必须三张齐全，对象池卸载时恢复外壳原 Sprite。
- 世界植株与收获物必须保留独立 Item ID；种下时把植株重置为幼苗，一次性作物成熟交互后由动作生成食物/种子并销毁植株，持续采果植株只扣果实库存；不能把世界植株直接改成食物实例。
- `Mod_Grow` 继续承担树木与自然植物成长，并实现 `IPlantableCrop` 接入同一播种入口；水肥、天气与 `CropGrowthMultiplier` 在权威成长模块中各结算一次。
- 自然生态伴生物通过 `NaturalItemPlacement.HostGuid` 绑定宿主；宿主模块可实现 `INaturalCompanionHostCondition` 提供运行态门禁。`Mod_Grow` 默认到“发育”阶段才允许蜂巢等伴生物出现，未达门槛时由 `ChunkNaturalItemRenderer` 延迟生成，禁止让幼苗/小树直接挂载伴生物。
- 苹果/桃/柑橘这类果树优先复用 `AppleTree` 与 `Apple` 的 JSON 继承链：果实只覆盖营养、腐败和视觉，果树只覆盖采收物、气候、战利品和视觉；若需要野生生成，再单独在地表 `ecologyRules` 注册稳定规则，禁止为每种果树复制一套成长代码。
- 使用 `_BodyClip` 裁剪作物精灵时，必须给 `Mod_CropVisual` 绑定支持该属性的 `Sprite-Lit-Master` 材质；通用 `Prop` 外壳默认材质不提供 BodyClip。
- `_BodyMinV/_BodyMaxV` 实际传入 `Sprite.bounds` 的本地 Y，不是 UV；主体和交互描边 Shader 都必须保留 `DisableBatching=True`，否则精灵合批预变换顶点后可能按世界位置误裁整株作物。不能通过提高 Sorting Order 修复，也不能只保护主体而遗漏描边 Pass。
- 废弃 `Mod_EquipmentRuntime.cs` 不再使用。
- `Mod_Food` 的被动生命联动必须读取 `Mod_PlayerDeathState`；玩家濒死或 `Mod_DamageReceiver.Hp <= 0` 时停止回血与生存伤害，避免死亡状态被抬成极低正数。
- `Mod_Food.HealthState` 的回血判定只看蛋白质；`HealInterval/HealAmount` 大于 0 时按间隔一次性回血，动物继续使用 `HealNeedRatio`，玩家创建模板通过 `proteinHealThreshold` 配置绝对蛋白质门槛。
- `Mod_Food` 仅在基础营养持续消耗或 `IFoodTickObserver` 规则要求时进入 `FixedInterval`；无角色模块的静态世界食物应休眠，库存腐败仍由 `IModuleDataTickObserver` 独立推进，可选角色模块必须静默查询。难度倍率的 `IsPlayer` 每次解析当前 Owner，再用 `TryGetComponent` 查询可选 Player，避免 Editor 缺失组件诊断分配；不能缓存可能随归属变化的玩家判断。
- `Mod_HeldFood` 的咬痕只读取 `EatingProgress` 与 `Max_EatingProgress`，按物品 GUID 确定性重建当前轮廓遮罩，不重复持久化随机点；实际口数取最大进度的向上整数，最后一口直接清空残余区域。

- `IInventoryHeatTreatment` 处理输入槽里的状态型液体容器，普通熔炼复用 `CraftingRecipeMatcher` 和 `CraftingTransaction`。具体液体的转化温度、时间、结果液体或副产物由 `LiquidDefinition.HeatProcess` 声明；需要产出物品时必须先确保输出事务成功再消耗液体，处理进度属于容器而非炉体。
- 通用液体容器由 `Mod_WaterVessel` 承载，但状态只保存稳定 `LiquidId + Amount + ProcessingSeconds`，其中 `Amount` 虽沿用 float 存档字段，运行时必须量化为整数“份数”；液体语义统一来自 `GameRes.LiquidDefinitions`。容器通过显式 `Stackable=false` 禁止堆叠，不同液体不能自动混装，整份转移保持数量守恒。新增本体或 MOD 液体不得复制容器 Item 变体，应该注册新的 `LiquidDefinition` 并复用同一容器模块。
- 木桶的固体内容由独立 `Mod_VesselContents` 六格库存持久化；手持与落地两端都要声明该模块并列入 `SharedModuleIds`，关闭水容器面板只解除 UI 槽位绑定，不清空库存。投到内腔空白处形成独立物品堆，投到同类图标才合并。`LiquidDefinition.ingredientReactions` 以当前液体、原料物品、数量和是否满桶声明投料反应；木桶库存变化与补液都要重新检查规则，数量可跨多个真实槽位累计，原料不足或液体条件不满足时保留原物品，成功时从真实槽位事务扣料后才转换液体。
- 液体容器的“饮水”按钮只按 `LiquidDefinition.Drinkable` 与剩余量决定是否可点，不得用当前补水收益或角色水分已满来阻止玩家主动饮用；`Mod_Food.DrinkWater` 即使实际补水量为 0 也要发布一次完整饮水结果。感染、脱水等饮用后果统一声明在 `LiquidDefinition.drinkEffects`，世界水源与容器都通过 `LiquidDrinkEffectProcessor` 结算，禁止在水地块或具体容器里按液体 ID 再写一套后果逻辑。
- 世界液体来源由 `WorldLiquidSourceResolver` 直接读取 Chunk 独立 Liquid 层，使用稳定 LiquidId 解析同一 LiquidDefinition；容器不得按水 Tile 名称或盐度猜身份。准心高亮与实际装液复用同一 WorldCell 入口；世界液深与容器份数固定按“1 份 = 0.1 液深”换算，世界抽水统一走 `WorldLiquidSystem.TryPump`。
- 液体容器图标由 `Mod_WaterVessel` 从 `ItemData` 状态重绘 Sprite：`visual.liquidSurface.bounds` 应按基础贴图的整个内腔开口标定，绘制层不再额外缩进；`maxSourceChannel` 按贴图的内腔暗色与边沿亮色分界设置，避免染到桶沿或罐口，省略时默认 165。液体颜色读取 `LiquidDefinition.primaryColor`，填充行数按容器容量量化；木桶、陶罐启用的基础贴图必须打开 Read/Write。展示端通过 `GameRes.TryGetItemPresentation(ItemData, ...)` 与 `ItemDataPresentationResolverRegistry` 获取最终 Sprite，槽位和掉落物展示代码不得直接引用具体玩法模块或叠加液面 Graphic。
- 容器重绘生成的运行时 Sprite 必须携带物理轮廓；落地建筑的 `ShadowCaster2D` 按当前状态 Sprite 取轮廓，没有轮廓会令建筑加载与放置事务失败。
- 快捷栏当前手持实例会把 `Item.OnUIRefresh` 绑定到 `Mod_HotBar.RefreshUI`；手持物模块只修改内部状态而不替换 `ItemData` 引用时，除了通知真实所属库存，还必须发布 `Item.OnUIRefresh`，使当前快捷栏槽立即重画状态型图标，不能只刷新世界中的手持 Sprite。

- 可放置设备若将齿轮等动态部件拆为世界独立图层，完整的背包/快捷栏静态图标声明在 `visual.spriteStates.inventoryIcon`；`visual.spriteAddress` 仍指向世界主体，由 `GameRes.TryGetItemPresentation` 优先选择库存图标，避免物品槽出现空洞或放置后重复叠图。
- `UI_WaterVessel` 的拖拽倾倒只消费现有 `Mod_WaterVessel.PourToGround`，不保存独立玩法手势状态。声明 `LiquidDefinition.worldWater` 的液体倾倒到干地或已有同种液面时都统一写入独立 Liquid 层，水与岩浆不得再按类别分流到土壤系统。所有液体容器统一以绝对倾角 90° 为完全倒空角：绝对倾角越大，允许保留的液量上限越低，达到 90° 时取消保留量上限；液流表现必须连续，但玩法层累计到 `AmountStep=1` 后才一次扣一份，禁止一帧批量扣多份或产生小数份。实际流速按基准份数流速、容器相对开口宽度和倾角倍率持续结算，水平倾倒时加速，容量不参与流速，扶正不会恢复已倒出的液体。正式外观只通过 `VesselAppearance.MouthWidth` 声明相对剖面宽度的实际开口比例，禁止重新增加逐容器完全倒空倾角配置。空容器仍允许完整拖动和自动回正，只提供交互反馈、不产生液体扣减。手势必须按独立 `pointerId` 持有触点，并在不随罐体旋转的父级坐标系计算：外圈拖拽优先按绕罐体中心的极角变化解释，因此左右、上下、斜向及半圆轨迹均可倾倒；中心起手才退化为二维线性位移。方向契约固定为屏幕右侧手势产生负 Z（顺时针、朝右倒），屏幕左侧手势产生正 Z（逆时针、朝左倒），圆弧与线性回退必须一致。罐内液面由容器真实余量驱动；反向旋转的液层网格必须覆盖旋转内腔遮罩的轴对齐包围范围，避免露出竖直裁剪边。罐口外液流由独立 `WaterVesselPourGraphic` 按倾倒状态持续表现，禁止用视觉帧反向扣减玩法数据。
- 食物机制除了目录注册，也组合消费物本身实现 `IFoodMechanic` 的模块；药品使用守卫与消费完成观察者应接入这条链，不在按钮响应时直接回血。
- `CraftingOutputRules` 在预检和真实提交共用；防腐加工的新鲜度必须来自匹配计划实际消耗的原料，保留最差剩余比例，不能读取整份输入库存或重建全新寿命。
- 植物环境通过 `IPlantEnvironmentCondition` 与 `PlantClimateTimeline` 结算；自主耐候树木只向成长器提供 `IPlantGrowthConstraint`，不能被两个模块重复推进。补算游标未追上当前时钟时禁止抢先收获。

## 验证

- 配方候选的右侧不是材料总量：`材料列表/材料模板` 下每项必须同时绑定 `材料图标` 与 `材料数量`。候选行和材料格都只复用正式隐藏模板；材料超过一行时同步扩展行高度，不能覆盖相邻配方。
- Tag 材料图标来自当前输入槽中实际符合 `CraftingIngredientMatcher.MatchesIdentity` 的物品；精确材料通过目录读取图标，不实例化 Item，也不根据显示名字猜测身份。数量为零的工具需求只标工具，不累计为消耗。

- ECS 掉落生成成功后才扣减原槽位；拾取使用从冷载荷克隆的候选数据进入 `Mod_ItemPicker.TryAcceptNetworkPickup`，成功后把剩余数量写回组件，整组入包才销毁实体。失败预检不得把改写后的候选数量覆盖世界权威数量。
- 玩家主动丢弃的特殊效果通过 `DroppedItemSpawnContext` / `IDroppedItemSpawnContextReceiver` 由物品模块扩展；`Mod_DiscardItem` 只标记通用 `PlayerDiscard` 来源，不按物品 ID 写特判。飞行伤害统一组合 `Mod_DiscardFlightDamage + Mod_Damage`，需要完整模块运行态的物品声明 `worldDropBehavior: interactive`。
- ECS 拾取仅查询玩家拾取器附近空间桶，复用拾取器 Collider 的接触范围；同一接触只尝试一次，离开或关闭拾取后重置。临时吸入精灵只在库存提交后创建，表现失败不得复活已拾取实体。

- 覆盖满包、回滚、普通合成多候选精确选择、Tag 全局分配、加热镜像/紧凑网格、快捷栏/手持同步、输入锁和作物存档往返。
- UI 契约联动 UI Skill；Item 生命周期联动 Item Skill；配方/农业存档联动 Data Skill。
- JSON 物品迁移时可以暂不填写 `visual` 图标；库存槽位的统一显示入口必须回退到 shell Prefab 的 `SpriteRenderer`，否则已有物品会在快捷栏中变成空槽。

- 向现有世界液体倾倒时，液深修改由 `WorldLiquidSystem` 提交并记录独立差量；农业状态只能持有土壤水肥和作物，不得再把液深藏入农业环境层或重新反算 height。

## Skill 维护原则

- 单件装配槽必须在 `Inventory.InitData()` 重设通用容量之后再次设置 `SlotMaxVolume=1` 并关闭无限堆叠；只限制 UI 文案或初次构造无法阻止整堆交换与重开面板超量。钓具等二进制嵌套库存恢复时逐槽使用当前 ItemDefinition 重建静态数据。
- 装配物抛出须先成功创建世界掉落物，再通过 `TryConsumeFromSlot` 扣除对应槽位；扣料失败撤回新掉落物。成功抛出的食物由世界掉落系统保存，工具切换、面板关闭与模块卸载只能解除临时钓线，不能复制或返还已经提交的鱼饵。

- `Mod_HandDrill` 使用独立二进制 `RecipeProcessingState` 与 `UI_HandDrill`；手持/建筑通过 `SharedModuleIds=["手钻模块"]` 迁移同一库存和进度，不再使用 amount=0 工具配方。加工表位于 `Resources/Config/Mechanical/mechanical-catalog.json`，MOD 注册入口为 `MachineCatalog.RegisterProcess`。
- `RecipeProcessor` 的输入过滤覆盖统一库存转移入口；预览与提交均走 `CraftingService`，输出满时不扣料、不清空已有进度。固定物料转换必须设置 `ApplyDifficultyOutputMultiplier=false`，避免难度增产倍率破坏 1:1 钻孔。
- 机械加工的手动推进通过 `RecipeProcessor.AdvanceManually` 复用同一进度与 `CraftingService` 事务；节点配置 `ManualWorkSecondsPerPress` 决定是否显示按钮及每次推进量，不能直接改库存或绕过产物预检。
- `Mod_ManualProcessor` 用 `Station + WorkPerClick` 配置可复用的手动加工台，按对应 `MachineCatalog` 站点读取配方并调用 `RecipeProcessor.AdvanceManually`；可放置设备的便携物/建筑本体须用 `SharedModuleIds` 保持加工库存与进度连续。
- 容器禁止放入状态统一保存在 `Inventory_Data.IsDepositBlocked`；物品新增与跨库存转入必须在数据事务入口检查该标识，阻止放入时仍允许取出与同库存整理，自动运输入口也应沿用同一标识。
- 熔炉燃料消费统一使用 `Inventory_Data.TryConsumeFromSlot`，不能直接扣 `Stack.Amount`；燃料副产物按配置规则 ID 保存累计进度与待交付数量，目标库存满时保留待交付量，库存释放后再提交。点火时可检查当前手持物的 `Mod_Combustion.IsActivelyBurning`，不能仅凭物品 ID 或燃料模块判断它正在燃烧。
- 玩家超重减速由主背包和快捷栏的 `Inventory_Data.Event_OnDataChanged` 事件驱动；库存 `InitData` 绑定、模块卸载解绑，玩家全部模块加载完成后做一次状态校准。不要在 `Mod_Inventory.ModUpdate` 中轮询重量。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

---
name: flatworld-inventory-crafting
description: "FlatWorld 库存、槽位交互、制作事务、装备、食物、液体容器和农业的数据与运行规则。"
---

# 库存、制作与农业

## 代码与内容

代码以 `Assets/5_Scripts/5-3_GamePlay/` 为根：

| 目录 | 职责与入口 |
| --- | --- |
| `Items/Inventory/` | Inventory、Mod_Inventory、Mod_HotBar、ItemSlot_UI、InventoryContextResolver、InventoryModuleDataScheduler |
| `Items/Crafting/` | CraftingService、CraftingRecipeMatcher、CraftingTransaction、CraftingStationController |
| `Items/Equipment/` | Mod_Equipment、EquipmentInstance*、装配槽 |
| `Items/Food/` | Mod_WaterVessel、液体容器端口 |
| `Entities/Item/Modules/Player/Food/` | Mod_Food：营养、饮用、回血 |
| `Entities/Item/Modules/Growth/` | Mod_Grow、Mod_Grow.AuthoritativeCrop：植物状态与时间线 |
| `World/Planting/` | Mod_Plantable、FarmlandSystem*：播种、水肥、锄地 |

- 权威库存：`Assets/5_Scripts/5-1_Data/CoreData/Inventory_Data*.cs`；UI/Prefab 位于 `Assets/2_Prefabs/{Inventory,Equipment,Food,Plant,Seed,Tools}/`。
- 固定/多原料配方：`Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json`；单原料加工：物品定义 processing。内容工坊位于 `Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`。
- 落地工作台、炉体、储物及加工设施由 `World/Machines` 的数据领域逻辑运行；手持载体仍为 Item。

## 库存与手持

- 联机客户端在同地表船体攻击的 `ClientGroundCombatPending` 期间暂缓槽位拖拽、快捷转移与制作事务；等待 Host 消耗结果成功应用后恢复，不能让旧库存包覆盖箭数或静默丢失本地事务。
- 联机本机库存事务待确认时，原生丢弃、快捷栏使用、进食和本地弓弹药消耗在副作用前查询 `ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction`；桥只由联机业务登记，单机/Host 默认放行，不停止移动、环境与 AI。测试接入保持在 Development/Testing，不在业务代码注册工具或增加测试分支。

- 重量 kg、体积 L、Stackable 独立。普通槽 `DefaultSlotVolume=int.MaxValue`，不可堆叠物一件一槽，装配槽显式为 1；数量/容量使用 float，整数转换用 `GetWholeStackAmount`。生存行囊固定 20 格，储物/制作采用配置格数。
- ItemStack.Amount/Weight 与 ItemData.Stack 原位修改发质量事件；Inventory_Data 绑定实际槽内 ItemData 的 MassChanged，供船载质量缓存使用。新增含质量载荷须复用 NotifyMassChanged/PayloadChanged 边界，温度/血量等无质量变化不使船重失效；运行时观察引用与订阅不进入存档或克隆。
- 携带量统一收集主背包、快捷栏和玩家手工制作的实际输入/输出库存，按数据引用去重；面板关闭仍计入，配方预览不计入。背包显示监听全部随身库存，手工台自行绑定数据事件更新超重状态并在卸载时解绑。储物独立容量为 kg/m³ 配置，InitData/恢复后转为内部 L 并重用容量策略。`IsDepositBlocked` 在数据事务入口拒绝跨库存转入，仍允许取出与内部整理。
- 世界拾取先用独立候选和去重目标生成只读容量计划，普通原木等按完整整数能收多少收多少，快捷栏余量继续进入背包；成功回执区分原堆扣减量与实际入包量，GM 倍率仍按完整原份结算。轻量掉落的 `body.Amount` 是唯一权威，物态推进先以它给冷载荷播种，不能让旧载荷数量复活余物；随身库存质量事件和倍率实际变化只标记拾取修订号，下次接触查询再重试，不在入包事件里递归拾取。
- 物品修改经 `TryConsumeFromSlot/TrySetSlotItemAmount` 等事务。`InventoryContextResolver` 以 ItemData 引用/Guid 找真实所属库存；快捷栏修改后按真实槽索引刷新。`BindController/UnbindController` 只管理输入，数据/UI 监听由 `UnbindRuntimeDataEvents` 清理。
- `Inventory_Data.GetItemSlot` 对空槽表或越界索引返回 null，不得回退第0格；库存 UI 的删除、装备和书籍输入调用链必须检查有效槽。
- 带初始内容的读物经 `Items/Readable/ReadableBookGrantService` 创建真实物品数据；独立书页保存为 `ReadableBookInstanceSnapshot` 的 `AddedPageCount/PageContents/InputInventory`，发放复用完整随身容量、槽接收策略与待确认库存事务门禁，不生成世界 Item 或改共享静态页。
- 服务器远端玩家的 Hotbar/Bag 加载真实数据和手持物，不创建 UI 或绑定本机输入；视觉副本用 `ApplyRemoteNetworkData`，权威手持用 `ApplyNetworkAuthorityData`，不能混用。太空拾取 commit 同时走服务器真实库存事务，Host 共享库存只入包一次；客户端选择请求不能改库存数量。
- PC `OnDesktopTap` 用 Inventory_Hand 整组取放，滚轮逐件；触屏轻点单件，直接槽间拖拽复用鼠标事务，异类交换双向校验容量/接收规则。世界目标实现 `IInventoryDragDropTarget`，使用来源事务原位修改物品。
- 仅有源库存界面时通过 `HasVisibleFlickTarget/HasFlickTarget` 关闭判定，快速拖出仍按普通拖拽拿到手部。目标可见性复用 `InventoryFlickTargets.HasVisibleTarget`，排除自己、相同数据、手部、输出、关闭/隐藏/禁用或无有效槽映射的界面；不按容量关闭开关，已满但打开的目标仍走拒绝留源。
- 甩动默认在消费来源前通过带起点/方向的 `HasFlickTarget` 检查真实目标范围，`Resolve` 使用相同角度资格。优先用已登记的 `UIWindowDragSurface.WindowRect`，无正式窗口时退到真实可见槽区；按屏幕/遮罩裁剪，每个分离窗口独立判定，边缘放宽 10°，范围外走普通拿到手部。起点在目标边界内允许任意有效方向；方向内候选仍按槽区中心夹角排序，容量失败只跳过同方向范围内的目标。
- `InventoryFlickUserSettings` 集中持有鼠标/触屏共用的速度、距离门槛百分比（25%～200%、整数步进、默认100%）和默认开启的角度限制；值越低越易触发，正式默认阈值仍为现有手感。关闭角度仅绕过几何资格，仍要求其他可见库存并保留容量预检、方向排序、满格留源与直接槽位拖放优先，不影响 Shift；设置通过 `inventory_flick` Provider 复用保存/放弃/恢复默认。
- 鼠标与触屏快速甩动共用 `InventoryFlickGesture` 检查净位移、末段速度与轨迹方向；无正式拖放目标时经 `TryConsumeSourceItem` 校验来源，再调用独立 `OnFlickTransfer`。`InventoryFlickTargets` 由正式库存 UI 生命周期登记真实输入（组合输出显式 `allowDirectionalDeposit:false`），以按下位置到可见槽区域中心和净甩动方向的夹角排序；满格/标签/承载限制拒绝的候选跳过，选定一个库存后只收可容纳部分，余物留原槽。容量预检与提交共用 `Inventory_Data.GetTransferableQuantityTo`；不试转移、不全场查找、不改 PC Shift 目标。鼠标与触屏使用同一门槛和提交入口，各自采集轨迹，先抬起再 EndDrag 也保留末段速度；单击不等待，半组拖拽保留原语义。
- 库存短音只通过 `InventoryAudioFeedbackEvents` 观察用户点击/滚轮/拖拽/快速转移成功事务，开始拖拽仅播拿起；交换有独立提示，装备槽由 `EquipmentAudioFeedbackEvents` 在真实物品更替时播穿脱声。全部选音与 UI 播放在 `InventoryInteractionAudio`，业务不操作 AudioService。
- 快速转移经 `InventoryQuickTransferFeedbackEvents` 在每个实际收货槽提交成功后发布物品/数量/源目标索引；`InventoryQuickTransferAnimation` 独立播放 0.24 秒无射线飞行，鼠标和触屏甩动均从松手屏幕位置起飞、Shift 快速转移从源槽起飞，按真实 Canvas 坐标与缩放显示、最多并发 32 个，关闭/卸载/虚拟化重绑取消；隐藏或不存在的槽位不编造落点。客户端机器命令只在对应 token 成功回执及同次权威库存快照应用后，按精确收货回执播放，请求阶段冻结本地松手起点并随回执复用；服务器执行远端命令时抑制 Host 本地动画。
- 长按触屏的整组放置在进度完成时提交并消费手势；进度先缓冲 0.2 秒再显示，原总时长不延长。空槽起手才交 ScrollRect。拖拽来源允许同槽、同非零 Guid、同定义的事务替换实例；持续加工在被拖槽上暂缓。
- 触屏空手按住槽内物品 1 秒查看同一个 PC 介绍窗；`Inventory.CanTouchInspectItem` 只检查携带手部库存，快捷栏装备不算手持。介绍松手保留、下次窗外按下收起；显示成功消费本次长按，手持物长按与拆半拖拽保留。移动、物品身份变化、手部非空或面板关闭取消查看。
- `UI_Hand/InventoryDragGhost` 使用顶层 Canvas 且不拦射线；触屏位置取本次 PointerEventData，抬起后保留，桌面明确接管才改用 Pointer.current。`MobileHeldItemDropSurface` 只操作手部携带槽，丢弃经 Mod_DiscardItem，成功生成世界物后才扣源槽。
- 库存与手工制作面板显式允许手持物世界丢弃并登记自身输入锁 Owner；手机在窗口外沿用轻点单件、长按整组，窗口内和槽位射线优先。该能力不解除移动/攻击输入锁，不改变直接槽间拖拽或松手转入手部的事务。
- `InstantiateHeldItem` 在注册前绑定真实 ItemData、Owner/inHand；库存事件标脏，在安全边界同步，同 GUID 跨槽保留实例。保存先 ModuleSave 手持物再存库存；卸载释放输入、事件、PlayerHand 与所属 Item 引用。退出场景时 ItemMgr 可能先销毁，清理通过 GetInstance 获取仍存在的管理器，缺失时销毁自有手持物，不调用会报缺失错误的 Instance。RuntimeInventory 在 Player Prefab 中以 `[SerializeReference]` 保存。
- 排序依次循环 ID/分类/数量/重量/体积；分类读 InventoryGroup 标签。整理稳定合堆压空槽；搜索只变槽 Alpha，以当前定义及 Tag 别名匹配，排序/整理将命中项稳定置顶。创造库存可扩缩、豁免重量体积，目录只含可持有定义；虚拟化槽映射可为 null，拖拽期间不重绑。
- Inventory.basePanel 只拥有通用 UI_Content 槽位面板；自管组合面板通过 SyncQuickTransferTarget 判断状态。槽位皮肤由 InventorySlotVisualProfile 应用；Tooltip 经 IItemTooltipInfoProvider 扩展。模态库存获取输入锁，快捷栏/手部和机器面板不锁。

## 制作与加工

`配方目录/物品 processing → Matcher 全部候选 → Controller 选择与进度 → CraftingService → CraftingTransaction → 成功动作/进度信号`

- 普通 Crafting 为 unordered：需求是物品/Tag 总量，amount=0 为必须存在但不扣数量的工具；耐久由配方 actions 独立控制，要求原样保留时不配置扣耐久动作。额外材料不屏蔽候选。Exact/Tag 重叠做全局容量分配，选择后按所选 RuntimeRecipe 精确提交。热加工保留有序、镜像和网格语义。
- 多产物全部放下才提交，失败无扣料或部分产出；非堆叠产物逐件占槽。合法产物来自 GameRes.ItemDefinitions；旧 recipeDict 仅供未迁移熔炼单值索引，普通多候选登记 CraftingRecipeCatalog。被动无配方只清预览。
- 单原料 processing 不从 parent 继承，经 ItemProcessingResolver 转为 RuntimeRecipe；设备提供 capability/level，材料 minLevel/maxLevel 为闭区间。手工台与工作台互认 handcraft/workbench，固定 4/2 和 5/2 输入输出槽；世界工作台手势数为手工基准 70%，四舍五入至少一次。
- 石臼提供 grind，材料 work 决定有效手势数；MortarState 保存配方与进度，切换清零，输出失败保留已完成进度。固定库存默认 6 格，手持/落地/未物化热加工保持同一库存；槽位视觉不拥有库存。
- 手钻/手动设备复用 RecipeProcessor 与 AdvanceManually、CraftingService；SharedModuleIds 转移库存和进度。固定转换不应用难度增产。`CraftingOutputRules` 共用预检/提交，防腐寿命来自实际扣料计划的最差余量；耐久品质保存 CraftedDurabilityMultiplier，手钻读取实际矿锭 DrillDurability Tag。
- 打火石 `Mod_FlintStrike` 直接复用 `UI_FireDrill`，按真实物品能力隐藏建筑按钮并绑定概率文案；每次点击独立 10%，不累积或衰减进度。成功时由 `CraftingTransaction` 一次扣一份火绒并产一份火种，失败/满输出不扣料，预览按库存事件刷新。
- Save 只保存，Unload 才解绑制作监听。库存冷 Tick 由 ModuleDataRuleRegistry/InventoryModuleDataScheduler 按规则和宿主代际推进；结构变化重建，数值变化不重建，跨库存共享状态扣除重叠时间而不补暂停。
- 便携气罐按不可变模块声明缓存配置解析，对外配置仍是独立副本；流体包装随真实 Contents、二进制载荷或目录换代重建，稳定 Tick 不重复序列化或刷新槽位。保存比较 StateRevision、超压时钟和破裂状态，界面比较实际内容与生命变化。

## 装备、食物与容器

- 装备能力显式组合 Mod_Equipment，按 EquipmentSlot 标签选槽；世界主交互优先。ThermalInsulation 修改安全温度上下界，耐寒/耐热独立，不改变传热或当前温度。
- 穿戴中的配置由装备效果实现 `IEquipmentConfiguration`，经 `Mod_Equipment.CollectConfigurationTargets/TryOpenConfiguration` 收集和校验；同件装备去重，卸下或更换后拒绝旧目标。宇航服生命支持/配置效果挂在持有气罐状态的衣服上，头盔只提供套装部件资格与自身防护。
- Food 静态世界实例可休眠，库存腐败/融化走冷数据规则。蛋白质负责回血门槛，成本通过 IHealingCostPolicy 一次结算；濒死或 HP≤0 时停止被动回血/生存伤害。体力恢复按有效 MaxValue；医疗等待/提交由 BodyPartTreatmentSession 管理，UI 只发意图。
- 食物机制由目录与消费物上的 IFoodMechanic 组合；咬痕由 EatingProgress 和 GUID 重建。宇航服的独立单槽气罐保存为 InventoryInstanceSnapshot，接收单只适配纯氧气罐；两相变化后停止供氧。装备效果读实际罐扣量再 SupplyOxygen，供氧/耐压来源不属于氧气模块。
- 液体只有容器/机器组分和世界 Liquid 层，无独立库存原料。LiquidContainerState.Composition 保存稳定 LiquidId 的真实份数，MixedLiquidContents 派生总量/比例；容量共用，小数尾量、随机态和各口 PendingOutputs 持久化。
- 朝上开口容器用 `Mod_WaterVessel.CollectsRainwater` 声明接雨；仅权威落地实例按当地真实雨量累计，0.5强度为60秒一份、限制30～120秒，随运行时间倍速/暂停。`VesselRainwaterCollection` 只管时钟与水质选择，污染由 `ContaminationSystem.TryGetNormalizedLoad` 汇总本格所有登记指标；零污染产纯水、任意正污染产脏水，查不到地格不产水。满桶不积攒债务，`RainwaterProgress` 随共享液体模块装拆/保存保留；铁桶与木桶共用正式建筑配对，不给 UI 加类型分支。
- 转液经 ILiquidTransferPort 原子事务，投料反应由 ingredientReactions 声明并事务扣真实槽。Mod_VesselContents 独立保存木桶六格固体库存，关闭面板只解绑视觉；热加工由 IInventoryHeatTreatment 读取 LiquidDefinition.HeatProcess，先提交产物再消耗液体。
- 世界装液由 WorldLiquidSourceResolver/WorldLiquidSystem.TryPump 读稳定身份；地表权威为 0～10 整数级，1 份=1 级，容器空位向下取整后先世界提交再写容器。具有 worldWater 的液体倾倒到干地/同种液面写独立 Liquid 层；成功后发布本格通知，由农业自行吸水。
- PourToGround 按开口、倾角持续计时，每批严格 1 份，可溢出不足一份时不扣液；90° 可倒尽完整份，工业混液小数尾量留桶，整份出口跳过尾量且尊重其它口预约。先扣源再写地表，拒收恢复容器快照；容量不决定流速。UI 以独立 pointerId、未旋转父级坐标解释手势，右侧负 Z、左侧正 Z；WaterVesselPourGraphic 按帧连续显示流，不反扣数据。
- 饮水只取 Drinkable 和余量，满水分仍可主动饮用；drinkEffects 由 LiquidDrinkEffectProcessor 统一结算。容器图标经 ItemDataPresentationResolverRegistry/GameRes.TryGetItemPresentation 状态重绘，Sprite 保留物理轮廓；手持内部状态改变同时通知真实库存与 Item.OnUIRefresh。

## 农业

- `Mod_Plantable → CreateEntityCrop → 扣真实种子`，失败 RollbackEntityCrop；resource 作物不临时物化 Item。空耕地播种不检查水肥，水肥只影响后续成长；范围内的种植基质保留播种意图，作物占格或播种失败不回退食用，离开基质才让食用接管。
- FarmlandSystem 操作 ChunkTerrainData；未完成锄地仅为会话进度，停工 10 秒后恢复，完成才写 Tile_Farmland。水肥快照修改经 CommitSoil；工具结算等待 IWeaponActionAnimation 确认真实挥动。
- 耕地 `waterValue` 为 `int 0～10`，一份世界水只增加一级；`WorldLiquidSystem.SurfaceLiquidAdded` 只通知倒入本格吸水，不遍历全部耕地，作物读取时保留九宫格灌溉。天气和作物速率使用级/秒，不足一份的补水/耗水进度独立随农业差量保存，所有消费源共用地块进度；湿润度比例计算必须显式转浮点。
- 播种作物句柄归 ChunkAgricultureRenderer，快照进 AgricultureCells；天然资源使用生态 GUID/差量。解绑不表示死亡；一次收获先生成全部产物再删除来源，持续采果只扣库存，植株/产物保持不同 ID。
- 植物保存绝对游戏秒，通过 IWorldTimePlant/PlantClimateTimeline 补算冻结历史；成长器统一结算气候、水肥、天气与难度倍率，游标未追上不收获。雨雪由 WorldWeatherSurfaceSystem 供水，植物不重复加雨。
- seedling/growing/mature 三图由 normalizedGrowth 派生，不增加存档阶段。BodyClip 材质的 BodyMinV/BodyMaxV 是本地 Y，主体与描边保留 DisableBatching。伴生物通过 HostGuid 和 INaturalCompanionHostCondition 门禁生成。
- BerryCrop 使用资源库存/日历生产，DayLength 决定批次，满库存仍走时钟；稳定 GUID 固定产量区间，一次采集一份，不套额外掉落倍率。WildCrop_Base 提供野生成熟初态/受击能力，具体死亡掉落仍由 lootTableId 声明。

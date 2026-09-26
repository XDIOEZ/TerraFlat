---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
---

# FlatWorld 建造

## 入口

- 主链：`Assets/5_Scripts/5-3_GamePlay/World/Building/{Mod_Building,BuildingShadow,BuildingOccupancyRegistry}.cs`
- 建筑 JSON：`Assets/StreamingAssets/GameConfig/Items/shells/{building_summoners,building_bodies}.json`；召唤器统一使用 `BuildingSummonerShell`，动态本体统一使用 `BuildingBodyShell`。
- 建筑 Shell/模块迁移：`Assets/Editor/FlatWorld/ContentTools/Migrations/BuildingShellMigrationTool.cs`；差异玩法位于 `Assets/2_Prefabs/Gameplay/Modules/Building/`。
- 门/堆肥：同目录 `Mod_Door.cs`、`Mod_CompostBin.cs`
- 结构：`World/Map/Structures/{ChunkGenerator_Structures,StructureData,StructureItemAuthoring}.cs`
- 结构资源：`Assets/4_ScriptObjects/World/Structures/`

## 核心链与不变量

- 门模块必须绑定所属 Item 的主 SpriteRenderer 与根实体碰撞体；Unity 缺失组件的假 null 不能用 `??=` 判定。打开时交互注册仍须保留，导航通行通过 `BuildingOccupancyRegistry.SetPassable` 更新，但建筑放置占格不撤销；安装流程完成后再次同步开门状态。
- 放置范围由交互半径的两倍统一派生，虚影与提交复用同一格边距离校验。越界隐藏虚影；仅在实际提交时以 `BuildingPlacementFailureReason` 向表现层区分越界和其它非法位置，不能逐帧发提示。
- 放置模式的右键所有权不等于位置有效性；虚影因越界隐藏后，仍需根据当前准线先做范围校验并发拒绝反馈，不能被“预览为空”提前返回吞掉。

`Summoner → BuildingShadow 校验 → PlacedBuilding → 注册占地 → 导航脏格`；拆除反向生成带 Snapshot 的 Summoner，成功后才删除建筑。

- 以 `BuildingRole` 区分 Summoner/PlacedBuilding，禁止用血量或位置推断。
- 无快照的新放置必须通过 `GameRes.CreateItemData(BuildingPrefabId)` 创建本体 JSON 数据；禁止再克隆 Summoner 数据后改 ID。拆除快照仍由 Summoner 携带并优先恢复。
- 可手持操作的设施仍使用真实 Summoner 与独立 BuildingBody；以 `RequiresPlacementRequest` 从正式面板显式进入放置模式，功能模块先调用 `TryHandlePlacementAction` 仲裁使用，禁止同一次动作同时建造、装水或开面板。`RequiresPlacementRequest=true` 只允许用于确有正式入口调用 `BeginPlacement()` 的物品；没有面板或其它显式入口的普通召唤器（例如简单载具）必须保持 false，让 `Item.OnAct -> Install` 直接进入标准放置链。两端通过 `Building_Data.SharedModuleIds` 声明需双向转移的模块，由 `BuildingModuleStateTransfer` 按唯一稳定 ID 深拷贝；拆回必须在载体 `Load` 前注入，重放旧建筑快照后必须再覆盖当前载体的共享状态，避免手持期间的水量/库存修改丢失。转移仅允许单件载体，失败不得修改或消费源物品。
- 便携建筑的制作耐久品质通过 `CraftedDurabilityMultiplier` 在 Summoner 与 PlacedBuilding 间迁移；`Item.Load` 按当前 `RuntimeItemDefinition.Health.MaxHp` 重新计算实际 `DamageReceiver.MaxHp`，避免放置、拆回、读档或重复 Load 时丢失品质或重复叠乘。
- 便携设施若由共享模块驱动状态贴图，Summoner 与 PlacedBuilding 都必须在各自当前 Item 定义中声明对应 `visual.spriteStates`；运行时视觉解析只读取当前载体 ID 的定义，不能假设落地本体会自动继承手持物的状态图。
- 当便携设施的 Summoner 与 PlacedBuilding 使用不同稳定 Item ID 时，`Building_Data.Role` 必须与当前载体 ID 对齐；模块加载、联机数据应用和面板绑定都要按 `SummonerPrefabId/BuildingPrefabId` 校正当前实例，禁止一个已落地实例的角色状态污染另一个同类手持物的“放到地上”按钮。
- 建筑 Summoner 一旦进入有效放置模式，玩家普通世界交互必须让位于放置：附近 `IInteractable` 不得因交互键、鼠标点选或同帧多输入被打开，交互描边也应隐藏；退出放置模式后再恢复普通目标选择。
- 通用建筑本体只提供 `Item + SpriteRenderer + BoxCollider2D`；伤害由 JSON `health` 注入，门、容器、工作台等反馈由独立 `IInteractable` Module 提供，不再依赖通用 `Mod_InteractReciver` 转发。
- 火把的手持职责与落地建筑本体保持分离：落地后统一使用 `Torch_Building`，`Torch_Summoner` 只能指向后者；攻击、武器动作、燃料、燃烧、光源、燃烧视觉、局部温度、命中 Buff、投料交互与建筑能力全部由 JSON 组合通用模块，运行时代码不得保留 `Mod_Torch` 或其它火把专用玩法聚合器。手持/落地之间只通过 `SharedModuleIds` 转移确需持续的燃料与燃烧状态。
- 普通动态建筑保持 GameObject + Collider，机械动力建筑走 `MechanicalWorld` 数据节点；放置冲突、导航占地以及 AI 视线遮挡统一读取 `BuildingOccupancyRegistry` 的离散世界格层，不得写入地形 `TileData`，也不得用 Physics2D Overlap/Collider Bounds 推导这些逻辑结果。
- 动态建筑的多格占地由建筑模块 `Building_Data.FootprintWidth/FootprintHeight` 声明，吸附格为左下锚点，缺省零值按 1×1 兼容旧数据；预览与提交必须逐格校验地形和占用，落地及读档逐格注册，拆除、禁用和失败回滚统一注销。视觉与物理碰撞体可覆盖多格，但不能代替离散格校验；跨世界边界的每格分别按拓扑归一化。
- 建筑占地的 Revision/CellChanged 同时服务 Native LOS 脏块桥，不能只依赖导航最终可走值的变化来刷新视线（例如原本不可走但不遮挡的格）。通知只标脏，复制前完成旧 Job；退出世界注销订阅，避免每个 AI 注册占地事件。
- `Module_Building` 不得再携带独立物理 Collider；其 `boxCollider2D` 运行时统一绑定所属 Item 根节点由 `visual.collider` 定义的碰撞体，避免模块默认框与建筑实体框叠加后产生额外阻挡或错误光照遮挡。`BuildingBodyShell` 的根碰撞体必须保持启用、非 Trigger，并位于 `Collider` Layer；召唤器查询碰撞体仍按召唤器规则处理。
- 动态建筑的局部光阴影配置集中在 `Mod_Building.LightOcclusion.cs`：`LightOcclusionMode=Automatic` 只在自身有效 `Module_LightSource` 的光源进入真实轮廓或边缘间隙时，裁去光源以上的遮挡，熄灭后恢复完整轮廓；读取真实 Light2D 启用状态以兼容燃料模块直接熄灯。默认间隙 `OwnLightClearance=0.0625` 世界单位，`FullSilhouette` 保留封闭外壳，`None` 适合纯火焰/透明物。参数属于 Prefab/JSON `parameters` 配置，不写入建筑快照；MOD 改接收层后调用 `RefreshLightOcclusion()`。裁剪作用于该建筑对全部局部光的轮廓，是 2D 近似，不代表按光源高度逐灯排除自身。
- 平台与地板铺设都由 `Tile_Block.groundPlacement` 与 `TileBuildingSystem.GroundPlacement` 负责，统一写入 `TerrainSupportLayer`，不替换底层地形、不占用 Blocking 层；来源地面标记与液体条件独立，平台用 `LiquidRequirement=Present`，地板用 `RequiredSourceFlags=Walkable + LiquidRequirement=Absent`，液体条件直接读取原始 LiquidDepth。预览、角色水域效果、建造和导航读取有效覆盖面；扣料失败或主动拆除只撤销覆盖值，底层原始地块自然恢复。
- 静态岩壁/结构墙才使用 Blocking Tile；例如 `Wall_Stone`、`Wall_Wood` 只有 Summoner JSON，不创建动态本体定义。Tile 栈只通过 `Data_TileMap` API 读写。
- 建筑“主动完整拆回”与“受伤摧毁”必须分开结算：锤类致命伤害可完整返还 Summoner，非锤类摧毁只按该 Summoner 的唯一精确制作配方逐份随机回收材料；天然 Tile 的资源掉落仍服从自身 `damageProfile`。建筑 Summoner 进入世界掉落态后的缩放由 `DroppedItemService` 统一派生，拆除调用方不得按落地建筑尺寸硬编码。
- 新版 WorldModel 的玩家格子建筑虽使用 `ChunkTerrainData.BlockingTileId`，仍必须接入存档的运行时区块差量；不能只依赖 `MapSave.items`。
- 新版格子建筑的耐久或累计损伤必须与 `BlockingTileId` 一起保存在 `ChunkTerrainData`，并进入 `RuntimeTileDeltas`；否则区块回收或重载后会回满。
- 新版 WorldModel 的动态建筑 Item 不挂旧 `Chunk.RunTimeItems`；必须按 `Mod_Building` 的角色筛选，在 `ChangedItems` 中保存完整 `ItemData`，并在区块就绪后恢复模块状态/耐久。
- `BuildingShadow` 的 `sourceRenderer` 与 `sourceRoot` 必须来自同一对象层级；共享本体 Shell 资源本身没有 Sprite，预览应从 `RuntimeItemDefinition` 创建无模块的纯视觉轻量预览源，禁止给虚影恢复 Rigidbody/Collider，也禁止回退到召唤器图标或共享 Shell 默认图片。
- 手持物的根 Transform 会被拖拽、倾倒等表现逻辑临时旋转；创建 PlacedBuilding 时必须把世界建筑根旋转重置为 `Quaternion.identity`，禁止继承 Summoner 或拆除快照的根旋转。建筑自身需要的固定朝向应写在最终视觉资源/Renderer 的局部变换中。
- 建筑本体 JSON 的 `visual.collider` 只描述最终实体的物理碰撞范围，不再参与放置合法性或导航占地计算；当前动态可交互建筑和格子墙一样以吸附后的世界格作为放置槽。
- 带 `Building_Data.TileBlockId` 的建筑最终由 `TileBuildingSystem` 写入 Tilemap；预览根与一格占地以格心为锚点，图片直接读取目标静态 `Tile` 的 Sprite 和 transform，禁止继承动态本体或召唤器图标的偏移。高墙图片应将 Pivot 放在底部占地格的中心，避免用 Tile transform 平移同时移动 Grid Collider；虚影同步 Tile 的缩放与旋转。
- 新增玩家格子墙需同时配置召唤器 `TileBlockId`、`Tile_Block` 伤害与掉落、Unity Tile、生成 Profile 的 `tile.block.*` 和 `ChunkTilePaletteSO`；权威落地生命来自 `Tile_Block.damageProfile`，不能按召唤器的 `health` 推断。保留的迁移源 Prefab 也需声明 `Data.TileBlockId` 与 `BitData.TileBlockId`，并在 `BuildingShellMigrationTool` 标记为 Tilemap 建筑，否则重新导出会恢复动态本体链路。
- 资源装配脚本只是编辑器工具，新增格子建筑交付时必须包含实际生成的 Tile、Tile_Block、图片导入设置、Profile/Palette 映射与 Addressables 注册，不能只交付脚本和可制作的召唤器。`GameRes.TileBlockDict` 在资源会话中加载；补齐资源后可返回主菜单按 F5 重载。世界生成仍使用存档冻结 Profile，但玩家可建造 Tile 的 `tile.block.*` 映射读取当前内容目录并在旧快照缺失时补充，因此新增地板/建筑不得要求玩家重开世界；数字 TileId 必须保持稳定且不能与旧快照已有映射冲突。建筑关系的权威配置位于模块 `data.BitData`；`BuildingResourceCatalogValidator` 与静态目录检查必须读取该字段，不能只查 `parameters.Data`。
- 手机准星可以停在最大建造半径；格心吸附会产生每轴最多半格的偏差，距离校验应按目标格最近边缘判断，禁止直接用吸附后格心距离或 `Ceil` 取整决定预览与放置资格。
- 动态建筑相邻放置只比较 `BuildingOccupancyRegistry` 中的离散格记录；相邻格永远不因实体 Collider 接触而互相否决，同一格则由占用层直接拒绝。
- Summoner 只能由快捷栏的真实手持实例放置：`IsItemInInventory`、`BuildingShadow` 与源槽扣减都依赖 `InHand + Owner + CurrentSelectItemSlot`；库存菜单不得临时实例化 Summoner 后直接 `Act`。
- Summoner 成功放置后的源槽扣减必须继续走 `Inventory_Data.TryConsumeFromSlot/TrySetSlotItemAmount`，并在事务成功后按真实快捷栏槽位索引显式调用 `Inventory_HotBar.RefreshUI`；不能只依赖通用库存事件或仅在数量归零时同步手持物，否则部分堆叠放置后快捷栏数量可能延迟刷新。联机权威数量回写同样遵守这一收尾规则。
- `GamePlay` 程序集不能反向引用已依赖它的 `FlatWorld.Dialogue`；放置失败等玩家反馈由玩法层发布语义事件，再由 Dialogue 表现桥接，并且只能在实际 `Install` 提交失败时发布，禁止从逐帧虚影校验中触发。
- 建筑模块对 `DamageReceiver` 等模块的依赖必须在加载阶段从 `ItemMods` 注册表解析；禁止序列化嵌套模块 Prefab 的组件引用，模块缺失修复后原引用可能成为无效组件。
- 占地算法或安装/拆除顺序变化时联动 `flatworld-navigation` 与 `flatworld-map`。

- 主动拆平台同时检查前后建筑层、设施占地和角色／世界物品占用；返还物完成装配后才撤销支撑。返还装配异常必须清理未完成物件，不能遗留可捡返还物又保留原平台。

## 验证

- 检查预览与最终占地一致、注册/注销成对、失败路径无残留、快照可还原。

## Skill 维护原则

- 机械节点通过 `IBuildingPlacementExtension` 声明占地层、候选数据与拆回快照处理；扩展校验必须同时覆盖放置预览和真实事务。只有 `IBuildingPlacementCommitted` 后才登记世界副作用，不能在未提交候选的 `Load` 中入网。
- 落地机械由 `MechanicalWorld` 保存纯数据节点，不实例化本体 Item/GameObject；`ChunkTilemapRenderer.Mechanical` 复用区块 BRG Owner 的分层槽，机械材质队列在地形之上、普通世界 Sprite 之下。机械绘制不依赖 `MechanicalShaft` Sorting Layer；轴芯、端环和前景层按 BRG VisualLayer 顺序提交。
- 普通建筑通行性由 `IBuildingTraversalPolicy` 声明；机械节点直接读取 `mechanical-catalog.json` 的 `BlocksMovement` 和 `PlayerMoveSpeedMultiplier`。`BuildingOccupancyRegistry` 负责机械导航与移速，`WorldMotionSystem` 对阻挡机械做数据格扫掠；传动轴不生成 Collider，不按具体机械 ID 硬编码规则。
- `CrossShaft` 只占 Layer1 中心一格，放置时要求同格 Layer0 存在下穿节点，但不要求 A/B 相邻端口预先连接；图构建只把 A/B 接到相邻 Layer0 节点，绝不把同格下穿节点接入跨轴器网络。普通建筑仍占 Layer0，导航只考虑下层阻挡。手持 R 朝向属于模块临时状态，拆回快照和重新选中都必须恢复初始朝向；禁止把方向写进 Summoner 堆叠数据。轴类按横竖两态切换，带定向轴接口的齿轮按四个九十度朝向轮换；世界节点的端口方向、固定输入杆和预览图必须使用同一朝向。
- 机械风箱的四向出风朝向与本体预览、落地旋转共用 `MechanicalNodeState.RotationQuarterTurns`；炉体由 `Mod_Furnace.acceptsMechanicalBellows` 显式配置接入，只影响出风口所对的相邻熔炉或高炉。60 RPM 对应最多 500℃温度上限增量，温度增益截断不改变其它机械用力器的线性效率。
- 机械风箱的传动轴入口使用局部 `AxlePorts:["left"]` 与 `InputShaftLocalPosition`，随 `RotationQuarterTurns` 与右侧出风口保持相反；独立轴图层应像风车叶轮一样挂在主体 Sprite 下，放置预览坐标需包含 ShadowRenderer 偏移，只有齿轮推进本体自转相位。
- 轴类机械端口属于独立视觉图层：物品 `visual.spriteStates.axisPorts` 声明端口贴图，`AxisPortLayout=mirroredSingle` 用于镜像单端接头，`centeredShaftRings` 则居中挂接整格传动杆的双端铁环图，并按 SortingOrder 让箱体遮住中间杆芯；两种布局都只显示一对相对端口并随 `RotationQuarterTurns` 同步旋转。用力器等设备的镜像单端接口总跨度统一为一节标准传动轴的可见长度：左右端口中心间距按 `Shaft_Wood` 轴芯长度推导，当前 128 PPU、116px 可见轴芯对应 `AxisPortOffset≈0.45` 世界单位；不要用 `0.75` 等偏移把每侧接头拉长。设备主体宽于一格时也不得随外壳放大接口，末端应贴合外侧轴承座。双端铁环应按现有传动杆贴图的格边位置与邻接轴环衔接；Summoner 与 BuildingBody 都要声明该状态，主体贴图不得烘入端口。
  - 双端轴环要连接到非地面高度时，将 `Sprite Pivot` 放在端口轴线，使端口层的局部零点、建筑格心和机械网络接口重合；Summoner 与 BuildingBody 使用相同轴线 Pivot，放置预览和落地表现共用 `AxisPortLayout` 与 `AxisPortDrawOnTop`，改 Pivot 后按轴承实际像素重算 `RotorLocalPosition`。主体需显露轴环时用 `AxisPortDrawOnTop` 绘制在支架上层，不要在静态图里重复烘入同一接口。
- 变速箱箱体贴图留空心腔，内部通过 `visual.spriteStates.gearboxGear` 挂接两枚独立齿轮图层；齿轮中心、尺寸比由机械模块参数配置，动画以 `MechanicalNode.Rpm` 和 `EntryDirection` 换算输入侧，输出齿轮反向转动并按半径比换算角速度，预览需与 R 键朝向保持一致。
- 变速箱两枚 BRG 齿轮各自保存相位与角速度锚点；供能侧或倍率切换时先按旧速度积分再上传新参数，避免旋转动画跳帧。其它机械转速变化也只更新 BRG 实例参数，Shader 连续计算角度与轴芯 UV。
- 小型机械传动件通过建筑模块的 `LightOcclusionMode=None` 与 `CastSunShadow=false` 同时关闭局部光遮挡和太阳长投影；大型机械建筑可保留投影。预览虚影仍用于放置反馈，不能因关闭真实阴影而停用。
- 机械用力器效率统一按 `Rpm / RequiredRpm` 线性计算且不封顶，不得再次除以全局基准转速。扭矩源为输出端、用力器为不转送扭矩的输入端；输入相接不入网，同速扭矩源允许合网并累加扭矩，异速扭矩源停转，未供能的源不参与转速冲突。变速箱左侧大齿轮输入时转速 ×2 / 扭矩 ×0.5，右侧小齿轮输入时转速 ×0.5 / 扭矩 ×2；负载按所在侧扭矩倍率折算，旧材料 ID 仅保留存档兼容且不应重新暴露为配方。
- 动力源的扭矩比例与实际转速是独立输入；动态源分别实现 `MechanicalWorld.RegisterSource` 与 `RegisterSourceRpm`，网络冲突、负载和输出转速统一读取本次求解的 `MechanicalNode.SourceRpm`。水流源读取权威水格的 `RuntimeWaterCurrentSample`，通过共享漂流速度规则和定义中的 `SourceRadius` 将线速度换算为 RPM；不能只用“邻格有水”判定供能。
- `CrossShaft` 的世界贴图占一格（128×128、128 PPU；由双格原图适配时应围绕中心裁切，不能横向缩放）：落地静态框架为两段滚动木芯留透明窗口，运行时木芯按节点 RPM 滚动；两端通过共享传动轴的 `shaftEndRings` 状态叠加铁环，Summoner 静态图也要合成同款端环，确保预览与落地外接端口一致。
- 建筑若需让外接传动轴与叶轮轴心同高，应以轴心作为 Sprite Pivot 和建造锚点，旋转图层使用轴心中心 Pivot 且局部位置为零；建筑支架可向锚点下方延伸，Summoner 图标使用静态合成图，落地本体使用固定主体与独立旋转层。
- 机械风车是机械动力源、用力器/出力器链路和外接传动轴视觉的权威参考对象：塔身 Sprite 使用 128 PPU、Pivot `(0.5, 0.06606607)`，把底部横向外露轴套放在放置锚点线上；独立转子使用中心 Pivot `(0.5, 0.5)`，风车自身的轴承中心对应 `RotorLocalPosition=(0, 1.3828125, 0)`，Summoner 静态合成图沿用塔身 Pivot。其它风/水动力源复用该根锚点、端口和分层旋转机制，但转子局部位置必须按各自贴图的轴承中心相对根 Pivot 换算，不能盲目复用风车的转子高度；运行转子、放置预览和 Summoner 静态合成图必须使用同一局部位置。水车本体必须落在 `LiquidDefinition.WorldWater` 有效的水格，三脚架底座作为建造锚点，独立叶轮按 `RotorLocalPosition` 配置并与召唤器合成图保持一致；机械节点保留逻辑传动端口，水车不额外挂传动杆 Sprite。
- 建筑实体碰撞范围配置在物品 `visual.collider`，`health.collider` 属于 `DamageReceiver` 的受击碰撞体；调整建筑底座阻挡范围时只改前者，避免缩小受击范围或把命中体误当成物理碰撞体。
- 可堆叠的召唤器若携带拆除快照，堆叠身份必须来自规范化后的快照内容，不能附加每实例 Guid；先归一化放置事务会重写的 Guid 与 Transform，再计算身份，并在 `IsValidSummonerData` 校验同状态堆叠。扩展确认状态可由物品定义完整重建时，可通过 `IBuildingSnapshotRepackPolicy` 省略默认快照身份，使拆回物品与新制物品合堆；共享模块、损伤或非默认状态仍须保留快照。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

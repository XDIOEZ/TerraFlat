# 库存权威与生命周期

## 容量、质量与拾取

- 普通槽 DefaultSlotVolume=int.MaxValue，不可堆叠物一件一槽，装配槽容量1；数量/容量为float，整数取 GetWholeStackAmount。生存行囊18格，储物/制作读配置。
- 创造背包 MaintainDynamicSlotCount 按 ContentStructureRevision 检查动态空槽；槽位/物品引用、容量模式变化使缓存失效，稳定 Tick 不重扫。创造目录仅可持有定义、可扩缩、豁免重量体积。
- ItemStack.Amount/Weight、ItemData.Stack 原位改动发质量事件。Inventory_Data 单格替换只重绑该格 MassChanged，换槽表才全绑；新含质量载荷走 NotifyMassChanged/PayloadChanged，无质量变化不使船重失效。观察引用/订阅不存档或克隆。
- GetCarryUsage 单次即时扫描实际随身库存并去重；预览不计入，不跨帧缓存，创造模式跳过无用预检。背包监听全部随身库存，手工台绑定数据事件，卸载解绑。储物 kg/m³ 在 InitData/恢复时换L、复用容量策略；IsDepositBlocked 拒跨库转入，仍可取出/内部整理。
- 世界拾取先用独立候选/去重目标生成只读容量计划，整数物能收多少收多少，快捷栏余量入背包；回执区分原堆扣量与实际入包量，GM倍率仍以完整原份结算。轻量掉落 body.Amount 为唯一数量权威，先播种冷载荷，防旧数量复活。
- 质量事件/倍率实变只标拾取修订号，下次接触重试，不递归拾取。Mod_ItemPicker 的 Trigger 每次只解析一次Collider；无Item/有效Stack不扫库存，飞行/禁拾取目标只登记延迟拾取、不做容量同步，恢复后按修订号重试。

## 联机与入口

- 使用/攻击共用服务器正式 Act/武器入口，客户端仅发槽位、aim、realm，服务器取真实物品；UsePlayerItem 走 pending/ack，ILocalItemUsePresentation 仅本机展示。
- ClientGroundCombatPending 暂缓拖拽、快捷转移、制作，Host消耗结果应用后恢复。原生丢弃、快捷栏、进食、本地弓耗弹在副作用前查询 ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction；桥仅由联机业务登记，单机/Host放行，不停移动/环境/AI，测试入口留 Development/Testing。
- TryConsumeFromSlot/TrySetSlotItemAmount 等事务改量；GetItemSlot 对空表/越界返null，不回退槽0，删除/装备/读书链均判有效槽。
- 远端 Hotbar/Bag 加载真实数据与手持，不建UI/本机输入；视觉副本用 ApplyRemoteNetworkData，权威手持用 ApplyNetworkAuthorityData。太空拾取commit也走服务器真实库存，Host共享库存只入一次，客户端选择请求不改量。
- ReadableBookGrantService 创建真实带内容读物；writingMaterialDefinitionId 经书写能力/材料标签校验后送一份至书内槽0。ReadableBookInstanceSnapshot 保存 AddedPageCount/PageContents/InputInventory；复用随身容量、接收策略及pending门禁，不生成世界Item或改共享静态页。

## 手持与显示

- InstantiateHeldItem 注册前绑定真实 ItemData、Owner/inHand；事件标脏、安全边界同步，同GUID跨槽保留实例。先 ModuleSave 手持再存库存；卸载释放输入/事件/PlayerHand/所属Item。ItemMgr先毁时用 GetInstance 取存活管理器，缺失则销毁自有手持，不调用 Instance。RuntimeInventory 在Player Prefab以[SerializeReference]保存。
- BindController/UnbindController 只管输入，UnbindRuntimeDataEvents 清数据/UI监听。Inventory.basePanel 仅拥有通用UI_Content，自管面板用 SyncQuickTransferTarget；皮肤由 InventorySlotVisualProfile，Tooltip 经 IItemTooltipInfoProvider 扩展。模态库存锁输入，快捷栏/手部/机器面板不锁。
- 排序循环ID/分类/数量/重量/体积，分类读InventoryGroup；整理稳定合堆压空。搜索仅改槽Alpha，按定义/Tag别名匹配，排序/整理稳定置顶命中项；虚拟映射可null，拖拽期间不重绑。

# 装备、食物与液体容器

## 装备与生存

- 熟铁五件套在 `Items/shells/character_outfits.json`，钢甲沿用 `equipment.json` 的 `Chestplate_Iron` 业务 ID，其余钢套在 `steel_equipment.json`；十件金属护甲的配方统一由 `Recipes/crafting/metal_armor.json` 管理，分别消耗熟铁锭和钢锭。
- Mod_Equipment按EquipmentSlot标签组合能力、世界主交互优先。ThermalInsulation只改安全温度上下界，耐寒/耐热独立，不改传热/当前温度。
- 眼部独立使用 `EquipmentSlot.Eyes`，与头部可同时穿戴；装备可用 `EquipmentBlocksSlot.<槽ID>` 声明双向互斥，统一由 `Inventory_Equipment.CanEquipAt` 校验。当前头盔未设置眼部互斥。
- `Mod_Equipment.TryEquipFromSlot` 通过真实库存 `TryMoveSlotTo` 穿戴/交换，旧装备回来源槽，保留容量、接收、拖拽和联机事务门禁；快捷栏 Shift+右键只负责选择这个入口。
- 配置效果实现 IEquipmentConfiguration，由CollectConfigurationTargets/TryOpenConfiguration收集/校验、同件去重，卸下/更换拒旧目标。宇航服生命支持/配置挂持罐衣服，头盔仅套装资格/自身防护；独立单槽InventoryInstanceSnapshot只收单只纯氧罐，两相变化停供，扣真实罐量后SupplyOxygen，来源不归氧气模块。
- FoodRulePipeline初始化后复用Tick/观察者列表，不定时全扫；RequiresFoodTick实时判，新扩展保持插入顺序。世界静态Food可休眠，腐败/融化走冷数据。蛋白质管回血门槛，IHealingCostPolicy一次结算成本；濒死/HP≤0停被动回血与生存伤害，体力按有效MaxValue恢复。
- 食物腐败共用 `FoodSpoilageObserverData` 游戏时钟与到期分流；仅显式配置 `DryingTargetItemID/DryingMaxMoisture` 的食品按含水量分流，生肉超过20%变腐肉、20%及以下变肉干。库存走食物替换网关，世界两后端走 `WorldItemMatterRules` 产物计划；未初始化含水量读物质定义初值，到期失败保留计时重试，大步进不得计入到期后的蒸发。
- 腐败/融化首次参与模拟时将配置转为 `FoodMechanicStateData.Timer` 强类型真源，快照、复制和指纹含计时；库存继续按原条目顺序领取时钟、提交事务。持续营养/水分消耗由 `FoodNutritionConsumptionData` 单次及 Span 入口共用，模块负责采样和通知。
- BodyPartTreatmentSession管医疗等待/提交，UI仅发意图；食物效果由目录与IFoodMechanic组合，咬痕从EatingProgress/GUID重建。

## 容器与热量

- 液体仅存在容器/机器组分和世界Liquid层，无独立库存原料。LiquidContainerState.Composition保存稳定LiquidId真实饮用/配方份数（默认0.25L），`MixedLiquidContents.GetVolumeLiters/GetReceivableServings` 按各组分真实体积校验共享容量，UI显示L；`capacity`按标准0.25L份配置：默认桶/陶罐/坩埚200=50L、椰壳80=20L、水槽1600=400L。尾量/随机态/各口PendingOutputs持久化。
- InventoryVesselHeating.ProcessWorldHeat供落地容器/已加载坩埚按当地环境每游戏秒消除剩余温差1%；坩埚仅自身Tick一次并继续凝固，炉内ProcessHeat主动10℃/秒独立。冷库存/轻掉落不额外模拟液体内容物。
- CollectsRainwater声明朝上接雨，仅权威落地实例按当地雨量累积：强度0.5每60秒一份、限制30～120秒，跟游戏倍速/暂停。VesselRainwaterCollection只管时钟/水质，ContaminationSystem.TryGetNormalizedLoad汇总本格全部指标，零污染纯水、正值脏水、无地格不产。满桶不积债，RainwaterProgress随共享模块装拆/保存；铁/木桶用正式建筑配对。
- 转液走ILiquidTransferPort原子事务，ingredientReactions扣真实槽；Mod_VesselContents独立存木桶6格固体，关面板只解绑。IInventoryHeatTreatment按LiquidDefinition.HeatProcess先提交产物再耗液。

## 世界装液与倾倒

- WorldLiquidSourceResolver/WorldLiquidSystem.TryPump读稳定身份，地表0～100整数份、每份10L、画面十档；`WorldLiquidVolumeConversion` 独立换算地表份与饮用份，`IQuantizedLiquidTransferPort` 让正式事务预检按来源量子取整，不把世界1份直接当饮用1份。worldWater液体倒到干地/同类液面写独立Liquid层，成功只通知本格、农业自行吸水。
- PourToGround按开口/倾角连续计时，每批严格10L、不足整批不扣；90°可倒尽完整批，混液尾量留桶，整批出口按每种组分体积跳尾量并尊重其他口预约。先扣源再写地表，拒收恢复快照，容量不决定流速。
- UI按独立pointerId/未旋转父坐标解释，右负Z、左正Z；WaterVesselPourGraphic连续显示、不反扣数据。
- 固体真实槽抵达倒置开口后经 `VesselContentsOperations → InventoryWorldDrop` 出料，和玩家丢弃共用先生成后扣量/失败撤销；客户端发容器槽位及GUID意图，服务端取真实内容。坩埚装拆共享液体和Mortar库存，空锅仍打开统一容器面板。
- 饮水只看Drinkable/余量，满水分也可主动喝，drinkEffects经LiquidDrinkEffectProcessor结算。图标由ItemDataPresentationResolverRegistry/GameRes.TryGetItemPresentation按状态重绘并保留物理轮廓；手持内部变化同时通知真实库存与Item.OnUIRefresh。

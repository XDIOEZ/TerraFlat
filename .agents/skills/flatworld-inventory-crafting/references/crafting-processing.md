# 配方、加工与冷调度

- 通用输入适配经 Inventory.TryTransferQuantityTo 复用容量/数量/接收/机器联网事务；ItemSlot_UI.BoundInventory 定身份、虚拟槽释放清空。读取 CraftingStationView.Controller 的候选/选择/进度，选择走真实按钮事件，不从EventSystem焦点推断。
- 查缺配方同时看 Recipes、物品processing、Mechanical Processes、matter温湿转化、便携映射；天然物/温变产物/内部或GM变体不机械补普通配方。手工目录：`Recipes/crafting/handmade_equipment.json`、`handmade_utilities.json`。
- 普通Crafting unordered，需求按物品/Tag总量，amount=0要求工具存在但不扣量，耐久由actions独立控制；额外料不屏蔽候选，Exact/Tag重叠做全局容量分配，按选中RuntimeRecipe精确提交。热加工保留有序/镜像/网格。
- 多产物全部放下才提交，失败不扣料/部分产出；非堆叠逐件占槽。合法定义来自 GameRes.ItemDefinitions；旧recipeDict仅留未迁移熔炼单值索引，普通候选用CraftingRecipeCatalog。被动无配方只清预览。
- processing不继承parent，ItemProcessingResolver转RuntimeRecipe；capability/level由设备给，minLevel/maxLevel闭区间。handcraft/workbench互认，手工台4/2、工作台5/2输入输出；落地台手势为手工70%，四舍五入至少1次。
- 石臼grind按材料work算手势，MortarState存配方/进度，切配方清零、满输出保留完成进度；固定库存默认6格，手持/落地/未物化热加工共用数据。
- 手钻/手动设备复用RecipeProcessor.AdvanceManually与CraftingService，SharedModuleIds转库存/进度。固定转换不加难度增产；CraftingOutputRules共用预检/提交，防腐寿命取真实扣料计划最差余量，品质存CraftedDurabilityMultiplier，手钻读矿锭DrillDurability Tag。
- 强化材料支持minTemperature/excludeTag，copyInputTag/reinforceWithAdhesive复制真实扣料工具；拒未初始化/非有限温度，提交重匹配。热树脂≥200℃或Adhesive各扣一份，先max×1.3再修复新缺损50%；实例AdhesiveReinforced防重复，保留品质/GUID/标签，按定义基线恢复倍率，工坊草稿保留全部字段。
- Mod_FlintStrike复用UI_FireDrill，按能力隐藏建筑按钮/绑定概率文案；每击独立10%，不累积/衰减。成功由CraftingTransaction扣一份火绒、产一份火种，失败/满输出不扣料，库存事件刷新预览。
- 库存冷Tick由ModuleDataRuleRegistry/InventoryModuleDataScheduler按规则与宿主代际推进；结构变才重建，数值不重建。Entry值类型、只读引用遍历，槽/代际/规则实时复核；共享跨库存状态扣除重叠时间、不补暂停。
- 便携罐按不可变模块声明缓存配置，对外仍独立副本；真实Contents/二进制载荷/目录换代才重建流体包装，稳定Tick不重复序列化/刷新。保存比较StateRevision/超压时钟/破裂，UI比较内容/生命。

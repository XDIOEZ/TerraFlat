# 装备清单、样例与整套图标

2026-10-24 更新：原“铁卫”五件已改为普通熟铁套（铁头盔、铁胸甲、铁护手、铁腿甲、铁战靴）；原 `Chestplate_Iron` 按其钢锭配方改名为钢甲。新增钢头盔、钢手套、钢腿甲、钢靴子四张图标，与钢甲组成完整钢套，当前可穿戴装备合计 23 件。钢套五件现已配齐独立的 32×48 穿戴层，头盔沿用头发遮罩并透明露出脸；[男女穿戴静态预览](../Characters/PlayerTemplate/Source/SteelArmor/SteelArmor_Preview.png)。[钢套图标绘图记录](GenerationPrompts_Steel.json) · [穿戴层源图与标定](../Characters/PlayerTemplate/Source/SteelArmor/Generation.json)。金属护甲配方统一在 `GameConfig/Recipes/crafting/metal_armor.json`。下方 2026-10-23 清单保留当时的交付记录。

同日补齐藤甲、木甲、工程护目镜和宇航服五件的独立人物穿戴层，并接入物品定义及 Addressables；当前 23 件人物穿戴装备均有对应素材。另有木盾沿用独立副手显示。[新增素材预览](../Characters/PlayerTemplate/Source/EquipmentCompletion/EquipmentCompletion_Preview.png) · [宇航服逐件预览](../Characters/PlayerTemplate/Source/EquipmentCompletion/Spacesuit_Preview.png) · [绘图与覆盖记录](../Characters/PlayerTemplate/Source/EquipmentCompletion/Generation.json)。

统计日期：2026-10-23。依据启用的物品目录和真实素材引用建立清单，先确认三张样例，再完成本轮指定的 19 件穿戴装备图标。

**当前交付：19 张图标已齐，并接入游戏物品配置。** [打开整套图鉴](EquipmentIcons_20261023.html) · [交付清单](ProductionManifest.json) · [完整绘图提示词](GenerationPrompts_Production.json)。武器、工具和箭支由其他聊天处理，下面的广义统计仅作原始清单参考。

## 统计口径

源目录：[item-manifest.json](../../../StreamingAssets/GameConfig/Items/item-manifest.json)。只计启用分包中的具体物品，排除抽象父项、旧素材包中没有对应物品定义的 PNG，以及建筑本体/召唤器的重复项。

| 范围 | 物品 ID 数 | 独立图标引用数 |
| --- | ---: | ---: |
| 可穿戴装备 | 19 | 19 |
| 手持武器与工具 | 42 | 41 |
| 两类合计 | 61 | 60 |
| 另列：箭支耗材 | 4 | 4 |

这里的“外观种数”按具体物品图标引用去重，不表示已经具备可用的动画或人物穿戴层。`Spear_Stone` 与 `Spear_Stone_Animation` 共用石矛图标，绘图按一件计。若把箭支也纳入美术重绘，共 64 种图标。

可穿戴装备按组为：基础胸甲 3、旅人 5、铁卫 5、宇航服 5、工程护目镜 1；按槽位为：头部 4、外甲 6、手套 3、裤装 3、鞋靴 3。

## 可穿戴装备完整清单

路径缩写：`PT = Assets/6_Art/Generated/Characters/PlayerTemplate`；`CHEM = Assets/6_Art/Generated/Chemistry`；`ART = Assets/6_Art`。`—` 表示物品配置未声明 `visual.spriteStates.wearable`。

| 名称 | 物品 ID | 槽位 | 现用图标 | 现用穿戴层 |
| --- | --- | --- | --- | --- |
| 工程护目镜 | `Engineering_Goggles` | 头部 | `ART/Generated/Equipment/Engineering_Goggles/Engineering_Goggles_Icon.png` | — |
| 铁甲 | `Chestplate_Iron` | 外甲 | `ART/Generated/Equipment/Chestplate_Iron/Chestplate_Iron_Icon.png` | — |
| 藤甲 | `Chestplate_Twine` | 外甲 | `ART/Generated/Equipment/Chestplate_Twine/Chestplate_Twine_Icon.png` | — |
| 木甲 | `Chestplate_Wood` | 外甲 | `ART/Generated/Equipment/Chestplate_Wood/Chestplate_Wood_Icon.png` | — |
| 旅人兜帽 | `Traveler_Hood` | 头部 | `ART/Generated/Equipment/Traveler_Hood/Traveler_Hood_Icon.png` | `PT/Wearables/Traveler_Hood.png` |
| 旅人皮甲 | `Traveler_Vest` | 外甲 | `ART/Generated/Equipment/Traveler_Vest/Traveler_Vest_Icon.png` | `PT/Wearables/Traveler_Vest.png` |
| 旅人手套 | `Traveler_Gloves` | 手套 | `ART/Generated/Equipment/Traveler_Gloves/Traveler_Gloves_Icon.png` | `PT/Wearables/Traveler_Gloves.png` |
| 旅人长裤 | `Traveler_Trousers` | 裤装 | `ART/Generated/Equipment/Traveler_Trousers/Traveler_Trousers_Icon.png` | `PT/Wearables/Traveler_Trousers.png` |
| 旅人皮靴 | `Traveler_Boots` | 鞋靴 | `ART/Generated/Equipment/Traveler_Boots/Traveler_Boots_Icon.png` | `PT/Wearables/Traveler_Boots.png` |
| 铁卫头盔 | `IronGuard_Helmet` | 头部 | `ART/Generated/Equipment/IronGuard_Helmet/IronGuard_Helmet_Icon.png` | `PT/Wearables/IronGuard_Helmet.png` |
| 铁卫胸甲 | `IronGuard_Cuirass` | 外甲 | `ART/Generated/Equipment/IronGuard_Cuirass/IronGuard_Cuirass_Icon.png` | `PT/Wearables/IronGuard_Cuirass.png` |
| 铁卫护手 | `IronGuard_Gauntlets` | 手套 | `ART/Generated/Equipment/IronGuard_Gauntlets/IronGuard_Gauntlets_Icon.png` | `PT/Wearables/IronGuard_Gauntlets.png` |
| 铁卫腿甲 | `IronGuard_Greaves` | 裤装 | `ART/Generated/Equipment/IronGuard_Greaves/IronGuard_Greaves_Icon.png` | `PT/Wearables/IronGuard_Greaves.png` |
| 铁卫战靴 | `IronGuard_Boots` | 鞋靴 | `ART/Generated/Equipment/IronGuard_Boots/IronGuard_Boots_Icon.png` | `PT/Wearables/IronGuard_Boots.png` |
| 宇航服头盔 | `Spacesuit_Head` | 头部 | `ART/Generated/Equipment/Spacesuit_Head/Spacesuit_Head_Icon.png` | — |
| 宇航服衣服 | `Spacesuit_Torso` | 外甲 | `ART/Generated/Equipment/Spacesuit_Torso/Spacesuit_Torso_Icon.png` | — |
| 宇航服手套 | `Spacesuit_Hands` | 手套 | `ART/Generated/Equipment/Spacesuit_Hands/Spacesuit_Hands_Icon.png` | — |
| 宇航服裤子 | `Spacesuit_Legs` | 裤装 | `ART/Generated/Equipment/Spacesuit_Legs/Spacesuit_Legs_Icon.png` | — |
| 宇航服鞋子 | `Spacesuit_Feet` | 鞋靴 | `ART/Generated/Equipment/Spacesuit_Feet/Spacesuit_Feet_Icon.png` | — |

配置来源：[engineering.json](../../../StreamingAssets/GameConfig/Items/shells/engineering.json)、[equipment.json](../../../StreamingAssets/GameConfig/Items/shells/equipment.json)、[character_outfits.json](../../../StreamingAssets/GameConfig/Items/shells/character_outfits.json)、[chemistry_equipment.json](../../../StreamingAssets/GameConfig/Items/shells/chemistry_equipment.json)。旅人兜帽与铁卫头盔还声明了 `PT/Masks/Headgear_Hair.png` 头发遮罩。

## 手持武器与工具原始清单（参考，本轮不绘制）

| 分组 | 物品 ID 数 | 独立图标数 |
| --- | ---: | ---: |
| 小刀 | 5 | 5 |
| 长矛 | 5 | 4 |
| 远程武器 | 4 | 4 |
| 斧 | 6 | 6 |
| 镐 | 5 | 5 |
| 锄 | 4 | 4 |
| 铲 | 6 | 6 |
| 锤 | 2 | 2 |
| 其他工具 | 5 | 5 |

下表保留配置中的原始名称。图标路径沿用前面的 `ART` 缩写。

| 分组 | 名称 | 物品 ID | 现用图标 | 配置分包 |
| --- | --- | --- | --- | --- |
| 小刀 | 石制小刀 | `Dagger_Stone` | `ART/Items/Tool_Weapon.png[石制小刀]` | `shells/weapons.json` |
| 小刀 | 铜制小刀 | `Dagger_Copper` | `ART/Items/Tool_Weapon.png[Tool_Weapon_19]` | `shells/weapons.json` |
| 小刀 | 骨制小刀 | `Dagger_Bone` | `ART/Items/Weapons/Item_Weapon_006.png` | `shells/weapons.json` |
| 小刀 | 燧石小刀 | `Knife_Flint` | `ART/Items/Weapons/Guns/iron-weapons.png[iron-weapons_248]` | `shells/weapons.json` |
| 小刀 | 黑曜石小刀 | `Dagger_Obsidian` | `ART/Generated/Combat/Dagger_Obsidian/Dagger_Obsidian_Icon.png` | `shells/obsidian.json` |
| 长矛 | 石矛 | `Spear_Stone` | `ART/Items/Tool_Weapon.png[Tool_Weapon_66]` | `shells/weapons.json` |
| 长矛 | 石矛 | `Spear_Stone_Animation` | `ART/Items/Tool_Weapon.png[Tool_Weapon_66]` | `shells/weapons.json` |
| 长矛 | 铜矛 | `Spear_Copper` | `ART/Items/Tool_Weapon.png[Tool_Weapon_69]` | `shells/weapons.json` |
| 长矛 | 铁矛 | `Spear_Iron` | `ART/Items/Tool_Weapon.png[Tool_Weapon_67]` | `shells/weapons.json` |
| 长矛 | 黑曜石长矛 | `Spear_Obsidian` | `ART/Generated/Combat/Spear_Obsidian/Spear_Obsidian_Icon.png` | `shells/obsidian.json` |
| 远程武器 | 木弓 | `Bow_Wood` | `ART/Generated/Combat/Archery/WoodBow_Icon.png` | `shells/weapons.json` |
| 远程武器 | 反曲弓 | `Bow_Recurve` | `ART/Generated/Combat/Archery/RecurveBow_Icon.png` | `shells/weapons.json` |
| 远程武器 | 吹箭 | `Blowgun_Wood` | `ART/Generated/Combat/Archery/Blowgun_Wood_Icon.png` | `shells/weapons.json` |
| 远程武器 | 投石索 | `StoneThrowingRope` | `ART/Generated/Combat/Sling/Sling_Icon.png` | `shells/weapons.json` |
| 斧 | 石斧 | `Axe_Stone` | `ART/Items/Weapons/Item_Weapon_031.png` | `shells/tools.json` |
| 斧 | 燧石斧 | `Axe_Flint` | `ART/Items/Tools/Item_Tool_366.png` | `shells/tools.json` |
| 斧 | 铜斧 | `Axe_Copper` | `ART/Items/Tools/Item_Tool_376.png` | `shells/tools.json` |
| 斧 | 青铜斧 | `Axe_Bronze` | `ART/Items/Weapons/Item_Weapon_032.png` | `shells/tools.json` |
| 斧 | 生铁斧 | `Axe_RawIron` | `ART/Items/Tools/Item_Tool_371.png` | `shells/tools.json` |
| 斧 | 铁斧 | `Axe_Iron` | `ART/Items/Weapons/Item_Weapon_033.png` | `shells/tools.json` |
| 镐 | 石镐 | `Pickaxe_Stone` | `ART/Items/Tools/Item_Tool_363.png` | `shells/tools.json` |
| 镐 | 铜镐 | `Pickaxe_Copper` | `ART/Items/Tools/Item_Tool_373.png` | `shells/tools.json` |
| 镐 | 青铜镐 | `Pickaxe_Bronze` | `ART/Items/Weapons/Item_Weapon_020.png` | `shells/tools.json` |
| 镐 | 粗铁铁镐 | `Pickaxe_RawIron` | `ART/Items/Tools/Item_Tool_368.png` | `shells/tools.json` |
| 镐 | 熟铁铁镐 | `Pickaxe_Iron` | `ART/Items/Weapons/Item_Weapon_021.png` | `shells/tools.json` |
| 锄 | 木锄头 | `Hoe_Wood` | `ART/Items/Tools/Item_Tool_362.png` | `shells/hoes.json` |
| 锄 | 石锄头 | `Hoe_Stone` | `ART/Items/Tools/Item_Tool_367.png` | `shells/hoes.json` |
| 锄 | 铜锄头 | `Hoe_Copper` | `ART/Items/Tools/Item_Tool_377.png` | `shells/hoes.json` |
| 锄 | 铁锄头 | `Hoe_Iron` | `ART/Items/Tools/Item_Tool_372.png` | `shells/hoes.json` |
| 铲 | 木铲 | `Shovel_Wood` | `ART/Generated/InventoryCrafting/WoodShovel/WoodShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 石铲 | `Shovel_Stone` | — | `shells/ancient_stage.json` |
| 铲 | 铜铲 | `Shovel_Copper` | `ART/Generated/InventoryCrafting/CopperShovel/CopperShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 粗铁铲 | `Shovel_RawIron` | `ART/Generated/InventoryCrafting/RawIronShovel/RawIronShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 青铜铲 | `Shovel_Bronze` | `ART/Generated/InventoryCrafting/BronzeShovel/BronzeShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 铁铲 | `Shovel_Iron` | `ART/Generated/InventoryCrafting/IronShovel/IronShovel_Icon.png` | `shells/ancient_stage.json` |
| 锤 | 木锤 | `WoodHammer` | `ART/Generated/InventoryCrafting/WoodHammer/WoodHammer_Icon.png` | `shells/tools.json` |
| 锤 | 石锤 | `StoneHammer` | `ART/Generated/InventoryCrafting/StoneHammer/StoneHammer_Icon.png` | `shells/tools.json` |
| 其他工具 | 剪刀 | `Scissors` | `ART/Generated/InventoryCrafting/Scissors/Scissors_Icon.png` | `shells/tools.json` |
| 其他工具 | 钓鱼竿 | `FishingRod` | `ART/Items/Tools/Item_Tool_389.png` | `shells/fishing.json` |
| 其他工具 | 打制石器 | `ChippedTool` | `ART/Items/icons_sheet.png[icons_sheet_98]` | `shells/weapons.json` |
| 其他工具 | 火把 | `Torch_Summoner` | `ART/Generated/Tools/Torch/Torch_Spent.png[Torch_Spent]` | `shells/building_summoners.json` |
| 其他工具 | 打火石 | `FlintStrike` | `ART/Items/Tools/Flint.png` | `shells/tools.json` |

## 额外项目和范围边界

| 分类 | 名称 | 物品 ID |
| --- | --- | --- |
| 箭支耗材 | 木箭 | `Arrow_Wood` |
| 箭支耗材 | 石箭 | `Arrow_Stone` |
| 箭支耗材 | 铜箭 | `Arrow_Copper` |
| 箭支耗材 | 铁箭 | `Arrow_Iron` |
| 材料，不计入装备总量 | 黑曜石 | `Obsidian` |
| 材料，不计入装备总量 | 木棍 | `Stick_Wood` |
| 材料，不计入装备总量 | 原木 | `Log` |
| 材料，不计入装备总量 | 火种 | `FireSeed` |
| 材料，不计入装备总量 | 柳枝 | `WillowBranch` |

原木、木棍、柳枝、火种、黑曜石虽然能使用攻击动画，仍按材料单列。`HandDrill` 与 `HandDrill_Summoner` 是建筑及放置物，本轮不归入手持工具。

## 统计时发现的情况

- 藤甲 `Chestplate_Twine` 原来借用 `Jade Chestplate.png`。本轮已重绘为藤编纤维结构并替换图标引用。
- 目前 10 件旅人/铁卫装备有独立穿戴层，另 9 件（基础甲 3、宇航服 5、工程护目镜 1）未配置穿戴层。只更换图标不会自动补齐人物穿戴外观。
- 基础铁甲 `Chestplate_Iron` 与铁卫胸甲 `IronGuard_Cuirass` 是两个独立物品，需要分别设计。
- 人物穿戴层必须单独遵守现有 32×48、32 PPU、底中 Pivot 和公共身体锚点；装备开口露出原人物，不画脸、皮肤或头发。图标使用独立构图，不能直接当人物穿戴层。

## 三张新画风样例

执行规范：`.agents/skills/flatworld-art/SKILL.md`、`references/art-direction.md`、`references/runtime-sprites.md`。使用内置 `image_gen`：先生成三个独立主体，再各做一轮像素简化。完整原始及简化提示词见 [GenerationPrompts.json](GenerationPrompts.json)。

统一方向是低饱和、偏写实的复古像素风：自然结构、大块硬边像素、有限明暗、左上光、独立轮廓透明。此次分别看布料、金属和玻璃三种材质。

| 样例 | 观察重点 | 最终预览文件 |
| --- | --- | --- |
| 旅人兜帽 | 棕色布料、折边、空脸口 | [打开 PNG](Traveler_Hood/Traveler_Hood_StyleSample_20261023.png) |
| 铁卫胸甲 | 灰蓝铁板、皮带、克制高光 | [打开 PNG](IronGuard_Cuirass/IronGuard_Cuirass_StyleSample_20261023.png) |
| 宇航服头盔 | 米白外壳、深青玻璃、密封圈 | [打开 PNG](Spacesuit_Head/Spacesuit_Head_StyleSample_20261023.png) |

三张文件均为实际 1254×1254 的 RGBA 风格预览，保留生成结果的画布与 Alpha。提示词中的 32×32 是低逻辑分辨率目标，这些 PNG 尚不是精确 32×32 的成品图标，也不是对齐人物的 32×48 穿戴层。风格确定后再制作实际消费规格。

第一轮新增预览和统计资料，没有修改物品配置、原图、Addressables 或人物外观。没有进入/退出/暂停 PlayMode，也没有运行玩法验证。按项目规则尝试读取 Unity MCP 时服务返回 HTTP 503，未能读取控制台。第一轮沿用当时的美术规范。

## 手工制作感样例（第二轮，2026-10-23）

依据用户补充的背景：游戏从零开始逐步发展科技。装备的工艺应随着材料、工具与加工能力进步，首先让玩家看得出它是怎样做出来的。

- 旅人兜帽：粗布拼接、少量粗针脚、纤维绳结和略不齐的裁边，保持完整耐用的衣物轮廓。
- 铁卫胸甲：哑光手锻铁片、简朴肩甲、皮绳绑扎与少量铆钉，收敛连续亮边和制式雕塑感。
- 宇航服头盔：保留高阶材料与可靠密封，强调分片金属壳、铆缝、玻璃压框和手动锁扣，呈现早期工坊自制装备的加工水平。

手工感主要通过结构和连接方式表达；粗像素、低饱和、左上光源和透明背景继续保持。避免用密集磨损与噪点制造粗糙感，也不把所有科技阶段的装备都画成原始石器。

| 样例 | 第二轮文件 |
| --- | --- |
| 旅人兜帽 | [打开手工版 PNG](../../../../开发文档文件夹/09_美术素材/参考样张/Equipment/Traveler_Hood/Traveler_Hood_Handcrafted_StyleSample_20261023.png) |
| 铁卫胸甲 | [打开手工版 PNG](../../../../开发文档文件夹/09_美术素材/参考样张/Equipment/IronGuard_Cuirass/IronGuard_Cuirass_Handcrafted_StyleSample_20261023.png) |
| 宇航服头盔 | [打开手工版 PNG](../../../../开发文档文件夹/09_美术素材/参考样张/Equipment/Spacesuit_Head/Spacesuit_Head_Handcrafted_StyleSample_20261023.png) |

第二轮使用内置 `image_gen` 编辑三件样例，其中胸甲另做一次高光与表面简化。完整提示词见 [GenerationPrompts_Handcrafted.json](GenerationPrompts_Handcrafted.json)。上一版样例保留用于对比。

三张仍为 1254×1254 RGBA 风格预览，尚未制作成精确像素规格的成品图标或人物穿戴层；没有修改物品配置、旧图、Addressables 或玩法。相关装备制作工艺已补充到 `.agents/skills/flatworld-art/references/art-direction.md`。本轮未运行玩法验证；读取控制台接口仍返回 HTTP 503。

## 整套装备图标交付（2026-10-23）

用户最终确认本轮只重绘穿戴装备的物品图标，并同时替换游戏内引用。武器、工具和箭支不属于本轮；人物穿戴层沿用现有资源。

- 共 19 张透明 PNG：基础胸甲 3、旅人 5、铁卫 5、宇航服 5、工程护目镜 1。16 张新绘，3 张复用已经认可的手作样例设计。
- 三个同级子智能体按基础甲/旅人、铁卫、宇航服分组，统一使用内置 image_gen。完整原始与修订提示词已汇总到 GenerationPrompts_Production.json。
- 四个物品分包中共替换 19 处 visual.spriteAddress；新图均注册到现有 PlayerAppearance Addressables 组，并使用 ItemSprite 标签。未修改装备属性、玩法、人物底图、穿戴层或工具资源。
- 图标源图保留 1254×1254 RGBA；Unity 导入为中心 Pivot、Point、无 Mipmap、无压缩、maxTextureSize 128、PPU 1254。PPU 按原图和新图的世界比例换算，Sprite 画布最长边继续为 1 世界单位；透明留白与可见主体比例以新设计为准。
- [交互图鉴](EquipmentIcons_20261023.html)可按套装查看全部原图，并切换深色、浅色和透明格底色。

本轮按用户要求未运行玩法验证，也未进入、退出或暂停 Unity PlayMode。游戏内实际观感由用户验收；已尝试读取 Unity 控制台，接口返回 HTTP 503，未取得日志。

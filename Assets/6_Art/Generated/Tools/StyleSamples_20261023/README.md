# 工具清单与新版美术样例

统计日期：2026-10-23。范围来自当前 `Assets/StreamingAssets/GameConfig/Items/item-manifest.json` 启用包，按具体物品定义、继承后的能力和配方用途归类。

本轮只交付风格样例与清单；样例尚未接入游戏。PNG 为生成原图，保留画布和透明通道。现有物品的贴图引用、玩法参数和 Addressables 配置均未修改。

## 首批三例

| 样例 | 文件 | 选择理由 |
| --- | --- | --- |
| 燧石斧 | [Axe_Flint_StyleSample.png](Axe_Flint_StyleSample.png) | 看石材大切面、木柄与绑扎结构 |
| 青铜镐 | [Pickaxe_Bronze_StyleSample.png](Pickaxe_Bronze_StyleSample.png) | 看青铜铸造质感与采矿工具轮廓 |
| 铁铲 | [Shovel_Iron_StyleSample.png](Shovel_Iron_StyleSample.png) | 看锻铁、刃口及长柄工具的辨识度 |

依据 `.agents/skills/flatworld-art/SKILL.md`：低饱和、偏写实的复古像素风，大像素簇、少量色阶、可信结构、左上光源。三件工具采用斜向构图与透明背景。生成方式为内置 `image_gen`；完整提示词见 [Prompts.md](Prompts.md)。

这些是放大展示的美术样例，提示词中的逻辑像素目标不等于已经制作了对应尺寸的正式 Sprite。当前以居中 Pivot 和约一格的预览 PPU 导入；正式采用时仍需按手持消费者确定逻辑像素、握柄 Pivot、PPU、rendererLocalPosition 与方向，不能直接替换后宣称已经完成手持标定。铁铲初稿纹理偏细，已用另外两张作风格参考重新简化；这里只收录最终选用版。

## 成品工具统计

| 分类 | 数量 | 款式概览 |
| --- | ---: | --- |
| 斧 | 6 | 石、燧石、铜、青铜、生铁、熟铁 |
| 镐 | 5 | 石、铜、粗铁、青铜、熟铁 |
| 铲 | 6 | 木、石、铜、粗铁、青铜、熟铁 |
| 锄 | 4 | 木、石、铜、铁 |
| 成品小刀 | 5 | 石、铜、骨、燧石、黑曜石 |
| 锤 | 2 | 木、石 |
| 采集与钓鱼 | 3 | 剪刀、打制石器、钓鱼竿 |
| 生火与照明 | 3 | 打火石、钻木取火工具、火把 |
| 钻孔 | 1 | 手钻 |
| **合计** | **35** | 便携态与放置态不重复计款 |

## 成品工具明细

贴图列以 `Assets/6_Art/` 为根；含方括号的地址表示子 Sprite。定义文件列以 `Assets/StreamingAssets/GameConfig/Items/` 为根。中文名保留当前配置原文。

| 类别 | 中文名 | ID | 当前 spriteAddress | 定义文件 |
| --- | --- | --- | --- | --- |
| 斧 | 石斧 | `Axe_Stone` | `Items/Weapons/Item_Weapon_031.png` | `shells/tools.json` |
| 斧 | 燧石斧 | `Axe_Flint` | `Items/Tools/Item_Tool_366.png` | `shells/tools.json` |
| 斧 | 铜斧 | `Axe_Copper` | `Items/Tools/Item_Tool_376.png` | `shells/tools.json` |
| 斧 | 青铜斧 | `Axe_Bronze` | `Items/Weapons/Item_Weapon_032.png` | `shells/tools.json` |
| 斧 | 生铁斧 | `Axe_RawIron` | `Items/Tools/Item_Tool_371.png` | `shells/tools.json` |
| 斧 | 铁斧 | `Axe_Iron` | `Items/Weapons/Item_Weapon_033.png` | `shells/tools.json` |
| 镐 | 石镐 | `Pickaxe_Stone` | `Items/Tools/Item_Tool_363.png` | `shells/tools.json` |
| 镐 | 铜镐 | `Pickaxe_Copper` | `Items/Tools/Item_Tool_373.png` | `shells/tools.json` |
| 镐 | 粗铁铁镐 | `Pickaxe_RawIron` | `Items/Tools/Item_Tool_368.png` | `shells/tools.json` |
| 镐 | 青铜镐 | `Pickaxe_Bronze` | `Items/Weapons/Item_Weapon_020.png` | `shells/tools.json` |
| 镐 | 熟铁铁镐 | `Pickaxe_Iron` | `Items/Weapons/Item_Weapon_021.png` | `shells/tools.json` |
| 铲 | 木铲 | `Shovel_Wood` | `Generated/InventoryCrafting/WoodShovel/WoodShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 石铲 | `Shovel_Stone` | `Generated/InventoryCrafting/StoneShovel/StoneShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 铜铲 | `Shovel_Copper` | `Generated/InventoryCrafting/CopperShovel/CopperShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 粗铁铲 | `Shovel_RawIron` | `Generated/InventoryCrafting/RawIronShovel/RawIronShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 青铜铲 | `Shovel_Bronze` | `Generated/InventoryCrafting/BronzeShovel/BronzeShovel_Icon.png` | `shells/ancient_stage.json` |
| 铲 | 铁铲 | `Shovel_Iron` | `Generated/InventoryCrafting/IronShovel/IronShovel_Icon.png` | `shells/ancient_stage.json` |
| 锄 | 木锄头 | `Hoe_Wood` | `Items/Tools/Item_Tool_362.png` | `shells/hoes.json` |
| 锄 | 石锄头 | `Hoe_Stone` | `Items/Tools/Item_Tool_367.png` | `shells/hoes.json` |
| 锄 | 铜锄头 | `Hoe_Copper` | `Items/Tools/Item_Tool_377.png` | `shells/hoes.json` |
| 锄 | 铁锄头 | `Hoe_Iron` | `Items/Tools/Item_Tool_372.png` | `shells/hoes.json` |
| 小刀 | 石制小刀 | `Dagger_Stone` | `Items/Tool_Weapon.png[石制小刀]` | `shells/weapons.json` |
| 小刀 | 铜制小刀 | `Dagger_Copper` | `Items/Tool_Weapon.png[Tool_Weapon_19]` | `shells/weapons.json` |
| 小刀 | 骨制小刀 | `Dagger_Bone` | `Items/Weapons/Item_Weapon_006.png` | `shells/weapons.json` |
| 小刀 | 燧石小刀 | `Knife_Flint` | `Items/Weapons/Guns/iron-weapons.png[iron-weapons_248]` | `shells/weapons.json` |
| 小刀 | 黑曜石小刀 | `Dagger_Obsidian` | `Generated/Combat/Dagger_Obsidian/Dagger_Obsidian_Icon.png` | `shells/obsidian.json` |
| 锤 | 木锤 | `WoodHammer` | `Generated/InventoryCrafting/WoodHammer/WoodHammer_Icon.png` | `shells/tools.json` |
| 锤 | 石锤 | `StoneHammer` | `Generated/InventoryCrafting/StoneHammer/StoneHammer_Icon.png` | `shells/tools.json` |
| 采集与钓鱼 | 剪刀 | `Scissors` | `Generated/InventoryCrafting/Scissors/Scissors_Icon.png` | `shells/tools.json` |
| 采集与钓鱼 | 打制石器 | `ChippedTool` | `Items/icons_sheet.png[icons_sheet_98]` | `shells/weapons.json` |
| 采集与钓鱼 | 钓鱼竿 | `FishingRod` | `Items/Tools/Item_Tool_389.png` | `shells/fishing.json` |
| 生火与照明 | 打火石 | `FlintStrike` | `Items/Tools/Flint.png` | `shells/tools.json` |
| 生火与照明 | 钻木取火工具 | `SparkMaker_Summoner` | `Items/ADDSparkMaker.png[ADDSparkMaker_11]` | `shells/building_summoners.json` |
| 生火与照明 | 火把 | `Torch_Summoner` | `Generated/Tools/Torch/Torch_Spent.png[Torch_Spent]` | `shells/building_summoners.json` |
| 钻孔 | 手钻 | `HandDrill_Summoner` | `Generated/Mechanical/Items/HandDrill/HandDrill_Icon.png` | `shells/hand_drill.json` |

## 补充范围

另有 1 款原料兼工具、7 款容器/加工器具、3 款鱼钩配件，共 11 款，和成品工具合计 46 种物品身份；不等于 46 张互不重复的旧图。

| 范围 | 中文名 | ID | 当前 spriteAddress（相对 Assets/6_Art/） |
| --- | --- | --- | --- |
| 原料兼工具 | 黑曜石 | `Obsidian` | `Generated/Environment/Obsidian/Obsidian_Icon.png` |
| 容器与加工器具 | 椰壳 | `Coconut_Shell` | `Food/椰子壳-空.png` |
| 容器与加工器具 | 陶罐 | `ClayJar` | `Generated/WaterVessel/ClayJar/ClayJar_Icon.png` |
| 容器与加工器具 | 铁桶 | `IronBucket` | `Generated/WaterVessel/IronBucket/IronBucket_Icon.png` |
| 容器与加工器具 | 木桶 | `WoodenBarrel` | `Generated/WaterVessel/WoodenBarrel/WoodenBarrel_Icon.png` |
| 容器与加工器具 | 水槽 | `WaterTrough` | `Generated/WaterVessel/WoodenBarrel/WoodenBarrel_Icon.png` |
| 容器与加工器具 | 石臼 | `StoneMortar` | `Generated/InventoryCrafting/StoneMortar/StoneMortar_Icon.png` |
| 容器与加工器具 | 坩埚 | `Crucible` | `Items/crucible.png[crucible]` |
| 鱼钩配件 | 骨质鱼钩 | `FishingHook_Bone` | `Generated/Items/Fishing/FishingHook_Bone.png` |
| 鱼钩配件 | 铁质鱼钩 | `FishingHook_Iron` | `Generated/Items/Fishing/FishingHook_Iron.png` |
| 鱼钩配件 | 金鱼钩 | `FishingHook_Gold` | `Generated/Items/Fishing/FishingHook_Gold.png` |

## 统计边界与后续注意点

- 小刀继承 `Knife_Base` 的资源工具能力，因此列入工具；不能因为定义放在 weapons.json 就排除。剪刀兼有小刀资源能力与剪草能力，打制石器可采草/采花。
- 黑曜石原料带 `Knife` 标签，生存加工配方允许它作为临时小刀；与成品黑曜石小刀分列。
- 木弓、反曲弓、吹箭、投石索虽有 Tool 标签，其用途属于远程战斗；矛、箭也未计入生产工具。木棍、原木、火种按材料/燃料处理。
- 火把、钻木取火工具、手钻及容器的便携/放置定义只算同一款。
- 当前启用清单没有镰刀。资源库中木、金、翡翠、石榴石斧/镐的图片没有相应启用工具定义，未纳入数量；不能仅凭此就宣布这些图片已经废弃。
- 水槽当前共享木桶图，后续整套重绘需要独立设计水槽轮廓。
- “粗铁铁镐”“熟铁铁镐”确为当前配置和中文本地化名称；铁铲的正式名为“铁铲”，描述说明是熟铁锻造。本次没有修改命名。
- 材质顺序不代表所有工具共用一套等级。斧、锤、小刀 harvestTier 均为 1；镐按石/铜/粗铁/青铜/熟铁为 1/2/3/4/4；铲按木/石/铜/粗铁/青铜/熟铁为 1/2/3/3/4/4。锄按木/石/铜/铁每格需要 6/4/3/2 次。采矿说明明确不设等级门槛。

## 来源与交付范围

主要来源为 item-manifest.json 与 `shells/tools.json`、`weapons.json`、`hoes.json`、`ancient_stage.json`、`obsidian.json`、`building_summoners.json`、`hand_drill.json`，并核对 `Assets/StreamingAssets/GameConfig/Recipes/crafting/survival.json` 的 Knife 工具用途。中文显示参考配置与 `Assets/Localization/StringTables/FlatWorld_zh-CN.asset`；新增缺键项由本地化服务回退 gameName。

本轮没有修改游戏系统，也没有执行玩法测试、场景试装或手持标定。美术 Skill 已覆盖这次需求，无需更新其规则。

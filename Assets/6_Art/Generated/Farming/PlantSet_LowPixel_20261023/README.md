# 植物作物低像素重绘套图

日期：2026-10-23。用户认可第二轮粗像素萝卜、水稻、浆果丛后扩展的完整套图，随后明确要求替换游戏内引用；当前已完成引用、加载登记及显示参数接线，保留全部种类。

[打开本地预览总览](Preview.html)：12 类卡片，水稻、棉花、辣椒、狗尾草可切换幼苗／生长／成熟，背景可切换；点击图片打开原图。

## 交付清单

共 **12 类植物、20 张透明 PNG 源图**：18 张作物本体/阶段图、1 张花朵、1 张包含 32 个野草变体的图集。萝卜、水稻成熟图、浆果丛沿用用户已认可的低像素结果，其余补齐同套画风。

| 名称 | 目录/提示词 | 图片数 | 图片 |
| --- | --- | ---: | --- |
| 萝卜 | [RadishCrop](RadishCrop/Generation.md) | 1 | [植株](RadishCrop/RadishCrop_Mature.png) |
| 蘑菇 | [MushroomCrop](MushroomCrop/Generation.md) | 1 | [植株](MushroomCrop/MushroomCrop_Mature.png) |
| 藤蔓 | [TwineCrop](TwineCrop/Generation.md) | 1 | [植株](TwineCrop/TwineCrop_Mature.png) |
| 浆果丛 | [BerryCrop](BerryCrop/Generation.md) | 1 | [植株](BerryCrop/BerryCrop_Mature.png) |
| 水稻 | [RiceCrop](RiceCrop/Generation.md) | 3 | [幼苗](RiceCrop/RiceCrop_Seedling.png) / [生长](RiceCrop/RiceCrop_Growing.png) / [成熟](RiceCrop/RiceCrop_Mature.png) |
| 棉花 | [CottonCrop](CottonCrop/Generation.md) | 3 | [幼苗](CottonCrop/CottonCrop_Seedling.png) / [生长](CottonCrop/CottonCrop_Growing.png) / [成熟](CottonCrop/CottonCrop_Mature.png) |
| 辣椒 | [ChiliTree](ChiliTree/Generation.md) | 3 | [幼苗](ChiliTree/ChiliTree_Seedling.png) / [生长](ChiliTree/ChiliTree_Growing.png) / [成熟](ChiliTree/ChiliTree_Mature.png) |
| 仙人掌 | [Cactus](Cactus/Generation.md) | 1 | [植株](Cactus/Cactus_Mature.png) |
| 药草 | [HerbCrop](HerbCrop/Generation.md) | 1 | [植株](HerbCrop/HerbCrop_Mature.png) |
| 狗尾草 | [FoxtailCrop](FoxtailCrop/Generation.md) | 3 | [幼苗](FoxtailCrop/FoxtailCrop_Seedling.png) / [生长](FoxtailCrop/FoxtailCrop_Growing.png) / [成熟](FoxtailCrop/FoxtailCrop_Mature.png) |
| 花朵 | [Flower](Flower/Generation.md) | 1 | [植株](Flower/Flower_Mature.png) |
| 野草 | [GroundGrass](GroundGrass/Generation.md) | 1 | [32变体图集](GroundGrass/GroundGrass_Atlas.png) |

单图作物沿用当前按缩放表现成长的资源需求，不额外增加未消费阶段。浆果丛本体不烘入果实，果实仍由库存状态的独立显示层负责。辣椒当前 ItemId 为 `ChiliTree`，按作物计入；仙人掌保留其植物身份。树木、种子、收获物与道具图标不在本轮植株清单内。

## 画风与源文件

使用当前 `.agents/skills/flatworld-art/SKILL.md`：低饱和、偏写实、自然比例、明显粗颗粒的大像素块、少量色阶、左上光源与透明轮廓。图片由内置 `image_gen` 逐张生成或编辑，没有 CLI/API 回退。每个目录的 `Generation.md` 记录参考角色、最终提示词、生成原始路径及尺寸/PPU；PNG 保留工具原始画布与 Alpha，不使用程序二次缩放、裁切或重绘。

源文件导出尺寸与逻辑像素密度不同；提示词中的 24～40 逻辑像素为风格目标，不表示原 PNG 是严格对应尺寸的小图。三阶段图使用同一物种成熟母版派生，并统一各自的画布、根点与导入 PPU。

## 游戏内引用与导入

10 种作物及花朵的 `visual.spriteAddress/spriteStates` 已切换到本套图；四类三阶段全部登记。19 张独立 Sprite 在 `Assets/AddressableAssetsData/AssetGroups/PlantSprites.asset` 按完整路径、`ItemSprite` 标签注册，独立分组不依赖 Default 组的临时变更。野草通过 ChunkView Prefab 直接引用新图集。

19 张植株导入设置为 Point、无 Mipmap/压缩、FullRect、最大尺寸 128，保留生成 PNG 原图与源 PPU；Unity 按导入缩放同步换算 PPU，维持原世界画布高度。各物种三阶段共用同一 Pivot，根点按可见底部像素中心校正；萝卜埋土比例与 Pivot 同为 0.36，水稻 Pivot 与原埋土比例同为 0.08，使裁切面落在植株原点；仙人掌保留 Render Y=0.5，并通过 Pivot 补偿底部透明留白。浆果的三个独立果实锚点已落在新图的实心叶团上，不改变生产或库存规则。

| ItemId | 当前统一 PivotY |
| --- | ---: |
| RadishCrop | 0.36 |
| RiceCrop | 0.08 |
| CottonCrop | 0.065789474 |
| ChiliTree | 0.048245614 |
| FoxtailCrop | 0.036283892 |
| MushroomCrop | 0.069073084 |
| TwineCrop | 0.043064024 |
| BerryCrop | 0.094396552 |
| HerbCrop | 0.042118432 |
| Flower | 0.088306452 |
| Cactus | 0.536283892 |

野草新源为 1254×1254 / 6×6 网格，已同步 ChunkView 的 `spriteSizePixels=209`、`pixelsPerUnit=418`，32 个变体与每格 0.5 世界尺寸保持。图集保留 2048 导入上限，不进行缩图，避免切片参数与实际纹理尺寸不一致。

各 `Generation.md` 保留绘图阶段的原始参数与来源，接入后的导入与引用以当前 `.meta`、物品 JSON 和本节为准。种类、成长、水肥、掉落、碰撞与采集规则未改。本轮按用户要求不运行游戏测试，不暂停当前 PlayMode；画面效果由用户验收。

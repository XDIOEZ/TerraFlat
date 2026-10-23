# 植物作物低像素重绘套图

日期：2026-10-23。用户认可第二轮粗像素萝卜、水稻、浆果丛后扩展的完整套图；用户明确只重绘，保留全部种类。

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

## 与游戏的接入边界

本轮只新增整套绘图素材与预览，不更改物品 JSON、原素材、Addressables、Prefab、玩法或种类。每张图和目录均有独立 GUID 的 `.meta`；Point、无 Mipmap、无压缩、Alpha，PPU 按旧图的画布世界高度换算，锚点按物种说明保存。旧配置中的 Render 缩放、裁切和特殊表现仍需在后续切换引用时结合新图验收。

野草新源为 1254×1254 / 6×6 网格（每格 209 像素），与当前 96×96 / 16 像素格不同；接入时要同步配置切片格尺寸与 PPU，见 [野草规格](GroundGrass/Generation.md)。本轮不进行运行验证，游戏内尺度、锚点与观感由用户验收。

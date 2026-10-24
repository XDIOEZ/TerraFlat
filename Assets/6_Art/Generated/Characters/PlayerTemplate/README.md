# 玩家底图、发型与共用装备层

人物的脸和身体属于人物；装备只画装备。穿上半包围头盔或兜帽时隐藏头发，在装备的透明开口中露出人物本来的脸，脱下后恢复所选发型。

## 正式素材

- `Bodies/Base_Male.png`、`Bodies/Base_Female.png`：无头发的人物底图，保留脸、头皮、身体和基础衣裤；肩、腰、四肢使用同一组像素，只有脸可以不同。
- `Hair/Male.png`、`Hair/Female.png`：独立发型，不包含脸、眼睛、耳朵或皮肤。
- `Wearables/<物品id>.png`：皮制、熟铁、钢制护甲各五件，以及藤甲、木甲、工程护目镜和宇航服五件，共 23 张空装备层；只含装备自身，男女共用。钢甲的穿戴文件为 `Steel_Cuirass.png`，物品业务 ID 仍为 `Chestplate_Iron`。
- `Masks/Headgear_Hair.png`：头部装备的头发隐藏遮罩，目前隐藏整个发型；遮罩只作用于头发，不作用于脸。Alpha为255的位置隐藏，0的位置保留，中间值按比例裁剪。
- `Icons/<物品id>.png`：分层构建器从装备层导出的十件旧图标，保留作重建参考。当前物品定义已改用 `Assets/6_Art/Generated/Equipment/<id>/<id>_Icon.png` 中独立重绘的装备图标；完整清单和图鉴见该目录 README。
- `Source/LayeredOutfits.png`：内置imagegen生成并清理过的分层源图，1448×1086；源区与公共锚点标定见`PlayerAppearanceLayerRaster.cs`。
- `Source/LayerPreview.png`：同一采样器导出的静态放大预览；上男下女，依次为底图加发型、旅人套装、铁卫套装、只穿胸甲、只穿靴、无发底图。此图说明像素组合，不代表游戏内验收。
- `Source/SteelArmor/`：钢套五件独立绘制的透明源图、`Generation.json` 标定记录和 [男女穿戴静态预览](Source/SteelArmor/SteelArmor_Preview.png)。预览从左到右为底图、钢套全套、单穿头盔、胸甲、手套、腿甲、靴子。
- `Source/EquipmentCompletion/`：其余八件的源图、绘图提示与采样参数；[藤甲、木甲、护目镜及整套宇航服](Source/EquipmentCompletion/EquipmentCompletion_Preview.png)和[宇航服逐件预览](Source/EquipmentCompletion/Spacesuit_Preview.png)。`Generation.json` 记录本轮启用目录中的 24 件装备覆盖口径：23 件人物穿戴层，木盾由既有副手渲染器单独显示。
- `Source/EquipmentCompletion/Spacesuit_PixelMaster.xcf`：2026-10-24 用 GIMP MCP 原生一像素铅笔重画的宇航服五件分层母版，32×48；逐像素调色板和图层网格见同目录 `Spacesuit_Pixels.json`。正式穿戴 PNG 以这版为准，上述两张旧预览和大图采样记录仅保留作历史参考。

目录根部原`Base_Male.png`、`Base_Female.png`暂留作Player Prefab初始Sprite和画风参考；正式运行组合由Profile指向新的`Bodies/`、`Hair/`。原旅人、铁卫男女四张整身装备图和`Source/ApprovedOutfits.png`保留作历史画风参考。旧整身图不再注册Addressables或用于穿戴。宇航服物品图与五件人物穿戴层分别维护。

## 共同画布与配置契约

人物、头发、穿戴层、遮罩一律**32×48像素、32 PPU、底中心Pivot（16,0）**；开启Read/Write，Point、FullRect、无压缩、无Mipmap、不进SpriteAtlas。各层使用同一朝左的源坐标；由`CharacterAppearanceProfile.SourceFacesLeft`统一朝向，不能逐件反转。分层构建器的旧图标使用32×32画布；当前独立重绘图标的源图和导入规格见 Equipment/README.md，均使用中心Pivot，不承担穿戴位置。

画布坐标标定从左上开始：人物底图约从y=4到45，脚底保留2像素；头发在上部，肩约y=22，腰约y=33，手约y=29..37，鞋约y=40..45。新增素材对齐这些身体锚点，不能把每个部位单独放大铺满人物画布。源母版中的空装备经过一次明确标定归位，运行时直接按全画布Alpha叠加，不再按头、胸、手、腿的矩形分区替换人物。

男角色头发于2026-10-24调整为比最初位置整体下移1个原始像素，采样目标区域为`(5,1,24,16)`；女角色发型保持原位。正式发型和`PlayerAppearanceLayerRaster`的重建标定同步维护。

装备配置位于`GameConfig/Items/shells/character_outfits.json`：

```json
"spriteAddress": "Assets/6_Art/Generated/Equipment/IronGuard_Helmet/IronGuard_Helmet_Icon.png",
"spriteStates": {
  "wearable": "Assets/6_Art/Generated/Characters/PlayerTemplate/Wearables/IronGuard_Helmet.png",
  "wearableHairMask": "Assets/6_Art/Generated/Characters/PlayerTemplate/Masks/Headgear_Hair.png"
}
```

`spriteAddress`负责背包、装备栏与物品展示；`wearable`负责穿戴外观。可选`wearableHairMask`裁剪头发，可选`wearableBodyMask`裁剪身体底图。遮挡要求由装备声明，合成系统统一执行，不按具体头盔名增加分支。身体遮罩不能误覆盖需要露出的脸。所有运行时层和图标注册到`PlayerAppearance` Addressables组，标签`ItemSprite`。

钢套的 `wearable` 分别指向 `Steel_Helmet.png`、`Steel_Cuirass.png`、`Steel_Gauntlets.png`、`Steel_Greaves.png`、`Steel_Boots.png`；头盔复用现有头发遮罩，面部开口透明。四件新增钢套定义在 `steel_equipment.json`，钢甲仍在 `equipment.json`。五张图沿用上述公共画布、源朝向与导入规格。

藤甲、木甲、工程护目镜及宇航服五件使用与业务 ID 同名的穿戴图，引用分别位于 `equipment.json`、`engineering.json` 和 `chemistry_equipment.json`。宇航服头盔复用 `Headgear_Hair.png`，护目镜保留原发型；面罩和镜片开口透明，露出人物本来的脸与眼睛。宇航服上衣包含长袖，各件按相同关节锚点衔接。

## 重建

钢套由 `Source/SteelArmor/Generation.json` 记录独立的源图、部件采样矩形和采样方式。五件统一按目标像素覆盖区域投票取色，并使用同一组六色钢灰/皮革调色板，避免高分辨率源图中的细碎高光缩成棋盘亮斑。胸甲、腿甲保留连续钢面，手套和靴子覆盖完整手脚轮廓。输出均为 32×48、Alpha 0/255，手套和靴子分左右部件标定。原 `LayeredOutfits` 构建器仍负责其原有十件装备，钢套素材独立维护。

藤甲、木甲和工程护目镜沿用区域投票取色，源图矩形、目标锚点和 Alpha 覆盖阈值见 `Source/EquipmentCompletion/Generation.json`，不由旧母版构建器覆盖。

宇航服保留用户已认可的 GIMP 重画版本，不统一追加黑框；分层母版为 `Source/EquipmentCompletion/Spacesuit_PixelMaster.xcf`，像素记录为同目录 `Spacesuit_Pixels.json`。其他 18 张穿戴图恢复到本轮全图重画之前的原版，保留原有造型、纹理、调色板、高光和明暗层次。在此基础上沿原色阶逐像素修顺局部暗点和细小色块，补齐藤甲 `(23,28)` 的透明漏点；不重铺整图颜色。

`Bodies/` 的两张正式底图于2026-10-23经GIMP逐像素精修，统一调色板并整理五官和衣料明暗，保留原32×48画布、姿势及透明轮廓。当前分层母版未包含这次手工精修；执行下述重建会覆盖精修底图，重建前需备份或先将精修同步回母版。

编辑模式下使用菜单`FlatWorld/其他工具/角色换装/从分层母版重建人物与装备`，或调用`PlayerAppearanceAssetBuilder.Build(sourceAtlas)`。构建器读取正式源母版和确定的标定参数，最近邻采样并二值化Alpha，重建2张底图、2张发型、10张共用装备、1张头发遮罩、10张物品图标，更新Profile、Player Prefab和Addressables。构建器在PlayMode拒绝执行，不会停止或暂停游戏。

当前提交已包含全部正式 PNG、meta、Profile 和目录引用，不需要手动运行构建器才能获得素材。旧母版使用 `PlayerAppearanceLayerRaster` 导出，钢套和其余八件按各自 `Generation.json` 标定独立导出；一次性导出程序不纳入项目。新增不同布局的源母版时先标定素材区域和目标锚点，再修改采样器；不能仅因图片尺寸相同就认定可直接替换。旧母版生成提示和清理记录见 `Source/Generation.md`。

## 人工体验入口

默认人物为女生。进入世界后按F2，在创造背包搜索“旅人”或“铁卫”；按H打开装备面板，可以逐件穿脱、混搭。头部装备应隐藏头发并保留原脸；其他装备不应改变脸、肤色或发型。运行时切换性别使用玩家`Mod_CharacterAppearance`的“应用人物外观”，两种人物应共用相同装备，不切换到另一张带人物的装备整身图。实际游戏内观感由用户自行验收。

## 局部像素修补

`Source/EquipmentCompletion/Wearable_Retouches.json` 记录原版 Git 版本与明确的坐标修补，不再使用全图重画或统一八色限色的源稿。各个 `*_Retouch.xcf` 保留完整原图层和单独的修补层；修补之外的像素保持原样。修补入口 `Tools/Art/PlayerWearablesGimp.py` 在 GIMP MCP 环境执行，只对清单中的补点落笔。

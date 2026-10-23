# 玩家底图、发型与共用装备层

人物的脸和身体属于人物；装备只画装备。穿上半包围头盔或兜帽时隐藏头发，在装备的透明开口中露出人物本来的脸，脱下后恢复所选发型。

## 正式素材

- `Bodies/Base_Male.png`、`Bodies/Base_Female.png`：无头发的人物底图，保留脸、头皮、身体和基础衣裤；肩、腰、四肢使用同一组像素，只有脸可以不同。
- `Hair/Male.png`、`Hair/Female.png`：独立发型，不包含脸、眼睛、耳朵或皮肤。
- `Wearables/<物品id>.png`：旅人、铁卫各五件空装备，只含装备自身；男女共用同一件穿戴图，不再提供男女装备图。
- `Masks/Headgear_Hair.png`：头部装备的头发隐藏遮罩，目前隐藏整个发型；遮罩只作用于头发，不作用于脸。Alpha为255的位置隐藏，0的位置保留，中间值按比例裁剪。
- `Icons/<物品id>.png`：分层构建器从装备层导出的十件旧图标，保留作重建参考。当前物品定义已改用 `Assets/6_Art/Generated/Equipment/<id>/<id>_Icon.png` 中独立重绘的装备图标；完整清单和图鉴见该目录 README。
- `Source/LayeredOutfits.png`：内置imagegen生成并清理过的分层源图，1448×1086；源区与公共锚点标定见`PlayerAppearanceLayerRaster.cs`。
- `Source/LayerPreview.png`：同一采样器导出的静态放大预览；上男下女，依次为底图加发型、旅人套装、铁卫套装、只穿胸甲、只穿靴、无发底图。此图说明像素组合，不代表游戏内验收。

目录根部原`Base_Male.png`、`Base_Female.png`暂留作Player Prefab初始Sprite和画风参考；正式运行组合由Profile指向新的`Bodies/`、`Hair/`。原旅人、铁卫男女四张整身装备图和`Source/ApprovedOutfits.png`保留作历史画风参考。旧整身图不再注册Addressables或用于穿戴。宇航服已有物品图没有重画，本次没有增加宇航服穿戴层。

## 共同画布与配置契约

人物、头发、穿戴层、遮罩一律**32×48像素、32 PPU、底中心Pivot（16,0）**；开启Read/Write，Point、FullRect、无压缩、无Mipmap、不进SpriteAtlas。各层使用同一朝左的源坐标；由`CharacterAppearanceProfile.SourceFacesLeft`统一朝向，不能逐件反转。分层构建器的旧图标使用32×32画布；当前独立重绘图标的源图和导入规格见 Equipment/README.md，均使用中心Pivot，不承担穿戴位置。

画布坐标标定从左上开始：人物底图约从y=4到45，脚底保留2像素；头发在上部，肩约y=22，腰约y=33，手约y=29..37，鞋约y=40..45。新增素材对齐这些身体锚点，不能把每个部位单独放大铺满人物画布。源母版中的空装备经过一次明确标定归位，运行时直接按全画布Alpha叠加，不再按头、胸、手、腿的矩形分区替换人物。

装备配置位于`GameConfig/Items/shells/character_outfits.json`：

```json
"spriteAddress": "Assets/6_Art/Generated/Equipment/IronGuard_Helmet/IronGuard_Helmet_Icon.png",
"spriteStates": {
  "wearable": "Assets/6_Art/Generated/Characters/PlayerTemplate/Wearables/IronGuard_Helmet.png",
  "wearableHairMask": "Assets/6_Art/Generated/Characters/PlayerTemplate/Masks/Headgear_Hair.png"
}
```

`spriteAddress`负责背包、装备栏与物品展示；`wearable`负责穿戴外观。可选`wearableHairMask`裁剪头发，可选`wearableBodyMask`裁剪身体底图。遮挡要求由装备声明，合成系统统一执行，不按具体头盔名增加分支。身体遮罩不能误覆盖需要露出的脸。所有运行时层和图标注册到`PlayerAppearance` Addressables组，标签`ItemSprite`。

## 重建

`Bodies/` 的两张正式底图于2026-10-23经GIMP逐像素精修，统一调色板并整理五官和衣料明暗，保留原32×48画布、姿势及透明轮廓。当前分层母版未包含这次手工精修；执行下述重建会覆盖精修底图，重建前需备份或先将精修同步回母版。

编辑模式下使用菜单`FlatWorld/其他工具/角色换装/从分层母版重建人物与装备`，或调用`PlayerAppearanceAssetBuilder.Build(sourceAtlas)`。构建器读取正式源母版和确定的标定参数，最近邻采样并二值化Alpha，重建2张底图、2张发型、10张共用装备、1张头发遮罩、10张物品图标，更新Profile、Player Prefab和Addressables。构建器在PlayMode拒绝执行，不会停止或暂停游戏。

当前提交已包含全部正式PNG、meta、Profile和目录引用，不需要手动运行构建器才能获得素材。本轮通过在Unity外执行同一个`PlayerAppearanceLayerRaster`采样器静态导出，未调用Unity导入、刷新或游戏控制；一次性导出程序不纳入项目。新增不同布局的源母版时先标定素材区域和目标锚点，再修改采样器；不能仅因图片尺寸相同就认定可直接替换。生成提示和清理记录见`Source/Generation.md`。

## 人工体验入口

默认人物为女生。进入世界后按F2，在创造背包搜索“旅人”或“铁卫”；按H打开装备面板，可以逐件穿脱、混搭。头部装备应隐藏头发并保留原脸；其他装备不应改变脸、肤色或发型。运行时切换性别使用玩家`Mod_CharacterAppearance`的“应用人物外观”，两种人物应共用相同装备，不切换到另一张带人物的装备整身图。实际游戏内观感由用户自行验收。

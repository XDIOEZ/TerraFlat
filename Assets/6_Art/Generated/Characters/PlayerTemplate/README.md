# 玩家母角色与装备外观

本目录是当前玩家低像素换装的正式美术参考。新人物、服装和护甲沿用这里的比例、像素密度、视角、脚底基线与灰阶，不再参考早期大头候选图。

## 素材

- `Base_Male.png`、`Base_Female.png`：男女母角色。
- `Traveler_Male.png`、`Traveler_Female.png`：旅人兜帽皮甲套装。
- `IronGuard_Male.png`、`IronGuard_Female.png`：铁卫金属护甲套装。
- `Icons/`：两套装备的十件库存图标。
- `Source/ApprovedOutfits.png`：内置 imagegen 按本轮选定男女预览制作的透明源母版。三列依次为基础、旅人、铁卫，上男下女。

正式人物贴图统一为32×48像素、32 PPU、Point、无压缩、无Mipmap、FullRect、底部中心Pivot。主体约42像素高，脚底保留2像素。人物源图需要开启Read/Write，不打入SpriteAtlas；部位像素会在穿脱时合成到同一张运行时贴图，继续复用原来的材质、排序、翻面和整体摇摆。

## 穿戴与手动体验

默认玩家为女生。进入世界后按F2，在创造背包搜索“旅人”或“铁卫”；按H打开装备面板，将对应物品放入头部、外衬/护甲、手部、腿部、鞋子五槽。可以逐件穿脱、混搭，移除后恢复母角色对应部位。

如需看男生，可在运行中的玩家`Mod_CharacterAppearance`组件上使用“应用人物外观”，选择Male；体型会随角色模块保存。普通Inspector字段只表示配置，运行时切换使用该方法。

装备定义位于`GameConfig/Items/shells/character_outfits.json`，通过已有`visual.spriteStates.wearableMale/wearableFemale`引用整身源图。每件装备只替换其实际槽位对应的像素区域，不接管防御、保温或其他装备效果。

部位分界由`Assets/Resources/Characters/PlayerAppearance_Default.asset`统一管理，坐标从左下角开始；当前头部为y≥25，鞋为y<10，腿为其余y<17，手为13≤y<19且x<10或x≥22，其余为胸衣。后续新增装备应对齐同一模板，而不是逐件加入特例。

## 重建与边界

使用菜单`FlatWorld/其他工具/角色换装/从透明母版导入角色与装备`选择项目内源母版，或调用`PlayerAppearanceAssetBuilder.Build(sourceAtlas)`。构建器只在编辑模式执行，写入六张正式源图、十张图标、Profile、玩家Prefab及独立PlayerAppearance Addressables组，不停止游戏。

源图生成提示要点：保持已选男女角色身份、低像素块与克制的小脸；透明三列两行等高人物；基础灰衣棕裤、棕色旅人皮甲、灰蓝铁卫护甲；统一站姿、肩腰手腕膝踝位置，无武器、投影、背景、文字或额外细碎装饰。源母版由导入器按Alpha取人物边界、最近邻归一化并二值化Alpha，不能把预览图直接当正式贴图。

本轮范围为本机/Host正式装备槽的外观组合；网络远端的装备快照表现尚未接入。未自动发放装备、改写玩家存档或添加配方，玩法观感由用户手动体验。

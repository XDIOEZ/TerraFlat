# 待补充 Item 贴图素材统计

## 文档用途

用于统计当前本体 Item 中仍在使用通用占位图，或暂时直接继承其他物品贴图的内容，供后续 AI 美术任务逐项补齐正式 Sprite。

状态说明：

- **已确认**：当前配置明确使用项目统一 `素材占位符`，需要制作正式贴图。
- **建议稿**：当前没有独立 `spriteAddress`，运行时继承父物品贴图；功能不缺图，但建议后续补成独立美术。
- **待实现**：正式 PNG、Unity Sprite 导入、Addressables 注册以及 Item JSON 引用替换尚未执行。
- **待讨论**：建议稿中的继承贴图 Item 是否全部需要独立美术，可按美术优先级决定。

## 一、已确认需要正式贴图：4 个

| 优先级 | Item ID | 显示名 | 当前状态 | 建议素材目录 / 文件名 | 美术要点 |
| --- | --- | --- | --- | --- | --- |
| P0 | `Scissors` | 剪刀 | 明确使用统一素材占位符 | `Generated/Scissors/Scissors_Icon.png` | 工具类；主体应明确表现剪刀轮廓，透明背景，与现有手持工具像素风统一。若作为手持物显示，接入前核对握持 Pivot。 |
| P0 | `Seed_Foxtail` | 狗尾草籽 | 明确使用统一素材占位符 | `Generated/FoxtailSeed/FoxtailSeed_Icon.png` | 种子类；需要和狗尾草穗明显区分，表现为少量细小草籽，不要画成完整草穗。 |
| P0 | `Flour` | 面粉 | 明确使用统一素材占位符 | `Generated/Flour/Flour_Icon.png` | 食材类；表现面粉本体，避免做成已经烹饪完成的面包等产物。 |
| P0 | `Honey` | 蜂蜜 | 明确使用统一素材占位符，当前额外乘金黄色 | `Generated/Honey/Honey_Icon.png` | 食材/液体原料；表现蜂蜜本体而非水容器。正式成图后应重新检查现有 `visual.color`，通常独立成图应改回白色乘色。 |

当前统一占位引用：

`Assets/6_Art/Generated/ItemPlaceholder/素材占位符.png[素材占位符]`

## 二、建议补独立贴图：10 个

以下 Item 当前并非“加载不到贴图”，而是没有自己的 `visual.spriteAddress`，因此直接继承父物品的视觉。它们属于**美术复用/临时替代候选**，与上面的明确占位图分开处理。

| 建议优先级 | Item ID | 显示名 | 当前继承视觉 | 建议处理 |
| --- | --- | --- | --- | --- |
| P0 | `Shovel_Stone` | 石铲 | 继承 `Pickaxe_Stone`，当前实际使用石镐贴图 | 制作独立石铲 Sprite；工具轮廓应能直接区分“铲”和“镐”，并核对手持旋转/Pivot。 |
| P0 | `MinersNote` | 矿工笔记 | 继承 `Rope`，当前实际使用绳子贴图 | 制作独立笔记/纸张 Sprite；这是语义差异最大的继承项之一。 |
| P1 | `StoneHammer` | 石锤 | 继承 `WoodHammer`，并使用灰色乘色 | 制作真正的石质锤头版本；完成后移除或重新评估灰色乘色。 |
| P1 | `Seed_Radish` | 萝卜种子 | 继承 `Seed_Apple` | 建议制作萝卜种子独立 Sprite。 |
| P1 | `Seed_Mushroom` | 蘑菇孢子 | 经 `Seed_Radish` 继续继承 `Seed_Apple` | 建议制作孢子独立 Sprite，外形不要继续与普通种子完全相同。 |
| P1 | `Seed_Berry` | 浆果种子 | 经 `Seed_Radish` 继续继承 `Seed_Apple` | 建议制作浆果种子独立 Sprite。 |
| P1 | `Seed_Herb` | 药草种子 | 继承 `Seed_Radish`，最终沿用苹果种子视觉 | 建议制作药草种子独立 Sprite。 |
| P1 | `Seed_Willow` | 柳树种子 | 继承 `Seed_Radish`，最终沿用苹果种子视觉 | 建议制作柳树种子独立 Sprite。 |
| P1 | `WillowBranch` | 柳枝 | 继承 `Stick_Wood`，当前沿用普通木棍贴图 | 建议制作更细、更柔韧、带柳枝特征的独立 Sprite。 |
| P1 | `Tree_Willow` | 柳树 | 继承 `AppleTree`，当前以绿色乘色区分 | 建议制作独立柳树 Sprite；保持现有树木世界尺度、底部 Pivot 和俯视表现，不再依赖苹果树染色。 |

## 三、后续 AI 美术任务建议顺序

1. 先完成 4 个**已确认占位图**：`Scissors`、`Seed_Foxtail`、`Flour`、`Honey`。
2. 再完成视觉语义明显不匹配的继承项：`Shovel_Stone`、`MinersNote`。
3. 再处理工具/植物的大型差异项：`StoneHammer`、`WillowBranch`、`Tree_Willow`。
4. 最后批量制作种子系列：`Seed_Radish`、`Seed_Mushroom`、`Seed_Berry`、`Seed_Herb`、`Seed_Willow`，统一种子类画风与世界尺度，同时保证轮廓可区分。

## 四、正式素材接入要求

- 运行时物品素材优先参考项目内同类别 Item 的视角、轮廓、像素簇、描边、配色和世界尺度，不强制固定 16×16。
- 普通物品使用透明 PNG、硬边缘像素风、Point Filter、无 Mipmap、无纹理压缩；图标通常中心 Pivot，手持工具必须按实际握柄核对 Pivot。
- 正式素材建议保存到 `Assets/6_Art/Generated/<ItemName>/`，不要覆盖统一占位图。
- 完成 PNG 后，为具体 Item 更新 `visual.spriteAddress`；如果正式 Sprite 不再依赖染色，同时检查并清理继承或现有的 `visual.color`。
- Sprite 注册到现有 Item Sprite Addressables 链路后，再运行 `FlatWorld/内容配置/校验全部本体内容`。
- 替换贴图时不要修改 Item ID、配方、玩法模块或数值；本任务只处理美术表现及其资源引用。

## 五、统计结果

- **已确认占位图：4 个**
- **建议独立美术的继承贴图 Item：10 个**
- **本清单合计候选：14 个**
- **当前已实现正式素材：0 / 14（本文件只做统计，不创建图片）**

## 六、待实现 / 待讨论

### 待实现

- 为已确认的 4 个占位 Item 制作正式运行时 Sprite，并替换 Item JSON 引用。
- 按确认后的优先级，为继承贴图 Item 制作独立 Sprite。
- 每批接入后执行像素素材静态检查、Addressables 检查和本体内容校验。

### 待讨论

- `StoneHammer`、种子系列、`WillowBranch`、`Tree_Willow` 当前通过父物品视觉或染色可以正常显示；是否全部升级为独立美术，由后续美术范围决定。

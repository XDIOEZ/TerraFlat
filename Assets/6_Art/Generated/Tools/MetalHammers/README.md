# 金属手锤

- `CopperHammer_Icon.png`、`IronHammer_Icon.png`、`SteelHammer_Icon.png` 同时用于图标与手持显示；右向基线沿用现有锤子的左下握柄、右上锤头。
- 成品为 32×32、20 个不透明色加透明背景；Alpha 仅 0/255。Point、无 Mipmap、无压缩，38.571558 PPU 与握柄 Pivot 沿用 `PrimitiveSet_20261023/StoneHammer`，JSON 保持 315° 方向补偿。
- `Source/*_Master.png` 是内置 ImageGen 输出的透明母版；GIMP 以最近邻缩到 32×32，再用无抖动 20 色索引化收色，未使用代码生成或重新上色。
- 生成参考：原石锤形状和 `StyleSamples_20261023/Shovel_Iron_StyleSample.png` 的像素颗粒；提示词要求单把金属锻锤、铜/熟铁/钢对应的哑光金属头、木柄、承担固定作用的皮条绑扎、左上光、粗像素簇、低饱和、透明背景，无文字与现代量产外观。
- 对应物品和配方在 `GameConfig/Items/shells/metal_hammers.json` 与 `GameConfig/Recipes/crafting/metal_hammers.json`；三把均保留通用 `Metal` 标签和 `ResourceToolKind.Hammer` 能力。

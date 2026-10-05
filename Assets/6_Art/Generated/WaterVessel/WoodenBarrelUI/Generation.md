# 木桶贴图

- 用途：木桶库存/放置物图标，以及水容器界面的木桶剖面和内腔水位遮罩。
- 风格参考：`../ClayJar/ClayJar_Icon.png`、`../ClayJarUI/ClayJar_Cutaway.png`、`../ClayJarUI/ClayJar_Interior.png`；沿用暖色像素簇、硬描边、透明留白和 128×128 剖面规格。
- `WoodenBarrel_Icon.png`：64×64，主体边界 x=4..59、y=4..59，8 个可见颜色，Alpha 仅 0/255；Point、无 Mipmap、无压缩、PPU 64。
- `WoodenBarrel_Cutaway.png`：128×128，主体边界 x=4..124、y=5..121，16 个可见颜色，Alpha 仅 0/255；Point、无 Mipmap、无压缩、PPU 128。
- `WoodenBarrel_Interior.png`：同画布白色硬透明内腔遮罩，供水层裁剪；在 `UI_WaterVessel` 的 `WoodenBarrel` 外观项中绑定。
- 图标使用内置 ImageGen 新绘；剖面使用内置 ImageGen 新绘后按最近邻缩至项目逻辑尺寸，并量化为固定木色调色板。内腔遮罩从正式剖面内侧暗腔提取。

## 提示词摘要

1. 图标：一只正立、略俯视的矮木桶，竖向橡木板条、两道深棕箍带、可见空桶口；匹配项目陶罐图标的轮廓重量、像素簇与有限暖色调色板，64×64，透明背景。
2. 剖面：水容器 UI 用的空木桶正面剖面；保留后侧弧形桶口、左右厚切壁与底部，移除前壁和前沿，让内腔从桶口连续露出；匹配陶罐剖面，128×128，透明背景，不含水。

# 浆果丛重绘素材

采用用户已认可的低像素样例，原样复制 `../../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png` 到本目录，不重复生成已认可画面。

- 成熟图：`BerryCrop_Mature.png`，1355×1160，透明 PNG 原图。
- 导入：Point、无 Mipmap、无压缩，PPU=1060.5714285714287，Pivot=(0.5,0)，无自动物理轮廓。
- PPU 沿用旧素材的画布世界高度折算；后续接入需结合原 Render 缩放与锚点验收。
- 只画植株本体，果实继续由库存驱动的独立层显示。
- 当前游戏引用保持原正式资源；本轮只重绘与归档素材。

## 最终提示词与生成方式

使用内置 `image_gen`、`transparent_background=true`。本图完整编辑提示词及旧目标图见 [已认可低像素样例记录](../../PlantStyleSamplesV2/LowPixelSamples.md) 的浆果丛条目。

# 仙人掌粗像素重绘

使用内置 `image_gen`，以旧 `Assets/6_Art/Generated/Farming/Cactus/Cactus_Idle.png` 保留直立主干及左右不等高上弯侧臂的物种轮廓，以已认可的 `../../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png` 统一大像素、低饱和及左上光。

本轮仅提供成熟植株本体新图，不修改运行时引用、成长、接触伤害、收获规则或种类，也不扩展仙人掌块图标。当前仙人掌由同一张图配合连续缩放表现成长，因此没有新增未被消费的阶段图。

## 图片与规格

- 文件：`Cactus_Mature.png`
- 原始尺寸：1254×1254；透明背景开启，原始 PNG 画布及 Alpha 原样复制，无程序缩放、裁切、重绘或量化。
- PPU：1254，一张画布对应一世界单位；与旧图源尺寸/PPU 的换算一致。
- Pivot：(0.5, 0.5)，保留旧图中心锚点意图；旧物品的 rendererLocalPosition.y=0.5、rendererLocalScale=(0.8,1,1) 未修改。
- 导入：Point、关闭 Mipmap、无压缩、Alpha Transparency、Max Texture Size 2048，不另生成碰撞轮廓。
- 旧图 Max Texture Size 为 64，新图保持生成源文件及粗颗粒外观供审阅；本轮不接入。提示词的约 28×32 逻辑网格为风格目标，并非严格网格实测结论。

源文件：`C:/Users/CatStudio/.codex/generated_images/01a1685b-6ed5-7d40-a5c4-a95e8c9debf1/exec-1f156bc8-88cb-42af-96e7-6495ce3af31d.png`

## 最终提示词

身份输入：`Assets/6_Art/Generated/Farming/Cactus/Cactus_Idle.png`。画风输入：`../../../../../../开发文档文件夹/09_美术素材/参考样张/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png`。

```text
Use case: style-transfer.
Asset type: one isolated mature cactus world sprite for FlatWorld, a 2D elevated top-down sandbox game.
Input image 1 is the EDIT TARGET for identity and silhouette: existing Cactus_Idle.png. Preserve its upright ribbed main stem with two asymmetric upward-bent side arms, left arm higher than the right. Input image 2 is the approved coarse-pixel art reference: BerryCrop_Mature_LowPixelSample.png. Use image 2 only for coarse square pixel clusters, muted matte shading and upper-left light, never for its shrub shape.
Primary request: redraw the same cactus in the approved MUCH LOWER pixel density. The ENTIRE cactus silhouette should look hand-pixelled on about a 28x32 logical grid, enlarged with perfect nearest-neighbor large uniform squares. Use only 8–12 restrained colors, broad hard-edged olive/sage-green rib planes, two main flat values plus one sparse highlight, no intricate texture.
Subject: ONE recognizable naturally proportioned small saguaro-like cactus with one stout upright central ribbed column, round stepped cap, and exactly two smaller bent-up side arms at different heights. Left arm cap below the central cap, right arm lower. Central stem visibly joins one compact flat stem base. A handful of short ivory-beige spine pixels on outer edges and a few on the ribs; these spines must be simple large pixel marks, not a dense thorn texture. Do not draw a round barrel cactus, prickly-pear pads, multiple cactus clumps, flowers or fruit.
Composition: square transparent canvas, complete cactus centered with open transparent margins. Main stem base at x50%, y90% measured from the top, central cap around y8%; full silhouette occupies about 70% canvas width and 82% canvas height. The image is one world sprite, not an inventory food icon. Keep natural mild asymmetry, upper-left light.
Constraints: actual transparent alpha. No pot, ground, soil, rock, horizon, cast shadow, label, text, border, grid or checkerboard. No pure-black thick outline, smooth edge, antialiasing, gradient, grain, noise, hairline detail, glossy 3D or high-resolution decorative texture.
```

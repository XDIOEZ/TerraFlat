# MushroomCrop 粗像素成熟植株重绘

- 日期：2026-10-23
- 方式：内置 `image_gen`，每个物种独立生成。
- 本次仅重绘：保留原有种类；本目录为独立的新素材，不替换旧图、不接入玩法或配置。
- 原生 PNG 与 Alpha 原样保存；没有程序缩放、裁切或改色。
- 风格参考：`Assets/6_Art/Generated/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png` 与 `RadishCrop_Mature_LowPixelSample.png`。
- 物种身份参考：`Assets/6_Art/Generated/Farming/Mushroom/Plant_Mushroom.png`。
- 工具原始输出：`C:/Users/CatStudio/.codex/generated_images/01a1685c-b50a-7e82-a8e3-2f3414b6364a/exec-0008d7f1-337a-4a7b-807f-c4bd474768ad.png`。
- 项目交付：`Assets/6_Art/Generated/Farming/PlantSet_LowPixel_20261023/MushroomCrop/MushroomCrop_Mature.png`。
- Sprite 导入：Point、无 Mipmap、所有列出的平台不压缩、Alpha Transparency、底中 Pivot `(0.5, 0)`、不自动生成物理形状。
- PPU 按原图画布世界高度等比换算，详见下方尺寸；只保存适用于后续接入的导入元数据，未改现有消费方。

## 最终提示词

```text
Use case: stylized-concept.
Asset type: a SINGLE mature plant world sprite for FlatWorld, a top-down 2D sandbox game, isolated with real transparent alpha.
Input image roles: Image 1 and Image 2 are APPROVED STYLE REFERENCES ONLY: match their visibly LARGE COARSE square pixels, sparse broad flat shading, muted olive foliage, naturally believable proportions and upper-left light. They do not specify this plant's shape. Image 3 is ONLY the existing species/identity and silhouette reference. Do NOT copy its old fine texture or old pixel density.
Style: explicitly very low logical resolution, approximately 24 to 40 visibly square logical pixels across the entire subject's larger dimension, uniformly enlarged by nearest-neighbor. Oversized hard-edged pixel clusters; only 2 or 3 flat tones per material, 12 to 18 muted colors overall. Every leaf, cap, stem and flower should be readable as a few LARGE pixel steps. Use the same coarse visual pixel scale as the approved radish. Never add mini-pixels, speckled highlights, smooth gradients, anti-aliasing, microtexture, painterly blur or ornamental detail.
Composition: one complete centered plant sprite, sparse transparent margins, all tips intact, root/base on a common near-bottom baseline at about 94% image height, mild game-view angle matching references. Keep the full shape including small roots if shown. No pot, no planter, no soil patch, no terrain, no cast shadow, no furniture, no additional separate items, no collage, no grid, no labels, no text, no logo, no watermark. Background genuinely transparent, including holes between leaves/stems.
Lighting: gentle UPPER LEFT light made of a few matte broad pixel shapes, no glow, no glossy 3D.
Subject: MushroomCrop mature edible mushroom CLUSTER, preserving the existing tiny warm golden ochre / brown cap identity and pale cream stems. Three overlapping mushrooms growing from the same small base: one medium-high central mushroom with a gently rounded uneven ochre cap, a lower one leaning left, a short small one to the right; natural cap/stem proportions with visible undersides as one darker band. Simplify into very bold coarse pixel masses. Tiny cap edge notch is sufficient for species texture; no dots, no white wart spots, no red caps, no poisonous fly-agaric look, no faces, no cartoon bulbous exaggeration, no green foliage. The cluster is compact, low to ground, wider than tall, unmistakably fungal and food-like.
```

## 尺寸与 PPU

- 原图：16 × 16 px，16 PPU，画布世界高度 1。
- 新图：1402 × 1122 px，1122 PPU；画布世界高度保持 1。
- 画布世界宽度：1.24955436720143。
- Alpha 的留白占比会影响可见植株大小；仅完成图片重绘，未进行 Unity 内场景验证。

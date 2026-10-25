# TwineCrop 粗像素成熟植株重绘

- 日期：2026-10-23
- 方式：内置 `image_gen`，每个物种独立生成。
- 本次仅重绘：保留原有种类；本目录为独立的新素材，不替换旧图、不接入玩法或配置。
- 原生 PNG 与 Alpha 原样保存；没有程序缩放、裁切或改色。
- 风格参考：`Assets/6_Art/Generated/Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png` 与 `RadishCrop_Mature_LowPixelSample.png`。
- 物种身份参考：`Assets/6_Art/Generated/Environment/TwineCrop/TwineCrop_Idle.png`。
- 工具原始输出：`C:/Users/CatStudio/.codex/generated_images/01a1685c-b50a-7e82-a8e3-2f3414b6364a/exec-510e8c77-a396-4615-ab36-2b4a33682a81.png`。
- 项目交付：`Assets/6_Art/Generated/Farming/PlantSet_LowPixel_20261023/TwineCrop/TwineCrop_Mature.png`。
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
Subject: TwineCrop / wild vine, preserving an airy vine identity rather than becoming a bush. One rooted clump of two gently curving and crossing flexible olive-green climbing stems, one taller curling up then left, one shorter arcing outward to the right, several simple alternate heart-shaped pointed broad leaves and two coarse hooked tendrils. Open spaces between stems and leaves must be transparent. Slightly asymmetric tall airy silhouette. No support stick, no trellis, no berries, no flowers, no fruit, no bark trunk, no harvested rope. The original fine small leaves must be simplified and enlarged into fewer broad coarse pixel leaves. Match the muted olive color family of Image 1 and Image 2, not the old reference's neon green.
```

## 尺寸与 PPU

- 原图：64 × 64 px，48 PPU，画布世界高度 1.33333333333333。
- 新图：1199 × 1312 px，984 PPU；画布世界高度保持 1.33333333333333。
- 画布世界宽度：1.21849593495935。
- Alpha 的留白占比会影响可见植株大小；仅完成图片重绘，未进行 Unity 内场景验证。

## 2026-10-25 导入修复

- 原图仍为 1199 × 1312 px；此前导入上限 128 导致 Unity 实际缩成 117 × 128 px。
- 主设置、默认平台、Standalone 和 Android 的 `maxTextureSize` 均改为 2048，保留原图分辨率，不重采样、不限色。
- 保留 984 PPU、现有 Pivot、Point、无 Mipmap 和无压缩设置，植株游戏内尺寸不变；游戏内观感由用户验收。

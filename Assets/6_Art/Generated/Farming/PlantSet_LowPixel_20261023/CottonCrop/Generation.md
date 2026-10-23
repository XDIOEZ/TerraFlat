# 棉花低像素三阶段重绘

日期：2026-10-23。

按用户认可的水稻、浆果丛低像素样例绘制；只重绘，保留物种与玩法。使用内置 `image_gen`，每张独立调用并开启 `transparent_background=true`。先绘成熟母版，再分别编辑为幼苗、生长期；生成原图直接复制，保留原始 Alpha，未用程序绘制、缩放、裁切或处理 PNG。

## 文件与导入

- 三张均为 **1254×1254** 原始透明 PNG；统一像素颗粒、低饱和大色块、左上光照；幼苗保留大量透明上方留白，不放大填满画布。
- 旧图尺寸：64×64 / 64 PPU = 1×1 世界单位。
- 新图三阶段统一 **1254 PPU**，以新画布高度 / 旧世界画布高度换算；新画布仍为 1×1 世界单位。
- Sprite Single、Point、无 Mipmap、无压缩、Alpha Transparency、底中 Pivot `(0.5, 0)`，不生成物理轮廓；每张图和目录使用新 GUID。
- 幼苗为短茎、两片子叶与初生真叶；生长期有分枝与掌状叶而无棉铃；成熟期有 3 簇乳白棉铃。
- 本目录仅新增素材；未改原图、JSON 引用、Addressables 或玩法参数。未执行 Unity/游戏测试，效果由用户在游戏中确认。

## 原始输出

- `CottonCrop_Seedling.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-1f502d29-126a-485e-bb5f-170fd377ec9d.png`
- `CottonCrop_Growing.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-7f9231bb-c314-4a92-a29c-7613c6b8ba63.png`
- `CottonCrop_Mature.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-2b7439d1-1b0e-4f52-a408-ea6184549d70.png`

## 最终提示词

### Mature

```text
Use case: style-transfer. Asset type: FlatWorld 2D sandbox game plant sprite, one isolated full plant on truly transparent alpha.
Input images 1 (rice) and 2 (berry shrub) are STYLE REFERENCES ONLY: obey their visibly COARSE, chunky stepped square pixels, broad matte colour clusters and muted natural palette. Input image 3 is SPECIES IDENTITY/STRUCTURE ONLY; DO NOT copy its finer pixel density.
Primary request: redraw the species in the user-approved much-lower-pixel plant style. Draw with the visible coarse pixel density of a roughly 28x32 to 32x36 logical sprite, each logical pixel a big uniform square enlarged with nearest-neighbour. Fewer, larger, unambiguous square pixels; hard stepped edges, just two or three flat shades per major material. The whole plant must read as large block clusters, not many tiny leaves, not a detailed illustration with a pixel filter.
Lighting: upper-left, low-saturation sage/olive greens, deep muted green shadow, a little warm brown stem. Natural botanical proportions and believable plant structure.
Composition: SAME fixed square canvas for this species and future seedling/growing edits. Ground contact centred horizontally at 50% x and 94% y, full plant within the canvas, transparent margin around entire silhouette. Plant height about 78% of canvas, no cropping, no automatic trim to silhouette. Base is just stem meeting air at the baseline; no soil tuft, no ground patch.
Avoid: thin fine pixels, dense veins, tiny speckles, texture noise, gradients, soft brushes, antialiasing, glow, shiny 3D, pure black heavy outline, cast shadow, backdrop, checkerboard baked in, labels, text, border. Keep every background pixel genuinely transparent.
Subject: one mature COTTON plant, a small herbaceous upright branching crop. Five or six broad lobed dark sage leaves in chunky simplified clusters; a warm olive-brown branching stem; only THREE clearly visible off-white ripe fluffy cotton bolls, simplified into a few cream square clusters with one muted beige shadow. Cotton bolls are not flowers with yellow centres. Preserve believable small crop scale, loose asymmetric branching and readable spaces between leaves. Not a tree, not a pot, not a round icon. Output one mature plant only, 1024x1024 square transparent canvas.
```

### Seedling

```text
Use case: precise-object-edit. Asset type: one transparent game sprite, an earlier growth stage from the supplied mature plant.
Input image 1 is the EDIT TARGET and authoritative master for this species. Keep the exact same entire 1254x1254 square canvas, same pixel-block size, same coarse hard stepped pixel art, same muted sage/olive and warm brown palette, upper-left light, and exact same bottom-centre root contact point and baseline as the mature plant. Genuinely transparent background. DO NOT zoom, crop, trim or enlarge the young plant to fill the image. Most of the canvas must remain empty transparent air above and beside the younger plant. Use fewer large uniform square logical pixels, the same big pixel pitch as the mature plant, not tiny detailed leaves. Natural growth: reduce height and branching while keeping the root in exactly the same screen position. No soil, no ground patch, no pot, no cast shadow, no text, no extra object, no border, no fine veins/noise/dithering, no gradients or smoothing, no black outline.
Change only the life stage described below; don't redesign the style or species. Preserve all transparent canvas margins and full original output canvas dimensions.
Replace the mature cotton crop with its SEEDLING: one tiny fresh upright stem and two broad simple cotyledons plus one very small incipient lobed true leaf. Total young plant height only 25% of the mature plant's visible height, width only about 25% of mature width. Stem emerges from precisely the same bottom root point. All mature branches and cotton bolls disappear. No white bolls or flowers. Leave the upper three quarters of the canvas empty transparent. Do not magnify the seedling.
```

### Growing

```text
Use case: precise-object-edit. Asset type: one transparent game sprite, an earlier growth stage from the supplied mature plant.
Input image 1 is the EDIT TARGET and authoritative master for this species. Keep the exact same entire 1254x1254 square canvas, same pixel-block size, same coarse hard stepped pixel art, same muted sage/olive and warm brown palette, upper-left light, and exact same bottom-centre root contact point and baseline as the mature plant. Genuinely transparent background. DO NOT zoom, crop, trim or enlarge the young plant to fill the image. Most of the canvas must remain empty transparent air above and beside the younger plant. Use fewer large uniform square logical pixels, the same big pixel pitch as the mature plant, not tiny detailed leaves. Natural growth: reduce height and branching while keeping the root in exactly the same screen position. No soil, no ground patch, no pot, no cast shadow, no text, no extra object, no border, no fine veins/noise/dithering, no gradients or smoothing, no black outline.
Change only the life stage described below; don't redesign the style or species. Preserve all transparent canvas margins and full original output canvas dimensions.
Replace the mature cotton crop with its GROWING vegetative stage: a clearly younger slender branched cotton stem about 60% of the mature plant's height, four or five broad recognisably lobed olive/sage leaf clusters, no ripe cotton, no flowers, no white bolls. It grows upward from precisely the original lower root contact; no movement upward in the image. Crown about 55% of mature width. Leave the upper half mostly transparent. Maintain identical large pixel-block pitch and shading, not a smaller-grained redraw.
```

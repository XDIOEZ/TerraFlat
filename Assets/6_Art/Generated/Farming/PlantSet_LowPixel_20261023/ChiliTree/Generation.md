# 辣椒低像素三阶段重绘

日期：2026-10-23。

按用户认可的水稻、浆果丛低像素样例绘制；只重绘，保留物种与玩法。使用内置 `image_gen`，每张独立调用并开启 `transparent_background=true`。先绘成熟母版，再分别编辑为幼苗、生长期；生成原图直接复制，保留原始 Alpha，未用程序绘制、缩放、裁切或处理 PNG。

## 文件与导入

- 三张均为 **1254×1254** 原始透明 PNG；统一像素颗粒、低饱和大色块、左上光照；幼苗保留大量透明上方留白，不放大填满画布。
- 旧图尺寸：40×48 / 32 PPU = 1.25×1.5 世界单位。
- 新图三阶段统一 **836 PPU**，以新画布高度 / 旧世界画布高度换算；保持旧世界画布高度 1.5；改用同样的方形画布后宽度为 1.5，横向留白随画布扩大，不改运行时实体碰撞或占地。
- Sprite Single、Point、无 Mipmap、无压缩、Alpha Transparency、底中 Pivot `(0.5, 0)`，不生成物理轮廓；每张图和目录使用新 GUID。
- 幼苗为细绿茎与少量子叶；生长期增加披针形叶片与分枝而无果；成熟期挂 4 个细长红椒。游戏 ID 虽名 ChiliTree，美术仍按草本辣椒画，不画粗树干。
- 本目录仅新增素材；未改原图、JSON 引用、Addressables 或玩法参数。未执行 Unity/游戏测试，效果由用户在游戏中确认。

## 原始输出

- `ChiliTree_Seedling.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-c5883f61-afd8-4d11-84fd-0f45fdd31215.png`
- `ChiliTree_Growing.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-013d4940-3ced-40b2-9270-81bf42c090e5.png`
- `ChiliTree_Mature.png` ← `C:/Users/CatStudio/.codex/generated_images/01a1685d-1ae3-7351-94f8-151ffa8902aa/exec-a8a5e1d1-df80-444e-8977-b5975cf728a0.png`

## 最终提示词

### Mature

```text
Use case: style-transfer. Asset type: FlatWorld 2D sandbox game plant sprite, one isolated full plant on truly transparent alpha.
Input images 1 (rice) and 2 (berry shrub) are STYLE REFERENCES ONLY: obey their visibly COARSE, chunky stepped square pixels, broad matte colour clusters and muted natural palette. Input image 3 is SPECIES IDENTITY/STRUCTURE ONLY; DO NOT copy its finer pixel density.
Primary request: redraw the species in the user-approved much-lower-pixel plant style. Draw with the visible coarse pixel density of a roughly 28x32 to 32x36 logical sprite, each logical pixel a big uniform square enlarged with nearest-neighbour. Fewer, larger, unambiguous square pixels; hard stepped edges, just two or three flat shades per major material. The whole plant must read as large block clusters, not many tiny leaves, not a detailed illustration with a pixel filter.
Lighting: upper-left, low-saturation sage/olive greens, deep muted green shadow, a little warm brown stem. Natural botanical proportions and believable plant structure.
Composition: SAME fixed square canvas for this species and future seedling/growing edits. Ground contact centred horizontally at 50% x and 94% y, full plant within the canvas, transparent margin around entire silhouette. Plant height about 78% of canvas, no cropping, no automatic trim to silhouette. Base is just stem meeting air at the baseline; no soil tuft, no ground patch.
Avoid: thin fine pixels, dense veins, tiny speckles, texture noise, gradients, soft brushes, antialiasing, glow, shiny 3D, pure black heavy outline, cast shadow, backdrop, checkerboard baked in, labels, text, border. Keep every background pixel genuinely transparent.
Subject: one mature CHILI PEPPER herbaceous crop, a slim upright branching green plant with several broad tapered sage-olive leaves and FOUR clearly visible hanging elongated curved dark-red chili peppers. Peppers are long tapered pointed pods, not round berries; simple dark brick-red body with one muted red flat highlight, no gloss. Preserve real pepper plant proportions, green branching herbaceous stem, compact common ground base and loose asymmetric leaf spacing. Despite its game identifier ChiliTree it is NOT a woody tree, no thick trunk. Output one mature plant only, 1024x1024 square transparent canvas.
```

### Seedling

```text
Use case: precise-object-edit. Asset type: one transparent game sprite, an earlier growth stage from the supplied mature plant.
Input image 1 is the EDIT TARGET and authoritative master for this species. Keep the exact same entire 1254x1254 square canvas, same pixel-block size, same coarse hard stepped pixel art, same muted sage/olive and warm brown palette, upper-left light, and exact same bottom-centre root contact point and baseline as the mature plant. Genuinely transparent background. DO NOT zoom, crop, trim or enlarge the young plant to fill the image. Most of the canvas must remain empty transparent air above and beside the younger plant. Use fewer large uniform square logical pixels, the same big pixel pitch as the mature plant, not tiny detailed leaves. Natural growth: reduce height and branching while keeping the root in exactly the same screen position. No soil, no ground patch, no pot, no cast shadow, no text, no extra object, no border, no fine veins/noise/dithering, no gradients or smoothing, no black outline.
Change only the life stage described below; don't redesign the style or species. Preserve all transparent canvas margins and full original output canvas dimensions.
Replace the mature chili crop with its SEEDLING: a tiny slender green stem with two small opposite smooth tapered cotyledons and one small upright true leaf. Total height only 23% of the mature plant's height, width about 25% of mature width. Its root meets exactly the same lower-canvas root contact point. No peppers, flowers or large branches. Top three quarters of the square canvas is empty transparent. Do not magnify the seedling, do not centre it vertically.
```

### Growing

```text
Use case: precise-object-edit. Asset type: one transparent game sprite, an earlier growth stage from the supplied mature plant.
Input image 1 is the EDIT TARGET and authoritative master for this species. Keep the exact same entire 1254x1254 square canvas, same pixel-block size, same coarse hard stepped pixel art, same muted sage/olive and warm brown palette, upper-left light, and exact same bottom-centre root contact point and baseline as the mature plant. Genuinely transparent background. DO NOT zoom, crop, trim or enlarge the young plant to fill the image. Most of the canvas must remain empty transparent air above and beside the younger plant. Use fewer large uniform square logical pixels, the same big pixel pitch as the mature plant, not tiny detailed leaves. Natural growth: reduce height and branching while keeping the root in exactly the same screen position. No soil, no ground patch, no pot, no cast shadow, no text, no extra object, no border, no fine veins/noise/dithering, no gradients or smoothing, no black outline.
Change only the life stage described below; don't redesign the style or species. Preserve all transparent canvas margins and full original output canvas dimensions.
Replace the mature chili crop with its GROWING vegetative stage: a younger thin green stem with three short side branches and five to seven simple tapered muted olive leaf clusters, about 60% of the mature plant's height and 55% of mature width. Root contact remains precisely in the same bottom-centre position as the master; keep empty transparent overhead. No ripe red peppers, no flowers, no fruit. Keep the same coarse pixel pitch and upper-left flat shading; do not draw more tiny pixels.
```

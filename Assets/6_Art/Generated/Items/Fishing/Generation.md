# 骨、铁、金鱼钩贴图

2026-10-11 使用内置 `image_gen` 分别生成三种材质的独立成图。

- 文件：`FishingHook_Bone.png`、`FishingHook_Iron.png`、`FishingHook_Gold.png`。
- 原图：1254×1254 RGBA，保留生成 Alpha 和透明留白；不裁剪、缩放或量化源图。
- 导入：Sprite Single、中心 Pivot、Point、无 Mipmap、无压缩；2508 PPU，保持原鱼钩画布宽度 0.5 世界单位。
- 消费：`fishing.json` 的三个 `FishingHook_*` 使用各自成图，不叠加旧材质染色；Default Addressables 登记同路径地址及 `ItemSprite` 标签。
- `FishingAssetBuilder` 只导入并登记正式成图，重建时不会覆盖成旧占位图。
- Unity 当时处于 Play Mode，本轮只修改静态资源，游戏内显示由用户验收。

## 共用提示词

```text
Use case: stylized-concept
Asset type: game-ready inventory sprite for FlatWorld, a 2D pixel-art sandbox game.
Primary request: draw exactly ONE fishing hook as a crisp authentic retro pixel-art item icon.
Composition: square transparent canvas, one centered complete hook, use about 70 percent of canvas height and 55 percent of its width, generous transparent margins. Upright J-shaped hook: the long shank on the RIGHT, small closed round/octagonal eye at top-right, bottom curve bending LEFT, pointed tip pointing UP at left with one small inward-facing barb. Clear empty space inside the eye and hook bend. No fishing line, no rod, no bait, no lure, no hands, no chain, no extra items.
Style: match the game's small pixel inventory tools: chunky hard square pixel clusters, clean stair-step edges and strong readable silhouette, deep charcoal-brown thin outline, a restrained small palette with 3 material shades and one highlight, light from upper left. Build the image as if designed on a 32x32 logical pixel grid and enlarged only with nearest-neighbor, about one logical pixel outline and two-three logical pixels thick shank. Avoid soft or high-resolution illustration. No smooth curves, no airbrushing, no gradients, no photorealism, no 3D render, no dense scratches, no noise. Fully opaque hard-edged object on actual alpha transparency, no cast shadow, no glow, no background card, no grid, no ground, no text, no watermark.
```

## 各材质补充

### Bone

```text
Material: a carved bone fishing hook. Warm ivory and cream, ochre-tan shadow, tiny warm pale highlight. A subtly thicker gently tapered shank and slightly irregular angular carved contour, one or two broad carve facets only. Visibly bone, plain and practical; do not add ornamental teeth, bindings, skulls or symbols.
```

### Iron

```text
Material: forged iron fishing hook. Desaturated cool gunmetal blue-gray, dark slate shadow, small pale silvery edge highlight. Smoothly regular but pixel-stepped metal curve, even narrow shank, simple robust round/octagonal eye and sharp barb. Visibly cool iron, plain and practical; no rust, no decorations or rings.
```

### Gold

```text
Material: solid gold fishing hook. Rich muted golden-yellow midtone, warm amber-brown shadow, small pale cream-yellow edge highlight. Regular pixel-stepped metal curve, even narrow shank, simple round/octagonal eye and sharp barb. Visibly gold, clean and practical; no jewels, no ornament, no sparkle particles.
```

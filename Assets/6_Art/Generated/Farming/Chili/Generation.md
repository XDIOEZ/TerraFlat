# 辣椒运行时素材

- 生成方式：内置 ImageGen；4 次独立生成，项目中的桃子、水稻和 TX Plant 植被作为画风、配色与比例参考。
- 运行时图标：24×24，主体范围 (3,1)–(21,23)，PPU 32，中心 Pivot。
- 三阶段植株：统一 40×48，幼苗主体 (12,34)–(28,46)，生长期 (7,17)–(33,46)，成熟期 (3,5)–(37,46)；PPU 32，Pivot (0.5,2/48)，统一根部基线。
- 四张图片各 20 种可见颜色，Alpha 仅 0/255，透明四角，无背景和内置投影；Point、无 Mipmap、无压缩、Single Sprite。
- 原生成图经透明主体裁剪、最近邻缩放、有限色量化后放回固定画布；高分辨率设计源不参与运行时加载。
- 植株实际最大约 1.06×1.28 世界单位；成熟图挂 5 个红辣椒，保留同类植被的橄榄绿色块与较暗轮廓。
- 已检查原尺寸和放大预览；人工还需在游戏里确认与同类农作物的世界比例、根部落点和交互范围。

## 提示词

### Chili

```text
Use case: stylized-concept. Create a single new FlatWorld 2D top-down sandbox pixel-art runtime sprite. Match the project references visible in context: peach item has simple hard clustered pixels, vegetation uses muted olive and moss greens with dark green-brown selective edges, one olive-lime highlight and one dark forest green shadow, subtle top-left light. Authentic coarse pixel art, limited 12-20 colors, strong clear silhouette, no gradients or anti-aliasing or smooth vector edges, no fine noisy detail. Transparent background with actual alpha, no ground or soil or pot, no cast shadow, no text labels or watermark. Square image with the complete subject and generous transparent padding. Keep pixel clusters large as if painted on a 32 by 40 logical grid, nearest-neighbor pixel appearance. Asset type: edible red chili pepper inventory icon. Exactly one elongated red chili fruit with a short hooked green stem, thick top tapering to a curved pointed tip, slight diagonal composition, red-orange upper-left highlight and burgundy shadow. Centered icon, compact bold silhouette like the project's peach, intended runtime canvas 24 by 24 pixels.
```

### ChiliTree_Seedling

```text
Use case: stylized-concept. Create a single new FlatWorld 2D top-down sandbox pixel-art runtime sprite. Match the project references visible in context: peach item has simple hard clustered pixels, vegetation uses muted olive and moss greens with dark green-brown selective edges, one olive-lime highlight and one dark forest green shadow, subtle top-left light. Authentic coarse pixel art, limited 12-20 colors, strong clear silhouette, no gradients or anti-aliasing or smooth vector edges, no fine noisy detail. Transparent background with actual alpha, no ground or soil or pot, no cast shadow, no text labels or watermark. Square image with the complete subject and generous transparent padding. Keep pixel clusters large as if painted on a 32 by 40 logical grid, nearest-neighbor pixel appearance. Asset type: chili pepper crop SEEDLING growth stage, a tiny upright green stem with four broad pointed oval leaves, 2 larger lower leaves and 2 tiny upper leaves, no flowers, absolutely no chili fruit. Show only one small plant above ground; bottom-center stem base. Same plant identity as the growing and mature chili bush: olive moss green clustered foliage and green branching stem. Intended 40 by 48 pixel runtime canvas, seedling will occupy 16 by 18 pixels.
```

### ChiliTree_Growing

```text
Use case: stylized-concept. Create a single new FlatWorld 2D top-down sandbox pixel-art runtime sprite. Match the project references visible in context: peach item has simple hard clustered pixels, vegetation uses muted olive and moss greens with dark green-brown selective edges, one olive-lime highlight and one dark forest green shadow, subtle top-left light. Authentic coarse pixel art, limited 12-20 colors, strong clear silhouette, no gradients or anti-aliasing or smooth vector edges, no fine noisy detail. Transparent background with actual alpha, no ground or soil or pot, no cast shadow, no text labels or watermark. Square image with the complete subject and generous transparent padding. Keep pixel clusters large as if painted on a 32 by 40 logical grid, nearest-neighbor pixel appearance. Asset type: chili pepper crop GROWING intermediate stage. Exactly one small upright branching chili bush with 3 green branches, around 9 broad pointed oval leaves, open readable silhouette, NO fruit and NO flowers. It is clearly larger and more leafy than a four-leaf seedling but smaller than a full mature fruiting bush. Green upright stem base aligned bottom-center. Intended 40 by 48 pixel runtime canvas, this plant will occupy about 26 by 30 pixels.
```

### ChiliTree_Mature

```text
Use case: stylized-concept. Create a single new FlatWorld 2D top-down sandbox pixel-art runtime sprite. Match the project references visible in context: peach item has simple hard clustered pixels, vegetation uses muted olive and moss greens with dark green-brown selective edges, one olive-lime highlight and one dark forest green shadow, subtle top-left light. Authentic coarse pixel art, limited 12-20 colors, strong clear silhouette, no gradients or anti-aliasing or smooth vector edges, no fine noisy detail. Transparent background with actual alpha, no ground or soil or pot, no cast shadow, no text labels or watermark. Square image with the complete subject and generous transparent padding. Keep pixel clusters large as if painted on a 32 by 40 logical grid, nearest-neighbor pixel appearance. Asset type: chili pepper crop MATURE growth stage. Exactly one healthy upright compact chili bush, 3 main green branches, around 12 broad pointed oval leaves clustered into 3 leafy tiers, visibly bearing FIVE elongated RED chili peppers hanging downward, each with a green top and a tapered slightly curved pointed tip; avoid round berries. Strong readable open leafy silhouette, at most 5 obvious red fruits separated across the upper and middle branches. Green stem base aligned bottom-center. Intended 40 by 48 pixel runtime canvas, this plant will occupy around 34 by 42 pixels.
```

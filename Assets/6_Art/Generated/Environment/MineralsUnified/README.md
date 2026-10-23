# 统一矿山素材

2026-10-23。正式使用增高贴地版：低饱和、偏写实的复古粗像素；同一套岩石尺度、左上光源和矿体结构，矿脉延伸到不规则贴地底边。

共 10 种矿山。铁、铜、煤复制用户认可的增高样稿，其余 7 种使用内置 image_gen 以该样稿为结构与画风参考重绘。每种为独立透明 PNG，通过对应物品定义的 visual.spriteAddress 和 ItemSprite Addressables 主地址引用；不替换掉落矿石图标或冰山。

| 矿山 | 正式贴图 |
| --- | --- |
| 铁矿 | [Mine_Iron](Mine_Iron/Mine_Iron_Idle.png) |
| 铜矿 | [Mine_Copper](Mine_Copper/Mine_Copper_Idle.png) |
| 煤矿 | [Mine_Coal](Mine_Coal/Mine_Coal_Idle.png) |
| 石矿 | [Mine_Stone](Mine_Stone/Mine_Stone_Idle.png) |
| 锡矿 | [Mine_Tin](Mine_Tin/Mine_Tin_Idle.png) |
| 金矿 | [Mine_Gold](Mine_Gold/Mine_Gold_Idle.png) |
| 钻石矿 | [Mine_Diamond](Mine_Diamond/Mine_Diamond_Idle.png) |
| 硫磺矿 | [Mine_Sulfur](Mine_Sulfur/Mine_Sulfur_Idle.png) |
| 硝石矿 | [Mine_Niter](Mine_Niter/Mine_Niter_Idle.png) |
| 黑曜石矿 | [Mine_Obsidian](Mine_Obsidian/Mine_Obsidian_Idle.png) |

按用户要求未运行游戏内验收。已进入世界的实例需要通过现有资源配置刷新流程或重新进入世界，才能观察当前定义的贴图。

## 绘图提示词

铁、铜、煤的提示词见 [已认可样稿](../MineralStyleSamples_20261023/README.md)。

### 石矿

Use case: precise-object-edit. Create ONE standalone ordinary STONE outcrop, entirely naturally fractured desaturated gray-beige slate/granite with a few broad muted gray stone bands; no colored ore, gems, metals or crystals for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 锡矿

Use case: precise-object-edit. Create ONE standalone TIN ore outcrop: embedded broad rough silver-gray and dull pewter-white cassiterite mineral plates with darker gray cores; non-polished, restrained metallic gray; no rusty red, no colored gems for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 金矿

Use case: precise-object-edit. Create ONE standalone GOLD ore outcrop: broad embedded dull ochre-gold mustard-yellow rough veins with muted amber shadows, little pale gold highlight; natural raw ore rather than refined ingots, no orange rust, no glitter stars for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 钻石矿

Use case: precise-object-edit. Create ONE standalone DIAMOND ore outcrop: a few short chunky milky pale cyan and desaturated blue diamond crystal facets embedded naturally IN the gray host rock, clear broad angular flat faces; no tall fantasy crystal towers, no sparkle stars, no glow for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 硫磺矿

Use case: precise-object-edit. Create ONE standalone SULFUR ore outcrop: broad earthy muted yellow ochre sulfur seams and chunky blunt yellow mineral nodules embedded in gray rock, matte dusty mineral with pale muted sulfur yellow highlights; no gold metal, no neon, no long crystals for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 硝石矿

Use case: precise-object-edit. Create ONE standalone NITER/SALTPETER ore outcrop: dull ivory-white and pale warm cream salt mineral crusts in broad thick veins, chunky shallow cream nodules embedded IN gray rock; chalky and matte, not metallic, not blue diamonds; very sparse detail for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.

### 黑曜石矿

Use case: precise-object-edit. Create ONE standalone OBSIDIAN outcrop: substantial dark charcoal-purple volcanic glass plates IN a slate-gray rocky outcrop, restrained desaturated violet-gray and cool blue-black facets, a few large pale gray sharp angular reflection faces; distinguish from coal by the broad glassy planar fractures, no shine stars for FlatWorld by editing this reference iron ore sprite. REPLACE ALL rusty red ore with the specified mineral identity; no red iron residue. The user approved this exact taller rooted rock silhouette and art style, which must remain consistent across all ten mines.
Keep the same elevated orthographic top-down viewpoint, substantial raised top and shoulders, broad solid natural rock body, wide nearly horizontal irregular soil-contact bottom with only shallow steps, upper-left lighting, scale and canvas aspect ratio. It is an in-situ mineral mass continuing underground, not a collectible rock on a pedestal. No U-shaped front rim, curved apron, round underside, separate stone base, ring, cavity, oval shadow or soil island. Mineral veins directly reach the bottom contact line.
STRICT STYLE: desaturated semi-realistic RETRO PIXEL ART, same VERY LARGE square pixel clusters as reference, sparse broad flat connected color patches, restrained palette and 2 or 3 shade steps. Keep detail low and shapes readable at small in-game size. Not a photo, not painterly, no pixel-filtered high-detail illustration, no soft gradient, no antialiasing, no tiny noise, no glitter or glow. Keep the host rock desaturated gray-beige top, medium slate gray and cool charcoal shadow. Customize mineral material/patch distribution and a few large fractures without changing shared silhouette scale.
Transparent background with preserved real alpha. Single coherent solid sprite centered, same 1402 by 1122 canvas if possible, full silhouette visible and padded. No other objects, labels, UI, frames, ground tile, grass, particles or text.


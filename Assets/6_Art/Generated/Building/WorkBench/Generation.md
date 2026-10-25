# 工作台贴图重绘

- 生成方式：内置 imagegen，2026-10-25，根据用户游戏截图重绘；木材配色参考 `../Chest_Wood/Chest_Wood_Closed.png`。
- 造型：手工木桌、厚木板桌面、木榫支撑和桌腿，桌上放低饱和蓝图、锤子与手锯。
- 成品：`WorkBench_Idle.png`，1430 × 1100，直接复制生成原图，保留原始颜色和透明通道，没有缩放、裁剪或全图限色。
- 导入：Sprite Single、中心 Pivot `(0.5, 0.5)`、1430 PPU、Point、无 Mipmap、无压缩；最大尺寸 2048，不降采样。完整画布为 1 × 0.76923 世界单位，沿用旧工作台的宽度和中心锚点，保持新桌子的自然宽高比。
- 接入：`WorkBench` 和 `WorkBench_Summoner` 共用本图，Addressables 同路径地址、`ItemSprite` 标签，覆盖建筑、预览、手持与背包图标。原有占地、碰撞和制作逻辑不变。
- 原素材：`Assets/6_Art/Building/Tileset.png[workbenches]`，只替换工作台的两处配置引用，不修改共享图集。
- 游戏内效果由用户验收；不操作用户正在使用的 PlayMode。

## 完整生成提示词

```text
Use case: style-transfer.
Asset type: one transparent world sprite for FlatWorld, a 2D top-down survival sandbox.
Primary request: Redraw the wooden WORKBENCH currently shown in the hotbar and held by the character in reference image 1. Output only a standalone redesigned workbench sprite, never the screenshot, UI or character.
Input images: Image 1 is the existing workbench identity and gameplay camera reference. Image 2 is the wooden chest, a supporting reference for muted wooden colors and broad pixel clusters, not an object to reproduce.
Subject: A sturdy handmade early-game wooden crafting workbench. A broad horizontal tabletop of a few thick weathered oak planks, solid visible wooden legs, a simple lower support beam and believable pegged wooden joints. A small muted slate-blue plan sheet rests flat on one part of the tabletop, with a simple handmade hammer and a short hand saw lying on the other part. The table itself must dominate the silhouette, clear and readable at game icon scale.
Style: restrained, low-saturation, slightly realistic retro PIXEL ART. Strong coarse square pixel clusters, approximately 48 to 64 logical pixels across the object; crisp stair-step edges, small number of connected material shade bands. Retain coarse blocks when exported at high resolution. No dense tiny pixels, no stippling, no soft painterly shading.
Composition: one centered complete sprite in a square canvas, fully visible, fills around 90 percent of the canvas width; compact wide rectangular workbench with width to total height around 1.3. Match an orthographic top-down RPG view looking down about 40 degrees, horizontal front edge, both tabletop and front legs clearly visible. No exaggerated side isometric view. All feet on a consistent baseline. Moderate transparent margin, transparent spaces between legs.
Materials and lighting: matte warm medium brown timber with dark umber joints, broad restrained grain patches, desaturated blue paper, dull gray tool heads. Light from upper left, coherent darker front apron and under-table structural shading. Use natural brown contours, avoid thick pure black outline.
Text: none, no legible writing or labels.
Background: true transparent alpha. No ground, floor, scenery, cast ground shadow, border, UI, halo or watermark.
Constraints: Keep it as a humble wooden crafting table appropriate to the earliest village crafting technology. No machinery, glowing parts, metal cabinet or ornate tool wall. Export the original generated resolution, do not resize, quantize, blur or compress it.
```

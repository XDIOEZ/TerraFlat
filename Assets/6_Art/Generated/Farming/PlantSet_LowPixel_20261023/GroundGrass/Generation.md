# 野草 32 变体图集重绘

采用已认可粗像素植物画风，野草仍是独立草层，不把草地 Ground 底材当成草株。内置 `image_gen`、透明背景，原 PNG/Alpha 保存，没有程序改图。

## 排布与交付规格

- 源图：`GroundGrass_Atlas.png`，1254×1254，六列六行。
- 前五行每行六个草簇，第六行前两格有草，后四格透明，共 32 个变体；前 24 个普通草簇、后 8 个强调变体。
- 每格源尺寸 209×209；整张源图设置 PPU=418、Point、无 Mipmap/压缩、Read/Write、透明背景。
- 当前正式源已接入本图集，由 `Assets/2_Prefabs/World/WorldModel/ChunkView.prefab` 按 6 列、32 变体、`spriteSizePixels=209`、`pixelsPerUnit=418` 消费；对应每格宽 0.5 世界单位。
- 2026-10-23 配色协调：草层独立使用 `Assets/9_Shaders/Material/Grass-Detail-Sway-Lit.mat`，Tint 为 `(1.25, 1.22, 1.30, 1)`，小幅提亮并减弱饱和感；野花保持原共用材质。源 PNG、Alpha 和切片参数不变，运行表现交用户验收。
- 最终源：`C:\Users\CatStudio\.codex\generated_images\01a16839-f982-7652-9f1b-d767112709d1\exec-ec542f96-384c-4c7d-a883-1c89d4f8f1d2.png`。

## 初次生成提示词

```text
Use case: style-transfer. Image 1 is the STRUCTURE/LAYOUT/IDENTITY reference for a grass sprite ATLAS. Image 2 is the USER-APPROVED coarse-pixel foliage STYLE reference only; do not copy its bush shape. Create ONE complete square sprite atlas on a REGULAR SIX-COLUMN by SIX-ROW grid with exactly 32 distinct short-grass tuft sprites. Fill rows 1–5 with six sprites each (30 total), and fill only the FIRST TWO cells of row 6 (sprites 31–32); the last FOUR cells of row 6 must remain empty transparent. All 36 grid cells are identical dimensions, no visible grid lines. This is one atlas asset, not a promotional collage. Each cell has a 16x16 LOGICAL pixel area, its tuft located near bottom center within the cell with at least 2 logical pixels transparent padding at cell edges. Each tuft is around 5–10 logical pixels wide and 5–11 tall, made from 3–7 simple angled grass blades, coarse hard-edged pixel clusters, mostly muted sage/olive/moss green. Natural variations in blade lean, short versus tall tufts, a few dry straw-green accents. First 24 cells are small common tufts; final 8 occupied cells are slightly taller accent tufts. All share the same visual pixel grid. Keep every tuft isolated; no sprite crosses a cell edge. FlatWorld art direction: low-saturation semi-realistic retro pixel sprites with VERY OBVIOUS large chunky square pixels, natural plant proportions, restrained 2–3 value shading, about 8–12 main matte colors, upper-left light. Match the coarse pixel density and olive/sage greens of the user-approved sample, not the fine pixels of any old identity reference. Express leaves using large solid stepped clusters, no fine veins, tiny speckles, antialiasing, smooth gradients, photorealistic texture or glossy 3D. Actual transparent alpha for all empty space, no baked checkerboard, ground, pot, shadow, writing, labels or decorative frame. Entire atlas background and unused cells genuinely transparent. No soil, opaque tiles, bushes, flowers, cereal heads, trees, numbers, cell labels, frames, outlines around cells or drop shadows.
```

## 最终补齐排布提示词

初次源图漏掉第五行后三格；仅补这三格，保留其它变体与原画布。

```text
Use case: precise-object-edit. Image 1 is the grass atlas EDIT TARGET.
The atlas currently has 29 grass tufts because THREE CELLS ARE MISSING. Make one narrow edit: ADD exactly three new, distinct coarse-pixel grass tufts into ROW FIVE, COLUMNS FOUR, FIVE AND SIX. Keep every existing tuft exactly as it is, in the same place. Six equal columns and six equal rows on the full square canvas.
The three missing cell centers, as fractions of the entire canvas, are approximately (0.5833,0.75), (0.75,0.75), (0.9167,0.75). Their grass roots/bases should align with the other tufts of row five at about y=0.80. Match the tuft scale and large pixel clusters of row-five columns one to three. These are accent grasses with a few tall olive blades and slightly dry straw-green tips; same muted palette, upper-left light, visible large square pixels and hard stepped contours.
FINAL COUNT: exactly 32 grass tufts. Rows 1,2,3,4,5 each have SIX tufts. Row 6 has only TWO tufts in its FIRST TWO cells; the last four cells in row 6 remain empty transparent.
Preserve the original canvas width/height, transparent background, all existing 29 sprites and their exact locations. Do not crop, rescale or redraw existing sprites. No text, cell labels, borders, grid lines, soil, stones, flowers, shadows, antialiasing or scenery. Output only the corrected transparent atlas.
```

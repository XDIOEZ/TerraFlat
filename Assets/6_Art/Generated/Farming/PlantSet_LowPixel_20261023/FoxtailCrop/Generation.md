# 狗尾草粗像素重绘

本轮按已认可的 `PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png` 和 `BerryCrop_Mature_LowPixelSample.png` 统一粗颗粒、低饱和复古像素风。三阶段分别独立使用内置 `image_gen` 生成；透明背景开启。PNG 原始画布及 Alpha 原样复制，未用程序缩放、裁切、重绘或量化。

仅提供新版素材，不修改物品定义、运行时引用、玩法或种类。该系统当前消费 `FoxtailCrop` 的 seedling/growing/mature 三张图，不扩展种子与收获物图标。

## 图片与规格

| 文件 | 原始尺寸 | PPU | Pivot |
| --- | --- | --- | --- |
| FoxtailCrop_Seedling.png | 1254×1254 | 1254 | (0.5, 0.055) |
| FoxtailCrop_Growing.png | 1254×1254 | 1254 | (0.5, 0.055) |
| FoxtailCrop_Mature.png | 1254×1254 | 1254 | (0.5, 0.055) |

旧图为 64×64、64 PPU，一张画布对应一世界单位；新图保留相同画布世界尺寸，三个阶段 PPU 一致。Pivot 采用新图公共底部基线，约为从顶部 94.5% 位置；幼苗与生长期保留大面积上方透明留白，不放大到成熟植株大小。正式接入前仍需在游戏里确认像素密度、落地位置与视觉比例。

统一导入：Point、关闭 Mipmap、无压缩、Alpha Transparency，Max Texture Size 2048，不另生成碰撞轮廓。文件的 1254 像素物理尺寸不等于逻辑像素分辨率；提示词的约 28×32 是生成目标，并非严格网格实测结论。

成熟穗采用无独立颗粒的橄榄棕短粗刷状轮廓，避免与水稻的金黄下垂分枝穗混淆。

## 源文件

- 成熟：`C:/Users/CatStudio/.codex/generated_images/01a1685b-6ed5-7d40-a5c4-a95e8c9debf1/exec-c55be6d3-8fb7-493e-89a1-b77f5c96a04b.png`
- 生长期：`C:/Users/CatStudio/.codex/generated_images/01a1685b-6ed5-7d40-a5c4-a95e8c9debf1/exec-e1dd575b-ffe0-4a33-b9f2-278c9ec59fcd.png`
- 幼苗：`C:/Users/CatStudio/.codex/generated_images/01a1685b-6ed5-7d40-a5c4-a95e8c9debf1/exec-d3e1f564-76f7-47fc-9c10-51512aba9a15.png`

初次成熟稿作为精修输入：`C:/Users/CatStudio/.codex/generated_images/01a1685b-6ed5-7d40-a5c4-a95e8c9debf1/exec-7f159ead-3939-48e4-b6f6-b87064ad4b56.png`。最终交付采用修正刷状穗后的成熟稿。

## 最终提示词

### 成熟初稿

身份输入：`Assets/6_Art/Generated/Farming/FoxtailGrass/FoxtailGrass_Mature.png`。画风输入：`Assets/6_Art/Generated/Farming/PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png`。

```text
Use case: style-transfer.
Asset type: one isolated mature foxtail grass crop world sprite for FlatWorld, a 2D elevated top-down sandbox game.
Input image 1 is the EDIT TARGET for botanical identity only: the existing tiny mature FoxtailGrass sprite. Input image 2 is the approved coarse-pixel art reference: RiceCrop_Mature_LowPixelSample.png. Use image 2 ONLY for the clearly visible large square pixel clusters, matte olive/sage palette, and upper-left light; DO NOT borrow its rice seed-head shape.
Primary request: redraw the foxtail at the much coarser pixel density of the approved reference, low-saturation semi-realistic retro pixel art. The complete mature plant should appear drawn by hand on about a 32x32 logical pixel grid, then enlarged with nearest-neighbor square blocks. Use only 8–12 matte colors, two flat values per material, hard pixel corners and large solid clusters. No subpixel detail or smooth edges.
Subject: ONE small naturally asymmetric clump of foxtail grass (Setaria). Four to six olive-green narrow arching blade leaves rise from a tight common stem base. Three distinctly short, plump, softly bristled cylindrical foxtail seed heads on thin green stems, in muted olive-tan and dry pale ochre, leaning gently outward. Each foxtail seed head is one simple compact cigar/bottlebrush cluster around 2–3 logical pixels wide and 5–7 pixels long. Its bristly impression is a few chunky silhouette steps. Never draw branched drooping rice panicles, individual rice grains, long wheat ears, or fluffy cattails.
Composition: ONE complete upright plant on a square transparent canvas. The entire mature clump occupies about 72% of canvas width and 82% of canvas height. Its common lowest stem base is centered at x50%, y90% measured from the top; highest head around y8%. Transparent padding surrounds all sides. This square canvas and this base position will stay fixed for all growth stages. Keep natural botanical proportions and a readable sparse silhouette.
Constraints: actual transparent alpha; no soil, water, pots, cast shadow, text, label, grid, checkerboard, panels or additional plants. Upper-left light, subdued olive/sage greens and muted dry tan heads. No pure-black thick outline, gradients, antialiasing, thin detailed veins, noise, glossy 3D, tiny highlights or high-resolution foliage. Preserve the complete plant base and do not crop.
```

### 成熟修正

编辑输入：上述成熟初稿；此轮输出为最终成熟图。

```text
Use case: precise-object-edit.
Input image 1 is the edit target: this is supposed to be FOXTAlL GRASS (Setaria) but its seed heads resemble dangling golden rice. Fix the botanical identity and lower the pixel density.
Keep the square transparent canvas, the common plant base in exactly the same position near the lower center, full plant visible, olive-green narrow arching leaves, mature overall height, and upper-left lighting. Do NOT zoom or crop.
Replace all three golden grainy heads with unmistakable short plump CYLINDRICAL GREENISH-TAN BOTTLEBRUSH SEED HEADS. They are solid fuzzy grass bristle cylinders, NOT bunches of separate kernels. Each seed head is only about 3 logical pixels wide and 6 pixels long, softly rounded by blunt stepped corners, with just two large contiguous matte olive-tan and dark-tan planes. Use a few coarse outward silhouette steps to suggest soft bristles. No gold rice grains, no individual grain highlights, no branchlets or rows of diamonds. Three unbranched terminal stems hold these compact seed heads almost upright or leaning slightly outward, NOT heavily drooping.
Make the ENTIRE foxtail clump look drawn on a STRICT approximately 28x32 logical pixel grid, with about 32 visible square pixel steps from top to bottom. Every visible logical pixel is one large solid-color square; leave only two values per material, 8–10 colors. The plant itself must be redrawn with fewer bigger square pixel clusters, not simply blurred.
No text, pots, ground, shadows, antialiasing, high-resolution texture or checkerboard. Preserve actual transparent alpha.
```

### 幼苗

编辑输入：最终成熟图。

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET and mature master of the same foxtail crop set. Create the requested earlier growth stage by editing this mature master.
NON-NEGOTIABLE FRAME INVARIANTS: preserve the master's full square 1254x1254 canvas, the exact same scale of every logical square pixel, colors and upper-left lighting. Do not crop or zoom. Keep the common lowest stem base at exactly the master's bottom-center position, approximately x50%, y94.5% from the top. Keep all transparency and the blank canvas above the young plant. Earlier plants are physically smaller in the SAME full canvas, never enlarged to fill it. This is one independent sprite on actual transparent alpha, no grid, label, text, panels, ground or cast shadow.
Art direction: muted matte olive and sage green, few large solid square pixel clusters, visibly coarse retro pixel art. Only two flat values for leaves, no smooth gradients, no antialiasing, no tiny veins, no fine noise. Match the master's clearly chunky pixel size.
REQUESTED STAGE: VERY YOUNG SEEDLING. Remove every seed head and all tall mature leaves and stalks. Draw only one tiny common green crown with three short simple blade leaves, two spread to the sides and a slightly taller one in the middle. The entire young sprout is only about 22% of the master's plant height and 22% of its width, occupying the lower center of the canvas. Leaf tips around y75%; base stays y94.5%. Each blade is just 1–2 large logical pixels thick. No mature seed heads, no flowers and no detached parts. Keep the rest of the canvas fully empty and transparent.
```

### 生长期

编辑输入：最终成熟图。

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET and mature master of the same foxtail crop set. Create the requested earlier growth stage by editing this mature master.
NON-NEGOTIABLE FRAME INVARIANTS: preserve the master's full square 1254x1254 canvas, the exact same scale of every logical square pixel, colors and upper-left lighting. Do not crop or zoom. Keep the common lowest stem base at exactly the master's bottom-center position, approximately x50%, y94.5% from the top. Keep all transparency and the blank canvas above the young plant. Earlier plants are physically smaller in the SAME full canvas, never enlarged to fill it. This is one independent sprite on actual transparent alpha, no grid, label, text, panels, ground or cast shadow.
Art direction: muted matte olive and sage green, few large solid square pixel clusters, visibly coarse retro pixel art. Only two flat values for leaves, no smooth gradients, no antialiasing, no tiny veins, no fine noise. Match the master's clearly chunky pixel size.
REQUESTED STAGE: JUVENILE GROWING GRASS. Remove every mature foxtail seed head completely. Draw a smaller green grass clump with five narrow natural arching blades and a short central green stem from the same crown. The full clump is about 62% of the master's plant height and 65% of its width; highest leaf around y41%, common base stays y94.5%. It is visibly fuller than the seedling but still shorter than the mature plant. Only green blades and stems, no seed heads, no fruit or flowers. Preserve the same plant identity, matte palette and large pixel size.
```

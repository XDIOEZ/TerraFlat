# 矿物统一画风样稿 · 2026-10-23

## V3 增高贴地版（当前样稿）

在 V2 的贴地结构上，以上部矿体增高约 25% 为绘图目标，抬高岩体顶部与肩部、增加中部块面；保持宽阔的贴地底边，避免重新出现独立底座。保留粗像素、矿物配色及左上光源，重绘轮廓，不拉伸像素。

| 文件 | 矿物 |
| --- | --- |
| `Mine_Iron_Grounded_Tall_Sample.png` | 铁矿脉 |
| `Mine_Copper_Grounded_Tall_Sample.png` | 铜矿脉（孔雀石） |
| [Mine_Coal_Grounded_Tall_Sample.png](../../../../../开发文档文件夹/09_美术素材/参考样张/Environment/MineralStyleSamples_20261023/Mine_Coal_Grounded_Tall_Sample.png) | 煤矿脉 |

使用内置 image_gen 编辑 V2 样稿，先增高铁矿，再以新铁矿的高度和轮廓作为另两种矿物的结构参考。继续保留工具输出 PNG 和 Alpha；这是风格样稿，尚未替换游戏素材，也未做游戏内验收。

<details>
<summary>增高贴地版完整提示词</summary>

### 铁矿

Use case: precise-object-edit.
The attached image is the grounded IRON ORE deposit for FlatWorld. The ground-contact treatment is correct, but the user says the exposed rock mass is too low and small. Make it visibly TALLER and more substantial.

Increase the HEIGHT OF THE ROCK ITSELF by roughly 25 percent, while retaining about the same broad footprint width and the same flat irregular grounded contact-edge shape. This is not a canvas change or adding empty space. Redraw the upper and middle mass: raise the central rear slab and shoulders, give it taller solid angular rock faces and a fuller upright body. The peak should be around the upper 5 to 8 percent of the canvas and the ground contact around the lower 92 to 94 percent, leaving clear transparent margins. Keep a squat believable heavy natural outcrop rather than a thin pillar, spike or tower. No vertical stretching of existing pixels: preserve big SQUARE pixel clusters and redraw the rock at that pixel scale.

Keep the successful rooted-ground structure: the low edge is wide, nearly horizontal with shallow jagged steps; the larger unseen rock is buried underground. Solid rock faces and red-brown iron seams extend directly down to the soil contact. Do not introduce any circular/U-shaped stone apron, supporting ring, separate pedestal, rounded underside or display base. There is no cavern or hole.

Preserve the same desaturated slate gray host rock, muted rusty red/brown iron veins, coarse semi-realistic retro pixel art, upper-left lighting, sparse detail, flat 2 to 3 tone planes, limited colors, orthographic elevated top-down RPG viewpoint and original canvas size and aspect ratio. A taller upper body, not an added lower base. Genuine transparent alpha surroundings; no terrain tile, soil island, grass, detached stones, oval cast shadow, text, labels, frame, glow, gradients, antialiasing, fine speckles or new tiny detail.
Output one single taller grounded iron ore sprite, with consistent coarse pixel size.

### 铜矿

Use case: precise-object-edit.
Image 1 is the existing grounded COPPER ORE sprite, which is the edit target.
Image 2 is the NEW TALLER grounded IRON ORE sprite, provided only as the height, proportions, silhouette and coarse pixel structure reference.

The user says the original grounded deposits look too low and small. Make image 1's exposed rock about a quarter TALLER, matching the substantial height of image 2. Adopt image 2's taller central rear slab and raised shoulders, solid angular top and middle faces, the same apparent width, baseline and overall grounded silhouette. This is an increase in the ROCK MASS height, not extra transparent canvas or adding a base. Preserve large square pixels; redraw the higher structure, never stretch the existing square pixels into rectangles. Peak around the top 5 to 8 percent of the canvas and ground contact around lower 92 to 94 percent, with clear margins. Keep a broad heavy natural outcrop, not a thin pillar.

The color and mineral identity come ONLY from image 1: desaturated moss-green and dark teal-green malachite copper veins, a few dull copper brown accents, and gray slate host rock. The new veins must remain green, never red. No rusty red iron from the reference.
Keep the same low-saturation semi-realistic RETRO PIXEL ART, very large hard square pixel clusters, sparse connected flat color areas, restrained 2 to 3 shade steps, same upper-left light and elevated orthographic top-down game view. Match image 2's canvas size and aspect ratio, pixel scale, full body height and bottom level so the mineral sprites are a coherent set.

Keep the successful rooted lower structure: solid rock faces and the mineral veins extend directly into a wide, almost level contact edge with a few shallow jagged steps. The unseen bottom is buried underground. Do not create a U-shaped stone apron, a crescent rim, horseshoe, supporting ring, display pedestal, arch, cavity, hanging rounded underside or any additional base. Height comes from the upper body.
Genuine transparent alpha background. No ground tile, soil island, grass, detached rocks, oval shadow, text, frame, labels, antialiasing, gradients, fine speckling, noise, shine or glow.
One single taller grounded copper ore sprite only.

### 煤矿

Use case: precise-object-edit.
Image 1 is the existing grounded COAL ORE sprite, which is the edit target.
Image 2 is the NEW TALLER grounded IRON ORE sprite, provided only as the height, proportions, silhouette and coarse pixel structure reference.

The user says the original grounded deposits look too low and small. Make image 1's exposed rock about a quarter TALLER, matching the substantial height of image 2. Adopt image 2's taller central rear slab and raised shoulders, solid angular top and middle faces, the same apparent width, baseline and overall grounded silhouette. This is an increase in the ROCK MASS height, not extra transparent canvas or adding a base. Preserve large square pixels; redraw the higher structure, never stretch the existing square pixels into rectangles. Peak around the top 5 to 8 percent of the canvas and ground contact around lower 92 to 94 percent, with clear margins. Keep a broad heavy natural outcrop, not a thin pillar.

The color and mineral identity come ONLY from image 1: matte charcoal-black coal seams with subtle dark blue-gray facets inside desaturated gray slate host rock. Black coal must remain readable against the slightly lighter gray host rock, never become red or green mineral. No rusty red iron from the reference.
Keep the same low-saturation semi-realistic RETRO PIXEL ART, very large hard square pixel clusters, sparse connected flat color areas, restrained 2 to 3 shade steps, same upper-left light and elevated orthographic top-down game view. Match image 2's canvas size and aspect ratio, pixel scale, full body height and bottom level so the mineral sprites are a coherent set.

Keep the successful rooted lower structure: solid rock faces and the mineral veins extend directly into a wide, almost level contact edge with a few shallow jagged steps. The unseen bottom is buried underground. Do not create a U-shaped stone apron, a crescent rim, horseshoe, supporting ring, display pedestal, arch, cavity, hanging rounded underside or any additional base. Height comes from the upper body.
Genuine transparent alpha background. No ground tile, soil island, grass, detached rocks, oval shadow, text, frame, labels, antialiasing, gradients, fine speckling, noise, shine or glow.
One single taller grounded coal ore sprite only.

</details>

## V2 贴地版

按本轮反馈，矿物改为半埋入地面的原位矿脉露头：去掉前景 U 形围边与圆弧底座，让岩体和矿脉直接延伸至较平、略有起伏的贴地底边。保留原有配色、粗像素、左上光源及透明背景。

| 文件 | 矿物 |
| --- | --- |
| `Mine_Iron_Grounded_Sample.png` | 铁矿脉 |
| `Mine_Copper_Grounded_Sample.png` | 铜矿脉（孔雀石） |
| `Mine_Coal_Grounded_Sample.png` | 煤矿脉 |

三张贴地版均保留生成原图 1402×1122 画布及 Alpha；旧版保留用于对比。仍为放大风格样稿，未替换游戏资源引用，未做游戏内验收。

生成方式：内置 image_gen 编辑原样稿；先调整铁矿底部结构，再以新铁矿作贴地结构参考，分别编辑铜矿和煤矿。

<details>
<summary>贴地版完整提示词</summary>

### 铁矿

Use case: precise-object-edit.
Edit the attached IRON ORE sprite for FlatWorld. The user likes its coarse pixel art and colors but says the bottom looks like a U-shaped base. Fix that specific structural problem.

Keep: the same low-saturation semi-realistic RETRO PIXEL ART, very large hard square pixel clusters, sparse flat-color shading, upper-left light, slate-gray stone, rusty red-brown iron ore, elevated orthographic top-down game view, source canvas aspect ratio, genuine transparent alpha background. Preserve the recognizable upper rock and ore vein identity.

Change the lower silhouette and lower rock structure substantially: show an IN-SITU ORE OUTCROP, the exposed upper part of a much larger bedrock mass buried underground. It grows directly out of the terrain. The bottom should be WIDE, SOLID and GROUNDED, terminating on an approximately horizontal but uneven jagged ground-contact line. Rock plates and rust-colored ore seams continue downward INTO that contact line and disappear underground. Give the contact edge three or four shallow irregular steps at slightly different heights, with broad stone faces reaching the bottom.

Remove the entire curved front apron / wraparound stone lip / rounded U-shaped supporting wall visible in the reference's bottom third. Replace it with two or three connected flat, solid rock facets that run down directly to the ground, with iron ore continuing to the lower edge. No bowl, no crescent, no horseshoe, no arch, no cavity, no detached display base, no ring of small rocks. No rounded underside or hanging lower edge; the unseen bottom is buried. Reduce the apparent height of the front face so it feels half-buried and heavy, not like a collectible stone sitting on top of a stand.

Transparent surroundings, no painted ground tile, no grass, no scenic soil island. At most a very narrow dark contact pixel seam directly touching the lowest rock pixels; do not draw an oval shadow or any separate platform. Keep simple large contiguous color blocks and the same coarse pixel size as the reference, not finer detail. One single sprite only, centered, similar overall width, generous transparent padding. No text, borders, labels, antialiasing, gradients, glow, polished 3D or tiny texture noise.
Priority: rock emerges straight out of the ground, with a broad nearly level rooted bottom, no U-shaped base.

### 铜矿

Use case: precise-object-edit.
Two reference images with separate roles.
Image 1 is the ORIGINAL COPPER ORE / MALACHITE deposit. Keep the original first image's desaturated moss-green to dark teal-green mineral seams, small dull brown-copper accents, and gray slate host rock. Do not introduce red iron ore. Let the green veins continue into the ground-contact edge. It is the edit target; preserve its mineral identity, colors, scale and coarse pixel art style.
Image 2 is a GROUNDED IRON OUTCROP with the corrected lower silhouette. It is the STRUCTURAL GROUND-CONTACT reference only, NOT a color reference.

Modify image 1 so it has the same grounded, buried-bedrock structure as image 2. Remove the whole U-shaped curved stone apron / crescent foreground rim / horseshoe-shaped supporting pedestal from the bottom third. Make the host rock and mineral seams form broad connected faces reaching directly to a WIDE, solid, nearly horizontal uneven ground-contact line. The rock is the exposed tip of a larger mass below the soil; bottom rock faces disappear underground. Make 3 or 4 very shallow jagged pixel steps along the contact line, like image 2. No round boulder underside, no display stand, no arch, no open cavity, no ring of rocks. Keep the upper rock's recognizable top planes and its upper-left lighting, adjust only what is needed in the lower faces to integrate naturally. Reduce the lower front face height; it should feel half-buried and firmly grounded.

Match image 2's canvas size, aspect ratio, overall width, baselines, big stepped pixel clusters and low-detail shading, so both sprites belong to one coherent game art set. Low-saturation semi-realistic RETRO PIXEL ART, LARGE HARD square pixels, sparse flat areas, limited color palette. No added small speckles, no fine texture, no gradients, no soft shading or antialiasing.
Genuine transparent alpha around the single solid sprite. No painted terrain tile, soil island, grass, oval shadow, detached particles, scene, text, border, label, glow or sparkles. Only at most a one-pixel-thin contact seam touching the rock.
One single sprite only: Copper ore emerging naturally out of the ground.

### 煤矿

Use case: precise-object-edit.
Two reference images with separate roles.
Image 1 is the ORIGINAL COAL deposit. Keep the original first image's matte black charcoal coal seams, subdued dark blue-gray top facets, and gray slate host rock. No red or green mineral. Let the dark coal seams continue into the ground-contact edge; distinguish them from the gray rock's shadow planes. It is the edit target; preserve its mineral identity, colors, scale and coarse pixel art style.
Image 2 is a GROUNDED IRON OUTCROP with the corrected lower silhouette. It is the STRUCTURAL GROUND-CONTACT reference only, NOT a color reference.

Modify image 1 so it has the same grounded, buried-bedrock structure as image 2. Remove the whole U-shaped curved stone apron / crescent foreground rim / horseshoe-shaped supporting pedestal from the bottom third. Make the host rock and mineral seams form broad connected faces reaching directly to a WIDE, solid, nearly horizontal uneven ground-contact line. The rock is the exposed tip of a larger mass below the soil; bottom rock faces disappear underground. Make 3 or 4 very shallow jagged pixel steps along the contact line, like image 2. No round boulder underside, no display stand, no arch, no open cavity, no ring of rocks. Keep the upper rock's recognizable top planes and its upper-left lighting, adjust only what is needed in the lower faces to integrate naturally. Reduce the lower front face height; it should feel half-buried and firmly grounded.

Match image 2's canvas size, aspect ratio, overall width, baselines, big stepped pixel clusters and low-detail shading, so both sprites belong to one coherent game art set. Low-saturation semi-realistic RETRO PIXEL ART, LARGE HARD square pixels, sparse flat areas, limited color palette. No added small speckles, no fine texture, no gradients, no soft shading or antialiasing.
Genuine transparent alpha around the single solid sprite. No painted terrain tile, soil island, grass, oval shadow, detached particles, scene, text, border, label, glow or sparkles. Only at most a one-pixel-thin contact seam touching the rock.
One single sprite only: Coal ore emerging naturally out of the ground.

</details>

## V1 原始样稿

三张放大预览原稿：铁矿脉、铜矿脉（孔雀石）、煤矿脉。

遵循 `.agents/skills/flatworld-art/SKILL.md`：低饱和、偏写实的复古像素风；大像素簇、少量色阶、左上光源、透明独立轮廓。矿物之间共用岩石母版，以矿脉颜色和材质区别。

| 文件 | 矿物表现 |
| --- | --- |
| `Mine_Iron_Sample.png` | 灰色母岩与铁锈红褐色矿斑 |
| `Mine_Copper_Sample.png` | 灰色母岩与暗绿孔雀石矿脉，少量暗铜色点缀 |
| `Mine_Coal_Sample.png` | 灰色母岩与黑灰煤层 |

这是看画风的样稿，未替换 `Mine_Iron`、`Mine_Copper`、`Mine_Coal` 的 `visual.spriteAddress`，未注册 Addressables，也未修改原图集。

三张原稿画布都是 1402×1122，保留生成工具的原始 PNG 和 Alpha。提示词中的 24×24 是低逻辑分辨率的造型目标，不表示文件已变为严格的 24×24 像素网格。正式进入游戏前仍需确定最终逻辑分辨率与显示大小。这次没有做游戏内验收。

样稿导入使用 Point、无 Mipmap、无压缩、中心 Pivot，PPU 1402（完整预览画布宽度为一世界单位），最大尺寸 2048，避免预览原图被缩小。

生成方式：内置 image_gen。先生成铁矿，再简化大像素块；铜矿和煤矿都以最终铁矿图作为风格参考。以下保存最终提示词，方便继续调整。

## 铁矿：最终简化提示词

Use case: style-transfer. This image is an iron ore sprite reference; REDRAW IT in substantially LOWER resolution and SIMPLER pixel art. The reference has too many tiny pixels and speckles. Make every pixel about THREE TIMES BIGGER. Desired actual DESIGN is a 24 by 24 grid enlarged as nearest-neighbor squares to a large transparent PNG, NOT a highly detailed image with a pixel filter. Every contour step should be one of those BIG square pixels. Stone should have just FOUR broad flat surfaces with 3 colors each. Use only about 12 solid colors for the ENTIRE sprite. Remove ALL isolated single-pixel speckling and small detail; replace textures with large connected flat pixel clusters of at least 3x3 logical pixels. Keep the natural squat irregular rock, desaturated slate-gray matrix, upper left gray-beige light, dark charcoal lower right side, and two thick rough embedded muted rusty red/brown iron seams. Simplify the red ore into just 4 to 6 big flat red/brown clusters; no sparkles or little dots. The goal is low-saturation semi-realistic RETRO GAME pixel art with BIG PIXELS and VERY SPARSE DETAIL, not a smooth vector, not a painting, not a polished 3D render. Maintain same raised top-down orthographic view, wider than tall, about 75 percent image width and 60 percent image height, fully centered with transparent padding. Hard square edges, flat shade areas. NO antialiasing, no blending, no gradient, no blur, no noise. Genuine alpha background, no backdrop, no ground shadow, no text. One single iron ore sprite. Strong priority: considerably fewer and BIGGER pixels than the reference.

## 铜矿：提示词

Use case: style-transfer. Input image is the approved STYLE / PIXEL SIZE / VIEWPOINT reference for a coherent FlatWorld mineral set, not an object that must stay iron. Create exactly ONE new COPPER ORE ROCK sprite in that SAME low-saturation semi-realistic coarse retro pixel art. Copper ore here is malachite-bearing raw ore: desaturated deep moss-green to muted teal-green ore veins embedded in slate-gray rock, with just two or three small dull brown-copper exposed accents. It must read as a natural mine deposit, NOT a metal ingot or a gem. Replace EVERY rusty red iron vein from the reference with these green copper mineral clusters. Give the copper sprite a slightly wider low silhouette and rearrange the top rock into THREE broad angular plates so it is its own deposit; keep the shared low mound structure, the shared apparent physical size, same elevated orthographic top-down view and same upper-left light. Match the reference's LARGE square pixel blocks exactly, strongly stepped square contour, sparse detail, broad connected flat patches, restrained roughly 12 solid colors, 2 to 3 tonal steps per material. Very low resolution as if drawn on about a 24 by 24 logical pixel grid, magnified with nearest neighbor; no little speckles or tiny details anywhere. Top faces warm gray-beige, middle gray slate, lower right shadows cool charcoal, ore subdued moss/teal green with no neon. Veins follow two big fractures and cover about a quarter of the sprite. Preserve the reference image's framing and canvas aspect ratio, centered subject width about 80 percent canvas, baseline at about 84 percent height. Real transparent alpha background, fully opaque rock with hard pixel edge. NO text, NO labels, no backdrop, no ground tile, no scenery, no particles, no detached rocks, no cast ground shadow, no glow, no star glints, no heavy pure-black border, no antialiasing, no blur, no gradients, no noise, no painterly rendering. One single copper deposit sprite only.

## 煤矿：提示词

Use case: style-transfer. Input image is the approved STYLE / PIXEL SIZE / VIEWPOINT reference for a coherent FlatWorld mineral set, not an object that must stay iron. Create exactly ONE new COAL DEPOSIT ROCK sprite in that SAME low-saturation semi-realistic coarse retro pixel art. Replace ALL rusty red iron ore in the reference with broad dark charcoal black coal seams and flat fractured coal blocks, embedded in desaturated slate-gray host rock. The coal is matte black with subtle dark blue-gray top facets, rough layered geology, no shiny gems, no bright metal. This is a raw coal outcrop. Give it a slightly lower broad squat silhouette and a visibly layered broken top with FOUR large connected faceted slabs. Keep the same apparent physical size as the iron reference and the same elevated orthographic top-down view with short front face, same upper-left light, and shared low mound language. Coal seam should be two broad nearly horizontal fractured bands so the mineral reads by both material and shape. About 45 percent dark coal, 55 percent gray host rock. Match the reference's LARGE square pixel blocks exactly, strongly stepped square contour, sparse detail, broad connected flat areas, restrained roughly 12 solid colors, 2 to 3 tonal steps per material. Very low resolution as if drawn on about a 24 by 24 logical pixel grid, nearest-neighbor enlarged; no speckles or fine texture. Gray-beige gray top rock planes, medium slate-gray middle, cool charcoal shadows; coal distinctly darker than the host, readable on light AND dark game terrain, avoid turning the entire sprite into an unreadable black lump. Preserve reference framing and canvas aspect ratio, centered subject about 80 percent canvas width, baseline about 84 percent height. Real transparent alpha background and hard opaque pixel edges. NO red or green ore, NO text, NO labels, no backdrop, no ground tile, no scenery, no particles, no detached pebbles, no cast ground shadow, no glow, no sparkles, no heavy pure-black outline, no antialiasing, no blur, no gradients, no noise, no painterly rendering. One single coal deposit sprite only.

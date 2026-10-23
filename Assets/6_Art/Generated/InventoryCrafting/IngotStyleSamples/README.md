# 矿物锭统一画风样例

2026-10-23。使用项目最新 `flatworld-art` 规则与内置 `image_gen` 生成。

| 样例 | 材质 |
| --- | --- |
| IronIngot_Icon_Sample.png | 冷灰铁锭 |
| CopperIngot_Icon_Sample.png | 红棕铜锭 |
| GoldIngot_Icon_Sample.png | 低饱和暗金锭 |

三张共用一张铁锭母版，统一铸锭轮廓、斜向视角、左上光源、明暗分区和主体占比，通过材质配色区分。先简化铁锭母版，再分别生成铜、金材质变体。

当前为 1254 × 1254 的透明底放大风格预览，不是严格对齐 24 × 24 或 32 × 32 网格的正式小尺寸源图。生成原图保留不重采样；正式使用前需整理逻辑像素网格、色阶和硬 Alpha。导入使用 Point、无 Mipmap、无压缩、中心 Pivot，PPU 1254，使整张画布对应 1 Unity 单位。仅新增独立样例，未修改物品配置或注册 Addressables。

本轮只制作风格样例，没有运行游戏测试；最终游戏内小尺寸可读性由用户确定。现有美术 Skill 已覆盖本次要求，无需增加新规则。

## 生成提示词

### 铁锭初稿

```text
Use case: stylized-concept.
Asset type: FlatWorld inventory item sprite style sample, MASTER for a coordinated set of iron, copper and gold ingots.
Primary request: Create ONE plain cast IRON INGOT in low-saturation, semi-realistic RETRO PIXEL ART at a very low logical resolution, suitable for a 2D top-down survival sandbox. This is the shape and rendering master for the other metals.
Subject and geometry: one single heavy, squat, elongated trapezoidal cast-metal ingot. Realistic foundry bar proportions, length about 2.1 times width, thickness about half the width; top face slightly smaller than base, subtly bevelled corners. Show three clearly readable broad planes: top, long front side, small right end. The long axis rises gently from lower-left toward upper-right, about 25 degrees. Plain solid bar, no stamped symbols, no grooves, no decorative fittings.
Composition: exactly one centered ingot on a square transparent canvas; comfortable equal margins, silhouette occupies roughly 75% canvas width and 48% canvas height. No props, no floor or cast shadow, no border, no text.
Pixel treatment: think of a hand-drawn native 32 by 32 pixel item icon, shown greatly enlarged with nearest-neighbor scaling. Coarse uniform square pixels, clean stair-step outline, large hard pixel clusters, broad flat planes, very sparse details. NO additional fine subpixels. Use a restrained palette about 10 to 12 visible colors: one main midtone, one main shadow group and a small highlight group. Tone follows material with a 1-logical-pixel dark material-colored edge, never a thick black outline. No smoothing or dithering.
Material: dull cast iron, muted cool charcoal-to-steel-gray with only a slight blue cast, medium-value top face, darker front face, deepest shade on the small right end. One short subdued pale-gray highlight on the upper-left bevel; a couple of broad shallow irregularities suggest casting, but no granular noise, rust, sparkle, oily gloss, or white specular streaks.
Lighting: single consistent upper-left light; natural believable volume via few clear pixel-value planes. Restrained and grounded, never cute, inflated, gem-like or photorealistic.
Background: genuine transparent alpha outside the solid sprite, no visible checkerboard painted into the image. Opaque hard-edged body.
Output: one PNG image only, no comparison board, no labels. Prioritize very low pixel count and readable simple shape.
```

### 铁锭最终母版

输入为铁锭初稿；下述提示词用于压低细节并统一轮廓表现。

```text
Use case: precise-object-edit.
Edit target: the supplied iron-ingot sample. Make one targeted change: SIMPLIFY THE RENDERING into extremely chunky, crisp, low-resolution pixel art for FlatWorld. Preserve this ingot's overall silhouette, proportions, three-quarter view, diagonal orientation, muted iron identity and upper-left lighting. This will be the master sprite of a uniform three-metal set.
Required appearance: a clean primitive inventory sprite drawn on a 24 by 24 logical pixel grid and enlarged with nearest-neighbor. Each logical pixel must be a LARGE solid square. Outline diagonals are regular stair steps made from these large squares. Use about SEVEN solid opaque colors in total: dark colored perimeter, shadow end, dark front, front midtone, top midtone, subdued bevel light, a tiny highlight. Three broad, almost flat planes. Remove every small pore, pit, fleck, scratch, speck, noisy texture and mottled patch. Keep just ONE small block of cast variation on the top.
Edges: hard completely opaque sprite pixels or fully transparent background only. Remove ALL blur, glow, haze, outer shadow, semitransparent fringe, painterly shading and lighting gradients. No antialiasing. Absolutely no color fog outside the object.
Composition: a SQUARE 1:1 canvas, one centered squat ingot, balanced transparent padding on all four sides. No ground, no shadow, no text, no panels, no checkerboard painted into image. A simple low-saturation semi-realistic retro PIXEL-ART game object, never a textured 3D render.
```

### 铜锭

输入为本目录铁锭最终母版。

```text
Use case: precise-object-edit.
The supplied iron-ingot PNG is the MASTER SPRITE for a coordinated FlatWorld inventory-icon set. Make a MATERIAL COLOR VARIANT, not a new drawing.
CRITICAL INVARIANTS: preserve the entire exact pixel silhouette, every stair-step edge, squat cast-bar shape, proportions, perspective, diagonal angle, flat top and side planes, edge thickness, upper-left light direction, highlight positions, tiny single top-face casting mark, canvas dimensions, sprite scale, position and transparent padding. It must visibly be the exact same ingot recolored. Keep the coarse low logical pixel count, large square pixels and sparse details. Do not introduce any new texture, pits, fine pixel noise, stripes, stamped symbols, props, lights or extra objects. Do not smooth or round the pixelated outline.
Style: low-saturation, semi-realistic retro PIXEL ART; restrained muted earthy palette; broad opaque color planes and a tiny subdued highlight. No bright white glare, golden glow, glitter, cute toy rendering or shiny 3D finish.
Background: preserve genuine transparency; absolutely no black painted background, cast shadow, blur, halo, haze, text, labels, ground or watermark.
Output one transparent square PNG with the same composition as the master.
Material change: Change ONLY the iron material colors to COPPER: muted reddish-brown and warm dusty copper midtones, subdued peach-copper bevel light, darker brown side and end. Suggested family: top #A57B62, front #795642, darkest #45382F, bevel #C7A086. Copper should read as warm red-brown metal, not orange plastic. Keep realistic restrained metal reflectivity and the exact same grayscale shading layout.
```

### 金锭

输入为本目录铁锭最终母版。

```text
Use case: precise-object-edit.
The supplied iron-ingot PNG is the MASTER SPRITE for a coordinated FlatWorld inventory-icon set. Make a MATERIAL COLOR VARIANT, not a new drawing.
CRITICAL INVARIANTS: preserve the entire exact pixel silhouette, every stair-step edge, squat cast-bar shape, proportions, perspective, diagonal angle, flat top and side planes, edge thickness, upper-left light direction, highlight positions, tiny single top-face casting mark, canvas dimensions, sprite scale, position and transparent padding. It must visibly be the exact same ingot recolored. Keep the coarse low logical pixel count, large square pixels and sparse details. Do not introduce any new texture, pits, fine pixel noise, stripes, stamped symbols, props, lights or extra objects. Do not smooth or round the pixelated outline.
Style: low-saturation, semi-realistic retro PIXEL ART; restrained muted earthy palette; broad opaque color planes and a tiny subdued highlight. No bright white glare, golden glow, glitter, cute toy rendering or shiny 3D finish.
Background: preserve genuine transparency; absolutely no black painted background, cast shadow, blur, halo, haze, text, labels, ground or watermark.
Output one transparent square PNG with the same composition as the master.
Material change: Change ONLY the iron material colors to GOLD: muted antique ochre and champagne-gold midtones, subdued pale warm-gold bevel light, darker olive-brown side and end. Suggested family: top #AC9660, front #806C3D, darkest #4A412C, bevel #D3BF86. Clearly valuable gold but low saturation, not neon yellow, orange or brass-green. Keep realistic restrained metal reflectivity and the exact same grayscale shading layout.
```

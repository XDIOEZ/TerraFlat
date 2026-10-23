# 工具样例绘图提示词

生成方式：内置 `image_gen`，三件独立图片均请求 `transparent_background: true`。本文件记录实际提交的提示词，不表示所有数值约束都已作为生产 Sprite 验证。

## 燧石斧（Axe_Flint）

```text
Use case: stylized-concept.
Asset type: FlatWorld game tool sprite STYLE SAMPLE, a single transparent inventory / handheld tool icon.
Style: low-saturation, semi-realistic RETRO PIXEL ART with VERY LOW logical resolution, approximating a 48 by 48 pixel game sprite enlarged with nearest-neighbor to a square image. Use large, clearly visible hard square pixel clusters throughout, including the object interior. Restrained 12 to 18 color palette, 2 to 3 shades per material, readable functional silhouette, natural believable construction and proportions. Sparse purposeful details. This is simple game pixel art, not a detailed illustration with a pixelated outline.
Composition: ONE isolated tool only. Matching family design across all samples: worn brown wooden handle running diagonally from lower-left to upper-right, tool head at upper-right, the whole tool comfortably inside the square canvas with generous transparent margins. Center the whole silhouette. Almost flat side view with just enough single-pixel thickness cues to describe construction. No dramatic perspective. Shared small-sprite pixel scale.
Lighting: from upper left, one midtone, one shadow band, a few hard pixel highlights. Desaturated earthy brown handle, believable material colors.
Background: genuinely transparent alpha, zero backdrop, no floor, no cast shadow, no checkerboard printed into the image.
Constraints: a single complete self-contained tool, no hands, no character, no ground, no decorative particles, no frame or typography.
Avoid: high pixel density, tiny noisy texture, soft gradients, antialiasing, blurred edges, glowing outlines, 3D render, glossy plastic, photorealism, cartoon toy proportions, saturated candy colors, thick black border, extra objects, words, watermark.
Subject: one practical prehistoric FLINT AXE. A credible asymmetrical chipped grey flint axe head lashed firmly across the end of a short, slightly irregular wooden haft with a few broad dark tan rawhide binding bands. The stone head has a broad wedge-shaped cutting edge and only 3 or 4 large knapped facets, no metal. Keep the short axe handle and axe-blade silhouette instantly readable. Stone should be matte muted cool grey, rawhide earthy tan, wood grey brown. Do not turn it into a pickaxe, spear or fantasy battle axe. Pixel count and detail must remain deliberately low.
```

## 青铜镐（Pickaxe_Bronze）

```text
Use case: stylized-concept.
Asset type: FlatWorld game tool sprite STYLE SAMPLE, a single transparent inventory / handheld tool icon.
Style: low-saturation, semi-realistic RETRO PIXEL ART with VERY LOW logical resolution, approximating a 48 by 48 pixel game sprite enlarged with nearest-neighbor to a square image. Use large, clearly visible hard square pixel clusters throughout, including the object interior. Restrained 12 to 18 color palette, 2 to 3 shades per material, readable functional silhouette, natural believable construction and proportions. Sparse purposeful details. This is simple game pixel art, not a detailed illustration with a pixelated outline.
Composition: ONE isolated tool only. Matching family design across all samples: worn brown wooden handle running diagonally from lower-left to upper-right, tool head at upper-right, the whole tool comfortably inside the square canvas with generous transparent margins. Center the whole silhouette. Almost flat side view with just enough single-pixel thickness cues to describe construction. No dramatic perspective. Shared small-sprite pixel scale.
Lighting: from upper left, one midtone, one shadow band, a few hard pixel highlights. Desaturated earthy brown handle, believable material colors.
Background: genuinely transparent alpha, zero backdrop, no floor, no cast shadow, no checkerboard printed into the image.
Constraints: a single complete self-contained tool, no hands, no character, no ground, no decorative particles, no frame or typography.
Avoid: high pixel density, tiny noisy texture, soft gradients, antialiasing, blurred edges, glowing outlines, 3D render, glossy plastic, photorealism, cartoon toy proportions, saturated candy colors, thick black border, extra objects, words, watermark.
Subject: one functional BRONZE PICKAXE. A long slim practical wooden handle with a simple one-piece dull cast bronze head mounted across the top. The broad arcing pick head has TWO tapered down-curved pick points extending clearly to either side of the handle socket; recognizable mining pickaxe geometry, not an axe. Bronze is muted ochre brown with one subdued warm pale-metal cutting-edge highlight, a couple large dark patina clusters, no neon green verdigris. Small dark collar fixes the bronze head to the worn wood. Head casting is sturdy and plain with a visible central socket. No fantasy decoration. Pixel count and detail must remain deliberately low.
```

## 铁铲（Shovel_Iron）

```text
Use case: stylized-concept.
Asset type: FlatWorld game tool sprite STYLE SAMPLE, a single transparent inventory / handheld tool icon.
Style: low-saturation, semi-realistic RETRO PIXEL ART with VERY LOW logical resolution, approximating a 48 by 48 pixel game sprite enlarged with nearest-neighbor to a square image. Use large, clearly visible hard square pixel clusters throughout, including the object interior. Restrained 12 to 18 color palette, 2 to 3 shades per material, readable functional silhouette, natural believable construction and proportions. Sparse purposeful details. This is simple game pixel art, not a detailed illustration with a pixelated outline.
Composition: ONE isolated tool only. Matching family design across all samples: worn brown wooden handle running diagonally from lower-left to upper-right, tool head at upper-right, the whole tool comfortably inside the square canvas with generous transparent margins. Center the whole silhouette. Almost flat side view with just enough single-pixel thickness cues to describe construction. No dramatic perspective. Shared small-sprite pixel scale.
Lighting: from upper left, one midtone, one shadow band, a few hard pixel highlights. Desaturated earthy brown handle, believable material colors.
Background: genuinely transparent alpha, zero backdrop, no floor, no cast shadow, no checkerboard printed into the image.
Constraints: a single complete self-contained tool, no hands, no character, no ground, no decorative particles, no frame or typography.
Avoid: high pixel density, tiny noisy texture, soft gradients, antialiasing, blurred edges, glowing outlines, 3D render, glossy plastic, photorealism, cartoon toy proportions, saturated candy colors, thick black border, extra objects, words, watermark.
Subject: one practical forged IRON SHOVEL / SPADE. A long worn brown straight wooden shaft ending at upper-right in a broad symmetrical pointed spade blade, with a short credible metal socket wrapping around the shaft. The blade is a flat, muted blue-grey forged iron plane, a few large darker hammer / oxidation marks and a narrow lighter hard-pixel working edge. Simple wooden butt, no D-shaped plastic grip. Shaft and blade must form one clear line; shovel blade has visible shoulders, a broad working face and a shallow pointed lower digging edge. A believable early hand-made farming shovel, not a spear or a narrow trowel. Pixel count and detail must remain deliberately low.
```

## 铁铲简化重绘

编辑目标为铁铲初稿；燧石斧和青铜镐只作风格参考。最终收录此轮结果，未收录铁铲初稿。

```text
Use case: style-transfer. Image 1 is the EDIT TARGET, a single iron shovel transparent sprite. Images 2 and 3 are STYLE REFERENCES ONLY (axe and pickaxe), never additional output subjects.
Edit ONLY the iron shovel from image 1. Preserve its exact functional identity, complete diagonal silhouette, overall proportions, muted grey forged-iron material, worn brown wooden shaft, true transparent background, head at upper-right and shaft butt at lower-left.
CRITICAL CHANGE: make the shovel much COARSER, SIMPLER retro pixel art to match the visible square pixel size of the axe and pickaxe references. Enlarge logical pixel clusters to about TWO TIMES their present size. Reconstruct the shovel as though drawn at only 48 x 48 logical pixels then enlarged with nearest-neighbor. Remove at least 75% of the tiny blade texture, pinprick highlights and shaft noise; use a single broad dark iron face, a broad mid grey face, a narrow sparse light grey working edge and just TWO large oxide patches. Wood is 3 broad brown bands, no tiny texture; socket is one simple dark cluster with one light edge. Keep left-upper lighting.
Output only ONE finished iron shovel, on real alpha transparency. No axe, no pickaxe, no typography, no scene, no cast shadow, no backdrop, no printed checkerboard. No soft shading, no antialiasing, no photorealism, no dense pixels. Match the style of reference sprites with large hard square pixel clusters and sparse restrained colors.
```

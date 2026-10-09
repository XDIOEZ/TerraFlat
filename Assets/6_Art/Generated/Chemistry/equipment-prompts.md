# 化学装备正式美术

制作方式：内置 ImageGen；每项独立 PNG。所有成品原样复制，保留生成的 RGBA、尺寸和 Alpha；没有裁剪、缩放、量化或本地像素修改。

实际查看的项目参考：

- `Assets/6_Art/Items/Equipment/Iron Equipment/Iron Helmet.png`
- `Assets/6_Art/Items/Equipment/Iron Equipment/Iron Chestplate.png`
- `Assets/6_Art/Generated/WaterVessel/IronBucket/IronBucket_Icon.png`

宇航服五件共用暖米白布料、炭黑密封圈、暗青局部面板和铜色扣具。铁气罐是灰铁加铜色 T 阀；钢气罐是蓝灰筒身、双强化箍和保护提环。

## 原始文件读取结果

边界均为 PNG 左上角坐标 `left, top, right, bottom`，右边和下边不包含在内。极低 Alpha 残边会增大非零边界，可见主体边界按 Alpha ≥ 128 读取。可见 RGB 数为原始高分辨率输出的统计，并非设计调色板数量；原图包含半透明像素，不能当成硬 Alpha 的低分辨率像素表。

| ID | PNG 尺寸 | Alpha 非零边界 | Alpha ≥ 128 主体边界 | 可见 RGB 数 |
| --- | --- | --- | --- | --- |
| PortableGasTank_Iron | 1254 × 1254 | 0, 28, 1218, 1158 | 406, 109, 847, 1154 | 13574 |
| PortableGasTank_Steel | 1254 × 1254 | 0, 31, 1214, 1230 | 354, 148, 901, 1135 | 22309 |
| Spacesuit_Head | 1244 × 1264 | 103, 53, 1143, 1246 | 110, 99, 1137, 1184 | 25133 |
| Spacesuit_Torso | 1312 × 1199 | 31, 27, 1264, 1157 | 56, 159, 1257, 1028 | 23445 |
| Spacesuit_Hands | 1265 × 1244 | 0, 45, 1249, 1220 | 98, 219, 1167, 1015 | 17015 |
| Spacesuit_Legs | 1117 × 1408 | 0, 17, 1081, 1400 | 232, 155, 885, 1231 | 14950 |
| Spacesuit_Feet | 1390 × 1132 | 41, 35, 1321, 1132 | 193, 190, 1197, 959 | 11849 |

所有 PNG 位于 `Assets/6_Art/Generated/Chemistry/<ID>/<ID>_Icon.png`。本说明只记录美术生成；Unity 导入、引用替换和游戏显示由集成方处理。

## 最终提示词

### PortableGasTank_Iron

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Primary request: create ONE compact 4-liter portable IRON gas cylinder. It is a handheld upright pressure vessel, not a large building tank.
Scene/backdrop: genuinely transparent background, no floor or shadow.
Subject: one stout vertical grey iron cylinder with rounded stepped shoulders, a thick short neck, a small warm copper valve and T-shaped copper valve handle at the top. One narrow iron foot ring at the bottom. The cylinder's plain mid-grey body has a large top-left light-grey highlight plane, dark charcoal right plane, and two simple large welded seams. No hose, no gauge, no extra item.
Style/medium: authentic chunky hard-block FlatWorld pixel art; strong compact silhouette and warm charcoal outline. Coarse squares and stepped contours comparable to project iron equipment and the IronBucket item. Approximately 64 logical pixels of detail per object; limited palette, large grouped square pixels, one shadow and one highlight per major material. Keep logical pixel clusters visibly large in high resolution.
Composition/framing: centered complete upright icon, front three-quarter view with a subtly visible top. Object spans around 72 percent of square canvas height, enough transparent padding at every side. Object width about 38 percent of canvas. Preserve silhouette and all valve parts.
Constraints: truly transparent alpha background; no text, numbers, logo, watermark, border, icon card, ground, cast shadow, glow, gradient, anti-aliasing, noise, realistic metal texture, thin lines, duplicate objects or 3D render. Colours are grey iron and warm copper only. Do not depict a fire extinguisher.
```

### PortableGasTank_Steel

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Primary request: create ONE compact 4-liter portable STEEL gas cylinder, a handheld upright pressure vessel rather than a building tank.
Scene/backdrop: genuinely transparent background, no floor or shadow.
Subject: one squat broad vertical dark blue-grey steel cylinder with stepped rounded shoulders. Two thick reinforcing light steel horizontal hoops circle the upper and lower body. A low bronze valve neck, small dark teal lever handle, and a robust rectangular dark steel protective carry loop above the valve form a distinctive silhouette. A flat steel foot ring at the bottom. No hose, gauge, label or extra item.
Style/medium: authentic chunky hard-block FlatWorld pixel art; strong compact silhouette and warm charcoal outline like the project iron equipment and IronBucket icon. Approximately 64 logical pixels of detail per object; limited muted palette, large grouped square pixels, one shadow and one highlight per material, top-left light. Coarse stepped edges visibly remain pixels at high resolution.
Composition/framing: one centered complete upright cylinder, front three-quarter view with subtly visible top; around 74 percent of square canvas height and 45 percent width with transparent padding. All handles and foot visible.
Constraints: actual transparent alpha; no text, digits, logo, watermark, border, card, ground, cast shadow, glow, gradient, anti-aliasing, noise, realistic metal texture, thin lines, duplicates or 3D render. Steel is desaturated blue-grey, darker than iron, with two clear reinforcing hoops and a steel carry loop. Not a fire extinguisher.
```

### Spacesuit_Head

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Primary request: create ONE standalone sealed spacesuit HELMET, a wearable equipment item only, no character.
Scene/backdrop: genuinely transparent background, no floor or shadow.
Subject: compact rounded helmet with a large simple dark teal-blue opaque visor, thick warm ivory/off-white fabric-padded hard shell, dark charcoal visor gasket and neck seal, small warm copper latches at the lower sides. Visor uses two large teal planes and a small top-left pale blue block highlight, with no face, reflections of scenery or stars. A short wide neck collar is part of the helmet.
Style/medium: chunky authentic hard-block FlatWorld pixel art for the same category as project Iron Helmet and Iron Chestplate. Match strong silhouettes, limited grouped colours, warm dark charcoal outlines and simple top-left lighting, around 64 logical pixels of detail per object. Use coarse square pixel clusters and clearly stepped edges rather than a smooth illustration.
Set identity: this is the helmet of a five-piece spacesuit set. Shared palette: warm ivory cloth #D6CCB3 with light #F0E7CD, muted beige shadows #9C947F, charcoal seals #333738, dark muted teal #29474D and copper clasps #A76E43. No bright white, neon or saturated colours.
Composition/framing: one centered complete helmet, front view with slightly visible top; strong domed silhouette and visor, around 66 percent canvas width and 67 percent height; transparent padding all around.
Constraints: actual transparent alpha background; no torso, wearer, mannequin, detached components, text, lettering, numbers, logo, watermark, border, icon card, ground, cast shadow, glow, gradients, anti-aliasing, photo texture, noise, thin lines or 3D render.
```

### Spacesuit_Torso

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Scene/backdrop: genuinely transparent alpha background, no floor or shadow.
Style/medium: authentic chunky hard-block FlatWorld pixel art, same category as the project's Iron Helmet and Iron Chestplate equipment. Compact strong silhouette, grouped hard square pixel clusters, warm charcoal stepped outline, simple top-left light with one large highlight and one shadow per material, around 64 logical pixels of detail per object even in a high-resolution PNG. Large clean readable forms; no tiny decorative detail.
Set identity: a five-piece spacesuit set with a warm ivory/off-white padded fabric outer layer (#D6CCB3, highlight #F0E7CD, muted beige shadows #9C947F), charcoal sealing rings and joint fabric (#333738), occasional small muted dark teal panels (#29474D), small copper clasps (#A76E43). Match these colours across the complete set.
Constraints: one equipment icon with complete silhouette, centered with ample transparent padding, no wearer, character, mannequin, skin, extra equipment, text, numbers, symbols, lettering, logo, watermark, border, card, background, ground, cast shadow, glow, gradients, anti-aliasing, thin lines, high-frequency noise, photographic texture or 3D render. Do not show a whole spacesuit.
Primary request: create ONE standalone sealed spacesuit UPPER BODY JACKET as an inventory equipment icon.
Subject: front-facing chunky warm ivory padded pressure jacket with short thick sleeves angled mildly downwards and sealed charcoal cuffs, a wide dark charcoal neck ring with an empty dark opening, simple central segmented dark seal down the front, a single small muted teal chest module with two tiny copper clasps, and a charcoal waist sealing ring. Small copper closure tabs at the front waist. The interior neck hole is dark, no helmet. No gloves or lower body.
Composition/framing: one centered complete detached jacket, front view, squat compact shoulder silhouette, around 70 percent canvas width and 68 percent height; entire sleeves, neck collar and waist visible.
```

### Spacesuit_Hands

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Scene/backdrop: genuinely transparent alpha background, no floor or shadow.
Style/medium: authentic chunky hard-block FlatWorld pixel art, same category as the project's Iron Helmet and Iron Chestplate equipment. Compact strong silhouette, grouped hard square pixel clusters, warm charcoal stepped outline, simple top-left light with one large highlight and one shadow per material, around 64 logical pixels of detail per object even in a high-resolution PNG. Large clean readable forms; no tiny decorative detail.
Set identity: a five-piece spacesuit set with a warm ivory/off-white padded fabric outer layer (#D6CCB3, highlight #F0E7CD, muted beige shadows #9C947F), charcoal sealing rings and joint fabric (#333738), occasional small muted dark teal panels (#29474D), small copper clasps (#A76E43). Match these colours across the complete set.
Constraints: one equipment icon with complete silhouette, centered with ample transparent padding, no wearer, character, mannequin, skin, extra equipment, text, numbers, symbols, lettering, logo, watermark, border, card, background, ground, cast shadow, glow, gradients, anti-aliasing, thin lines, high-frequency noise, photographic texture or 3D render. Do not show a whole spacesuit.
Primary request: create ONE inventory equipment icon comprising a matching PAIR of sealed spacesuit GLOVES.
Subject: exactly two warm ivory thick padded gloves placed alongside each other with a narrow transparent gap, wrists up and fingers down. Each glove has one simplified chunky thumb pointing inward, rounded stepped mitten-like grouped fingers, a charcoal sealed wrist cuff with one small copper fastener and a single dark charcoal knuckle inset. Both gloves clearly use the same warm ivory suit fabric and charcoal seal palette. No arm or hand inside.
Composition/framing: the pair together centered as one equipment icon, front view, complete gloves, around 65 percent total canvas width and 64 percent height, symmetrical simple shapes, enough transparent padding.
```

### Spacesuit_Legs

```text
Use case: stylized-concept
Asset type: final FlatWorld 2D pixel-art inventory equipment icon, transparent PNG.
Scene/backdrop: genuinely transparent alpha background, no floor or shadow.
Style/medium: authentic chunky hard-block FlatWorld pixel art, same category as the project's Iron Helmet and Iron Chestplate equipment. Compact strong silhouette, grouped hard square pixel clusters, warm charcoal stepped outline, simple top-left light with one large highlight and one shadow per material, around 64 logical pixels of detail per object even in a high-resolution PNG. Large clean readable forms; no tiny decorative detail.
Set identity: a five-piece spacesuit set with a warm ivory/off-white padded fabric outer layer (#D6CCB3, highlight #F0E7CD, muted beige shadows #9C947F), charcoal sealing rings and joint fabric (#333738), occasional small muted dark teal panels (#29474D), small copper clasps (#A76E43). Match these colours across the complete set.
Constraints: one equipment icon with complete silhouette, centered with ample transparent padding, no wearer, character, mannequin, skin, extra equipment, text, numbers, symbols, lettering, logo, watermark, border, card, background, ground, cast shadow, glow, gradients, anti-aliasing, thin lines, high-frequency noise, photographic texture or 3D render. Do not show a whole spacesuit.
Primary request: create ONE standalone sealed spacesuit PANTS equipment inventory icon.
Subject: one pair of chunky padded warm ivory pressure trousers, two complete legs connected at the waist. Wide charcoal sealed waistband with one copper rectangular clasp, dark charcoal stretchy inner hip/crotch section, broad warm ivory thighs, simple charcoal oval knee reinforcements drawn as stepped pixel blocks, and short charcoal ankle seals. A small muted teal tab near one hip. No torso, shoes, belt pockets or wearer.
Composition/framing: one centered complete pair of pants, front view, compact broad waist and two straight separated legs; around 59 percent canvas width and 74 percent height, transparent padding, both cuffs visible.
```

### Spacesuit_Feet

```text
Make a new original simple flat 2D pixel-art inventory icon for a sandbox game. Draw ONE matched PAIR of short spacesuit boots, toes toward the viewer, side by side, fully visible, centered with transparent padding on all sides.
Each boot has a warm ivory padded shaft and warm ivory broad toe, a chunky charcoal cuff around its empty ankle opening, a small copper clasp in the centre of a charcoal ankle strap, and a thick charcoal flat sole. Simple, compact stubby form. Shared palette is cream ivory #D6CCB3, light ivory #F0E7CD, muted beige #9C947F, charcoal #333738, warm copper #A76E43, with one small muted teal side tab #29474D.
2D flat Sprite pixel art only: HARD EDGE square pixels, coarse stepped silhouette, solid opaque colour blocks, limited palette, very few broad highlight and shadow blocks INSIDE THE BOOTS ONLY. Approximately 64 logical pixels across the pair. This is a game item Sprite, not a rendering or photograph.
Use real transparent background. There is NOTHING outside the stepped boot silhouettes, only fully transparent pixels. No grey mist around the boots. NO glow, NO halo, NO bloom, NO drop shadow, NO lighting backdrop, NO fuzzy outline, NO soft alpha, NO gradient, NO ground, NO characters, NO text, NO numbers, NO symbols, NO logos, NO extra accessories. The pair of opaque boots stands isolated with sharp charcoal pixel outlines and actual transparent background. Do not refer to or preserve any earlier image.
```

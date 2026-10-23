# 矿物锭斑驳纹理样例

2026-10-23，根据用户两张参考图调整。使用内置 image_gen，以铁锭为母版派生铜锭、金锭。

- 保持统一铸锭造型、斜向视角与左上光源。
- 恢复块状氧化暗斑、浅凹坑、铸造斑驳和少量边缘磕碰，避免低像素简化后只剩光滑大面。
- 三张文件分别为 IronIngot_Textured_Sample.png、CopperIngot_Textured_Sample.png、GoldIngot_Textured_Sample.png。
- 保留原始透明 PNG，铁锭画布 1613 × 975；这是放大风格预览，尚未制作严格统一像素网格的正式小尺寸源图。
- 独立样例；旧样例与正式物品引用保留。本轮未运行游戏测试。
- 当前美术 Skill 已允许硬边像素簇和可信材质，本次偏好通过提示词表达，无需扩充 Skill。

## 铁锭提示词

```text
Use case: precise-object-edit. Create a revised FlatWorld IRON INGOT sprite sample.
Input image 1: texture and material reference, APPROVED dirty cast-metal character. The user LIKES its patchy gritty oxidized surface and small shallow pits.
Input image 2: previous simplified sprite, shape and low pixel-count reference ONLY. The user finds its empty smooth planes and single centered square monotonous.
Request: preserve the compact diagonal cast-bar geometry, scale, three visible planes, upper-left light, low-saturation cool iron colors, consistent dark colored perimeter and readable coarse pixel silhouette of image 2; BRING BACK the irregular patchy cast-metal surface character from image 1.
Material texture must remain visible at low pixel resolution: about 8 to 12 irregular dark pixel-cluster patches across top and front, several broad darker casting-scale stains near the lower front edge, 3 or 4 shallow square-ish pits with a restrained lighter lip, a few small edge nicks. Varied asymmetric placement and varied cluster shapes, never evenly spaced dots. REMOVE the previous single lonely central square. Top remains mostly mid-gray, front darker; texture varies within each plane without obscuring its lighting. Patina and casting imperfections, not crumbling rock or excessive orange rust. Iron should look dense, used and slightly dirty.
Style: low-saturation semi-realistic retro pixel art, roughly 32 to 40 logical pixels across the ingot, large hard square pixel clusters, restrained palette about 12 to 16 tones. Keep coarse pixel art while preserving rough material character. Broad meaningful patches rather than fine noise or uniformly smooth flat fills. No photographic grain, dithering, smooth gradients, blur, glow, airbrushed haze or polished 3D look.
Layout: one single centered ingot, consistent silhouette and diagonal orientation, square canvas with balanced transparent padding, suitable as inventory icon. Genuine transparent alpha outside the opaque object; no painted checkerboard, background, floor, cast shadow, frame, labels or text.
Make this a new textured master for a coordinated iron/copper/gold set.
```

## 铜锭提示词

```text
Use case: precise-object-edit. The supplied PNG is the textured master ingot for a coordinated FlatWorld game sprite set. Change ONLY the metal palette. Keep exactly the same silhouette, pixel size, composition, canvas proportions, scale, position, diagonal angle, perspective, upper-left lighting and chunky casting texture layout. Crucially PRESERVE the dirty patchy metal feeling: every irregular oxide-like patch, shallow pit with its tiny lighter lip, mottled casting scale, dark lower edge, little edge nick. Do not smooth away the texture, do not replace it with clean flat planes. Low-saturation semi-realistic retro pixel art, hard square pixel clusters, few restrained tones, no fine grain, no glow, no blur or new details. Preserve genuinely transparent background, with no floor, ground shadow, text, labels or painted checkerboard. One single ingot. Recolor to weathered COPPER: muted dusty reddish-copper top, deep reddish-brown front and shadow end, subdued pale copper bevel. Cast grime and pits are darker brown and umber, same texture positions and contrast as the master. Warm red-brown metal, not bright orange and not green verdigris.
```

## 金锭提示词

```text
Use case: precise-object-edit. The supplied PNG is the textured master ingot for a coordinated FlatWorld game sprite set. Change ONLY the metal palette. Keep exactly the same silhouette, pixel size, composition, canvas proportions, scale, position, diagonal angle, perspective, upper-left lighting and chunky casting texture layout. Crucially PRESERVE the dirty patchy metal feeling: every irregular oxide-like patch, shallow pit with its tiny lighter lip, mottled casting scale, dark lower edge, little edge nick. Do not smooth away the texture, do not replace it with clean flat planes. Low-saturation semi-realistic retro pixel art, hard square pixel clusters, few restrained tones, no fine grain, no glow, no blur or new details. Preserve genuinely transparent background, with no floor, ground shadow, text, labels or painted checkerboard. One single ingot. Recolor to slightly dirty GOLD: muted antique ochre/champagne top, dark olive-brown shadow planes, restrained pale-gold bevel. Keep casting pits and dark smudges visibly present but mineral dirt and casting residue, not iron rust. Same texture layout as master, dark patches in low-saturation umber/ochre. Metallic gold reads clearly, never neon yellow or orange.
```

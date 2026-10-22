# 换装角色母版拆解图

- 文件：`EquipmentBase_Exploded_Concept.png`，1536 × 1024，透明 PNG。
- 用途：角色换装部位设计源图；左侧完整外形，右侧拆解。当前未接入角色、切片或动画系统。
- 这里的“母角色”指换装基础模板。参考用户提供的紫色晶石角色图的像素观感和比例，创建原创基础角色。
- 部件：头发、头、胸腹、骨盆，以及左右上臂、前臂、手掌、大腿、小腿、脚，共 16 件。
- 身体用灰色底衣、底裤，后续装备覆盖对应部位。正式使用需以项目角色的最终帧尺寸统一像素网格，切片并设置关节锚点、遮挡层级；本图不保证直接拼装时逐像素严丝合缝。
- 本次只新增美术设计源图；未执行游戏运行验证，相关 Skill 无需调整。

## 生成记录

使用内置 image_gen，以用户附件作风格参考；第二轮以第一轮图及附件共同作参考。保留最终图，不保存废弃版本。

### 初始提示词

```text
Use case: stylized-concept.
Create ONE transparent PNG pixel-art modular character base / paper-doll exploded parts sheet for a top-down 2D sandbox RPG.
Input image 1 is STYLE AND PROPORTION REFERENCE ONLY: use its chunky small RPG humanoids, warm muted colors, dark brown-black pixel outline, large head and short stout legs. Do not reproduce any existing character or equipment. The user's "母角色" means a reusable base template, not a female character.
Canvas landscape, approximately 1536x1024. A clean production art sheet with NO TEXT, NO labels, NO lines, NO panels, NO grid, NO background, real alpha transparency.
At LEFT (one third width): ONE fully assembled original human adventurer BASE, neutral standing idle, slightly three-quarter front facing viewer's left as in reference, both hands empty, short dark brown hair, peach skin, simple desaturated gray sleeveless undershirt and dark neutral gray undershorts, bare feet. No armor, weapons, belt, bag, accessories. Friendly neutral face, large head, tiny eyes, adult chibi proportions. Compact silhouette with approximately 32x48 logical pixels, enlarged uniformly with crisp square nearest-neighbor-like pixel blocks (roughly 10 display pixels per logical pixel). Restrained 16-24 color palette, hard pixel clusters, no fine texture. Light from upper left.
At RIGHT (two thirds width): an exploded paper-doll assembly of the EXACT SAME character at the EXACT SAME scale and view as the assembled character. All individual sprites separated by abundant empty alpha gaps. Human anatomical arrangement: detachable hair cap above bald head, below that separate upper torso and pelvis/shorts, arms at each side, thighs below pelvis, shins below thighs, bare feet below shins. Each left/right body part must appear ONCE and be distinctly separate from its partner. Precisely these isolated component sprites: 1 hair cap, 1 bald head with face and ears, 1 chest/torso including short neck and undershirt, 1 pelvis wearing undershorts, 2 upper arms, 2 forearms, 2 hands, 2 thighs, 2 shins, 2 feet = 16 isolated pieces. No duplicate heads or loose extra limbs. Limb cut ends are simple neutral rounded joint overlap stubs; do not draw wounds, bones, gore, joints as mechanical balls, or cutaway anatomy.
The assembled figure and exploded parts must clearly belong to the SAME base, with matching shading, pixels, dimensions and pose. Keep right and left limbs distinct and aligned for equipment layering. Flat true pixel art, NOT smooth digital illustration, NOT vector, NOT 3D. All pixels opaque except the empty transparent background; no shadows cast onto a background, no glow, no blurred edge. No armor set previews.
```

### 最终修订提示词

```text
Edit this character base exploded sheet. Preserve its overall left assembled character / right exploded character layout, matching neutral gray undershirt and undershorts, original human identity, neutral pose and separate hair/head/torso/pelvis/upper arms/forearms/hands/thighs.
Make the pixel art genuinely MUCH LOWER RESOLUTION, as a compact 32x48-pixel RPG humanoid blown up with nearest-neighbor square pixels. Simplify aggressively: tiny single-pixel dot eyes without white sclera, no brows, no visible nose/mouth, very simple hair made of 3 large solid dark-brown pixel clusters, skin only 3 solid colors, cloth only 3 solid colors, outline 1 logical pixel dark brown. No rendered anatomy, no grainy fine detail, no gradients, no semitransparent shadows, no bloom or glow anywhere. Match the stout tiny medieval RPG pixel people in the second reference image. DO NOT add helmets, weapons or other equipment. 
Crucial precise correction: at the bottom of the exploded figure, both BARE FEET MUST BE DETACHED from the shins. The lowest four pieces must be 2 isolated shins in one row and 2 isolated feet clearly BELOW them in another row with large transparent gaps. No foot may touch a shin. All 16 separate parts must be present. Each detached piece stays the same size as its counterpart on the assembled figure. Keep generous transparent margins around every piece and all outer canvas edges.
Remove ALL the dark brown background haze visible in the source. Entire background must be TRUE TRANSPARENT ALPHA, including the spaces between the pieces. Only character sprite pixels have opacity. No text or markings.
```

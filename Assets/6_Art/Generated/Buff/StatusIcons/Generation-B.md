# Buff 状态图标生成记录 B

2026-10-18。使用内置 `image_gen.imagegen`，每张图独立调用并设置 `transparent_background: true`；未使用生成 CLI/API。绘制前实际查看项目现有辣椒、草药、草药敷剂和夹板图。正式 PNG 保留工具原始输出，没有本地缩放、量化或修改 Alpha。

## 正式文件与真实规格

本组全部为 1254×1254 RGBA 原始 PNG。单图 Sprite 名取英文文件名，中心 Pivot、Point、关闭 Mipmap、无压缩、Alpha Transparency；`spritePixelsToUnits=1254`，默认及平台 `maxTextureSize=128`，满足约 29 UI 像素消费并限制贴图内存。导入时缩小由 Unity 完成，原始 PNG 不变。

“32×32 逻辑像素”是生成提示词中的造型目标，不能当成源图实际像素数。只读统计确认有 Alpha 且四角为 0；实际颜色和半透明像素数量如下，没有声称固定色数或硬 Alpha。用户自行做游戏内验收，本组未操作 Unity、Play Mode 或运行游戏测试。

恢复图统一用灰色正面人形定位，暖肤色和米白绷带突出恢复部位，绿色十字表示恢复。左右按角色解剖学左右：正面看时，角色左侧在画面右边。

| Buff ID | 显示名 | PNG / Sprite 名 | 图案 | 实际可见颜色数 | 半透明像素数 |
| --- | --- | --- | --- | ---: | ---: |
| `速度1` | 速度1 | `SpeedBoost.png[SpeedBoost]` | 棕靴与绿色上箭头 | 34407 | 530311 |
| `背包超重` | 负重过高 | `Overweight.png[Overweight]` | 鼓胀背包与红色下箭头 | 47124 | 915434 |
| `潮湿` | 潮湿 | `Wet.png[Wet]` | 湿衣与水滴 | 27676 | 506596 |
| `core:night_vision` | 夜视 | `NightVision.png[NightVision]` | 绿色眼睛与冷色月牙 | 36454 | 462593 |
| `core:concussion` | 脑震荡 | `Concussion.png[Concussion]` | 包扎头部与旋转星星 | 34201 | 465398 |
| `core:leg_fracture` | 腿部骨折 | `LegFracture.png[LegFracture]` | 断裂腿骨与红色裂口 | 26107 | 300271 |
| `core:arm_fracture` | 手部骨折/脱臼 | `ArmFracture.png[ArmFracture]` | 弯曲手臂与断裂骨 | 23133 | 353587 |
| `core:body_recovery.head` | 头部固定恢复 | `BodyRecoveryHead.png[BodyRecoveryHead]` | 正面人形，头部包扎 | 17384 | 344516 |
| `core:body_recovery.chest` | 胸部固定恢复 | `BodyRecoveryChest.png[BodyRecoveryChest]` | 正面人形，胸部包扎 | 18131 | 297094 |
| `core:body_recovery.abdomen` | 腹部固定恢复 | `BodyRecoveryAbdomen.png[BodyRecoveryAbdomen]` | 正面人形，腹部包扎 | 16798 | 279254 |
| `core:body_recovery.pelvis` | 骨盆固定恢复 | `BodyRecoveryPelvis.png[BodyRecoveryPelvis]` | 正面人形，低位髋部包扎 | 19776 | 292186 |
| `core:body_recovery.lefthand` | 左手固定恢复 | `BodyRecoveryLeftHand.png[BodyRecoveryLeftHand]` | 正面人形，画面右手臂包扎 | 13972 | 329867 |
| `core:body_recovery.righthand` | 右手固定恢复 | `BodyRecoveryRightHand.png[BodyRecoveryRightHand]` | 正面人形，画面左手臂包扎 | 14211 | 367870 |
| `core:body_recovery.leftleg` | 左腿固定恢复 | `BodyRecoveryLeftLeg.png[BodyRecoveryLeftLeg]` | 正面人形，画面右腿包扎 | 16588 | 265173 |
| `core:body_recovery.rightleg` | 右腿固定恢复 | `BodyRecoveryRightLeg.png[BodyRecoveryRightLeg]` | 正面人形，画面左腿包扎 | 18865 | 322172 |
| `核聚变接触` | 核聚变接触 | `FusionContact.png[FusionContact]` | 白热核心与金橙恒星爆裂 | 30960 | 530483 |

## 原始生成提示词

每图完整提示词 = 公共前缀 + 一个空格 + 对应 Subject。恢复图的 Subject = 恢复通用主体 + 对应部位补充。新图均未传图片引用参数。

公共前缀：

```text
Use case: stylized-concept. Asset type: one transparent pixel-art Buff HUD icon for FlatWorld, a 2D top-down survival sandbox. Make ONE isolated sprite, square canvas, true transparent background. Strict chunky 32x32 LOGICAL PIXEL art, enlarged with nearest-neighbor appearance: every visible edge is a hard stair-step, each logical pixel is an equal square; dark charcoal outline about one logical pixel, limited 12-20 clearly separated flat colors, 2-3 shades per material, upper-left light. Match handmade retro inventory icons such as a tiny red chili, green herb and tan splint. Strong silhouette readable when displayed at 29 UI pixels. No text, numbers, letters, watermark, frame, card, circle badge background, checkerboard drawn into image, ground shadow, blur, glow, gradients, smooth vector curves, 3D render or antialiasing. Use the central 80% of canvas, leave clean transparent margins. Keep the main identification in upper-left and center so a UI stack-count badge can overlap lower-right.
```

恢复通用主体：

```text
Subject: a single simple FRONT-FACING full-body human medical mannequin, 22 logical pixels tall, round head, broad shoulders, clear narrow waist, short arms held slightly out away from body, two separated legs and flat feet. No hair, face, fingers or clothing detail. All inactive body areas are muted slate gray, with dark charcoal one-logical-pixel outline. Include ONE bright leaf-green medical plus as the recovery symbol in the upper-left side, flat hard-edge pixels and no glow.
```

### SpeedBoost

Subject：

```text
Subject: a single compact brown leather running boot viewed from the side, toe pointing left, warm tan cuff and one clear cream highlight. A large lime-green upward arrow rises beside the boot at upper-right; the arrow is a secondary symbol for faster movement. One simple boot and one bold arrow, no speed lines clutter.
```

### Overweight

Subject：

```text
Subject: one overstuffed brown adventurer backpack, front view, huge bulging olive/tan pack with a rolled pale blanket across top and taut broad straps; a bold red downward arrow at its upper-right tells heavy load slows movement. Clearly a backpack, not treasure or a suitcase. Keep the low center of gravity and strained bulging silhouette.
```

### Wet

Subject：

```text
Subject: a simple dark teal short-sleeved tunic made visibly wet with a large bright sky-blue water droplet in its center, plus two smaller blue drops above the shoulders. Distinct droplet silhouettes, simple dark-outlined shirt shape. Cool cyan and blue flat colors, a little muted cloth color. Immediately readable as being soaked.
```

### NightVision

Subject：

```text
Subject: one bold luminous-looking but NOT glowing lime-green eye with charcoal iris outline and a clean dark pupil, plus a pale cool-blue crescent moon above-left of the eye. The eye is large and friendly, a simple crisp almond silhouette with stair-step edges. Midnight teal shadow and green eye colors suggest seeing in darkness. No eyelashes, realism or scattered stars.
```

### Concussion

Subject：

```text
Subject: a compact cartoon front-facing head with short dark brown hair, tan skin, simple dot eyes shown dizzy and a pale bandage at the left temple. Above the head, two small flat golden-yellow four-point stars follow a single curved ochre arc, meaning dizziness after head injury. One clear head silhouette with dizzy stars; chunky and simple, not a detailed portrait.
```

### LegFracture

Subject：

```text
Subject: one bent lower human leg silhouette cropped above the knee, tan shin and a brown foot at bottom-left, with a central simplified ivory shin bone showing a very obvious jagged break. A single short red zigzag marks the crack. The leg bends awkwardly at the shin; serious fracture icon. Only one strong limb shape with a red injury mark, no blood splashes, no anatomy gore, no hand.
```

### ArmFracture

Subject：

```text
Subject: one simplified human arm bent in an L shape at the elbow, palm/mitten at upper-left and shoulder cropped at upper-right, warm tan skin. A central simple ivory forearm bone has an obvious jagged break marked by a short bold red zigzag; clear elbow bend and hand silhouette distinguish this from a leg fracture. No detailed fingers, no blood splashes or gore, no foot.
```

### BodyRecoveryHead

部位补充：

```text
The ONLY highlighted anatomical area is the HEAD, a large pale tan round head with thick ivory forehead bandage. Chest, abdomen, hips, arms, legs remain muted gray. Put the bandaged head large and clear near the top-center, green plus beside head. Meaning head is recovering. No red injury marks.
```

### BodyRecoveryChest

部位补充：

```text
The ONLY highlighted anatomical area is the UPPER CHEST, a broad pale tan trapezoid between shoulders and the mid-torso, wrapped with one thick ivory horizontal chest bandage just BELOW the neck. Head, belly below ribs, hips, arms, legs remain muted gray. The broad chest bandage and recovery plus are the main features. Meaning chest is recovering. No red injury marks.
```

### BodyRecoveryAbdomen

部位补充：

```text
The ONLY highlighted anatomical area is the ABDOMEN / BELLY at the narrow middle torso, clearly BELOW the chest and ABOVE the hips. This belly area is pale warm tan, wrapped with one thick ivory bandage around the stomach/navel. Head, shoulders, UPPER CHEST, arms, hips and legs remain gray. Green plus upper-left. The bandage is centered at waist height, NOT across the chest. Meaning abdomen recovering.
```

### BodyRecoveryPelvis

部位补充：

```text
The ONLY highlighted anatomical area is the PELVIS / HIPS, a wide pale tan hip shape at the bottom of the torso where the two upper legs begin. One thick ivory support bandage sits very LOW across BOTH HIP BONES, shaped like short briefs or a low hip belt. The abdomen ABOVE that belt, chest, head, arms and legs remain muted gray. Green plus upper-left. Make this bandaged hip noticeably lower and wider than a belly bandage. Meaning pelvis recovering.
```

### BodyRecoveryLeftHand

部位补充：

```text
The ONLY highlighted anatomical area is the patient's ANATOMICAL LEFT HAND AND FOREARM, which appears on the VIEWER'S RIGHT side of the front-facing body. Draw the whole LEFT lower arm/hand on SCREEN RIGHT in warm pale tan and thick ivory bandage; a small tan mitten hand at its end. The entire arm on SCREEN LEFT stays muted gray. Keep BOTH arms slightly angled outward so the two sides are unmistakable. Head, chest, belly, hips and both legs gray. Green recovery plus upper-left. No injury red marks. This is LEFT hand recovery, screen-right bandaged arm ONLY.
```

### BodyRecoveryRightHand

部位补充：

```text
The ONLY highlighted anatomical area is the patient's ANATOMICAL RIGHT HAND AND FOREARM, which appears on the VIEWER'S LEFT side of the front-facing body. Draw the whole RIGHT lower arm/hand on SCREEN LEFT in warm pale tan and thick ivory bandage; a small tan mitten hand at its end. The entire arm on SCREEN RIGHT stays muted gray. Keep BOTH arms slightly angled outward so the two sides are unmistakable. Head, chest, belly, hips and both legs gray. Green recovery plus upper-left. No injury red marks. This is RIGHT hand recovery, screen-left bandaged arm ONLY.
```

### BodyRecoveryLeftLeg

部位补充：

```text
The ONLY highlighted anatomical area is the patient's ANATOMICAL LEFT LEG, which appears on the VIEWER'S RIGHT side of the front-facing body. Draw the whole leg on SCREEN RIGHT warm pale tan with thick ivory bandage around both thigh and shin, and one simple tan foot. The entire leg on SCREEN LEFT stays muted gray. Head, chest, abdomen, hips and both arms remain gray. Keep the two legs clearly separated. Green recovery plus upper-left. No red injury marks. This is LEFT leg recovery, screen-right bandaged leg ONLY.
```

### BodyRecoveryRightLeg

部位补充：

```text
The ONLY highlighted anatomical area is the patient's ANATOMICAL RIGHT LEG, which appears on the VIEWER'S LEFT side of the front-facing body. Draw the whole leg on SCREEN LEFT warm pale tan with thick ivory bandage around both thigh and shin, and one simple tan foot. The entire leg on SCREEN RIGHT stays muted gray. Head, chest, abdomen, hips and both arms remain gray. Keep the two legs clearly separated. Green recovery plus upper-left. No red injury marks. This is RIGHT leg recovery, screen-left bandaged leg ONLY.
```

### FusionContact

Subject：

```text
Subject: an extremely dangerous miniature FUSION STAR, a large white-hot square-pixel central core surrounded by a thick golden yellow starburst with four broad uneven spikes and a few compact burnt-orange outer flame tips. A dark charcoal stair-step contour makes the overall starburst readable as one isolated icon. Use solid flat white/cream/yellow/orange/red colors in large shapes; despite the hot subject, NO soft glow, NO haze, NO gradient, NO lens flare. The white core is a clear hard-edged shape, the outer orange flames form a compact jagged blazing sun. Tiny danger cue: a bold charcoal crack dividing one orange spike; do not add radiation trefoil, warning triangle, text, numbers or an atom orbit. Meaning direct contact with a fusion sun is lethal. Keep icon center and upper-left readable.
```

## 工具原始输出来源

以下源文件目录为 `C:\Users\PC\.codex\generated_images\01a14aed-d088-76d2-8de8-47cdbb27ac6e`，复制到本目录时保持 PNG 字节原样。

- `SpeedBoost.png` ← `exec-5dea3344-55db-4102-8827-b458b53b54df.png`
- `Overweight.png` ← `exec-6b69b923-1020-407d-9f37-8d98c456d606.png`
- `Wet.png` ← `exec-309ca6a7-0117-4ded-89df-c73824f05c69.png`
- `NightVision.png` ← `exec-b006450e-285c-4762-baab-c83e69b25806.png`
- `Concussion.png` ← `exec-00451ca9-7e62-499b-9b77-addb235cf53a.png`
- `LegFracture.png` ← `exec-9c9a3451-f681-4389-aa46-ee1e4f5c6cc0.png`
- `ArmFracture.png` ← `exec-e8bd4876-48d6-4998-9199-9273b54e061c.png`
- `BodyRecoveryHead.png` ← `exec-db7a8cca-1daf-4576-ac05-9664646b548c.png`
- `BodyRecoveryChest.png` ← `exec-54c1a4d1-76d0-471a-ba39-d94e95462f02.png`
- `BodyRecoveryAbdomen.png` ← `exec-ee411630-62c8-42e9-9591-5662cf9dae79.png`
- `BodyRecoveryPelvis.png` ← `exec-ac44c59e-2808-46f2-ac92-62f01d8a8728.png`
- `BodyRecoveryLeftHand.png` ← `exec-76b59915-5b4f-4a32-981e-e909f468733a.png`
- `BodyRecoveryRightHand.png` ← `exec-6060d735-9c5f-4b66-bbf0-88d1e9d9d4ac.png`
- `BodyRecoveryLeftLeg.png` ← `exec-ea1f9bc4-1641-4975-a49a-a5ae68be77e3.png`
- `BodyRecoveryRightLeg.png` ← `exec-0a6866af-05b6-45bf-a43b-f11e51eab92b.png`
- `FusionContact.png` ← `exec-7810061c-cdd6-48ac-9b87-556842893eab.png`

# Buff 状态图标生成记录 A

2026-10-18。通过内置 `image_gen.imagegen` 为每张图标单独生成原始透明 PNG，未调用图片生成 CLI/API。参考项目现有辣椒、草药敷剂、夹板的轮廓和色块语言。

## 正式文件与语义

全部图标在本目录，中心 Pivot、Single Sprite、Point、关闭 Mipmap、无压缩。`spritePixelsToUnits` 使用源图宽度。源 PNG 保持生成工具原始尺寸及 Alpha，不进行缩放、量化或抠图；Unity 导入 `maxTextureSize=128`（含默认和各平台），适配当前约 29 像素的 HUD 图标并限制贴图内存。

“32×32 逻辑像素”是生成时的造型目标，不代表原始 PNG 是严格 32×32，也不代表通过了硬 Alpha 或固定色数校验。源图含半透明像素和较多实际颜色，保留原始数据。

已运行项目现有 PNG 只读检查脚本，A 组 16 张全部具有 RGBA Alpha，四角均完全透明；检查未启用硬 Alpha 或固定色数限制。未控制 Unity Play Mode。

| Buff ID | 显示名 | 正式 PNG | 源图尺寸 | 图案 |
| --- | --- | --- | --- | --- |
| `辣` | 辣 | `Spicy.png` | 1254×1254 | 红辣椒与热气 |
| `出血1` | 出血1 | `Bleeding1.png` | 1254×1254 | 单血滴 |
| `出血2` | 出血2 | `Bleeding2.png` | 1269×1240 | 双血滴 |
| `出血3` | 出血3 | `Bleeding3.png` | 1254×1254 | 三血滴 |
| `燃烧` | 燃烧 | `Burning.png` | 1254×1254 | 橙黄火焰 |
| `感染` | 感染 | `Infection.png` | 1254×1254 | 绿病菌 |
| `光耀` | 光耀 | `Radiant.png` | 1254×1254 | 金色日芒与小幽灵 |
| `低温冻伤` | 低温冻伤 | `Frostbite.png` | 1254×1254 | 雪花与结冰手指 |
| `热射病` | 热射病 | `Heatstroke.png` | 1254×1254 | 高位红温度计与太阳 |
| `生命恢复(1)` | 生命恢复(1) | `HealthRecovery.png` | 1312×1199 | 红心与绿色十字 |
| `core:herbal_recovery` | 草药恢复 | `HerbalRecovery.png` | 1254×1254 | 草叶与红心 |
| `补水` | 补水 | `Hydration.png` | 1312×1199 | 蓝水滴与向上箭头 |
| `脱水` | 脱水 | `Dehydration.png` | 1254×1254 | 干裂水滴与向下箭头 |
| `减伤` | 减伤 | `DamageReduction.png` | 1254×1254 | 盾牌挡住撞击 |
| `跑步` | 跑步 | `Running.png` | 1402×1122 | 皮靴与两条速度线 |
| `禁锢(99)` | 禁锢(99) | `Immobilized.png` | 1254×1254 | 被锁链束住的皮靴 |

## 原始生成提示词

每次调用均设 `transparent_background: true`，不传编辑图片引用。下方公共提示词与每张图的 Subject 拼接为完整提示词。

```text
Use case: stylized-concept. Asset type: FlatWorld Unity 2D sandbox Buff HUD icon. Primary request: make ONE original pixel-art status-effect icon. Pixel style: an actual deliberately coarse 32 by 32 LOGICAL PIXEL sprite shown enlarged with crisp nearest-neighbor square pixel blocks; each logical pixel is a flat solid square, consistent square grid. Match existing FlatWorld item icons: compact readable silhouette, dark charcoal-brown one-logical-pixel contour, 2 or 3 flat tones per material, warm earthy palette, light from upper left, at most 12 visible colors. Composition: single centered isolated symbol occupying about 26 by 26 logical pixels, leave transparent margin on all sides; do not place important detail in extreme bottom-right (HUD stack badge). Background: genuine transparent alpha, transparent corners. No backdrop, floor, scene, frame, card, border, button, shadow, text, letter, number, label, gradient, bloom, antialiasing, realistic texture, smooth vectors, or 3D rendering.
```

### Spicy

Subject:

```text
a single curved red chili pepper with a small green stem, tilted bottom-right, with two short orange heat wisps above it. Communicate spicy food causing body warmth and a little damage. The pepper is the unmistakable dominant shape.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-cf24eb6d-16ad-4061-ab8f-28768338c5fc.png`。

### Bleeding1

Subject:

```text
one large deep-red blood droplet, simple pointed top and round bottom, with a single tiny pale coral highlight on its upper-left shoulder. This represents mild bleeding. Exactly ONE blood drop, nothing else.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-f97dc2e6-079e-42c8-bd08-fea248f5dbf7.png`。

### Bleeding2

Subject:

```text
exactly TWO distinct deep-red blood droplets in a compact diagonal pair, one large drop upper-left and a slightly smaller one lower-right. Both fully visible and separated by a transparent gap. This represents moderate bleeding; blood red with small pale coral highlights.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-8bf494d1-cacb-4d81-8529-e1d909404762.png`。

### Bleeding3

Subject:

```text
exactly THREE distinct deep-red blood droplets arranged in a compact triangular cluster: one medium drop at upper center, one at lower left and one at lower right, with small transparent gaps between. The three droplets have the same shape and size, matching a coherent bleeding-level icon family. This represents severe bleeding. Count must be exactly THREE; no pool, wound, hand or person.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-2950f949-0a4d-4e94-8040-97c1a44d271c.png`。

### Burning

Subject:

```text
one simple stylized flickering flame, with three broad blocky upward tips, deep burnt-orange outside, golden yellow middle and a small pale warm-yellow heart. A compact dangerous burning silhouette, red-orange and gold, no candle, torch, wood, sparks or smoke.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-78667755-5b0e-4534-b030-998175a2e0cd.png`。

### Infection

Subject:

```text
one large green germ / bacteria silhouette: a lumpy round olive-green body with six short square-ended stubs, two dark square pits and small pale yellow-green highlights. It should instantly communicate infection or sickness in a survival game, compact and slightly ominous, not a smiling character, no face, no neon glow or realistic biology.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-6aed57ca-e184-4c0f-bdde-162e8474fa13.png`。

### Radiant

Subject:

```text
a compact golden sunburst with an angular pale gold bright core and exactly eight short thick stepped rays. A very small dark purple ghost silhouette is cut into or placed just in front of the lower-left part of the sun, with two tiny pale square eyes. Communicate holy sunlight damaging ghosts. Sun occupies two thirds of icon, ghost one third; no person, shield, cross, circular badge, rings or text.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-3a54b7a0-8ce1-45d5-b1c4-0208cee5ab52.png`。

### Frostbite

Subject:

```text
one bold icy-blue six-armed snowflake in the upper-left, overlapping a compact pale skin-colored fingertip / small hand silhouette with a blue frozen tip in the lower center. Communicate damaging body cold, distinct from merely a snow weather symbol. Simple strong readable shapes, blue/cyan/cream and charcoal-brown outline, at most two subjects, no scene.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-2a25f42b-4607-4286-89bd-4e72e8558e4c.png`。

### Heatstroke

Subject:

```text
a large upright red-bulb thermometer silhouette with a dark-brown edge, red column nearly at the top, pale cream housing, and one small angular orange sun upper-left. Instantly communicate dangerously overheated body rather than fire. Thermometer dominates; no tick marks, digits, letters, flames, smoke, face or extra objects.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-cbd60edc-4ad3-4aa8-a217-f350077d3f9b.png`。

### HealthRecovery

Subject:

```text
one large classic red pixel heart with a bold green medical PLUS SIGN overlapping its upper-right shoulder. The red heart remains unmistakable and takes two thirds of icon; green plus is a thick 5-square cross, distinct from an arrow. Communicate active health regeneration. At most two simple shapes; no bottle, leaf, wing, frame, text or numbers.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-9e68a47e-3462-47f2-8bc4-ba51e38560eb.png`。

### HerbalRecovery

Subject:

```text
one small red heart nestled beside two large fresh olive-green herb leaves, leaves rising from lower-left to upper-right and dominating the upper half; heart in the lower center. Communicate herbal healing, distinct from generic health recovery heart-plus. Simple readable two-leaf silhouette, earthy greens and red, no green medical cross, potion, bandage, vase, pile of tiny leaves or text.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-5d18e8cd-8ae6-42b6-a902-f6ca937c7f6e.png`。

### Hydration

Subject:

```text
one large clear deep-blue water droplet with a small sky-blue highlight at upper-left, beside one bold light-mint UPWARD ARROW on the right. The arrow points unmistakably UP with a wide triangular stepped head and short stem. Communicate hydration increasing, no wavy water, container, circle, heart, letters or numbers.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-f5461328-4f6a-499c-a9c7-60a7b70f9f27.png`。

### Dehydration

Subject:

```text
one large desaturated pale-blue water droplet with a single clearly visible jagged dark crack splitting part of its body, beside one bold muted-orange DOWNWARD ARROW on the right. The arrow unmistakably points DOWN, triangular stepped head at its bottom and short vertical stem. Communicate losing body water / dehydration. Match a water-drop-with-arrow status icon family, but cracked droplet and downward direction make it structurally distinct from hydration. No vessel, ground, sun, text, numbers or letters.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-ec35906b-81a9-4f61-a35e-fb72947780c2.png`。

### DamageReduction

Subject:

```text
one squat upright medieval survival-game steel-blue shield with a pale gray rim and a simple dark central boss, angled very slightly, intercepting a small orange two-pronged impact spark at its upper-left edge. Shield is the dominant immediately recognizable shape. Communicate incoming damage reduced or blocked. Broad large blue-gray panels with flat stepped highlights, no insignia, weapon, cross, letters, ornate decoration or enclosing badge.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-a3afc3d5-44d1-4159-a84b-9deac0acdf15.png`。

### Running

Subject:

```text
one sturdy tan-brown leather boot pointing right in a dynamic running position, toe lifted slightly, with exactly TWO short pale-cream horizontal speed streaks trailing to its LEFT. Boot has simple dark sole and ochre upper, no elaborate laces. Communicate full running speed; use motion streaks, no arrow, wings, person, frame, text, numbers, ground or dust.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-60c12962-2386-4002-a718-71812c1547f1.png`。

### Immobilized

Subject:

```text
one sturdy tan-brown leather boot pointing right, fixed upright and tightly wrapped around the ankle by a bold dark steel-gray chain made of three simple interlocking square links; one small closed square padlock hangs on the front-left of the chain. Communicate being immobilized / bound / unable to move, distinct from running boot. Boot and chain must be large readable compact shapes, no arrow, speed streaks, prisoner, ground, frame, numbers or text.
```

工具源文件：`C:\Users\PC\.codex\generated_images\01a14aeb-f518-73b1-aa67-7bd3b3ffb885\exec-faa9286f-e612-4908-b29c-605a33a66152.png`。

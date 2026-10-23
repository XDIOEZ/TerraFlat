# 植物作物重绘样例（2026-10-23）

本轮为画风评审样例，采用当前 `.agents/skills/flatworld-art/SKILL.md` 的低饱和、偏写实复古像素风。使用内置 image_gen 生成，保留原始 PNG 画布和透明通道；当前游戏引用未切换到样例。

## 当前清单

统计范围：排除树木，按“不阻挡角色移动”的植物类型去重；不把生长阶段、贴图变体、种子和收获物重复计数。作物仍可有受击用 Trigger，不等于彻底没有 Collider。

| 类别 | 名称 | ID | 播种入口 |
| --- | --- | --- | --- |
| 作物 | 萝卜 | RadishCrop | Seed_Radish |
| 作物 | 蘑菇 | MushroomCrop | Seed_Mushroom |
| 作物 | 藤蔓 | TwineCrop | 当前无 |
| 作物 | 浆果丛 | BerryCrop | Seed_Berry |
| 作物 | 水稻 | RiceCrop | RiceGrain |
| 作物 | 棉花 | CottonCrop | Seed_Cotton |
| 作物 | 辣椒（当前显示名“辣椒树”） | ChiliTree | Chili |
| 作物 | 仙人掌 | Cactus | 当前无 |
| 作物 | 药草 | HerbCrop | Seed_Herb |
| 作物 | 狗尾草 | FoxtailCrop | Seed_Foxtail |
| 地表植被 | 野草 | 独立草层 | 无 |
| 地表植被 | 花朵 | Flower | 无 |

合计 10 种作物，其中 8 种有播种入口；连同地表野草、花朵共 12 类。仙人掌虽有接触伤害，仍不阻挡移动；辣椒按作物计数。

清单来源：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json` 的已启用分包、`shells/crops.json`、`shells/chili.json`、`shells/cactus.json`、`shells/ancient_stage.json`、`shells/seeds.json`、`shells/basic_items.json`、`shells/resource_nodes.json`；草花表现分别由 ChunkGrassRenderer / ChunkGroundCoverRenderer 管理。

## 本轮图片

- `RadishCrop_Mature_Sample.png`：完整萝卜植株和根，供后续按种植裁切规则装配。
- `RiceCrop_Mature_Sample.png`：成熟稻株，金黄下垂稻穗；最终套图还需要幼苗、生长两阶段。
- `BerryCrop_Mature_Sample.png`：浆果丛植株本体；果实由库存驱动的独立图层显示，未画进灌木本体。

本轮仅确定画风；提示词中的逻辑网格是生成目标，不代表原图已经达到严格网格的运行时规格。最终整套采用统一实际逻辑分辨率、画布和基线后再接入。样例导入使用 Point、无 Mipmap、无压缩、透明背景，PPU 暂按各图原始高度设置为约一单位画布高。

## 生成方式与最终提示词

内置 `image_gen`，每张独立调用，`transparent_background=true`；没有使用 API/CLI 回退。

### 萝卜

```text
Use case: stylized-concept.
Asset type: a single isolated mature crop world-sprite style sample for FlatWorld, a 2D top-down sandbox game. This is asset art, not a scene or an inventory photograph.
Art direction: low-saturation, semi-realistic retro pixel art at VERY LOW logical resolution. Design on a strict 32 by 32 pixel logical grid, then enlarge with perfect nearest-neighbor square pixel blocks for clear preview. Large clean hard-edged pixel clusters, sparse detail, 12–18 carefully chosen matte colors, restrained 2–3 value shading per material. Natural botanical proportions, recognizable from a small silhouette. Elevated three-quarter game view, standing plant seen slightly from above, upper-left lighting. Colored dark olive/brown edge accents only where needed, no thick pure-black outline.
Composition: ONE isolated complete plant centered on a square canvas with generous transparent padding, base near bottom center and full plant visible. Keep the grid scale consistent across this three-plant sample set.
Background: actual transparent alpha, no white background and no baked checkerboard.
Exclude: text, labels, borders, UI, people, containers, pots, ground patches, soil mounds, scenery, cast shadows, soft blur, antialiasing, gradients, watercolor, tiny noisy texture, glossy 3D, cartoon faces, neon greens.
Subject: ONE mature planted red radish crop, drawn as a full plant so its lower root can later be hidden by the game's planting shader. A compact, naturally tapered muted dusty-rose red root with a pale ivory lower tip and a short thin root tail. Five to six broad lobed olive and sage green leaves spread upward and slightly sideways from a tight central crown. Leaves occupy approximately two thirds of the plant height; the root occupies the lower third. Clearly stepped chunky pixel leaf lobes, one restrained vein highlight per major leaf. Avoid a turnip's overly round bulb and avoid a gigantic food icon. This must read as a low growing world plant with its complete root, not a harvested vegetable lying sideways. Target silhouette around 23 logical pixels wide and 27 pixels high.
```

### 水稻

```text
Use case: stylized-concept.
Asset type: a single isolated mature crop world-sprite style sample for FlatWorld, a 2D top-down sandbox game. This is asset art, not a scene or an inventory photograph.
Art direction: low-saturation, semi-realistic retro pixel art at VERY LOW logical resolution. Design on a strict 32 by 32 pixel logical grid, then enlarge with perfect nearest-neighbor square pixel blocks for clear preview. Large clean hard-edged pixel clusters, sparse detail, 12–18 carefully chosen matte colors, restrained 2–3 value shading per material. Natural botanical proportions, recognizable from a small silhouette. Elevated three-quarter game view, standing plant seen slightly from above, upper-left lighting. Colored dark olive/brown edge accents only where needed, no thick pure-black outline.
Composition: ONE isolated complete plant centered on a square canvas with generous transparent padding, base near bottom center and full plant visible. Keep the grid scale consistent across this three-plant sample set.
Background: actual transparent alpha, no white background and no baked checkerboard.
Exclude: text, labels, borders, UI, people, containers, pots, ground patches, soil mounds, scenery, cast shadows, soft blur, antialiasing, gradients, watercolor, tiny noisy texture, glossy 3D, cartoon faces, neon greens.
Subject: ONE mature rice plant clump in natural proportions, rooted from one compact base. Slender green stalks rise and gently bend under four drooping branching rice panicles with muted straw-gold grains. Long olive and sage green narrow blade leaves arch outward with a few clear angular bends. Preserve the botanical difference from wheat: the panicles are loose, branched and drooping, never thick upright wheat ears. A compact clump, naturally asymmetric, clearly legible at 32 pixels. Use deliberately simplified large pixel clusters rather than hairline high-resolution stems or individually shaded tiny grains. Target silhouette around 24 logical pixels wide and 29 pixels high. No water, ground, cut stalks or straw bundle.
```

### 浆果丛

```text
Use case: stylized-concept.
Asset type: a single isolated mature crop world-sprite style sample for FlatWorld, a 2D top-down sandbox game. This is asset art, not a scene or an inventory photograph.
Art direction: low-saturation, semi-realistic retro pixel art at VERY LOW logical resolution. Design on a strict 32 by 32 pixel logical grid, then enlarge with perfect nearest-neighbor square pixel blocks for clear preview. Large clean hard-edged pixel clusters, sparse detail, 12–18 carefully chosen matte colors, restrained 2–3 value shading per material. Natural botanical proportions, recognizable from a small silhouette. Elevated three-quarter game view, standing plant seen slightly from above, upper-left lighting. Colored dark olive/brown edge accents only where needed, no thick pure-black outline.
Composition: ONE isolated complete plant centered on a square canvas with generous transparent padding, base near bottom center and full plant visible. Keep the grid scale consistent across this three-plant sample set.
Background: actual transparent alpha, no white background and no baked checkerboard.
Exclude: text, labels, borders, UI, people, containers, pots, ground patches, soil mounds, scenery, cast shadows, soft blur, antialiasing, gradients, watercolor, tiny noisy texture, glossy 3D, cartoon faces, neon greens.
Subject: ONE mature low berry shrub plant body for a game that draws collectible berries in a separate runtime layer. Draw ONLY the shrub foliage, branches and compact plant base; DO NOT bake berries or fruit into the sprite. A low, gently spreading, naturally asymmetric bush with several overlapping broad lobed leaf masses in muted moss, sage and olive greens. A few subdued brown twig pixels show near the narrow base. Leaf groups form large readable pixel clusters with open silhouette notches; avoid a perfect sphere, topiary, tree trunk, pine shape, ornamental hedge or individually painted hundreds of leaves. The empty berry bush should still look lush and believable, approximately 28 logical pixels wide and 24 pixels high. Keep three broad crown zones that can later host separate berry overlays, but do not draw marker dots or holes.
```

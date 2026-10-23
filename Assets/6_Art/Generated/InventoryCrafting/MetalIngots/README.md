# 金属锭贴图

2026-10-23，依据用户认可的粗糙铸锭风格，统一替换铜、青铜、生铁、熟铁、钢、锡、金7种锭。

| 金属 | PNG | 表面与光泽 |
| --- | --- | --- |
| 生铁 | RawIronIngot_Icon.png | 最暗哑、粗铸面 |
| 熟铁 | WroughtIronIngot_Icon.png | 偏哑、断续冷灰反光 |
| 青铜 | BronzeIngot_Icon.png | 克制的暖色缎面 |
| 铜 | CopperIngot_Icon.png | 中等、暖铜反光 |
| 锡 | TinIngot_Icon.png | 柔和宽银亮 |
| 钢 | SteelIngot_Icon.png | 较强、锐利银蓝反光 |
| 金 | GoldIngot_Icon.png | 较强、明亮暖金反光 |

统一轮廓、斜向视角、左上光源及粗像素尺度；每种锭的暗斑、凹坑、铸造痕迹独立重绘，位置、形状、尺寸和疏密不同。保留用户要求的脏旧质感，同时让光滑面的反光更连贯，粗糙、氧化和凹坑处打断高光。

光泽按该物品的默认铸造/精炼表面处理设计，不将金属种类视为光滑程度的唯一原因。反光直接绘入贴图，没有新增材质或Shader系统。

所有图由内置image_gen基于统一母版分别编辑。PNG保留生成原图画布和Alpha，不裁剪或重新采样；这是大图呈现的复古像素风，不是严格原生32×32源图。导入Point、无Mipmap、无压缩、中心Pivot、FullRect，PPU按实际图宽对应1世界单位。

接入7种物品的JSON visual.spriteAddress、6个原有Prefab的Sprite引用，以及Addressables ItemSprite；金锭由JSON定义。旧工具贴图、共享矿石图集保留给其他使用者。

按用户要求未运行游戏测试，背包、掉落物与实际缩放视觉由用户确认。美术Skill的锚点与材质参考补充了金属锭的独立纹理与不同光泽规则。

## 最终提示词

每张输入为此前同材质的粗糙铸锭原图，先保持轮廓与画布，再独立修改纹理分布和光泽。

共同部分：

```text
Use case: precise-object-edit. Revise this particular FlatWorld metal ingot PNG for TWO user-requested refinements: material-specific metallic sheen AND a unique independently drawn arrangement of cast-metal imperfections.
KEEP the existing single ingot shape, squat cast bar proportions, silhouette, diagonal orientation, three visible planes, source canvas dimensions/aspect ratio, transparent padding, object size and upper-left lighting. Keep this metal's recognizable base hue, desaturated semi-realistic retro pixel art, coarse hard square pixel clusters. No finer pixel density.
CHANGE THE SURFACE TEXTURE LAYOUT COMPLETELY. Erase the old pit and blotch arrangement and draw a different, natural asymmetric arrangement of dark casting marks and shallow pits. Do NOT trace the source's repeated large center-top blotch, diagonal row of pits, or lower-front blotches. The seven metals must have visibly DIFFERENT surface marks, not palette swaps. The instructions below give this metal its own distinct arrangement. Retain the approved rough, slightly dirty cast-metal character; it must not become a blank smooth bar.
Metallic sheen must come from a few crisp stepped pixel-value groups on the smoother planes and bevels, with highlights interrupted by rough pits and dirty patches. Use the material's specified level of gloss. No airbrushed shine, glow, haze, smooth gradients, lens flares, star sparkles or shiny plastic. Highlights never hide the material identity or all its surface imperfections.
Genuine transparent alpha outside the solid object. No floor, cast shadow, text, labels, props, frame or painted checkerboard. One sprite only.

```

### 生铁

```text
Dark neutral-warm graphite PIG IRON. ROUGHEST, ALMOST MATTE (sheen 1/7): only short dull mid-gray broken bevel lights, no white rim and no bright reflection stripe. New unique texture: broad irregular casting-scale patches concentrated at the TOP-LEFT and lower RIGHT end, a couple of tiny top pits near the FAR RIGHT, a ragged dark seam-like casting patch low on the long FRONT-LEFT. Dark rough patches vary in scale, avoid uniformly distributed spots. Very dark dense gray bar but planes still readable.
```

### 熟铁

```text
Medium cool-neutral gray WROUGHT IRON. LOW SHEEN (2/7): restrained broken silver-gray bevel highlight and one short muted flat reflective patch, substantially softer/dimmer than polished steel. New unique texture: a loose group of THREE uneven small pits toward the TOP-RIGHT, a long irregular gray casting stain just off the TOP-LEFT edge, two darker blocky marks on the far LEFT end, sparse small wear marks across the FRONT-MIDDLE. No large centered top blotch. Industrial slightly dirty metal.
```

### 青铜

```text
Muted olive-brown/umber BRONZE. SATIN LOW-MEDIUM SHEEN (3/7): subdued warm brass-like stepped reflection near the upper bevel, mostly dull broad planes. Brown and subtly olive, not copper red and not yellow gold. New unique texture: two offset broad shallow patina islands near the TOP-RIGHT corner and TOP-LEFT center, ONE tiny pit close to the near LEFT edge, uneven dark residue patches grouped toward the FRONT-RIGHT end, a nearly clean front-left area. Irregular restrained brown casting patina, no green coating.
```

### 铜

```text
Muted reddish warm COPPER. MEDIUM SHEEN (4/7): clear warm pale-copper bevel lights and a short crisp stepped red-peach reflection on the top-left portion; brighter smoother metal than bronze, but still cast and used. New unique texture: a shallow angular gouge-like casting pit near the TOP-LEFT, a small paired pit group close to the TOP-MIDDLE/RIGHT, elongated irregular dark-brown stains mostly along the FRONT-LEFT lower edge; leave most of the TOP-RIGHT smoother so its reflection reads. Different mark sizes, asymmetrical, no identical row of holes.
```

### 锡

```text
Light warm silver/pewter TIN. MEDIUM-HIGH SOFT METALLIC SHEEN (5/7): a wider soft-looking reflection rendered ONLY as 2-3 hard stepped ivory-silver pixel-value bands, not blur. A pale bright top and silver-gray sides, softer reflection than steel. New unique texture: scattered small dark-gray pits concentrated on the TOP-LEFT and FAR RIGHT end, one medium irregular dull-gray oxidized patch low on the FRONT-CENTER, a little casting scar beside the RIGHT upper bevel. Mostly smoother top-middle interrupted by just one tiny mark. Dirty soft silver, not white ceramic.
```

### 钢

```text
Desaturated blue-gray STEEL. HIGH CRISP METALLIC SHEEN (6/7): a narrow interrupted pale silver-blue highlight on the upper bevel, one modest crisp elongated stepped reflection along the top's long direction, dark-blue-gray shadows; highlights sharp and more continuous on clean metal than wrought iron. Keep low saturation, not vivid blue, not mirror chrome. New unique texture: a few small shallow pits near the TOP-FAR LEFT and one along the near TOP-RIGHT edge; a diagonal irregular dark scale patch across the FRONT-RIGHT below that edge, two tiny chips at the LEFT end, otherwise sparse fine-sized blocky casting marks. Do not reuse the original big top-middle patches.
```

### 金

```text
Warm antique yellow/champagne GOLD. VERY CLEAR WARM METALLIC SHEEN (7/7): a strong pale warm-gold bevel highlight plus a modest stepped bright gold reflective band on the cleaner top plane; warmer richer light/shadow separation, never neon yellow, white chrome, bloom or glare. Still dirty cast gold, no full clean polish. New unique texture: small irregular dark ochre casting-residue patches near the TOP-FAR RIGHT, TWO modest shallow pits near the TOP-LEFT edge with subtle warm rims, an asymmetric broad umber smudge toward the lower FRONT-LEFT, tiny scattered residue on the RIGHT end. Keep the center-top mostly smoother to show warm reflection. Patina is dirt/residue, not red iron rust.
```

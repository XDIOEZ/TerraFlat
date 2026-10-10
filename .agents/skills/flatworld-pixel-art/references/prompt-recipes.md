# FlatWorld 运行时像素美术提示词模板

先读取 `style-guide.md` 并选择最接近目标类别的参考图。以下模板只提供结构，不得无故增加角色、道具或剧情。

## 通用角色或 NPC

```text
Use case: stylized-concept
Asset type: game-ready FlatWorld 2D top-down pixel-art character, <single pose / sprite sheet>, using the runtime canvas and logical pixel density required by the existing character consumer or closest same-category project reference
Input images: Image 1 is the closest existing project character and is the primary reference for world scale, proportions, pixel clusters, outline weight, palette structure, and animation layout; Image 2 is the approved merchant style anchor and may supplement character design language. Use references only for style and scale; create an original subject.
Primary request: create one <角色身份> for FlatWorld.
Subject: <少量可辨识服装与 1-3 个身份配件>
Style/medium: authentic FlatWorld pixel art; oversized head; compact body; dark charcoal outline matching the closest project character's visual weight at the same world scale; limited muted palette; hard square pixel clusters; one shadow and one highlight per major material. Higher logical pixel density is allowed only if the same visual style is preserved.
Composition/framing: exactly one full-body sprite, <朝向>, neutral pose, complete feet and silhouette, generous padding in the high-resolution source; final runtime subject must fit the target consumer canvas, align bottom-center, and preserve the same world-scale proportions and pixel-cluster language as the closest existing project character.
Scene/backdrop: perfectly flat solid #ff00ff chroma-key background.
Constraints: no gradients; no anti-aliasing; no cast shadow; no floor; no text; no watermark; no extra characters; do not use #ff00ff in the subject.
Avoid: realistic anatomy; detailed fingers; thin lines; smooth vector edges; tiny decorations that disappear at target size; high-resolution UI portrait treatment.
```

## 动物或怪物

```text
Use case: stylized-concept
Asset type: FlatWorld 2D top-down pixel-art creature sprite
Input images: Image 1 is the closest project animal or monster reference and is the primary authority for species silhouette, world scale, viewpoint, pixel clusters, outline weight, palette structure, and material rendering; Image 2 is the merchant anchor and may only supplement broad FlatWorld color-clustering language when it does not conflict with creature references.
Primary request: create one original <动物/怪物> readable at the target runtime size, using the closest existing project creature as the primary style, world-scale, and pixel-density reference.
Style/medium: compact FlatWorld pixel art; strong species silhouette; limited muted palette; dark outline matching the closest project creature's visual weight at the same world scale; hard color clusters; no anti-aliasing or gradient. Higher logical pixel density is allowed without changing the established creature style.
Composition/framing: exactly one complete creature using one canonical lateral direction only — default to <向左> so the head and body face screen-left — with <动作>; no cropped ears, tail, legs, or wings; final runtime subject fits the target canvas and aligns bottom-center. Put locomotion and requested special-action frames into one equal-size Sprite Sheet when possible; derive the right-facing version by Unity horizontal mirroring instead of generating duplicate right-facing frames. Never include front-facing, back-facing, upward, downward, or mixed four-direction frames. Higher logical pixel density is allowed, but silhouette, outline weight, palette structure, and material rendering must still match the closest existing project creature.
Scene/backdrop: perfectly flat solid chroma-key background.
Constraints: preserve simplified animal anatomy; no clothing unless requested; no floor, shadow, text, watermark, or extra creature; keep every source frame in the same canonical lateral direction and let Unity's `flipX`/equivalent mirror produce the opposite side.
```

## 物品、工具、树木、植被、建筑或世界道具

```text
Use case: stylized-concept
Asset type: FlatWorld pixel-art <inventory icon / tool / tree / vegetation / building / world prop>
Input images: Image 1 is the closest same-category project asset and is the primary authority for viewpoint, world scale, silhouette, pixel-cluster size, outline treatment, palette structure, and material rendering; additional same-category project assets may be used to confirm the shared style. Do not use the merchant anchor to override item, tool, tree, vegetation, or building style.
Primary request: create one <物品名称>.
Style/medium: FlatWorld pixel art matching the closest same-category runtime asset; strong compact silhouette; hard grouped pixels; limited material color groups; hard edges; limited palette; subtle top-left highlight. Logical pixel density may be higher than older assets, but the result must not become smoother, more realistic, more detailed, or visually denser than the established style language.
Composition/framing: one centered object, complete silhouette, readable at the target runtime size and world scale. Choose canvas size and PPU from the consumer requirements so the object remains proportionally consistent with existing same-category assets.
Scene/backdrop: perfectly flat chroma-key background.
Constraints: no card background or decorative card frame; no label; no shadow unless same-category references contain one; no watermark; no extra objects. Structural reinforcement frames are part of the object. For trees, vegetation, large props, and buildings, preserve the existing project's leaf/shape clustering, trunk or structural massing, outline weight, light direction, saturation, and top-down viewpoint even when using a higher pixel density.
```

## 使用程序化接口的设备机身

先从消费方和同类机身确定画布、目标可见大小、Pivot 与 PPU。机械轴口、电线和流体管道的接入/离开由程序化表现层或标准 Sprite 负责，生成器只画完整机身；参考图里的接口只用于尺寸与遮挡定位。设备本体、召唤器和库存图标均不烘入连接件。

新绘制工业机身时，将风格指南中的铁罐、钢罐 PNG 作为实际输入参考，提示词明确写出干净大面板、直线加固框、少量方形铆钉及克制金属明暗。保留目标设备的功能结构；满格连片方块改用不透明满画布，独立轮廓设备使用透明背景。

```text
Asset type: FlatWorld pixel-art machine housing, prepared for separate procedural mechanical/electrical/fluid connector layers.
Input images: Image 1 is the approved iron or steel gas-tank block from the FlatWorld industrial style guide; Image 2 is the closest same-category housing or approved target design; additional images are existing shaft/collar, cable or pipe references only when needed. Borrow the tank's material palette, panel/frame construction and restrained detail density; preserve the target device's functional shape and use connectors only as scale and alignment references.
Primary request: create one <设备名称> housing with a visible size of <目标世界宽高> on a <画布尺寸> canvas, intended for <机身 PPU> PPU.
Composition/alignment: preserve the approved housing's aspect ratio, runtime canvas and Pivot; keep the housing centered and closed, with enough opaque body to cover the inner portions of runtime connectors. A marked size box indicates overall size only, not a new aspect ratio. For a cell-filling connected block, use a flat front-facing square body filling the entire canvas, no transparent padding or perspective side, with frame/corner details confined to the consumer's edge slices and a clean continuous center panel.
Layers: complete housing only. Do NOT draw external input/output pipes, pipe mouths, fluid connector flanges, shafts, axle ports, iron connector collars, cables or cable junctions. All connections entering and leaving the device are drawn procedurally or from standard project sprites at runtime. Do not add holes or replacement stubs after removing connectors. Keep genuine working parts such as valve wheels, gauges, filter chambers and ventilation grilles.
Style/medium: clean large gray-blue metal panels, straight reinforcement frames, dark structural seams, a few readable square rivets and corner plates; optional restrained orange accents like the iron-tank reference. Hard pixel edges on outlines and hardware; subtle broad metal shading matching the tank references is allowed, without glossy 3D rendering, photo textures, dense scratches or granular noise. Match the target device's viewpoint and world scale. Transparent outside standalone silhouettes; fully opaque canvas for cell-filling connected blocks. No ground, cast shadow, text, watermark, or extra objects.
```

接入时按真实 PPU、Pivot、偏移和程序化连接图层核对大小、遮挡及旋转。仅大小不合适时调整机身 PPU 或等比例缩放，接口保持标准尺寸；静态物品图标沿用无外部接口的机身。流体消费方若分别缩放宽高，画布宽高比必须匹配目标比例，当前方形节点使用正方形画布。

## 保持身份的变体或动画

```text
Use case: identity-preserve
Input images: Image 1 is the approved runtime character anchor; Image 2 is the project animation-layout reference.
Primary request: create <新动作/装备变体> for the same character while keeping the approved canonical lateral direction.
Constraints: preserve head shape, face, proportions, outfit construction, accessory placement, palette, outline thickness, light direction, feet baseline, identity, and the selected canonical lateral direction; change only <明确变化>; do not generate a duplicate opposite-facing frame because Unity mirrors it at runtime; no redesign; no front/back/up/down direction; no extra elements; no text; no watermark.
```

## 迭代原则

- 一次只修正一个可观察问题，例如“让眼睛在目标运行时尺寸下保留”或“让描边在与现有素材相同世界尺度下保持一致的视觉粗细”。
- 每轮重复身份、同类项目参考、调色板、比例、背景、目标画布、世界尺度和对齐约束，不因进入迭代而省略硬规则；其中画风一致性优先于机械复刻某个固定像素尺寸。
- ImageGen 结果需检查消费方尺寸、画风、透明度和导入设置；仅在实际需要时缩放、量化或清边，用户指定原图直用时保留文件内容。

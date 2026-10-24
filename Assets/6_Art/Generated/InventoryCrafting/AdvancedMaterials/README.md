# 高级材料贴图

2026-10-24，为细分科技时代新增两种材料占位：`Plastic`（塑料）与 `SiliconPlate`（硅板）。物品定义在 `Assets/StreamingAssets/GameConfig/Items/shells/advanced_materials.json`，暂不配置配方、生产线或后续用途。

两张图分别使用内置 `image_gen` 生成。风格参照项目的钢板、橡胶和布材料：低饱和、粗颗粒复古像素、左上光源、斜向视角。塑料为浅色板料；硅板为深蓝灰薄硅材板，没有电路线条。

| 物品 | 最终文件 | 内置工具输出文件 |
| --- | --- | --- |
| 塑料 | `Plastic/Plastic_Icon.png` | `exec-94984ddd-ffdf-4a42-ab2a-74ef05731c1a.png` |
| 硅板 | `SiliconPlate/SiliconPlate_Icon.png` | `exec-20fa7574-b1af-44dd-94ba-b9d864a98f7a.png` |

实际输出均为 **1254 × 1254 RGBA**；提示词请求 1024 方形，但内置工具返回上述尺寸。直接复制原始 PNG，保留完整画布和生成 Alpha，没有裁剪、缩放或描摹。背景透明、四角 Alpha 为 0，主体不透明，生成边缘含半透明过渡。大图中的粗像素风格不等同严格的原生 40 × 40 像素源图。

Unity 导入为 Single Sprite、FullRect、Point、无 Mipmap、无压缩、Clamp、中心 Pivot、1254 PPU，对应整张画布 1 世界单位，最大尺寸 2048，不因导入缩放。独立 GUID，Addressables `Default` 组注册完整 Assets 路径及 `ItemSprite` 标签。PNG 与 `.meta`、JSON 引用一一对应。

本次只检查 JSON 发现/继承、PNG 尺寸/透明度、导入规格及 Addressables 引用；没有触发 Unity 刷新、运行或游戏验收。

## 实际最终提示词

### 塑料

```text
Use case: stylized-concept. Asset type: FlatWorld game inventory material sprite, one isolated PLASTIC slab. Create a square 1024 x 1024 transparent PNG canvas. Subject: one small unbranded rectangular piece of ivory/off-white molded plastic stock, thick enough to read as a solid resin slab with a softly beveled edge; simple broad unpatterned top, one short warm-gray side plane, one shallow stepped highlight that makes the material read as molded plastic rather than stone or cloth. Three-quarter top-down view, long rectangle oriented diagonally lower-left to upper-right, centered, occupying about 75 percent of the canvas width with balanced transparent padding. Style: desaturated semi-realistic retro PIXEL ART for a 2D survival crafting game, very COARSE large hard square pixel clusters, approximately 40 x 40 logical pixels for the entire canvas enlarged with nearest-neighbor, restrained palette about 12 to 16 colors. Natural material proportions, clear silhouette, broad flat color groups and 2 to 3 hard stepped shade levels. Upper-left light; muted cream, pale warm gray and dull beige shadows. The plastic is opaque and cleanly cut, not transparent glass. NO smooth gradients, no antialiasing, no tiny high-resolution detail, no dense scratches, no stone chips, no holes, no grain, no cloth folds, no 3D render, no paper, no circuitry, no text, logo, label, watermark, frame, props, ground, floor or cast shadow. Genuine transparent alpha outside the solid sprite, opaque interior, no painted checkerboard. This is a raw material icon, not a finished consumer product.
```

### 硅板

```text
Use case: stylized-concept. Asset type: FlatWorld game inventory material sprite, one isolated SILICON PLATE, the raw semiconductor material and NOT an electronic circuit board. Create a square 1024 x 1024 transparent PNG canvas. Subject: a thin rectangular plate cut from polished solid silicon stock, broad clean dark charcoal-blue / slate-gray face, almost black narrow thickness edge, subtly chamfered corners; one restrained muted steel-blue stepped reflection across part of the top face shows the material's smooth slightly reflective finish. No circuits, chips, wiring, traces, connectors, sockets, rivets or stamped symbols. Three-quarter top-down view, long rectangle oriented diagonally lower-left to upper-right, centered, occupying about 75 percent of the canvas width with balanced transparent padding. Style: low-saturation semi-realistic retro PIXEL ART for a 2D survival crafting game. Very COARSE large hard square pixel clusters, approximately 40 x 40 logical pixels for the entire canvas enlarged with nearest-neighbor; broad uniform flat color regions with only 2 to 3 crisp stepped shade groups, restrained palette about 12 to 16 colors. Large clearly visible stair-step silhouette, natural material proportions and simple structure, upper-left lighting. Muted charcoal-gray, deep slate-blue shadows and a single dull blue-gray reflection. Not green, not vivid blue, no rainbow sheen, no mirror chrome. NO antialiasing, no smooth gradients, no airbrush, no tiny high-resolution detail, no noise or scratches, no 3D render, no glass, no solar-cell grid, no text, logo, label, watermark, frame, props, ground, floor or cast shadow. Genuine transparent alpha outside the solid sprite, opaque interior, no painted checkerboard. This is a plain raw silicon material icon, not a finished electronic product.
```

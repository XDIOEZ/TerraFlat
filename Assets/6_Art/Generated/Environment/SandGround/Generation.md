# 沙地地面贴图

- 消费链：`Assets/StreamingAssets/GameConfig/Tiles/terrain.json` 中的 `Tile_Sand` → `Assets/7_Tiles/Base/Tile_Sand.asset` → `Sand_Ground.png`。只替换现有 Tile 的 Sprite 引用。
- 外观：暖黄色沙底、少量深浅沙粒和短小的阶梯状风纹；参考现有泥土与石头的像素块和对比度。
- 参考：`Assets/6_Art/Generated/Environment/Dirt/Dirt_Ground.png`、`Assets/6_Art/Generated/Environment/StoneGround/Stone_Ground.png`。
- 生成方式：内置 ImageGen；最终源 PNG 原样复制到项目，未做本地像素编辑。最终生成文件：`exec-52664041-3797-4154-9cf1-6213e7e08e96.png`。
- 实际源图：1254×1254、RGB、1600 个可见颜色，全幅不透明。提示词中的 32×32 是逻辑像素风格目标，生成文件不是原生 32×32 或严格有限色图。
- 2026-10-12 淡化调整：保留现有纹理布局，将深色草纹、沙粒与风纹的颜色向底色靠近，降低连续铺开时的深色斑点反差。
- Unity 导入：沿用泥土与石头的 Single Sprite、Full Rect、中心 Pivot、Point、关闭 Mipmap、无压缩设置；各平台最大导入尺寸 32，源 PPU=1254，保持 1×1 世界尺寸。源图颜色统计不代表导入后的颜色数。
- Sprite GUID：`9287b607ef054fa88cdf6fa6ca5d3a1b`，fileID 为 `21300000`；Tile 保持白色、原变换和碰撞设置。
- 现有 Tile 位于带 `TileBase` 标签的 Addressables 文件夹中，新 Sprite 由 Tile 作为直接依赖加载。
- 原共享图集和其他切片保留；沙漠、沙滩与其他使用 `Tile_Sand` 的地方统一使用新沙地纹理。
- 本次为静态美术资源接入，游戏内观感和连续铺接由用户验收。
- 导入参数参考：[Unity Sprite 导入说明](https://docs.unity.com/en-us/engine/6000.5/manual/materials-and-shaders/textures/textures-reference/texture-type-sprite)。

## 当前淡化调整提示词

Use case: precise-object-edit. Edit this existing FlatWorld pixel-art desert sand ground tile. Make ONE VERY NARROW COLOR CORRECTION ONLY: lighten all darker brown sand dots, grain clusters and short stepped ripple marks so they blend gently with the warm sand base and no longer look like a dense collection of dark spots when repeated. The dominant base is approximately RGB(215,193,134), #D7C186; existing dark sand marks are approximately RGB(191,165,106), #BFA56A. Change these dark marks toward pale sandy ochre near RGB(205,182,123), #CDB67B, reducing their contrast against the base by about 60 percent. Make ALL dark dots and ripple strokes substantially softer and lighter. Keep a sandy warm hue; do not turn marks white. Preserve the original base color, pale highlights, exact grain and ripple shapes, exact positions, sizes, number, spacing, overall pixel grid, and all four edge contents. Do not redraw, move, add or remove any texture element. Do not globally brighten, wash out, or recolor the entire image. Preserve the square 1254x1254 canvas and edge-to-edge opacity, with no crop, margin, tile frame or extra scene. This is a production texture, not a comparison image. Only the darker marks become lighter. Output one texture image.

## 生成提示词

Use case: style-transfer. Asset type: one production desert/sand ground texture for the FlatWorld 2D top-down pixel-art sandbox. Input images: Image 1 is the existing dirt ground texture and Image 2 is the existing stone ground texture; both are STYLE REFERENCES ONLY, especially their sparse texture density and simple square pixel clusters. Primary request: redraw the plain sand ground into textured desert sand matching these ground tiles. Create exactly ONE square, edge-to-edge, fully opaque, seamless repeating texture, viewed straight down. Use a warm pale ochre sand midtone close to #DCC78E, slightly darker sand #C3AA73 and restrained pale highlights #E9D7A8; preserve a quiet sandy surface distinct from the brown dirt reference. Scatter a few tiny sand grain pixel clusters and 3-4 short, broken, shallow wind ripple marks, made of flat chunky square pixel steps. Ripple fragments should be only 3-6 logical pixels long, irregularly placed and oriented roughly sideways, with no long stripe extending across the entire texture. Keep at least 75 percent quiet base color; sparse details and gentle contrast comparable to the dirt and stone references. Design on a uniform 32x32 logical pixel grid, crisp square edges and flat solid color blocks, enlarged uniformly for output; marks must survive a 32x32 game import. Keep texture density uniform at all four edges for seamless tiling, no border, no vignette, no central object. Avoid gradients, dunes rendered as hills, realistic photo noise, antialiasing, brush strokes, heavy rocky cracks, stones, plants, footprints, structures, perspective, cast shadows, text, labels, gridlines, frames or watermarks. Output only the single square sand texture, no comparison sheet or repeated tile preview.

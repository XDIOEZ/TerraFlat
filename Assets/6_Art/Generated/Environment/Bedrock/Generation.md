# 基岩地表贴图

- 消费：`Tile_Bedrock` 的完整地面 Sprite，单帧，不透明，中心 Pivot，世界尺寸 1×1 格。
- 模式：内置 ImageGen 生成，再以内置 ImageGen 简化纹理密度；源 PNG 原样保存，不做本地像素编辑。
- 参考：实际查看 Stone.png 图集、Peat_Ground.png 和 Clay_Ground.png，沿用俯视地面与硬像素簇，基岩使用灰蓝色厚实岩层。
- 源图：1254×1254，RGB，2151 个可见颜色，无透明留白，主体边界为整张画布。
- 导入：Single Sprite、Full Rect、中心 Pivot、Point、无 Mipmap、无压缩；各平台 maxTextureSize=32，源 PPU=1254，按源图换算一格世界尺寸。源图颜色数不等于导入缩小后的颜色数。
- 引用：Tile_Bedrock.asset 使用新 Sprite，并恢复白色乘色。现有 TileBase 文件夹 Addressables 负责加载 Tile；Sprite 通过 Tile 的直接依赖进入构建。
- 静态资源接入，游戏内表现由用户验收。

## 最终编辑提示词

Simplify this bedrock texture strongly to match a small 32x32-pixel ground tile in an old-school top-down game. Keep the dark blue-charcoal rock identity but change only the detail density and palette complexity. EXACTLY a 32-column by 32-row logical pixel grid across the entire square. Every logical pixel is one large perfectly solid square; enlarge those 32x32 pixels sharply to fill the output canvas. Large chunky rock masses only: about 8 to 12 irregular geological slabs across the ENTIRE image, not 50 or 100 small stones. Slabs occupy roughly 6–12 logical pixels each. At most SIX flat colors. Color palette #263139 #35434c #45545e #56656d #61717a #29363e. Use restrained dark seams, extremely sparse one-logical-pixel highlights and large clean solid clusters. Remove tiny flecks, microtexture and multilevel shading. No smooth gradients or anti-aliasing. Seamlessly repeating in X and Y, edge-to-edge continuous stone, no rectangular border. Full canvas opaque; no transparent or empty pixels. No bricks, cubes, scene, icon, margin, letters or watermark. Final must look like a native 32x32 pixel tile displayed with nearest-neighbor, with large visible hard square pixels.

生成器没有严格输出六色和原生 32×32 文件，因此保留原始成图，并通过 Unity 导入尺寸适配现有地面消费尺寸；不将提示词目标当作已经达到的像素统计。

# 水体与地表

## 数据与刷新

- Ground/Liquid 独立合批，主路径 `ChunkGroundMeshRenderer/Chunk-Mesh-Water-Lit`，BRG 复用同一效果。外观来自 `LiquidDefinition.WorldWater`，岸线来自 LiquidDepth>0，水深四角连续插值；边界变更更新八邻共享边/角，深度 UV 用世界坐标和水层原点/边框，不用合批局部 POSITION。
- `ChunkTilemapRenderer` 的液体/相邻液体/材质变化只重提 Water Mesh；真实地形、邻块加载、全量绑定仍刷新 Ground/Back/Blocking。无岩浆时只看变化格是否新生岩浆，不因普通水流扫描全块；真实岩浆/平台遮挡照常重建，分帧绑定不绕预算。
- 天然水面低于同地势岸 1 层，不随 2～3 层底床起伏，抽干才露底；普通倒水仍高于底床 1 层，采挖/填土照常叠加、平台与地面基准齐平。高度层只读天然凹陷，不识别河流来源或改水量。
- `ChunkEnvironmentRenderer` 排除有液格，抽干恢复覆盖。`ChunkSnowCoverRenderer/ChunkSupportSurfaceRenderer` 只读所属层、释放自己的表现，支撑面不画阴影；覆雪 `_SnowCoverage` 用 MPB，脚印按自身寿命消退。无雪且无旧表现只查雪层正值，有雪复用有效地表查询；高低阴影位于地面/支撑/雪上、墙下。

## 材质与运动

- `WaterSurfaceCommon.hlsl` 共用深度/岸线/月光；UsePass 包装同步暴露新增属性。月光在 Light2D 后合成，月亮出现与月相亮度分开；写实连续水色、风格化十档，所有风格 `ResolveWaterSurfaceAlpha` 随深度 0.30→0.50且封顶0.50。岩浆使用 `LavaSurfaceCommon/LavaMode/LavaPatternTex` 独立分支。
- `WaterSurfaceFlow.hlsl` 共用完整输入：河流/实验 FlowX/Y 为四角实际速度，海洋为风向；只有 kind=2 标 ocean，kind=1/3 走河流。双相平流用世界坐标/二维噪声交接，保留速度大小；unity-voronoi-water 的 MIT-0 许可在 ThirdPartyNotices。
- `RiverSurface.hlsl`：Mochie FlowUV 与 Scrawk 格角局部方向混合，独立纹理法线/稀疏泡沫，不恢复噪声中值闭环描边；未量化世界坐标显式梯度避免格边 Mipmap 错采，泡沫先阈值后混透明。`RiverPattern_RGBA` 为线性 Repeat/Mipmap、不压缩数据图（RG斜率/B碎光/A泡沫），生成器 `Tools/Rendering/generate_river_surface_pattern.py`，许可 `RiverSurface-LICENSE.txt`。
- `_FlowSpeed` 仅流速倍率；`_WaveSpeed/_WaveDirection` 独立传播（零方向跟随水/风），`_FlowCycleSeconds` 为交接周期。湖泊无定向漂移但保留细波；潮汐 `_GlobalGameDay/_TideCyclesPerDay`，海风浪 `_OceanWaveTime/_OceanWaveFactors`。三种海洋风格禁噪声等值线/网状焦散，写实禁噪声扭曲。
- 风格化海洋 `OceanSurfaceStylized.hlsl` 共用 RiverPattern 亮纹/格角采样，海风/潮汐/白沫独立；`SampleDirectionalWaterCorner` 已接收乘过倍率的速度和独立泡沫覆盖，不再乘第二次。
- `WaterVisualSettings` 四风格保留水深 MPB/地形实例：stylized、realistic、unity-stylized-water、low-spec。Unity 分支 `FLATWORLD_WATER_UNITY_STYLIZED/UnityStylizedWaterSurface.hlsl` 使用四向法线/碎光/深浅色/泡沫，材质由 `ChunkView.unityStylizedWaterMaterial` 直接引用；数据图生成器 `generate_unity_stylized_water_patterns.py`，BSD-3-Clause 许可见 ThirdPartyNotices。
- 轻量 `Tilemap-Water-LowSpec-Lit/WaterSurfaceLowSpec.hlsl`、`FLATWORLD_WATER_LOW_SPEC` 与 `ChunkView.lowSpecWaterMaterial`：保留连续水深/透明度/线性水色/三角波流纹/静态岸线/2D 光照，编译绕过完整运动噪声、反射/焦散/月光/灯光遮罩；不得降低模拟频率。
- 四水体本地关键字用 `multi_compile_local_fragment` 保留运行时切换，不依赖 Tilemap 材质替 Shader.Find 的 Mesh/BRG 留变体。`FLATWORLD_CHUNK_CELL_BUFFER` 两条光照 Pass 必须 `multi_compile_local`，不能改 shader_feature_local；Shader 由 Resources/ChunkMesh-Contact-Lit.mat 引用，否则包内逐格地面会露黑。

## 角色接触

- `ActorWaterCommon.hlsl` 共用 Sprite-Lit-Master/AIECS，旋转附属物用角色世界水线；读取 `Mod_TileEffectReceiver` 有效淹没，浮动不改权威 LiquidDepth。接触独立于 Ground，液体身份变化才 Exit/Enter，邻格容错只用于交互。
- 掉落沉没先浸没再远离，SubmergedProgress 只缩表现矩阵，不改质量/体积/原 Scale；WaterElapsed 延续进度，权威层销毁，离水/拾取/回池恢复。

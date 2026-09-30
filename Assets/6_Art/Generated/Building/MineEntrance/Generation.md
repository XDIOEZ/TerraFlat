# 人工矿井入口贴图

- 资产：单帧世界建筑 Sprite，透明底，原始画布 1254 × 1254，没有缩放、裁剪或量化。
- 外形：横向椭圆井口、贴地木框、铁钉与包角、向地下延伸的木梯、分层变暗的内壁。
- 导入：Sprite Single、中心 Pivot、1254 PPU、Point、无 Mipmap、无压缩，保留默认物理轮廓；Transform Scale 为 1 时，整个画布为 1 × 1 世界单位。
- 参考：用户提供的游戏截图草图、Wall_Wood_Medium.png、GrassBed.png；旧 CaveEntrance_Natural.png 只参考石材颜色，不沿用拱洞造型。
- 生成方式：内置 imagegen，透明背景，参考图引导。
- 接入状态：人工入口 MineEntrance 与召唤器 MineEntrance_Summoner 的 JSON、迁移源 Prefab 共用本图，白色乘色、运行时视觉缩放为 1；本图以同路径地址注册为 ItemSprite。游戏内验收由用户进行。
- 空间层：入口本体与召唤器均为 `PlacementLayer=Ground`，使用独立 Layer2 占地；允许墙和普通建筑共格，不阻挡移动或视线，不生成局部光遮挡、太阳投影或脚底阴影。维度入口模块继续负责交互和存档。
- 绘制：`ground-building` 类别使用 Default/0，`Ground-Facility-Lit.mat` 使用 Queue2991，将入口画在地表覆盖之上、墙体与玩家之下；仍像地面一样接收环境明暗，井内深度由贴图自身表现。
- 维护约束：只有参与局部光遮挡的建筑需要 Sprite 物理轮廓；该入口关闭局部遮光，不再依靠物理轮廓完成安装。

## 完整生成提示词

```text
Use case: sketch-to-render.
Asset type: one transparent PNG pixel-art world sprite for FlatWorld, a 2D top-down sandbox game. Square 1:1 canvas, a compact footprint intended for exactly 1 by 1 world unit.
Input images: Reference 1 is the user's game screenshot; ONLY the red sketch at the right of the character defines the requested shape, camera and footprint: a horizontally oval hole in the ground with a ladder inside. Do not reproduce the screenshot, UI or red markings. Reference 2 (Wall_Wood_Medium) defines the game's warm timber palette, hard pixel clusters and outline treatment. Reference 3 (GrassBed) confirms the game's warm wooden material and sprite rendering style. Reference 4 is the OLD natural stone arch; it is only context for existing stone colors, DO NOT repeat its upright arch shape.
Primary request: render the user's sketch as a small MAN-MADE MINE SHAFT ENTRANCE sunk DOWN into the ground, viewed from the same overhead camera as the game. The main silhouette is a wide, horizontally flattened oval shaft opening, about 1.8 times wider than tall. Around the oval mouth, build a low sturdy timber collar/frame from chunky squared wooden beams joined with a few small iron nails or brackets. This collar lies close to ground level. Keep a clearly oval aperture visible inside the frame.
Subject detail: a single narrow wooden ladder inside the oval hole, about one third of the hole width, with two rails and 5 to 6 clearly readable rungs. Its top hooks onto the upper inner edge of the timber rim; it descends DOWN into the shaft, with the lower rungs narrower, darker and gradually lost in the deep nearly-black interior. Around the ladder reveal a few layered rough-cut earth/stone inner wall bands and wooden retaining supports; use discrete hard shadow color bands to show real depth below the surface. Rim and first rungs lit, lower recess dark. Rich enough to show worn timber grain, beam joints, a few rim stone chips and a couple of nails, while remaining simple and readable in a one-tile footprint.
Style/medium: authentic game pixel art matching the references, compact hard square pixel clusters, clear dark-brown/charcoal outlines, limited muted warm brown timber and desaturated gray-brown stone colors, subtle top-left highlight. Approximate 64 to 96 logical-pixel detail across the subject on a larger square output, with crisp visibly stepped pixels. About 20 to 28 deliberate solid colors. No anti-aliasing, no smooth rendering, no tiny noise texture.
Composition/framing: exactly ONE sprite centered in a square 1:1 canvas. Complete outline with modest transparent margins, using about 88 percent of canvas width and 68 percent height. Symmetric overall hole/frame massing with natural minor material irregularity. Strong wide oval opening and readable ladder as the main features. TRUE overhead/three-quarter top-down view, looking into the shaft, in the screenshot camera direction. Preserve empty dark space around the ladder to convey depth.
Scene/backdrop: genuinely transparent alpha outside the sprite; the dark hole interior is OPAQUE, not transparent. Rim earth/stone confined to the immediate lip, with no separate patch of ground.
Constraints: ground-level downward shaft, low wooden structural frame, horizontally oval opening, ladder visibly descending inside, single static sprite, square image, no text.
Avoid: upright cave arch, hillside cave, freestanding doorway, mine hut, roof, tall gantry, crane, bucket, sign, mining tools, water well, above-ground stone cylinder, stairs instead of ladder, second opening, flat hatch cover, character, grass background, scenery, cast shadow outside the object, decorative ground platform, red sketch lines, labels, watermark, checkerboard painted as background, photorealism, smooth illustration, glow, gradients, blur.
```


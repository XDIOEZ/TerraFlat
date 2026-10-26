# 熊单图素材


## 当前版本（2026-10-27）

- 按用户提供的玩家截图重新绘制，减少毛发碎纹理，轮廓与五官改用更粗的像素块。
- 工具：内置 `image_gen`，实际传入用户截图、`Bodies/Base_Male.png` 和旧熊图；前两张负责像素密度参考，旧熊图只负责物种、朝向和体态。
- 玩家基准：32×48、32 PPU，可见身体28×42像素；新熊以约60～64个逻辑像素表达全身宽度为绘制目标，靠近玩家每米32个逻辑像素的观感。
- 当前成品：`Assets/6_Art/Generated/Actors/Bear/Bear_Idle_Right.png`，1504×1046 RGBA；保留生成原图，未缩图、未全图限色。
- 当前导入：650 PPU、Point、无 Mipmap、无压缩、最大尺寸2048；单张全幅切片 `Bear_Idle_Right`，脚底 Pivot `(0.5,0.06022944)`。
- Alpha > 128 的主体范围：`(80,117)–(1470,983)`，约2.14×1.33米；沿用原贴图GUID、切片身份和Addressables地址。
- 仅更新素材与导入配置；用户PlayMode中不主动刷新或切换播放状态。

### 当前实际提示词

Use case: style-transfer.
Asset type: exactly ONE transparent single-frame game creature sprite for FlatWorld, a 2D top-down sandbox.
Inputs: Image 1 is the user's in-game screenshot: the PLAYER IN THE CENTER is the authoritative reference for pixel density, large pixel steps, dark outline weight and connected simple shading; ignore all UI, ground and other objects. Image 2 is the actual native player body sprite, 32 by 48 pixels at 32 pixels per world metre; it is the authoritative logical pixel scale. Image 3 is the existing brown bear to REPLACE: retain its species, brown color, right-facing four-legged neutral stance, shoulder hump, round ears, short muzzle, tiny tail and approximate natural proportions, but completely redraw its rendering at the player's MUCH COARSER logical pixel density.
Primary request: REDRAW the bear as true low-resolution chunky pixel art. The old bear has FAR TOO MANY small fur marks and tiny shading steps. The new visible bear should contain only approximately 60 to 64 logical square pixels across and 42 to 46 logical pixels high, so a two-metre-long bear has the SAME sized world-space pixel blocks as the 32-PPU player. This is a coarse sprite, NOT a detailed illustration with a pixel filter.
Render those few logical pixels as clearly enlarged equal-sized square blocks with crisp hard staircase edges. All marks, including the eyes, nose, paw claws, ears and outline, must obey the SAME coarse grid. Use the player's strong dark brown/near-black ONE-LOGICAL-PIXEL outline, and only a few connected large brown shading areas: muted medium brown main coat, dark underside/far legs, warm restrained shoulder highlight. The face is only a tiny eye, broad dark nose and short simple muzzle. Fur is shown by the silhouette and at most a handful of big clusters, with NO individual hairs, NO repeated fur dashes, NO small noisy highlights, NO thin texture lines, NO fine edge pixels. Keep a calm adult quadruped brown bear, not a toy or teddy, with four short robust readable legs and natural body proportions. Facing RIGHT (+X), three-quarter side view with only the same slight top-down angle as existing game animals.
Composition: only one complete full-body bear, compact and centered, all feet and ears visible, comfortable transparent margins; use a wide animal silhouette within the canvas. Background must be genuinely transparent alpha. No player in output, no scenery, no floor or cast shadow, no props, no UI, no text, no sprite sheet, no animation variants, no blur, no antialiasing, no smooth gradients, no photographic or painterly detail. Produce the coarse artwork directly; do not preserve the dense texture from Image 3.

## 初版记录（已替换）

- 工具：内置 `image_gen`，透明背景。
- 成品：`Assets/6_Art/Generated/Actors/Bear/Bear_Idle_Right.png`，原始 1254×1254 RGBA，未缩图、未限色。
- 导入：600 PPU、Point、无 Mipmap、无压缩、最大尺寸 2048；一张全幅切片 `Bear_Idle_Right`，Pivot `(0.5, 0.13158)` 对齐脚底。
- 主要可见范围（Alpha > 128）：`(30, 222)–(1244, 1089)`，约 2.02×1.45 米；站姿朝右，运行时镜像和单图摇摆。

### 初版实际提示词

Use case: stylized-concept. Asset type: ONE single-frame transparent game creature sprite for FlatWorld, a 2D top-down wilderness sandbox. Primary request: a calm adult brown bear, realistic natural quadruped proportions, stocky deep torso, shoulder hump, stout short legs with four readable paws, small round ears, short broad muzzle, tiny tail, facing RIGHT (+X) in a clean three-quarter side view with only a slight top-down angle; neutral standing idle pose suitable for procedural sway movement. Style: low-saturation, grounded retro pixel art, clearly coarse large square pixel clusters and hard stepped edges, simplified connected color masses, restrained warm brown fur with dark brown underside and a few muted tan highlights, upper-left lighting. Match a small forest-animal sprite game's readable silhouette and simple low logical resolution, approximately 64-80 visible logical pixels across the bear, rendered as crisp enlarged pixel blocks without blur. Composition: exactly one complete full-body bear centered, body fills about 80% of a square image, comfortable empty transparent margin around all feet, ears and muzzle. True transparent alpha background. Constraints: one image only, one bear only, no animation grid, no duplicate views, no text, no frame, no props, no scenery, no ground plane, no cast shadow, no glow, no blurred shading, no dense individual hairs, no painterly texture, no cartoon teddy-bear proportions, no roaring or attacking expression.

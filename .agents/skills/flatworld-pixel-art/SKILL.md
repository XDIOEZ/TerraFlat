---
name: flatworld-pixel-art
description: "FlatWorld 运行时像素 Sprite、动画表、物品与建筑美术及 Unity 导入；高清立绘使用 flatworld-portrait-art。"
---

# FlatWorld 运行时像素美术

## 资源与画风

- 同类运行时参考位于 `Assets/6_Art/Generated/<System>/`，轮廓、像素簇、描边、配色、光源、视角和世界尺度由实际使用的同类素材确定。画布与像素密度由消费方决定，没有统一尺寸。
- 角色锚点：`assets/merchant-style-anchor.png`、`assets/merchant-game-sprite-anchor.png`；工业金属风格锚点见 [style-guide.md](references/style-guide.md)。新像素图以硬边色块为主；金属面板可用锚点中的轻微明暗，半透明特效按消费方规格。
- 输出目录 `Assets/6_Art/Generated/<System>/<Name>/`，单帧常用 `<Name>_<State>_<Direction>.png`。用户指定原图直用时保留 PNG；透明留白可能承担 Pivot、基线及世界尺度，不默认紧裁。
- AI 动物/怪物表只含一套标准侧向动画（默认 Left）和特殊动作；Right 由 Unity `flipX/Visual.FlipX` 镜像，不混入上下或正背面组。帧网格、身份、比例、调色板、光源、基线和 Pivot 保持一致。参考表：`Assets/6_Art/Characters/MinifolksForestAnimals/Outline/MiniWolf.png`。

## 导入与消费方契约

- Sprite 导入使用 Point、无 Mipmap、无压缩、Alpha Transparency；PPU 按源图像素与目标世界尺寸换算。`maxTextureSize` 缩小导入时 Unity 同比调整实际 PPU，导入器不能直接填缩小后的目标像素数。
- 世界实体常用底部中心 Pivot，图标用中心；手持工具对齐握柄，并结合 Render 层级及 `visual.rendererLocalPosition`，避免重复偏移。左上像素中心 `(x,y)` 对应 Unity Pivot `((x+0.5)/width, 1-(y+0.5)/height)`。
- 铺满整格的 Tile/连续工业方块使用全幅不透明画布、中心 Pivot 和实际一格 PPU；独立轮廓图使用透明背景。地墙共享 TileBase/图集时，外观替换以具体消费引用为界。
- 局部光遮挡建筑依赖 Sprite 物理轮廓，自动轮廓为 `spriteGenerateFallbackPhysicsShape: 1`；Ground 或 `LightOcclusionMode=None` 不参与遮光。轮廓与 JSON Collider、占地是不同契约。
- JSON 的 `visual.spriteAddress` 可覆盖外壳 Sprite；新成图还受 `visual.color`、继承缩放影响。Addressables 条目的地址、子 Sprite 身份及 `ItemSprite` 标签与消费引用一致；外部修改分组 YAML 后需重导入，以刷新 Fast Mode Locator。静态目录诊断入口：`FlatWorld/诊断/检查 Addressables 目录`。
- 通用占位图为 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，384×384、全幅切片、中心 Pivot、384 PPU，标签 `ItemSprite`。正式图替换具体物品引用；资源 GUID 独立保留。
- 替换由资源构建器生成的占位图时，同步让构建器只导入、配置和注册正式成图，避免重建时重新绘制旧占位图。

## 设备图层

- 机身、召唤器和库存图标只画完整主体；外部管道/法兰、轴接口和电线由连接表现层绘制。端口位置、朝向和拓扑来自配置，连接件保留标准尺寸，由机身遮住内部部分。
- `Fit(sprite,width,height)` 按宽高分别缩放，机身画布比例与目标比例一致；当前方形流体节点用正方形、中心 Pivot。内部植物或库存内容是独立状态图层。
- 连续轴/电线的 Alpha、轴线和接头颜色延伸到对应格边；电线由无封头线段及统一接线头组合，避免邻格黑边。齿轮旋转层采用各方向一致的材质明暗，固定投影归独立图层；输入杆保留藏在齿轮下的连接段。
- 通用管道使用银蓝粗管、青色管箍和八边螺栓法兰，与橙铜细电线保持轮廓和配色差异；16方向切片保留掩码身份，格边排除邻片串入且切片宽高/PPU保持一格。

## 按需资料与工具

- 玩家换装母版位于 `Assets/6_Art/Generated/Characters/PlayerTemplate/`：先参考正式男女母角色和旅人/铁卫套装，沿用32×48、32 PPU、底中心Pivot及Profile部位分界；`README.md`说明重建入口。外观源图必须可读写且不进SpriteAtlas，`visual.spriteStates.wearableMale/wearableFemale`供装备槽按部位拼合；不再沿用早期大头候选图。

- [style-guide.md](references/style-guide.md)：分类画风及参考图。
- [prompt-recipes.md](references/prompt-recipes.md)：生成与动画提示词。
- [validate_pixel_asset.py](scripts/validate_pixel_asset.py)：PNG 透明度、尺寸、颜色统计。独立轮廓可用 `--require-alpha --require-transparent-corners`；硬 Alpha、颜色数和 `--exact-size` 仅对应真实规格，满格 Tile 不用透明四角规则。

# 运行时 Sprite 与接入契约

## 输出与动画

- 输出目录 `Assets/6_Art/Generated/<System>/<Name>/`，单帧常用 `<Name>_<State>_<Direction>.png`。画布、像素密度和世界尺度分开处理，按实际消费方决定。
- AI 动物/怪物表只含一套标准侧向动画（默认 Left）和特殊动作；Right 由 Unity `flipX/Visual.FlipX` 镜像，不混入上下或正背面组。参考表为 `Assets/6_Art/Characters/MinifolksForestAnimals/Outline/MiniWolf.png`。
- 动画帧网格、身份、比例、调色板、光源、基线和 Pivot 一致；其他角色的方向数量以真实 Animator/消费方为准。
- 独立轮廓使用透明背景，普通不透明像素主体默认硬 Alpha `0/255`。烟雾、玻璃、火焰等按实际需要保留半透明；满格 Tile/连续工业方块保持全幅不透明，不套用透明四角规则。

## 导入与引用

- Sprite 使用 Point、无 Mipmap、无压缩、Alpha Transparency；PPU 按源图像素与目标世界尺寸换算。`maxTextureSize` 缩小导入时 Unity 同比调整实际 PPU，导入器不能直接填缩小后的目标像素数。
- 世界实体常用底部中心 Pivot，图标用中心；手持工具对齐握柄，并结合 Render 层级及 `visual.rendererLocalPosition`，避免重复偏移。左上像素中心 `(x,y)` 对应 Unity Pivot `((x+0.5)/width, 1-(y+0.5)/height)`。
- 铺满整格的 Tile/连续工业方块使用中心 Pivot 和实际一格 PPU；地墙共享 TileBase/图集时，替换以具体消费引用为界。
- 局部光遮挡建筑依赖 Sprite 物理轮廓，自动轮廓为 `spriteGenerateFallbackPhysicsShape: 1`；Ground 或 `LightOcclusionMode=None` 不参与遮光。轮廓与 JSON Collider、占地是不同契约。
- JSON 的 `visual.spriteAddress` 可覆盖外壳 Sprite；成图还受 `visual.color`、继承缩放影响。Addressables 地址、子 Sprite 身份及 `ItemSprite` 标签须与消费引用一致；外部修改分组 YAML 后需重导入以刷新 Fast Mode Locator。静态目录诊断入口：`FlatWorld/诊断/检查 Addressables 目录`。
- 通用占位图为 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，384×384、全幅切片、中心 Pivot、384 PPU，标签 `ItemSprite`。正式图替换具体物品引用，独立保留资源 GUID；不把占位图尺寸当作所有正式素材的默认值。
- 替换构建器生成的占位图时，同步让构建器只导入、配置和注册正式成图，避免重建时重新绘制旧占位图。

## 设备图层

- 机身、召唤器和库存图标只画完整主体；外部管道/法兰、轴接口和电线由连接表现层绘制。端口位置、朝向和拓扑来自配置，连接件保留标准尺寸，由机身遮住内部部分；不烘入接口，也不改成孔洞或替代短桩。
- `Fit(sprite,width,height)` 按宽高分别缩放，机身画布比例须与目标比例一致；当前方形流体节点用正方形、中心 Pivot。内部植物或库存内容属于独立状态图层。
- 连续轴/电线的 Alpha、轴线和接头颜色延伸到对应格边；电线由无封头线段及统一接线头组合，避免邻格黑边。齿轮旋转层采用各方向一致的材质明暗，固定投影归独立图层；输入杆保留藏在齿轮下的连接段。
- 通用管道使用银蓝粗管、青色管箍和八边螺栓法兰，与橙铜细电线区分；16 方向切片保留掩码身份，格边排除邻片串入且切片宽高/PPU 保持一格。

## 玩家换装

- 正式素材位于 `Assets/6_Art/Generated/Characters/PlayerTemplate/`：`Bodies` 无发人物底图、`Hair` 独立发型、`Wearables` 男女共用空装备、`Icons` 独立物品图标；装备不含脸、皮肤、头发。人物/发型/装备/遮罩统一 32×48、32 PPU、底中心 Pivot、Read/Write、FullRect 且不进 SpriteAtlas；标定与重建入口见该目录 `README.md`。
- `visual.spriteStates.wearable` 按公共画布 Alpha 叠加；可选 `wearableHairMask`/`wearableBodyMask` 用 Alpha255 隐藏、0 保留对应层。半包围头盔开口透明露原脸，只裁头发；不按装备名或性别写特例，不分区替换头部。`spriteAddress` 仍是独立图标。
- 母版朝向由 `CharacterAppearanceProfile.SourceFacesLeft` 统一转换，不反转鼠标转身逻辑。

## 可选检查工具

[validate_pixel_asset.py](../scripts/validate_pixel_asset.py) 提供 PNG 尺寸、颜色与透明度统计。仅在用户需要检查时运行，并按真实规格启用参数：独立轮廓可用 `--require-alpha --require-transparent-corners`；硬 Alpha、颜色上限和 `--exact-size` 不能跨类别机械套用。

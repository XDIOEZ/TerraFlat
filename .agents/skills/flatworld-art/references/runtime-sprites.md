# 运行时素材

## 动画与导入

- AI 动物/怪物仅一套标准侧向（默认 Left）及特殊动作，Right 用 `flipX/Visual.FlipX`；不混正背/上下方向。布局参考 `Assets/6_Art/Characters/MinifolksForestAnimals/Outline/MiniWolf.png`；其他角色方向数服从消费方。
- 帧网格、比例、配色、光源、基线和 Pivot 一致；单帧命名 `<Name>_<State>_<Direction>.png`。普通主体 Alpha 为 0/255，玻璃、烟火等按需半透明。
- Sprite：Point、无 Mipmap、无压缩、Alpha Transparency。PPU 按源像素/世界尺寸换算；`maxTextureSize` 缩小时 Unity 同比调整实际 PPU，不能直接填缩小后的目标像素数。
- 世界实体常用底中 Pivot，图标居中；工具对齐握柄，结合 Render 层级和 `visual.rendererLocalPosition` 防止重复偏移。左上像素中心 `(x,y)` 的 Pivot 为 `((x+0.5)/width,1-(y+0.5)/height)`。
- 满格 Tile/工业方块用中心 Pivot、一格 PPU；共享 TileBase/图集的地墙按具体消费引用替换。

## 引用与遮光

- 建筑遮光轮廓用 `spriteGenerateFallbackPhysicsShape: 1`；Ground、`LightOcclusionMode=None` 不遮光。物理轮廓、JSON Collider、占地分别配置。
- `visual.spriteAddress` 可覆盖外壳，`visual.color`、继承缩放也影响成图。Addressables 地址、子 Sprite、`ItemSprite` 标签须匹配引用；外改分组 YAML 后重导入刷新 Fast Mode Locator。诊断：`FlatWorld/诊断/检查 Addressables 目录`。
- 占位图：`Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，384×384、全幅、中心 Pivot、384 PPU、`ItemSprite`。正式图替换具体引用并保留独立 GUID；同步构建器为仅导入/配置/注册，避免重绘占位图。

## 设备图层

- 机身、召唤器、库存图标只画闭合主体，不烘入外部管道/法兰、轴口或电线，也不留替代孔洞/短桩。连接位置/拓扑来自配置，标准连接件由机身遮住内部；植物与库存内容独立分层。
- `Fit(sprite,width,height)` 分别缩放宽高，画布比例必须匹配目标；方形流体节点用正方形、中心 Pivot。
- 轴/电线延伸到格边；电线用无封头线段与统一接头，避免接缝黑边。齿轮旋转层明暗方向一致、固定投影独立，输入杆保留齿轮下的连接段。
- 管道为银蓝粗管、青色管箍、八边螺栓法兰，与橙铜细电线区分；16 方向切片保留掩码身份、格边无邻片串入，宽高/PPU 保持一格。

## 玩家换装

- `Assets/6_Art/Generated/Characters/PlayerTemplate/`：`Bodies` 无发底图、`Hair` 发型、`Wearables` 男女共用空装备、`Icons` 图标；装备不含脸/皮肤/头发。人物/发型/装备/遮罩均为 32×48、32 PPU、底中 Pivot、Read/Write、FullRect、不进 SpriteAtlas；重建与标定见目录 `README.md`。
- `visual.spriteStates.wearable` 按公共画布 Alpha 叠加；`wearableHairMask`/`wearableBodyMask`：255 隐藏、0 保留。半包头盔开口透明露原脸、只裁头发；不按装备名/性别写特例，不分区替头；`spriteAddress` 仍是图标。
- `CharacterAppearanceProfile.SourceFacesLeft` 统一母版朝向，不反转鼠标转身逻辑。

可选统计工具：[validate_pixel_asset.py](../scripts/validate_pixel_asset.py)。透明四角仅适用独立轮廓；硬 Alpha、颜色数、精确尺寸按真实规格启用。

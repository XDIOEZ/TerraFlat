# 运行时素材

## 动画与导入

- 游戏统一以右（+X）为默认朝向，新增人物、动物及手持武器/工具素材按右向基线制作；AI 动物/怪物仅一套标准右向及特殊动作，朝左由运行时镜像，不混正背/上下方向。既有源图的真实朝向由 `visual.flipX` 等资源配置先校正为右向，不反转移动、瞄准或攻击方向。布局参考 `Assets/6_Art/Characters/MinifolksForestAnimals/Outline/MiniWolf.png`；其他角色方向数服从消费方。
- 帧网格、比例、配色、光源、基线和 Pivot 一致；单帧命名 `<Name>_<State>_<Direction>.png`。普通主体 Alpha 为 0/255，玻璃、烟火等按需半透明。
- Sprite：Point、无 Mipmap、无压缩、Alpha Transparency。PPU 按源像素/世界尺寸换算；`maxTextureSize` 缩小时 Unity 同比调整实际 PPU，不能直接填缩小后的目标像素数。
- 大小验收用 [Unity 单位尺寸检测器](../../../../Tools/Art/SpriteWorldSizeValidator.md)，按 Alpha 可见像素、PPU 和消费方缩放计算，设置本类物品目标或对照参考；不以画布像素数判断大小统一，也不为通过检测缩图。
- **禁止强制压缩贴图。** 默认保留用户认可原图的分辨率、色阶与 Alpha，仿粗像素风也可使用 500 多、1000 多像素原图；未经明确要求不得缩到 32×32 等尺寸、全图限色或启用有损纹理压缩，主设置与平台覆盖的 `maxTextureSize` 不得迫使原图缩小。按源尺寸换算 PPU 控制手持大小；弩 `Crossbow_Wood_Icon.png` 使用 1672×941 原图、1672 PPU。
- 世界实体常用底中 Pivot，图标居中；工具对齐握柄，结合 Render 层级和 `visual.rendererLocalPosition` 防止重复偏移。左上像素中心 `(x,y)` 的 Pivot 为 `((x+0.5)/width,1-(y+0.5)/height)`。
- 满格 Tile/工业方块用中心 Pivot、一格 PPU；共享 TileBase/图集的地墙按具体消费引用替换。

## 引用与遮光

- 建筑遮光轮廓用 `spriteGenerateFallbackPhysicsShape: 1`；Ground、`LightOcclusionMode=None` 不遮光。物理轮廓、JSON Collider、占地分别配置。
- `visual.spriteAddress` 可覆盖外壳，`visual.color`、继承缩放也影响成图。Addressables 地址、子 Sprite、`ItemSprite` 标签须匹配引用；外改分组 YAML 后重导入刷新 Fast Mode Locator。诊断：`FlatWorld/诊断/检查 Addressables 目录`。
- 占位图：`Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，384×384、全幅、中心 Pivot、384 PPU、`ItemSprite`。正式图替换具体引用并保留独立 GUID；同步构建器为仅导入/配置/注册，避免重绘占位图。

## 设备图层

- 机身、召唤器、库存图标只画闭合主体，不烘入外部管道/法兰、轴口或电线，也不留替代孔洞/短桩。连接位置/拓扑来自配置，标准连接件由机身遮住内部；植物与库存内容独立分层。
- `Fit(sprite,width,height)` 分别缩放宽高，画布比例必须匹配目标；方形流体节点用正方形、中心 Pivot。
- 普通木门、石门与飞船门统一用方形满格闭合图，中心 Pivot、一格 PPU、零渲染偏移。`UseItemSprite=true` 时 `DoorSlideVisual` 沿画布中央竖缝拆成左右两半，框也随各半图滑动；不再绘制或消费独立开启状态图，不能画跨过中缝的锁栓。
- 轴/电线延伸到格边；电线用无封头线段与统一接头，避免接缝黑边。齿轮旋转层明暗方向一致、固定投影独立，输入杆保留齿轮下的连接段。
- 管道为银蓝粗管、青色管箍、八边螺栓法兰，与橙铜细电线区分；16 方向切片保留掩码身份、格边无邻片串入，宽高/PPU 保持一格。

## 玩家换装

- `Assets/6_Art/Generated/Characters/PlayerTemplate/`：`Bodies` 无发底图、`Hair` 发型、`Wearables` 男女共用空装备、`Icons` 保留分层构建器导出的旧图标；装备不含脸/皮肤/头发。人物/发型/装备/遮罩均为 32×48、32 PPU、底中 Pivot、Read/Write、FullRect、不进 SpriteAtlas；重建与标定见目录 `README.md`。
- 当前装备物品图标位于 `Assets/6_Art/Generated/Equipment/<id>/<id>_Icon.png`，与人物穿戴层独立；清单和导入规格见该目录 `README.md`，不能将图标直接用作穿戴层。
- 钢套使用 `Wearables/Steel_{Helmet,Cuirass,Gauntlets,Greaves,Boots}.png` 五张独立穿戴层；钢甲业务 ID 仍为 `Chestplate_Iron`。源图、采样参数及静态穿戴预览在 `Source/SteelArmor/`，不混入旧 `LayeredOutfits` 母版构建。整套使用同一组六色调色板和区域投票取色，保持连续钢面与完整手脚轮廓，避免单点采样产生棋盘高光及零散像素；局部修图同时看全套是否一致。
- 藤甲、木甲、工程护目镜和宇航服五件也有同 ID 的独立穿戴图，源图在 `Source/EquipmentCompletion/`；宇航服当前以 `Spacesuit_PixelMaster.xcf` 和 `Spacesuit_Pixels.json` 的原生 32×48 像素为准，五件用 GIMP 一像素铅笔重画，不再按旧大图采样记录导出；保留用户认可的未统一加黑边版本。护目镜不隐藏头发，宇航服头盔使用既有头发遮罩，透明开口露原脸。启用物品目录当前为 23 件人物穿戴装备；木盾走 `Mod_ShieldCombat` 的独立副手显示，不能仅按缺少 `wearable` 字段误判缺图。
- 皮装、铁甲、钢甲、藤甲、木甲和护目镜均保留原图及原调色板；修复只在明确缺陷处用一像素铅笔补点，不做全图限色或重新分配明暗。局部修补记录在 `Source/EquipmentCompletion/Wearable_Retouches.json`，逐点记录局部色阶修顺和透明漏点修补；正常细节保持原样，可以成片整理但不改造整套设计。
- `visual.spriteStates.wearable` 按公共画布 Alpha 叠加；`wearableHairMask`/`wearableBodyMask`：255 隐藏、0 保留。半包头盔开口透明露原脸、只裁头发；不按装备名/性别写特例，不分区替头；`spriteAddress` 仍是图标。
- 眼镜使用独立 `Eyes` 槽；`Mod_CharacterAppearance` 在 `Head` 后叠加眼部穿戴层，同戴头盔复用同一画布与原脸，不新增人物底图。
- `CharacterAppearanceProfile.SourceFacesLeft` 描述整套母版的真实源朝向，合成后统一朝右；当前人物、头发、装备及遮罩共用朝左源坐标，须保留该标记，不能逐件翻图或反转鼠标转身逻辑。

### 既有装备的像素细修

“像宇航服那样修改”指沿原图把像素关系修顺，保留装备原本的设计和质感；不代表允许把其他装备重画成同一套平面色块。修改范围可以较大，判断依据是原版细节是否得到改善，而不是改了多少像素。

- **先认清原图。** 同时看单件原图、最近邻放大图和穿在人物上的组合，分清材料纹理、衣料褶皱、金属高光、绑带与真实杂点。细小高光和孤立暗点不自动等于噪点。
- **沿原结构修。** 可成片整理断开的轮廓、接缝、高光和细小色阶，补透明漏点，让相邻像素簇更连贯；保留原造型、侧向姿势、主配色、材质纹理和明暗层次。正常部位保持原样，不为凑数量修改。
- **保留颜色层次。** 优先取原图同一材质、同一受光面的邻近颜色；不为追求“低像素”全图压成六色或八色，也不抹掉渐进色阶、重铺大色块或统一补黑框。新素材的大色块约定不能代替既有素材的细修判断。
- **GIMP MCP 原位落笔。** 以完整原图为底层，另建修补层，用原生 `1. Pixel` 铅笔处理细节；一像素是笔刷精度，不是总改动范围的上限。沿用原画布、PPU、Pivot、留白和穿戴锚点，不通过再次缩小图片完成修复。本机给 `Gegl.Color.new` 传精确颜色时使用 `#RRGGBB`，避免整数 `rgb(...)` 被截成白色。
- **保留可复查的差量。** 原图层和修补层分别保存，记录原图版本、修改坐标、颜色与原因；批量执行仅应用明确的修补点，不重新生成全图。现有记录为 `Source/EquipmentCompletion/Wearable_Retouches.json`，入口为 `Tools/Art/PlayerWearablesGimp.py`。
- **对照结果。** 看原尺寸、最近邻放大和整套穿戴对比，确认细节仍像原版、局部更连贯；核对修补范围外的像素没有被改写，面部开口与透明关系正确。保留用户已认可的宇航服版本，其他装备按各自材料处理；游戏内观感由用户验收，不操作用户正在使用的 PlayMode。

### 铁套穿着图基准

用户于 2026-10-24 指定以后创建或修改装备穿着图时，参考这套铁头盔、铁胸甲、铁护手、铁腿甲、铁战靴的大小、侧向姿势及像素质量。正式参考是 `IronGuard_*` 五件穿戴层；`Steel_*` 是另一套钢甲，`Chestplate_Iron` 当前也是钢甲，不能按名称误选。

- 先打开[用户认可的游戏上身截图](../assets/iron-armor-worn-anchor.png)，看整套比例和像素观感；再打开对应部位的穿戴原图及 `Bodies/Base_Male.png` 或 `Bodies/Base_Female.png`，确定公共坐标。截图有游戏光照，只参考形体、姿势和像素颗粒，不采样夜间暗色或复制 UI。
- 原图根目录：`Assets/6_Art/Generated/Characters/PlayerTemplate/`。五件均在 `Wearables/`，按下表选对应参考；整套绘制时查看全部五件。

| 部位 | 穿戴原图（相对上述根目录） |
| --- | --- |
| 铁头盔 | `Wearables/IronGuard_Helmet.png` |
| 铁胸甲 | `Wearables/IronGuard_Cuirass.png` |
| 铁护手 | `Wearables/IronGuard_Gauntlets.png` |
| 铁腿甲 | `Wearables/IronGuard_Greaves.png` |
| 铁战靴 | `Wearables/IronGuard_Boots.png` |

- 大小与摆放：沿用 32×48 公共画布、身体比例、各部位覆盖范围和透明留白；对齐脸口、肩、腰、手腕、膝及脚底。保留铁套原图的侧向角度、前后肢关系及站姿，不改成正面展示或纯侧面剪影，也不把独立部件居中放大铺满画布。源图仍共用朝左坐标，由 Profile 统一转向。
- 像素质量：以铁套穿戴原图的原生像素为准，保持同样粗细的硬边像素簇、轮廓重量、少量连续明暗和克制高光；不靠放大高分辨率插画、柔化、密集纹理或零散亮点增加“精细度”。新装备保留自己的材料、配色和科技阶段，只沿用参考的比例、姿势和像素语言。
- 给 AI 绘图工具实际传入上身截图、对应铁套穿戴原图和人物底图，逐张注明职责；不能只在提示词里写“参考铁套”或填写本地路径而不传图。头盔单独传头盔参考，胸甲传胸甲参考，手、腿、鞋同理；输出仍只含装备，透明露出原人物。
- 装备栏图标另按图标规则绘制，不把右侧背包图标当作穿戴层参考；截图中的整个人物也只作上身效果参考，不作为带脸、皮肤或头发的成品装备层。

可选统计工具：[validate_pixel_asset.py](../scripts/validate_pixel_asset.py)。透明四角仅适用独立轮廓；硬 Alpha、颜色数、精确尺寸按真实规格启用。

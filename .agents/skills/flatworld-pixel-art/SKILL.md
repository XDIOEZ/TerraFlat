---
name: flatworld-pixel-art
description: "为 FlatWorld 生成、重绘、转换或评审运行时像素美术，并处理画风一致性、动画一致性、透明 PNG 校验和 Unity Sprite 导入。Use when: 游戏内小人、玩家或 NPC Sprite、AI 控制的动物或怪物、俯视像素角色、物品、工具、树木、建筑、世界道具、图标、Pixel Art、Sprite、Sprite Sheet 或动画帧。AI 生物贴图默认只制作一套标准侧向动画与特殊动作，另一侧由 Unity 水平镜像，不生成上下/正背面混合表。运行时素材的画布与像素密度由实际消费方决定，用户指定的成品贴图可原样接入。不要用于高清角色立绘、对话人物图或 UI 人物展示；这些任务使用 flatworld-portrait-art。"
---

# FlatWorld 运行时像素美术

## 必读

- 每次完整读取 `references/style-guide.md`；编写 ImageGen 提示词、生成或制作变体时，再完整读取 `references/prompt-recipes.md`。
- 绘图前先确定目标的消费系统，进入 `Assets/6_Art/Generated/<System>/` 查看该系统已有的道具与实体贴图；实际打开并比较同类别、同世界尺度的参考图，而不只看文件名。优先看已在游戏中使用的素材；同系统参考不足时，再从项目其他目录选同类别素材，不能用占位图或概念图代替画风依据。
- 比较参考图时重点核对轮廓、像素簇、描边、配色、明暗、视角和世界尺度。角色类可额外使用 `assets/merchant-style-anchor.png` 理解角色设计语言，并使用 `assets/merchant-game-sprite-anchor.png` 参考小尺寸可读性和对齐，但它们都不得强制其他素材采用相同画布或像素密度。
- 生成或编辑位图时同时使用系统 `imagegen` Skill，并遵守其参考图、透明背景、输出路径与结果检查规则。
- 生成 AI 控制的动物或怪物时，优先参考 `Assets/6_Art/Characters/MinifolksForestAnimals/Outline/MiniWolf.png` 这类现有生物表：同一张 Sprite Sheet 只放一套标准侧向动画（默认向左）和需要的特殊动作组，再交给 Unity 按统一网格切割；向右由 Unity 的水平镜像（如 `SpriteRenderer.flipX` 或项目的 `Visual.FlipX`）得到，不重复生成右向帧。这里的向左/向右是生物头部和身体朝屏幕左/右侧的侧视，不是画布位置；禁止加入正面、背面、朝上或朝下组。
- 若目标是高清角色立绘、对话人物图或 UI 人物展示，停止套用本 Skill，改用 `flatworld-portrait-art`。同一任务需要两类资产时分别生成、命名、验收和导入。

## 工作流

1. 明确所属系统、运行时类别、消费位置、朝向、动作、帧布局、逻辑尺寸与 Pivot；AI 生物必须规划同一张表中的一套标准侧向帧和特殊动作分组，默认生成 `Left`，由 Unity 镜像得到 `Right`。接入现有 Animator、Tile、Prefab 或 UI 图标前先检查消费方契约，不凭空假定切片网格。
2. 依据实际查看的同类别项目素材确定比例、视角和画风；角色、动物、物品、树木和建筑不得强行共用比例。画布和逻辑像素密度由消费方契约与最近的同类现有素材共同决定，不预设统一尺寸，也不因参考素材较小就缩小用户指定的成品图。新绘制素材即使使用更高像素密度，仍应保持同类素材的剪影、像素簇、描边、色块、光源和材质表达。
3. 需要设计探索时可先生成 `<Name>_Concept_HighRes.png` 作为内部设计源；是否将它用作运行时纹理由消费方契约和用户选择决定，不预设缩小步骤。
4. 只对需要处理的源图执行必要编辑，例如移除色键或按明确的帧布局调整画布。用户指定直接使用的成品贴图保持原始 PNG，不默认裁剪、缩放、量化或硬化 Alpha；需要改变尺寸时，先核对消费方的画布、Pivot 和基线，再选择适合的缩放方式。
5. 新绘制素材按消费方契约放置主体并清理残边、孤立像素与透明孔洞；用户指定原图直用时只检查并报告问题，不擅自修改像素。目标画布中的透明留白属于 Pivot、动画基线和世界尺度契约，不得在没有检查消费方的情况下紧裁删除。
6. 在 1 倍和至少 8 倍最近邻预览下检查剪影、眼睛、脚底、手持物、身份配件和像素簇。动画帧还必须保持身份、比例、轮廓、调色板、光源、基线与 Pivot 一致；AI 生物的标准侧向组和特殊动作组不能混入正面、背面、朝上或朝下帧，并确认镜像后仍保持 Pivot、握持点和动作方向契约。
7. 保存到 `Assets/6_Art/Generated/<System>/<Name>/`，运行时单帧默认命名 `<Name>_<State>_<Direction>.png`；除非用户明确要求替换，不覆盖旧素材。
8. 配置 Unity 时使用 `Sprite (2D and UI)`、Point、关闭 Mipmap、关闭纹理压缩并启用 Alpha Transparency。PPU 按实际画布像素与目标世界尺寸确定，使场景中的物体大小与同类素材一致，无须为沿用旧 PPU 而缩小 PNG。角色/世界实体通常使用底部中心 Pivot，图标使用中心 Pivot；Sprite Sheet 切片服从现有消费方。
9. 对新绘制的运行时 Sprite 按消费方要求静态校验透明度、边角、颜色规模与硬 Alpha；只有明确的尺寸契约才传 `--exact-size`。用户指定原图直用时，校验文件完整性、资源引用和导入配置，不让风格检查驱动缩放或重绘：

```powershell
python .agents/skills/flatworld-pixel-art/scripts/validate_pixel_asset.py <sprite.png> --require-alpha --require-transparent-corners
# 仅在真实契约要求时追加 --require-hard-alpha、--max-visible-colors 或 --exact-size
```

## 边界与交付

- 通用物品占位图统一复用 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png`；未制作正式美术时优先使用它，不必重复生成。其子 Sprite 名为 `素材占位符`，Addressables 引用为 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，标签 `ItemSprite`。PNG 已紧裁为 384×384，主体接近填满画布，运行时切片覆盖整张 PNG（中心 Pivot、384 PPU、Point、无压缩、无 Mipmap）；裁剪 PNG 后必须同步切片坐标和尺寸，并保留子 Sprite 身份及资源地址，不能只改切片而保留大幅画布留白。正式素材完成后替换具体物品的引用，通常使用白色乘色，并核对继承缩放。

- 新绘制的像素素材禁止抗锯齿、渐变、照片纹理、高频噪点、无关背景、地面、投影、光晕、文字、Logo、水印或无法在最终尺寸辨认的装饰；用户指定原图直用时以保留文件内容为准，发现画风差异只报告。半透明特效等明确例外按消费方单独制定规则。
- AI 生物尽量合并为一张等尺寸 Sprite Sheet，只制作一套标准侧向帧（默认 `Left`）和特殊动作组；`Right` 通过 Unity 的水平镜像得到，不在 PNG 中复制第二套帧，不能把四方向移动表混进来，Unity 负责按帧网格切割。
- 仅生成美术时不创建 Prefab、Animator、SO 或玩法代码；需要接入时再读取对应 FlatWorld 领域 Skill，通过 Unity MCP 操作时读取 `unity-mcp-orchestrator`。
- 不复制其他资源的 GUID；仅在目标 `.meta` 已存在时精确修改导入字段。高清设计源默认不进 Addressables，也不挂到 Prefab。
- 用 `maxTextureSize` 限制高分辨率源图的导入尺寸时，Unity 会同比缩放 Sprite 的实际 PPU；导入器 PPU 应按源图像素换算世界尺寸，不能直接填写缩小后的目标像素数，否则主体会被放大。
- 参与局部光遮挡的 `Mod_Building` 世界建筑 Sprite 需要物理轮廓；未手工绘制时保留 `spriteGenerateFallbackPhysicsShape: 1`，否则安装可能因轮廓缺失回滚。`PlacementLayer=Ground` 或 `LightOcclusionMode=None` 不生成遮光轮廓。Sprite 物理轮廓不代替 JSON 中的实体 Collider 或离散占地。
- 替换 JSON 定义物品的贴图时，先核对 `visual.spriteAddress`，它可能覆盖外壳 Prefab 的 Sprite；若旧引用指向共享地形图集，应为道具生成独立 Sprite 并同步注册同址 `ItemSprite` Addressables 条目，禁止直接覆盖共享图集。
- 新增或替换物品 Sprite 后，不能只凭 PNG、`.meta` GUID 和 `Default.asset` 的 YAML 文本一致就认定地址可加载：运行中的 Addressables Fast Mode Locator 可能仍持有旧键索引。先确认 Unity 已将目标导入为 Sprite，优先通过 `AddressableAssetSettings.CreateOrMoveEntry` 在默认组注册 GUID、设为与 `visual.spriteAddress` 完全一致的地址并添加 `ItemSprite` 标签；若从外部修改了 Addressables YAML，须在 Unity 中重新导入对应分组资源，使 `AddressablesCatalogRefreshPostprocessor` 刷新 Locator。交付前执行 `FlatWorld/诊断/检查 Addressables 目录` 静态预检，确认没有“Sprite 地址未注册或类型不符”；这一步不需要进入 Play Mode。
- 将继承其他物品贴图的染色占位物替换为独立成图时，同步检查 `visual.color` 与继承的 `rendererLocalScale`；原有乘色会改变新图配色，成图通常显式设为白色，尺寸则结合继承缩放和 PPU 验收。
- 手持工具的 Pivot 必须对齐实际握柄，不能机械套用图标中心；先核对外壳 `Render` 层级和 JSON `visual.rendererLocalPosition`，避免导入 Pivot 与外壳偏移重复补偿。PNG 以左上计像素，Unity Pivot 以左下归一化；像素中心 `(x, y)` 对应 `((x + 0.5) / width, 1 - (y + 0.5) / height)`。
- 铺满整格的地面 Tile 使用中心 Pivot、全幅不透明画布和当前地块 PPU；不要套用物品的透明四角/底部对齐检查，否则拼接时会露出原地形。其 Sprite 图标可复用同一图，运行时地面不能保留图标安全留白。
- 地面与墙体可能共用同一个 TileBase 或共享图集切片；只重绘地面时，先检查全部消费方，必要时新增独立地面 Tile 并仅修改 terrain JSON 的外观引用，保留墙体和其他切片。

- 按格连续拼接的传动轴等世界 Sprite，应先核对 Grid 格宽、Renderer 缩放、PPU 与中心 Pivot；需要首尾无缝时，轴线处的 Alpha 和接头颜色应延伸并匹配画布相对边界。用相邻两到三格及旋转 90 度的最近邻预览核对接缝；贴图的视觉相接不代替机械端口和网络拓扑校验。

- 机械、电力、流体设备的接口与机身：
  - 设备本体、召唤器和库存图标只绘制完整机身。接入和离开的管道、流体接口短管/法兰、传动轴接口/铁环及电线连接由程序化表现层绘制或复用标准 Sprite；不得烘入设备 PNG，不为画面平衡添加假接口。独立的管道、传动轴物品仍绘制自己的主体；阀轮、表盘、喷口格栅等设备功能结构可以保留，但不附带外部连接件。
  - 出图前读取实际消费方的端口配置、朝向、Pivot、PPU 和局部偏移。机身保留完整轮廓和足够的遮挡区域，让程序绘制的连接件进入机身；连接位置及方向由运行时配置负责，不能通过重画接口改变拓扑。参考相邻轴、电线和同类设备确定机身大小，接口保持各自标准尺寸。
  - 流体表现若使用按宽高分别缩放的 `Fit(sprite, width, height)`，机身须采用匹配目标宽高比的画布；当前方形流体节点使用正方形画布、中心 Pivot。按源图尺寸与目标世界尺寸换算 PPU，使本体与建造预览一致，不紧裁主体后造成运行时拉伸。
  - 接入时核对机身与程序化连接层的实际世界坐标、遮挡顺序和旋转表现。静态组合图只用于检查接缝，正式物品图标沿用不带外部接口的主体；游戏内验收按用户要求执行。
- 持续自转的齿轮等 Sprite，旋转图层不要烘焙固定世界方向的投影、偏侧高光或下缘暗面；改用各方向一致的材质明暗和必要轮廓。如需地面投影，应由不旋转的独立图层承载。
- 固定输入杆与旋转齿轮叠图时，所需露出长度不等于整张杆图长度；保留藏在轮盘下的连接段，并检查齿尖与齿谷朝向接头时是否都能视觉相接。
- 电线方向图应从同一无封头线段和同一接线头合成：线段延伸到格边，接线头盖住内部交汇处，不能把单端黑封边带进直线、拐角、三通或十字的邻格接缝。混合电机的线口和轴口由程序化表现层复用标准 Sprite，保留原 PPU，由不带接头的机身遮住内部；物品静态图标只使用机身。
- 最终报告资产类别、所属系统目录、画布尺寸、主体边界、可见颜色数、透明度、动画/单帧状态、标准侧向/特殊动作分组、Unity 镜像方式、导入设置、使用的提示词/模式和必要人工观感检查；同时说明实际查看了哪些同系统、同类别项目贴图作为画风/比例参考，并确认新素材在相同世界尺度下没有因更高像素密度而产生明显风格跳变。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

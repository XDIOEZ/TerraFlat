# 锚点与材质

画风见[主入口](../SKILL.md)。先打开 `Assets/6_Art/Generated/<System>/` 中实际使用的同类素材；身份、结构、姿势参考各管自身，不跨类别套尺寸或比例。

- 商人身份与小尺寸可读性：`../assets/merchant-style-anchor.png`、`../assets/merchant-game-sprite-anchor.png`。
- 人物保留年龄感、体型、发型、服装、主配色和标志配件；动物保留物种剪影；树木归纳为叶簇与树干；道具突出功能轮廓。
- 默认左上光源，中间色、一档阴影和必要高光；描边按同类素材的视觉重量，避免纯黑粗框、柔化、照片纹理、油亮 3D 和噪点。小 Sprite 可参考 12–20 色，不作为全类别上限。
- 画布、逻辑像素、Alpha 包围盒与世界尺寸分开处理；大图用最近邻保留像素块，不自动增加细节或裁掉对齐留白。

## 工业设备

材质与结构锚点：

- `Assets/6_Art/Generated/Chemistry/GasTankBlock_Iron/GasTankBlock_Iron_Idle.png`
- `Assets/6_Art/Generated/Chemistry/GasTankBlock_Steel/GasTankBlock_Steel_Idle.png`

灰蓝大面板、直线加固框、深色接缝、少量方形铆钉，橙色仅小面积强调。连片方块正视、方形、全幅不透明，边角服从切片、中心连续，不加透视侧面或底座。独立设备保留阀轮、表盘等真实部件，外部连接件另走运行时图层。

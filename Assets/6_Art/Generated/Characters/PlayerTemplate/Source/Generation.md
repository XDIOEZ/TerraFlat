# 分层素材生成记录

2026-10-22，使用内置`image_gen`工具，两次生成/编辑，均请求真实透明背景；没有使用外部API或Python绘制像素。参考为原`Source/ApprovedOutfits.png`六人母版及原`Base_Female.png`、`Base_Male.png`。

第一轮生成规格：同画风、人物身份、左向站姿、统一身体规格；4列4行透明母版，依次为无发男底图、无发女底图、男发型、女发型、旅人兜帽/皮甲/手套/长裤/靴、铁卫半开放头盔/胸甲/护手/腿甲/战靴，最后两格留空。装备只含材质，脸、眼睛、皮肤、头发全部透明；男女人物只在脸/发型上不同。保持原棕色皮革、蓝灰铁甲、灰衣棕裤、小脸和低像素块。

第一轮没有严格遵守共用画布位置，而且发型混入耳朵、上衣混入裤腿，故发起以下最终编辑请求。最终产物保存为`LayeredOutfits.png`，1448×1086。

## 最终编辑提示（原文）

```text
Use case: precise-object-edit. Edit target: this transparent modular pixel-art clothing atlas. Preserve the exact layout, each piece's scale, color, pixel style and all original artwork, except make the following very specific corrections for real transparent layering: 1) Brown hair-only pieces in first row columns 3 and 4 must contain NO skin, NO ear, NO face and NO eyes. Remove the small peach/pink ear patch currently attached to each hairstyle, make that patch fully transparent; do not alter the hairstyle outline. 2) Traveler leather vest in row 2 column 2 and IronGuard chest armor in row3 column3 must end at the bottom of their brown belt, remove all pants below the belt, leaving them as chest/upper sleeve clothing only. 3) Traveler trousers in row2 column4 must end at the ankle, remove the boot shapes and keep brown trouser legs only. 4) IronGuard trousers and shin guards in row4 column1 must also end above the boots, with no shoes/feet at all. Separate boots remain unchanged in row3 column1 and row4 column2. Preserve every other pixel image and positioning, especially bald male/female bases, half-open hood and half-open helmet, which both must keep a completely TRANSPARENT open face area. Genuine alpha transparent background and empty cutouts, no chessboard, no labels, no text, no scenery, no floor, no shadows. The exact same 4-column 4-row atlas layout with last two cells empty.
```

## 确定性加工

正式`PlayerAppearanceLayerRaster`按源区标定最近邻采样到统一32×48画布，不在运行时识别脸或肤色。头发锚点上移以露出原脸眼睛；男女肩腰和四肢复用同一底图像素。左右手套和靴子各自定位到对应手脚。图标只对干净装备层取边界并居中缩放。技术用头发遮罩为全幅Alpha255，只隐藏头发层，不影响底图的脸。

本轮在Unity外执行该正式采样器导出25张正式PNG和`LayerPreview.png`，没有启动Unity构建器或控制游戏；一次性文件导出入口和编译产物不纳入Assets。所有正式PNG、meta、Profile、物品地址和Addressables条目已直接落地。预览用于人工检查素材叠加，游戏内效果由用户自行验收。

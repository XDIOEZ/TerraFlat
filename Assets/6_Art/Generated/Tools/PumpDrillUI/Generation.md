# PumpDrillUI

- 工具：内置 imagegen，2026-11-04。
- 生成要求：泵钻的钻杆、横梁、石飞轮、压帽四个独立部件；低饱和像素风，透明底。参考项目对应物品原图，保持材质与配色。
- 正式图：`PumpDrill_Components.png`，保留生成原始像素。透明轮廓、切片和 Point 导入由 `Tools/UIArtwork/build_device_artwork.py` 生成；uGUI 使用 Sprite Mesh。
- 动作由 UI RectTransform 分层实现，不增加存档或网络字段。

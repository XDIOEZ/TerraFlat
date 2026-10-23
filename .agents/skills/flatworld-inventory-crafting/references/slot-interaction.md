# 槽位交互

## 统一事务与目标

- PC OnDesktopTap 经 Inventory_Hand 整组取放、滚轮逐件；触屏轻点单件，槽间拖拽复用鼠标事务，异类交换双向校验。世界目标实现 IInventoryDragDropTarget，经来源事务原位修改。
- 拖放、指定数量、Shift、双端甩动选定目标后都走 TryCommitSlotTransfer，预检/提交共用 CanTransferToSlot；仅allowSwap启用交换。快捷转移先合堆再填空，提交统一刷新、调用双方 OnSlotTransferCommitted，快捷栏在此同步手持/网络，输入层不补发。数量预检走 GetTransferableQuantityTo，不试转移。
- 装备窗口 InitUI 绑定 Opened/Closed，覆盖Open/Toggle/交互/恢复入口；Shift/甩动复用 CanAcceptQuickTransfer 的标签/容量策略。

## 快速甩动

- InventoryFlickGesture 共检净位移、末段速度、轨迹方向；鼠标/触屏各采轨迹但共用门槛，先抬起再EndDrag仍保留末速。正式槽位拖放优先；无目标才经 TryConsumeSourceItem 校验来源后 OnFlickTransfer，单击不等待，半组拖拽语义不变。
- InventoryFlickTargets 随正式UI生命周期登记输入，组合输出 allowDirectionalDeposit:false；排除自身、同数据、手部、输出、关闭/隐藏/禁用或无有效槽映射的界面。HasVisibleFlickTarget/HasFlickTarget 无其他可见库存即禁甩，快速拖出仍拿到手部；不按容量关开关，满目标保留拒绝留源。
- 消费来源前 HasFlickTarget 用起点/方向查范围，Resolve 共用角度资格。优先 UIWindowDragSurface.WindowRect，否则真实可见槽区；按屏幕/遮罩裁剪，分离窗口独立，边缘放宽10°，范围外走普通手部拿取。起点在目标内允许任意有效方向，仍按槽区中心夹角排序。
- InventoryFlickUserSettings 统一速度/距离门槛：25%～200%、整数步进、默认100%，角度限制默认开；值越低越易触发，正式阈值保持既有手感。关角度仅绕过几何资格，可见性/容量/排序/留源与槽拖放优先仍在，不影响Shift；inventory_flick Provider 管保存/放弃/默认。
- 容量/标签/承载拒绝则跳过同方向范围内候选；选定一个库存后仅收可容纳部分，余物留原槽。不全场查找、不改PC Shift目标。

## 触屏与手部

- 长按整组放置在进度完成时提交并消费手势，先缓冲0.2秒再显示、不延长总时长；空槽起手才交ScrollRect。拖拽来源允许同槽/同非零Guid/同定义的事务替换实例，被拖槽持续加工暂缓。
- 空手按槽1秒显示同一PC介绍窗；CanTouchInspectItem 仅检查手部库存，快捷栏装备不算手持。松手保留、下次窗外按下收起，成功后消费长按；移动、换身份、手部非空、关面板取消，手持长按/拆半语义保留。
- UI_Hand/InventoryDragGhost 为顶层Canvas、不拦射线；触屏取本次PointerEventData并保留抬起位置，桌面明确接管才用Pointer.current。MobileHeldItemDropSurface 仅动手部槽，经Mod_DiscardItem成功生成世界物后才扣源。
- 库存/手工面板显式允许手持世界丢弃并登记自身输入锁Owner；手机窗外轻点单件/长按整组，窗内/槽位射线优先。此能力不解移动/攻击锁，不改槽间拖拽与松手转入手部事务。

## 反馈

- InventoryAudioFeedbackEvents 只观察成功点击/滚轮/拖拽/快速转移，开始拖拽仅播拿起；交换独立，装备真实更替由 EquipmentAudioFeedbackEvents 播穿脱。InventoryInteractionAudio 选音/播放，业务不调AudioService。
- InventoryQuickTransferFeedbackEvents 按实际收货槽发布精确物品/数量/源目标索引；动画独立0.24秒、无射线、最多32并发。甩动从松手屏幕点、Shift从源槽起飞，按真实Canvas显示；关闭/卸载/虚拟化重绑取消，不给隐藏/缺失槽编落点。
- 客户端机器命令须对应token成功且同次权威快照已应用才按精确收货回执播动画；请求时冻结松手点供回执使用，服务器远端命令抑制Host本地动画。

# 表现、面板与 C# MOD

- 主体/运动件按脚点Y行合批；MechanicalDepthVisual 仅交互/灯光，不持有HP/库存。MechanicalShaft 连接层在主体下，端口/铁环/短管独立生成，主体只画设备。
- Pivot/Renderer 局部变换共供预览、落地、阴影，逻辑占地不随贴图偏移；运动件同画布/PPU/Pivot，变速先积旧相位再改角速度。太阳/接触阴影独立BRG，SpatialInteractionRegistry 注册同一数据目标。
- 多零件机器在机械模块声明 `Articulation`，`visual.spriteStates` 引用独立透明零件并登记 Addressables；锚点用原画布左上像素，零件固定比例校准，不降采样。`MachineArticulatedKinematics` 解刚性曲柄/连杆/竖直滑杆，地表与船上共用模式8/9/10和停机相位；气泵只在实际交付后置 `LastFlowPhase=gas` 驱动，表针表示运转反馈。GPU、交互、阴影与CPU包围盒同步，新数据需在 `Mod_MechanicalNode` 声明以通过严格JSON校验；回归 `mechanical/machine_articulation`。
- 无端口设施落地批绘须读取 `visual.rendererLocalScale`，接触阴影按同一缩放测底边；不能只改预览或源 Prefab。现场回归 `building/facility_visual_scale`。
- 降落经 MechanicalLandingPreparation 分帧预建停用外壳、灯光、部件缓冲、共享Sprite几何，不复制机器；BeginCommit 提供候选，区块接管后走正常生命周期，未消费候选随准备器释放。BeginSurfaceChangeBatch 仅在同步交接按世界/格去重，保留 CellChanged；SurfacePresentationCell(s)Changed 最终格/邻格各刷新一次，不延迟常规 Tick。
- MachineDefinition.VisualLayer 共用管→线→设备的显示/命中顺序，GridInteractionSelector 对完整逻辑格叠层从上到下等分选择，不按窄贴图/邻格半径命中。FluidTankSelectionOutline 按 GetFluidTankMembers 的四邻连通生成组外框，切片/内容条/端口不各描边，逻辑节点不变。
- 组合储罐的实体碰撞按 `CombineAdjacent` 和存储能力统一合并，不按罐体名称或气液种类加分支；同物理模板可跨库存组拼接，精确保留凹口/孔洞/间距。源几何变化才重建，普通机器与自然物保留独立盒，规则见 `industrial_fluids/combined_tank_collision_*`。
- Lighting 复用 electric-light，以实供 ElectricalSuppliedWatts 控制亮度/powered贴图，断电/休眠撤光；地表/船共用 MachineLightPresentation。保留玻璃Alpha，专用自发光按脚点排序、不提亮底座；灯泡120V/60W、半径16格，火把8格。
- BatteryChargePresentation 三格门槛33%/66%/99%，BatteryIndicator.Cells 用原图左上像素矩形；地表 MechanicalDepthVisual.Battery 合入脚点行，船 ShipPieceView.Battery 跟主体变换。档位变化才更新，未亮保留底图，精确值看面板。
- 非模态面板：电池读数/竖向填充/居中百分比每0.2秒非缩放刷新，关闭停止、文字变化才写；比例为节点 ElectricalStoredJoules/CapacityJoules，离网仍显示，UI不改能量。燃烧灯光不跟加工进度，Mod_CombustionVisual 与主体共读燃烧状态、复用粒子/脚点排序，停用/拆除清空。
- ConveyorPath 同时管运输/推动/显示；ConveyorMode 0自动、1–12手动，自动传播优先手动段。DroppedItemRuntime/TransportItemBacked 用局部索引每轮搬一次，末端 ContainerTransferService 按成功量扣地面物。
- 传送带侧轴伸缩读 ConveyorConnectedDriveMask：正式图边构建完成后，与路径变化合并进 ConveyorChanges 通知本格刷新，不按 RPM 判断或逐帧扫邻格；直带连接时木芯与铁环伸至半格边界，断开收回，拐角仍隐藏侧轴图。
- MachineLogicRegistry 租约可乱序释放，link.xml 保留托管入口；ModRuntimeManager.Managed/ModManagedAssemblyStore 在启动加载已启用且清单声明的程序集。C# MOD 仅桌面Mono：Initialize 注册、ContentReady 用内容、Dispose 撤本MOD资源；IL2CPP 拒绝托管DLL。

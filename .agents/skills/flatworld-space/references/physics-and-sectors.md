# 星区、时钟与原生物理

## 地址、目录与流送

- `SpaceSectorId(long X/Y)` + 所属区内 double 米；边长 256000 米，局部范围 [-128000,128000)。Normalize/Relative 先算整数星区差再转距离；成员、重心、点速度与接触快照显式带 Sector、成对转到参考区，不合成巨大绝对 double 或借静态上下文。
- SpaceSessionState 保存 Sectors/SimulationSeconds；Universe 只是主区别名。BodyId/PlanetId 为全局实例身份，TemplateBodyId 查所属区模板；主地球 PlanetId 沿用存档主世界名，其他区不得复用 earth/moon。
- 全目录共用固定游戏秒轴。Kepler 椭圆按半长轴、偏心率、近日点方向和恒星周期推进，默认天文倍率 720；先父后子，复用步前后位置/速度，自转读权威时钟。载入用 SpaceCatalog.RebindUniverse 应用新目录，保留身份、种子、初相和时钟，不挪船坐标。
- SpaceMgr 登记星区时间/采光，统一引用 SpaceScene 时钟；跨区不重置昼夜、不依赖 WorldEnter。每区动态表现 Scene 的身份用 SpaceSectorCoordinates.SceneKey，太空判断用 SpaceMgr.IsSpaceScene。
- SpaceSectorStreaming 只预取前进目标区，越界后到离开目标侧过渡带才释放源区，允许折返；多人共享场景/租约，边界仍可见、攻击、碰撞、交互。美术未就绪不阻挡飞行，无玩家区只推进计算场景。
- ViewSector/ViewOrigin 只管显示；LateUpdate 先投影玩家，换区保持显示平面，离原点 4096 米才整体平移并通知镜头。自由物创建用 UnprojectSectorPosition，视图接 SectorPosition。

## 物理所有权

- SpacePhysicsWorld 以真实 Dynamic Rigidbody2D/Collider2D 在独立 PhysicsScene2D 推进船、残骸、自由玩家和散物，读回业务状态；岛场景生命周期复用 WorldPhysicsScene2D，精度与分组仍归 SpacePhysicsWorld。禁止叠加自研积分、扫掠、冲量。原生回报只结算损伤，不再次施加反作用；视图刚体只显示。
- 稀疏网格分配可复用局部物理岛，近物共场景，代理不依赖美术。支撑乘员圆归整船且质量已计入；自由玩家/散物不重复计船重。地板等非阻挡件忽略自由圆，舱壁/关舱门仍阻挡。
- ShipColliderGeometry 按格去重、阻挡优先，合并同类连续矩形并保留洞/分离区域；结构修订或门状态才重建。整船用阻挡/非阻挡两个 CustomCollider2D，乘员圆归阻挡组，几何未变不提交；地表数学接触不走该合批链。
- 船/对接成员/残骸 FreezeRotation，地表接触逆惯量零；散物仍可旋转。使用 Unity 2022.3 velocity/drag API，计算单位米；读回原生位移差保留 double 余量，同引力源的岛以星体公转作平移参考。
- 每固定步至少 4 子步；只在独立场景 Simulate 内临时提高平移/旋转上限，finally 恢复，不能永久改地面物理。显示场景 Additive 替换，船内世界及物理岛不随显示切换卸载；自有物理根先停用再延迟 Destroy，避免 DontSave 刚体晚于原生世界销毁。复用前检查 Scene/PhysicsScene2D，失效从权威状态重建；结束释放计算场景。
- SpaceGravity 由半径/表面重力推质量，W 半径随质量立方根、边界释放基准速度随平方根缩放，参考 8 米/秒；W 内仅取最大质量星体，星体仍按目录公转。仅向原生刚体施加中心引力、相对大气阻力和公转随动，不随自转/父子节点。AtmosphereThickness=0 无阻尼，正值加速衰减且欠阻尼，近中心低速停止相对漂移；离 W 撤引力/阻力。两份太空 JSON 改动后重建会话缓存，解析运动函数仅供离线参考。
- 冷散物质量随 Snapshot 解码变化，代理/形状复用；地表散物先筛空间/高度/扫掠范围再读质量。ShipGeometry.HasFloor 直接检查旋转矩形，不逐格分配。

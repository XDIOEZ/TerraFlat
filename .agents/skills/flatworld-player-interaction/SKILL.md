---
name: flatworld-player-interaction
description: "FlatWorld 玩家身份、输入仲裁、指针交互、移动/承载、镜头与玩家运行时重建。"
---

# FlatWorld 玩家、输入与交互

## 入口

`Assets/5_Scripts/5-3_GamePlay/`：

- `Entities/Item/Player/Player.cs`：玩家；数据为 `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`。
- `Player/Controller/`：`Mod_GameController` 输入，`InputBindingService` 重绑定，`PlayerAimCursorSystem` 指针，`Mod_InteractSender/Mod_InteractReciver` 交互，`Mod_PlayerAdminController` 权限。
- `Entities/Move/`：移动、相机、焦点；相机 Prefab 为 `Assets/2_Prefabs/Gameplay/Modules/Camera/Main Camera.prefab`。
- Action 源/生成包装：`Assets/PlayerInput/PlayerInputActions.{inputactions,cs}`。
- 创建模板：`Assets/StreamingAssets/GameConfig/Players/player-creation-manifest.json`；环境推动：`GameConfig/Movement/water-current.json`。

## 身份与生命周期

- `IsLocalProfile/ProfileContext` 区分本地档案；远程副本无本地保存、教程或语音。`ProfileName/ProfileId` 是角色槽位身份，`Name_User` 仅为显示名。
- 创建模板在新档案 `Player.Load()` 前解析注入；MOD 使用 `playerCreationTemplates` 或 `playerTemplate:ID` Patch。管理员控制器唯一位于 `Module_Player`。
- 单机 F5 资源发布保存本地 Data、注销旧 Player、清理运行时 UI、从最新 Prefab 重建同一档案；世界/区块/存档会话保留，引用重绑事件为 `Event_LocalPlayerRuntimeReloaded`。
- Wrapped 世界的本地刚体/Transform 在局部平面连续移动，`PlayerWorldWrapController` 规范化 Data 并发布跨周重投影；存档权威坐标与表现坐标分离。实体非 Trigger 碰撞体为 Player 层，模块 Trigger 保留专用层。

## 输入与指针

`Input System → Mod_GameController → 玩家模块`；控制器位于玩家根，模块 ID 为 `ModText.Controller`。

- 键位源为 `.inputactions`，包装器由生成器同步；Action 名是内部身份，物理键来自 binding `effectivePath`。覆盖按 binding GUID 保存，加载过滤不存在的 GUID；冲突检测将 Shift/Ctrl/Alt 与其左右键统一为物理修饰语义。
- 控制偏好手动保存为键鼠/手柄/手机，只影响 HUD 与指针；玩法/UI ActionMap 接受并行设备输入。设备变化仅更新 UI/指针，手机触摸由自身生命周期清理；鼠标/Touchscreen 点击退出手柄 UI 模式，Shift 等修饰键不切设备。
- 文本焦点由 `EventSystemGuard.IsTextInputFocused` 与 `IsGameplayInputAllowed` 拦截键盘玩法，移动返回零；鼠标仍经 UI 射线仲裁。鼠标位置缓存响应 performed/canceled，按键读取当前坐标；切回键鼠清理非文本选择焦点。
- 世界指针统一经 `GetPointerScreenPosition/GetMouseWorldPosition(screenPosition)`；准线距离来自普通交互 `maxInteractDistance` 或建筑 `maxVisibleDistance`。`Move_Player` 保留 0～1 模拟幅度。
- 外部控制持有唯一 External Gameplay Control 租约，期间真实设备退出玩法仲裁，移动/瞄准/攻击注入现有生产链；打包运行时 `Mod_GameMCP_LLM` 复用该租约与移动输入。
- 丢弃组合态让 Ctrl+滚轮缩放让位，普通滚轮仍可换槽。返回顺序为顶层可取消面板 → 手机抽屉 → 设置；返回处理先于玩法锁拦截。聊天 Enter 打开当帧抑制提交。

## 交互

- `IInteractable` 的交互键通道为 Start→按住 Update→松开 End，取消/目标失效为 Cancel；鼠标与外部单次调用为 Start→End。`CanPointerInteract=false` 仅禁鼠标点选。
- 接口持有 Unity 组件时先按 UnityEngine.Object 原生存活状态检查；目标销毁后清除交互与描边缓存，不再向销毁对象发送回调，纯 C# 世界目标保持普通引用生命周期。
- 右键只使用当前快捷栏中由本玩家持有的 `Item`，经 `Item.Act/OnAct` 分发；F 只交互世界建筑、工作台、设备、门、入口和载具。`CanPointerInteract` 默认 false，仅采集等左键动作显式允许；F 的指针选目标和描边不受点选权限影响。
- `PreciseInteraction` 默认关闭：光标命中优先，空命中按朝向/距离回退；开启后仅认落点。全部选择受距离约束，鼠标始终精确；手持 `Item.InHand` 排除于候选，包括同时实现世界目标接口的组件。手持建筑放置由右键使用提交，不阻断 F 交互和世界描边；放置态只暂停左键采集。
- 发送器使用 Physics2D 查询，无交互 Trigger，`Module_Hand` 无 Collider2D，查询跳过伤害专用 Collider。无 Collider 目标及机器由 `SpatialInteractionRegistry.Query` 提供；逐帧描边只用 `QueryPreview` 的注册视觉，机械视觉以同一目标接入 `IWorldInteractionPreview`。
- 光标未命中后同轮半径兜底复用已完成的 `Physics2D.SyncTransforms`，显式点选仍独立同步；Profiler 区分 `FlatWorld.Interaction.PointerQuery/RadiusQuery`。
- 环境输入只转发按下/持续/松开，环境定义由 `EnvironmentInteractionRunner` 建独立实例；玩家状态不存于共享地块定义。
- 载具按键交互和描边共享落点查询，上下船均要求实际命中；无邻近陆地时允许安全水面落点，未命中载具的可触及水面仍可交给环境动作。

## 移动、承载与镜头

- `Mod_Mover` 向 Dynamic Rigidbody2D 提交 `DrivenVelocity + ExternalVelocity`，Physics2D 求解接触；主动速度决定步行动画，水流/输送带/承载不计步行或体力。`WorldUnitChanged` 按刚体实际到达的拓扑规范 1×1 格发布。
- Mover 的移动动画事件只在加载时绑定、卸载时解绑；输入锁使用已绑定控制器，太空直立状态切换时才更新相机跟随模式，避免对象池重复订阅和逐帧模块查找。
- 风区边缘的推动按 `WeatherMgr.TryGetWindBlendAt` 给出的邻区距离权重混合；各区先用原始风强换算推速，再混合速度向量，不能拿混合后的风强重新套起推门槛。默认过渡总宽 32 格，玩家被吹出强风区时按距离逐渐减速。
- 水流推动复用 movement JSON 的 Actor 指定值或重量规则，河流强度来自权威 Flow 与 `ResolveRiverStrength`；动态建筑减速仅作用主动目标速度。输送带读取实际带面与有符号 RPM，本机物理步合入外速，输入锁保留环境推动。
- 风推动共用 `WindEnvironmentRules/WorldMotionSystem.SampleWindVelocity`：等效质量=当前堆叠总质量×星球重力/9.81；目录最轻 B 到轻重中点 C=(A+B)/2 均分三档，分别需 8/9/10 级风，达到 C 不推动。达到起吹级/高一级/高两级分别为 0.06/0.2/0.5 单位/s，同风级内不随风强连续变化，邻风区保留空间过渡。玩家/GO 生物/载具合入 ExternalVelocity，不计步行与体力；载具含乘员质量，乘员不重复受风；太空与禁用天气维度返回零。
- `ICarrierMotionSource/Mod_Mover.TryAttachCarrier` 管理承载租约：载具拥有位移/速度/力，乘员无 Transform 父级变更，关闭自身刚体模拟并跟座；瞬时承载关系不存档，恢复点由登船作用域租约持有。船头只由有效乘员划行输入按固定角速度调整，水流/滑行/推动不改变朝向；根刚体 FreezeRotation，视觉子节点转向。
- `Mover_SaveData.isRunning` 保存奔跑开关，输入锁停止位移但保留开关。Run 与 Move 分离，静止动画为 Idle，跑步播放不改全局 Animator.speed。体力统一经带来源的 `Mod_Stamina` API；管理员无限体力也在该权威模块处理。普通划船不耗体力，按住奔跑且移动时的载具加速记为 `flatworld.carrier.boost`。
- 太空场景禁止玩家奔跑；失重 WASD 输入由 `SpaceSession.StepFreePassengers` 在固定步调用 `Mod_Mover.SmoothSpaceVelocity`，按屏幕方向给 FreeWalkVelocity 累加推力，松键无摩擦并保持速度，反向输入逐渐制动。离船继承船体点速度，继承/碰撞速度不受主动移动限速影响。Shift 不参与冲刺、借力或抓墙，普通地表奔跑保留。
- `Mod_FocusPoint` 独占翻身/瞄准节点的最终旋转，`Mod_TurnBack` 提供本地 Y 转角，手持物瞄准换算到父节点坐标。太空 `SetSpaceUpright` 只固定本体/刚体与镜头朝上，仍开启鼠标左右翻面；不能调用 `SetMirroringEnabled(false)` 把角色锁向右侧。`CameraFollowRotation` 在 Cinemachine 管线内写绝对朝向，不叠加 Dutch，`Mod_Cam.SetFollowTargetRotation(false)` 明确恢复零角度。镜头使用 Cinemachine 2.x Framing Transposer 的 Lookahead/XY Damping；缩放解析玩家兄弟模块 `Mod_ChunkLoader` 刷新区块窗口。管理员无限视野只跳过最终尺寸上限，有限 `MaxPovValue` 仍供普通玩法/UI 使用。
- 环绕副本使用 URP Overlay，`WrappedWorldCameraRenderer` 唯一管理相机后处理开关：有副本仅最后活动相机启用，无副本/解绑恢复主相机；`ScreenPostProcessManager` 只合成 Volume 参数。
- 高视野的星球柔边云团与外圈浓云由 ZoomCloudRendererFeature 在最终环绕补绘后叠加；SurfaceZoomTransition 共用逻辑视野进度，拉远从四周靠近并盖满后再交接宇宙，拉近反向退开。实际相机尺寸独立驱动外圈黑边保护，风只控制漂移；正常太空和禁天气维度不叠地表云，HUD 不被覆盖。
- `CameraZoomExtensions` 注册通用缩放策略；地表宇宙观察把逻辑尺寸与实际相机尺寸分开，`CurrentOrthographicSize` 在这种策略下读逻辑尺寸，其余模式仍读 lens。滚轮/双指共用线性→倍率的连续步坐标，手机滑条通过 GetViewSliderValue/SetViewSliderValue 使用稳定分段映射；驾驶跟随覆盖优先，观察不改变玩家位置、听音或区块加载距离。
- `Mod_Cam.SetFollowOverride/ClearFollowOverride` 供临时驾驶覆盖跟随目标并同步环绕副本，释放回玩家，不改变听音主体；可随跟随租约传入最大视野与缩放倍率，`EffectiveMaxPovValue/UsesProportionalZoom` 供 UI 同步。太空驾驶上限为 120000、滚轮乘/除 1.25，双指共用比例缩放、手机滑条使用对数映射；退出租约清除临时范围，普通地表在宇宙观察起点前按原尺寸增减，缩放不改变区块加载距离。飞船驾驶目标由 SpaceSession 投影整船中心，镜头固定朝上，下降触地/退出恢复原视野。相机根实例进入持久场景，由 Mod_Cam 卸载/销毁时释放并清订阅与引用，普通星区卸载不销毁或重建相机。
- 开发快捷键：Ctrl+F1 管理员，F1 录制 UI 隐藏，F2 创造背包（主背包打开不拦截，其它模态/加载锁拦截），T 与 GM 点选共用 `Mod_PlayerTraits.TryTeleportToScreenPosition`；太空先经 `SpaceSession.UnprojectPosition` 还原宇宙坐标，再调用 `TryTeleportPlayerToSpace`，地表传送先复用中立乘员重置解除乘船/下降，再写拓扑规范坐标并刷新区块。

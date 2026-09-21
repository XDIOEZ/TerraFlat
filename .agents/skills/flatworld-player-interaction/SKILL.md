---
name: flatworld-player-interaction
description: "Use when: 定位或修改 FlatWorld 的玩家实体、输入系统、鼠标/手柄、虚拟光标、移动、摄像机、焦点、交互发送接收、管理员控制或玩家 Prefab。关键词：Player、GameController、InputBindingService、Mod_InteractSender。"
---

# FlatWorld 玩家、输入与交互

## 入口

- 玩家：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Player/Player.cs`；数据：`Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`
- 输入：`Player/Controller/{GameController,InputBindingService}.cs`
- 交互：同目录 `{Mod_InteractSender,Mod_InteractReciver}.cs`
- 管理员：`PlayerAdminController.cs`；移动/相机/焦点：`Entities/Move/`
- `PlayerAdminController` 的 Ctrl+F1 用于启用管理员身份；裸 F1 由 `UIManager` 专用于宣传片录制 UI 隐藏。F2 是本地开发用创造背包一键入口：不依赖管理员身份，按下后复用 `Mod_PlayerTraits.InitializeCreativeInventoryForAdmin` 初始化/补充物品，并确保玩家主背包面板打开；管理员专属的其它快捷键仍保持原有权限门槛。
- GM 传送由唯一的 `Development/Debug/GMReflectionConsole.Teleport.cs` 消费 Ctrl+T 和点选入口；不能放回 `PlayerAdminController.Update`，因为玩家外壳与 Module_Player 均可能挂载管理员模块，且 GM 传送不依赖角色显示名。落点统一交给 `Mod_PlayerTraits.TryTeleportToScreenPosition` 同步刚体、玩家位置和区块加载。
- 游戏镜头由 `Mod_Cam` 实例化 `Assets/2_Prefabs/Gameplay/Modules/Camera/Main Camera.prefab`；2D 跟随使用 Cinemachine 2.x `Framing Transposer`，跟随手感优先在该 Prefab 的 Lookahead 与 XY Damping 调整。
- `Mod_Cam` 与 `Mod_ChunkLoader` 是玩家下的兄弟模块；镜头缩放需要刷新区块窗口时必须经玩家根节点/ItemMods 解析区块加载器，不能只用 `GetComponentInParent<Mod_ChunkLoader>()`，否则大视野变化只能等加载模块下一次 Tick 才被动追上。

## 不变量

- 玩家主动速度与环境速度必须分开：`Mover.DrivenVelocity` 决定步行动画，水流和承载只写 `ExternalVelocity`；不能把上一帧总刚体速度重新当作主动移动的缓动起点。
- `WorldMotionSystem` 用作者矩形占地与扫掠统一处理推动，载具通过 `IWorldPushTarget` 注册来源；船的水流、划行与推动先合成，再由 `ICarrierMotionSource` 传递给乘员。禁止乘员反推自身载具或用物理冲量代替游戏速度规则。
- 载具的按键、鼠标和白色描边必须共用光标落点查询；指向可触及水面才允许喝水长按，指向载具优先交互。远海登船也合法，恢复位置优先附近安全陆地，否则保留真实登船坐标，不凭空传送到陆地。

- 输入链为 Input System → `GameController` → 玩家模块；不要让 UI、物理输入和玩法模块各自维护冲突状态。
- Editor Agent、自动化或其它非物理输入源接管本地主角时统一使用 `GameController` 的唯一 External Gameplay Control 租约；租约期间真实设备退出玩法输入仲裁，移动/瞄准/攻击继续注入现有生产链。禁止为自动化直接改玩家 `Rigidbody2D`、Transform 或另建平行输入状态。
- `InputBindingService` 的覆盖存档按 binding GUID 关联输入资产；输入资产删改绑定后，加载前必须过滤当前资产不存在的 GUID 并重存清理后的配置，因为 Unity 内置加载器会直接输出警告而不会抛出异常。
- 输入重绑定冲突检测必须按物理修饰键语义统一 `<Keyboard>/shift` 与左右 Shift、`ctrl` 与左右 Ctrl、`alt` 与左右 Alt；历史冲突覆盖加载时应自动清理，避免镜头缩放等组合输入被静默改绑到已有玩法键。
- 需要按触点落地的世界玩法统一调用 `GameController.GetMouseWorldPosition(screenPosition)`，不得在手机玩法模块内直接读取相机或共享虚拟光标坐标。
- `Move_Player` 的二维幅度同时表达模拟移动速度比例：手机虚拟摇杆与手柄左摇杆必须保留 0～1 幅度，玩家移动路径不得提前归一化；键盘满幅输入与目标寻路接口保持原有语义。
- 玩家乘坐载具统一经 `ICarrierMotionSource` 与 `Mover.TryAttachCarrier` 仲裁：载具源拥有位移积分、速度和力，乘员不改 Transform 父级，只在租约期间关闭自身 Rigidbody2D 模拟并跟随座位；乘员引用和瞬时速度不进存档，恢复位置使用登船时取得的作用域租约。无 Collider 载具交互使用 `SpatialInteractionRegistry`，不要为了点选重新添加物理碰撞体。
- 环境交互输入只转发按下/持续/松开；具体环境提供 `IEnvironmentActionDefinition` 或 `IEnvironmentEffectDefinition`，角色侧 `EnvironmentInteractionRunner` 每次创建独立实例，禁止把玩家长按或被动效果状态存进共享地块配置。
- 世界实体的持续交互统一走 `IInteractable.OnInteractUpdate`：`Mod_InteractSender` 只在交互键从按下到松开的保持期间向本次按键命中的目标转发 Update；鼠标单击和外部单次 `TryInteractTarget` 不自动进入持续通道，业务模块不得自行读取 E 键状态。
- 本地档案由 `Player.IsLocalProfile`/ProfileContext 判定；远程副本不得持久化、跑本地教程或玩家语音。
- 玩家存档与 `Player_DIC` 必须使用 `Player.ProfileName` 稳定档案键；`Data_Player.Name_User` 可能被显示名、旧存档或管理员身份临时改写，禁止用它决定保存、卸载或跨维度重建的角色槽位。
- 手柄焦点只能停留在顶层导航面板；虚拟光标/虚拟键盘按现有模式接管。
- 当前玩法控制偏好由设置页手动选择并持久化为键鼠、手柄或手机；禁止按最近输入自动切换 HUD。输入资产、玩法 ActionMap 和 UI ActionMap 不得用 binding mask 互斥键鼠、手柄和手机输入；设置页/手柄焦点的设备状态也不能清空手机触摸。
- 手机方案下 Shift 等键盘修饰键不参与设备切换；真实鼠标/Touchscreen 点击可退出手柄 UI/虚拟光标模式，硬件鼠标位置优先用于 UI 命中，但不能因此切走手机 HUD 或改变手机触控语义。
- 切回键鼠模式时必须清空非输入框的 `EventSystem.currentSelectedGameObject`，且选中描边只在真实手柄模式显示，避免鼠标点击后残留手柄焦点框。
- 交互描边脚本只能保留在 `GamePlay` 程序集源目录，禁止在 `Assets/5-3_GamePlay` 与 `Assets/5_Scripts/5-3_GamePlay` 同时放置同名类型，否则会触发 CS0436。
- 同时需要左右翻身与上下瞄准的 Transform 只能由 `Mod_FocusPoint` 写最终旋转；`Mod_TurnBack` 只提供 `CurrentTurnAngleY`，禁止把同一 Transform 再加入其方向控制列表，否则 Y 翻转会被 Z 瞄准覆盖。
- 手机/手柄交互优先选择普通指向前方的可交互目标，前方没有目标时才按距离兜底；鼠标点击仍按落点精确选择。
- 手持建筑处于正式放置模式时，世界交互发送器必须整体让位：停止当前交互、隐藏普通交互描边，并拒绝按键、鼠标和外部控制源对附近 `IInteractable` 的触发；建筑仍由手持物“使用”动作提交，避免同一输入阶段既放置又打开旁边设施。
- 玩家交互发送器必须是纯 Physics2D 查询通道，不得拥有或临时启用 Trigger；`Module_Hand` 禁止挂载 Collider2D。交互查询必须跳过 `DamageSender`/`DamageReciver` 专用 Collider，避免交互与伤害系统产生接触回调或互相解析。
- 手机准线的有效距离不能固定写在输入层；空手和普通物品应跟随交互发送器距离，手持建筑应跟随建筑模块的放置距离。
- 玩家跑步模式与视觉状态分离：`Run` 只表示逻辑奔跑模式，`Move=false` 时 `Player.controller` 必须切换到 `Idle`；进入 `Run` 必须直接播放，不添加播放倍率渐起或 Animator 混合延迟，禁止修改全局 `Animator.speed`，否则会连带暂停攻击等其他动画。
- 管理员身份拥有无限体力：权限统一读取 `PlayerAdminController.IsAdministrator`，所有体力消耗继续汇入 `Mod_Stamina.AddStamina` / `TryConsumeStamina` 由体力权威模块统一拦截；禁止在移动、武器、游泳、高温等消费方分别添加管理员特判。
- `Mod_Cam` 的管理员“无限视野”属于运行时权限状态，不得把 `MaxPovValue` 改成 `float.MaxValue`；`MaxPovValue` 仍是普通玩法、UI 滑条和镜头预判的有限配置上限，只有最终镜头尺寸约束在无限模式下跳过该上限。
- Escape/Android 返回遵循“最上层可取消面板 → 手机抽屉 → 设置面板”的统一顺序；不要在尝试关闭顶部面板之前用 Gameplay Input Lock 拦截，否则持锁面板会让返回键表现为完全失效。
- `Mover_SaveData.isRunning` 是玩家奔跑开关的持久字段；输入锁定只停止位移，不清空该字段，跨维度重建后须在解锁输入后恢复。
- 玩家创建参数来自 `StreamingAssets/GameConfig/Players/player-creation-manifest.json`，由 `PlayerCreationTemplateCatalogService` 在新档案 `Player.Load()` 前解析并注入；MOD 可在 definition JSON 中增加 `playerCreationTemplates`，或用 `playerTemplate:ID` Patch 修改模板；已有存档不得再次套用模板。
- `Player.prefab` 根的环绕控制只处理本地玩家且仅在 Wrapped 拓扑启用。
- 环绕世界副本以 URP Overlay 加入主相机栈；URP 14 的 `Renderer2D.Setup` 按每台相机的 `postProcessEnabled` 执行后处理，Base 不会自动延后到整个栈的末尾。`WrappedWorldCameraRenderer` 是后处理开关的唯一写入者：绑定时保存主相机配置，有副本时只在最后一台活动副本开启，无副本/停用/重绑时恢复主相机；副本沿用主相机的 Volume Mask 和采样位置。`ScreenPostProcessManager` 只合成 Volume 参数，禁止逐帧重新开启 Base，否则会重复处理或被后续世界补绘覆盖。判断行为应核对项目实际 URP 源码，不套用其它版本或渲染器结论。
- 玩家实体非 Trigger 碰撞体固定使用 Player 层，不递归覆盖模块 Trigger 专用层。
- UI 焦点联动 `flatworld-ui`，网络身份联动 `flatworld-networking`，移动可走性联动 `flatworld-navigation`。

## 验证

- 输入验收必须在真实 Play Mode 中走正式 Input System → `GameController` → 玩法模块链；GamePlayMCP 可通过生产输入入口注入动作，不能直接改业务状态。
- 实际覆盖锁定/释放、短按/长按、切设备和重复绑定等受影响路径；不再维护 `PlayerInteraction.Input` 或 `Assets/GameTest` 测试程序集。
- 编译与 Console 只作为进入运行态的门禁和故障证据，最终结果以真实玩法状态变化为准。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

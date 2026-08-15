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
- 游戏镜头由 `Mod_Cam` 实例化 `Assets/2_Prefabs/Gameplay/Modules/Camera/Main Camera.prefab`；2D 跟随使用 Cinemachine 2.x `Framing Transposer`，跟随手感优先在该 Prefab 的 Lookahead 与 XY Damping 调整。

## 不变量

- 输入链为 Input System → `GameController` → 玩家模块；不要让 UI、物理输入和玩法模块各自维护冲突状态。
- `Move_Player` 的二维幅度同时表达模拟移动速度比例：手机虚拟摇杆与手柄左摇杆必须保留 0～1 幅度，玩家移动路径不得提前归一化；键盘满幅输入与目标寻路接口保持原有语义。
- 环境交互输入只转发按下/持续/松开；具体环境提供 `IEnvironmentActionDefinition` 或 `IEnvironmentEffectDefinition`，角色侧 `EnvironmentInteractionRunner` 每次创建独立实例，禁止把玩家长按或被动效果状态存进共享地块配置。
- 本地档案由 `Player.IsLocalProfile`/ProfileContext 判定；远程副本不得持久化、跑本地教程或玩家语音。
- 玩家存档与 `Player_DIC` 必须使用 `Player.ProfileName` 稳定档案键；`Data_Player.Name_User` 可能被显示名、旧存档或管理员身份临时改写，禁止用它决定保存、卸载或跨维度重建的角色槽位。
- 手柄焦点只能停留在顶层导航面板；虚拟光标/虚拟键盘按现有模式接管。
- 当前玩法控制方式由设置页手动选择并持久化为键鼠、手柄或手机；禁止按最近输入自动热切换。切换时只给玩法 ActionMap 设置 binding mask，禁止遮罩整个 InputActionAsset，否则会一并禁用设置页所需的鼠标、触摸和返回输入。
- 切回键鼠模式时必须清空非输入框的 `EventSystem.currentSelectedGameObject`，且选中描边只在真实手柄模式显示，避免鼠标点击后残留手柄焦点框。
- 同时需要左右翻身与上下瞄准的 Transform 只能由 `Mod_FocusPoint` 写最终旋转；`Mod_TurnBack` 只提供 `CurrentTurnAngleY`，禁止把同一 Transform 再加入其方向控制列表，否则 Y 翻转会被 Z 瞄准覆盖。
- 玩家跑步模式与视觉状态分离：`Run` 只表示逻辑奔跑模式，`Move=false` 时 `Player.controller` 必须切换到 `Idle`；进入 `Run` 必须直接播放，不添加播放倍率渐起或 Animator 混合延迟，禁止修改全局 `Animator.speed`，否则会连带暂停攻击等其他动画。
- `Mover_SaveData.isRunning` 是玩家奔跑开关的持久字段；输入锁定只停止位移，不清空该字段，跨维度重建后须在解锁输入后恢复。
- `Player.prefab` 根的环绕控制只处理本地玩家且仅在 Wrapped 拓扑启用。
- 玩家实体非 Trigger 碰撞体固定使用 Player 层，不递归覆盖模块 Trigger 专用层。
- UI 焦点联动 `flatworld-ui`，网络身份联动 `flatworld-networking`，移动可走性联动 `flatworld-navigation`。

## 验证

- 输入测试必须注入输入，不依赖真实鼠标、键盘或手柄；验证锁定/释放、短按/长按、切设备和重复绑定。
- 默认做静态诊断与编译；需要时运行 `PlayerInteraction.Smoke` 或专项 `PlayerInteraction.Input`。
- 测试目录：`Assets/GameTest/PlayerInteraction/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

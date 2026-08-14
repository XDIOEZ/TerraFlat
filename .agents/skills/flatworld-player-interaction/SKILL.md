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

## 不变量

- 输入链为 Input System → `GameController` → 玩家模块；不要让 UI、物理输入和玩法模块各自维护冲突状态。
- 环境交互输入只转发按下/持续/松开；具体环境提供 `IEnvironmentActionDefinition` 或 `IEnvironmentEffectDefinition`，角色侧 `EnvironmentInteractionRunner` 每次创建独立实例，禁止把玩家长按或被动效果状态存进共享地块配置。
- 本地档案由 `Player.IsLocalProfile`/ProfileContext 判定；远程副本不得持久化、跑本地教程或玩家语音。
- 玩家存档与 `Player_DIC` 必须使用 `Player.ProfileName` 稳定档案键；`Data_Player.Name_User` 可能被显示名、旧存档或管理员身份临时改写，禁止用它决定保存、卸载或跨维度重建的角色槽位。
- 手柄焦点只能停留在顶层导航面板；虚拟光标/虚拟键盘按现有模式接管。
- 切回键鼠模式时必须清空非输入框的 `EventSystem.currentSelectedGameObject`，且选中描边只在真实手柄模式显示，避免鼠标点击后残留手柄焦点框。
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

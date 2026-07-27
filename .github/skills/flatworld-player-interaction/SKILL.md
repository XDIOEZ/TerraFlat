---
name: flatworld-player-interaction
description: "Use when: 定位或修改 FlatWorld 的玩家实体、输入系统、鼠标/手柄、虚拟光标、移动、摄像机、焦点、交互发送接收、管理员控制或玩家 Prefab。关键词：Player、GameController、InputBindingService、Mod_InteractSender。"
argument-hint: "玩家输入、移动或交互问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 玩家、输入与交互定位

> 最后核对：2026-07-27。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Item/Player.cs`：玩家 Item 实体。
2. `Assets/5_Scripts/5-1_Data/ItemData/Data_Player.cs`：玩家持久化数据。
3. `Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs`：输入动作、鼠标/手柄、虚拟光标、输入锁定。
4. `Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs`：输入绑定服务。

## 关键功能

- 交互发送：`Assets/5_Scripts/5-3_GamePlay/Controller/Mod_InteractSender.cs`。
- 交互接收：`Assets/5_Scripts/5-3_GamePlay/Controller/Mod_InteractReciver.cs`。
- 管理员控制：`Assets/5_Scripts/5-3_GamePlay/Controller/PlayerAdminController.cs`。
- 移动/朝向：`Assets/5_Scripts/5-3_GamePlay/Move/`。
- 摄像机模块：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_Cam.cs`。
- 世界焦点：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_FocusPoint.cs`。
- AI 焦点：`Assets/5_Scripts/5-3_GamePlay/Move/Mod_FocusPoint_AI.cs`。
- 玩家 Prefab：`Assets/2_Prefabs/Player/`。

## 调用边界

```text
Input System / PlayerInputActions
→ GameController
→ 点击、移动、交互或模块事件
→ 玩家 Item 上的 Inventory / Skill / Building / Hand 等模块
```

- UI 上方点击由 `GameController.IsPointerOverUI()` 拦截。
- 濒死、过场或联机准备期间使用输入锁定，不要通过禁用整个玩家对象规避输入。
- 玩家运行时引用优先从 `ItemMgr.User_Player` / `UserPlayerTransform` 获取，兼容单机与联机本地玩家。

## 近期变更

- 2026-07-27：输入中心同时支持键鼠与手柄虚拟光标，业务模块应从 `GameController` 获取统一指针世界坐标。

## 修改后维护本 Skill

改变 Input Action 名称、玩家 Prefab、控制器模块、交互协议、摄像机/焦点路径或本地玩家解析方式后，必须更新本 Skill；影响 UI、网络或 Item 注册时同步更新对应 Skill。

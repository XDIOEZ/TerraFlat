---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名契约、动态 UI、UI 音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI。"
argument-hint: "UI 面板、控件或 Prefab 问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld UI 系统定位

> 最后核对：2026-07-27。修改 Prefab 位置或控件节点名后必须立即更新本 Skill。

## 修改前先读

1. `Assets/5_Scripts/5-5_UI/UIManager.cs`：面板根节点、创建、注册、查询、显示和销毁。
2. `Assets/5_Scripts/5-5_UI/BasePanel.cs`：密封通用面板组件、控件收集、开关、拖拽和主题。
3. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.UI.cs`：主菜单、新游戏、存档面板及控件命名契约。
4. `Assets/5_Scripts/5-3_GamePlay/Manager/SaveDataManager_UI.cs`：存档动态列表与玩家按钮。

## 关键脚本

- 存档条目：`Assets/5_Scripts/5-5_UI/GameSaveItemView.cs`。
- 通用旧基类：`Assets/5_Scripts/5-5_UI/BaseUIManager.cs`。
- 视觉主题：`Assets/5_Scripts/5-5_UI/FlatWorldUITheme.cs`。
- UI 反馈：`Assets/5_Scripts/5-5_UI/FlatWorldUIFeedback.cs`。
- 游戏内适配：`Assets/5_Scripts/5-3_GamePlay/UI/`。
- UI 音频绑定：`Assets/5_Scripts/5-5_UI/Audio/`。
- UI Prefab 根目录：`Assets/2_Prefabs/2-1_UI/`。

## 当前架构

```text
领域控制器（GameManager、Inventory、NetworkModeUIController 等）
→ 直接持有/创建 BasePanel
→ 按 GameObject 节点名查询 Button/Input/Text/Toggle/Slider
→ UIManager 注册、查找和管理生命周期
```

- `BasePanel` 是 `sealed`，不要再建立领域 View 继承层或代理层。
- 面板控制器依赖节点名作为键；重命名 Prefab 节点必须同步修改对应 `*Key` 常量。
- UIManager 默认使用 `PanelRoot`，必要时运行时创建 Canvas。
- Prefab 移动后同时检查场景 Inspector 引用、Addressables/Resources 引用和本 Skill 路径。

## 主菜单与存档

- 主菜单/新游戏/存档 Prefab 引用字段位于 `GameManager.UI.cs`。
- 存档磁盘读写在 `SaveDataMgr.cs`，存档列表显示在 `SaveDataManager_UI.cs`。
- 主菜单控件名常量统一位于 `GameManager.UI.cs`，不要散落魔法字符串。

## 联机动态 UI

- 会话逻辑：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.cs`。
- UI 状态绑定：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModeUIController.UI.cs`。
- 动态视觉树：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModePanelView.cs`，文件中声明的是 `NetworkModeUIController` partial，不存在独立 `NetworkModePanelView` 类型。

## 近期变更

- 2026-07-27：领域 UI 改为直接组合密封 `BasePanel`；`GameManager` 与联机控制器使用 partial 分离业务和 UI。
- 2026-07-27：联机动态视觉树仍在 `NetworkModePanelView.cs`，但类型已并入 `NetworkModeUIController` partial。

## 修改后维护本 Skill

任何 UI Prefab 移动、重命名、删除，控件节点名变化，PanelKey 变化，动态 UI 文件拆分，`PanelRoot` 规则或领域控制器绑定变化后，必须在同一任务内更新本 Skill 的路径、命名契约和近期变更；涉及具体系统时也更新该系统 Skill。

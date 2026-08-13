---
name: flatworld-ui
description: "Use when: 定位或修改 FlatWorld 的 UIManager、BasePanel、主菜单、新游戏、存档列表、游戏内 UI、控件命名、动态 UI、UI 文案、多语言、音效或 UI Prefab。关键词：UIManager、BasePanel、GameManager.UI、SaveDataManager_UI、LocalizedTextBinder。"
---

# FlatWorld UI

## 入口

- 生命周期：`Assets/5_Scripts/5-5_UI/{UIManager,BasePanel}.cs`
- 主菜单/存档：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/{GameManager.UI,SaveDataManager_UI}.cs`
- 游戏内 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`
- Prefab：`Assets/2_Prefabs/2-1_UI/`；根：`Assets/Resources/UI/UIRoot.prefab`
- 运行时键/构建器：`RuntimeUIPrefabKeys.cs`、`Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs`

## 架构与不变量

- 领域控制器创建/持有正式 Prefab，`UIManager` 管生命周期；控件节点名是绑定契约。正式 UI 不用 `new GameObject/AddComponent` 拼视觉。
- Prefab 是视觉真相；`BasePanel` 不在初始化时重写结构。新增 Prefab 位于 Addressables `Prefab` 标签范围并用稳定键加载。
- 常驻 HUD 不拦截输入，Graphic 关闭 raycastTarget；若 HUD 提供展开/收起功能，只允许开关按钮接收 raycast，内容和装饰元素仍必须输入透明；模态面板才获取输入锁和顶层手柄焦点，关闭/失败路径释放。
- 坐标、角色状态等信息型 HUD 使用屏幕角落锚点和透明容器，只显示会随运行时变化的字段/状态条；禁止为这类 HUD 添加整块背景、卡片标题或装饰性介绍文字。
- 常驻组件事件驱动；禁止等待绑定或比较静态状态的 Update/LateUpdate 和逐帧 `GetComponent*`。
- 动态列表复用条目；结构变化才局部 MarkLayoutForRebuild，数值/颜色更新不强制布局。热路径禁止 ForceUpdateCanvases/ForceRebuild。
- EventSystem 反馈保持唯一非缩放 Tween，重入先 Kill，失活/销毁清理。
- 主菜单控件名集中在 `GameManager.UI.cs`；定向构建 Prefab，避免无关重写。

## 文案、焦点与联动

- 所有玩家可见文字同时使用 `flatworld-localization`：静态文本进入 `FlatWorldUI`，动态模板登记英文覆盖并使用 GetUiText/GetUiFormat；节点名不翻译。
- 同类主菜单模态面板（新建世界、存档选择、设置、联机）统一使用 `FlatWorldUIPanelMetrics.SharedModalCardSize`；调整任一面板尺寸时必须同步检查其余面板的卡片尺寸、锚点、边距和内容是否越界。
- 面板文案只保留完成当前操作所需的标题、字段名、状态、按钮和必要提示；删除眉题、重复介绍、流程串、装饰性英文和不会改变操作结果的占位说明，禁止为了填充留白新增无用文字。
- 手柄焦点限制在当前顶层导航面板；TMP 输入框在确认后才进入虚拟键盘编辑。
- UI 音效联动 Audio；存档/联机/背包/Quest/Buff HUD 只加载实际命中的领域 Skill。

## 验证

- 检查 Prefab/节点/组件/事件、重复开关、输入锁、焦点边界、输入穿透、条目复用和本地化切换；最终布局再人工看。
- 默认静态诊断、编译和 Console；系统级变化运行 `UI.Smoke`。测试入口：`Assets/GameTest/UI/UISmokeTests.cs`；真实库存面板可用 Golden Path `ui.inventory-panel`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

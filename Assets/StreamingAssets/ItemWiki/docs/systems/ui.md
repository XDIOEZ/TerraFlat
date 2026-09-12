# UI 系统

## 系统定位

负责主菜单、游戏 HUD、模态面板、库存/制作/装备界面、设置页、输入焦点和统一 UI 视觉规范。

## 当前架构

- `UIManager + BasePanel` 负责正式面板生命周期。
- 正式 UI 结构来自 Prefab，运行时不通过大量 `new GameObject/AddComponent` 临时拼装视觉。
- Prefab 节点名称是绑定契约，脚本依赖稳定节点名和序列化引用。
- 常驻 HUD 默认不锁玩法输入；模态面板才获取输入锁和顶层焦点。

## 当前视觉基准

- 中性灰表面。
- 近白文字。
- 低对比约 2px 边界。
- 暖黄只用于焦点、选择和少量关键操作。
- 普通面板不继续引入蓝绿高饱和主题。
- 物品图标、世界美术和有明确玩法语义的颜色保持原色。

## 关键入口

- `Assets/5_Scripts/5-5_UI/Core/UIManager.cs`
- `Assets/5_Scripts/5-5_UI/Core/BasePanel.cs`
- `Assets/5_Scripts/5-3_GamePlay/Presentation/UI/`
- `Assets/2_Prefabs/2-1_UI/`
- `Assets/Resources/UI/UIRoot.prefab`

## 输入与层级

- 同一 PanelRoot 内置顶/置底用兄弟顺序。
- 独立 Canvas 才使用 sortingOrder。
- `UI_Hand` 等跟随指针视觉必须位于顶层且不拦截目标射线。
- Android 返回/Escape 优先关闭最上层可取消面板。
- HUD 中只有真正可点击的按钮接收 Raycast，纯信息和装饰节点保持输入透明。

## 设置系统

设置项通过 `ISettingsProvider` 等通用契约暴露，不让设置 UI 直接读写业务管理器内部字段。

## 修改时联动

- 玩家可见文案：Localization。
- 库存槽位：背包系统。
- Buff 状态栏：Buff。
- 生存 HUD：生存/环境。
- 新正式 Prefab：Addressables 和 RuntimeUIPrefabKeys。

## 对应 Skill

`.agents/skills/flatworld-ui/SKILL.md`

# FlatWorld Editor 工具目录

编辑器工具按职责归档，新增工具应放入对应目录，避免重新堆积在根目录。

- `Automation/`：编译、构建与内容校验工具。
- `ContentTools/Items/`：物品、武器与配方内容维护。
- `ContentTools/Migrations/`：一次性或兼容性数据迁移。
- `ContentTools/Validation/`：只读内容校验与构建前检查。
- `DataTables/Balance/`：ItemWiki 运行时热更新桥接、旧数据格式（不再提供独立工作台窗口）。
- `DataTables/Food/` 与 `DataTables/Prefab/`：旧表序列化数据，供工作台首次迁移；不再提供重复窗口。
- `PrefabBuilders/UI/`：UI Prefab 重建和主题迁移。
- `PrefabBuilders/Building/`：建筑相关 Prefab 生成。
- `Productivity/Todo/`：项目待办窗口与数据。
- `Productivity/`：编译通知等通用编辑器效率工具。
- `ProjectTools/Structures/`：遗迹编辑器；临时 Authoring Prefab 按需生成到其 `AuthoringAssets/`，不再另建顶层 `Structures/`。

`Assets/Editor` 根部的 DOTween DLL、XML 与 `Imgs/` 属于第三方编辑器资源，保持供应商布局，不并入 FlatWorld 自研工具目录。

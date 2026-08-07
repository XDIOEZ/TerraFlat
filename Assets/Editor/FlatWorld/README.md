# FlatWorld Editor 工具目录

编辑器工具按职责归档，新增工具应放入对应目录，避免重新堆积在根目录。

- `Automation/`：Golden Path 与自动化测试桥接。
- `ContentTools/Items/`：物品、武器与配方内容维护。
- `ContentTools/Migrations/`：一次性或兼容性数据迁移。
- `ContentTools/Validation/`：只读内容校验与构建前检查。
- `DataTables/Food/`：食物数值表、配置资源与文档。
- `DataTables/Prefab/`：Prefab 数值表与配置资源。
- `PrefabBuilders/UI/`：UI Prefab 重建和主题迁移。
- `PrefabBuilders/Building/`：建筑相关 Prefab 生成。
- `Productivity/Todo/`：项目待办窗口与数据。
- `Productivity/`：编译通知等通用编辑器效率工具。
- `Structures/`：结构编辑器的 Authoring 资源。

`Assets/Editor` 根部的 DOTween DLL、XML 与 `Imgs/` 属于第三方编辑器资源，保持供应商布局，不并入 FlatWorld 自研工具目录。

# Unity Prefab Browser

这是一个 VS Code 自定义编辑器扩展：在资源管理器中单击 `.prefab` 后，中间编辑区会显示 Unity Hierarchy 风格的可视化层级，而不是 YAML 文本。

## 当前功能

- 解析 Unity 文本序列化 Prefab 的 GameObject / Transform 层级。
- 展示组件类型、启用状态、嵌套 Prefab 标记和统计信息。
- 支持节点搜索、展开全部、折叠全部、复制路径和在资源管理器中定位。
- 监听 `.prefab` 文件变化并自动刷新。
- 支持与其他 Prefab Custom Editor 共存；可使用 VS Code 的“重新打开方式 / Reopen Editor With”切换编辑器。

## 使用

1. 在 VS Code 中打开 `Tools/VSCode/UnityPrefabBrowser` 文件夹。
2. 按 `F5` 启动 Extension Development Host。
3. 在新窗口中打开 TerraFlat Unity 项目，单击右侧资源管理器里的任意 `.prefab`。

右键 `.prefab` 可以选择“Unity Prefab Browser: Open with Unity Prefab Hierarchy”，也可以选择其他扩展提供的 Custom Editor。单击文件默认使用本扩展，满足直接浏览层级的操作习惯。

如果要安装到普通 VS Code，需要先用 `vsce package` 打包成 `.vsix`，再通过扩展面板的“从 VSIX 安装”安装。

## 限制

扩展读取的是保存到磁盘的 Prefab YAML；二进制 Prefab 和 Unity 编辑器里尚未保存的临时状态不会被读取。项目需要在 Unity 的 `Editor Settings > Asset Serialization` 中使用 `Force Text`。

本扩展不会读取或修改其他 Custom Editor 的内存状态；其他编辑器保存文件后，本扩展会自动刷新磁盘上的结果。

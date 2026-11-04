# Android MOD 原生目录入口

- Unity 菜单经 `AndroidModDirectoryLauncher` 同步调用 `Assets/Plugins/Android/FlatWorldMods.androidlib`；Java 入口返回系统 Activity 是否启动，不持有菜单回调。
- `FlatWorldModDocumentsProvider` 独立定位 `getExternalFilesDir(null)/Mods`，启动时核对 Unity 传入的实际安装根；无需 Unity 初始化，系统文件界面也可独立查询。
- Manifest authority 为 `${applicationId}.mods.documents`，只通过系统 SAF 授权开放 MOD 根，使用 `MANAGE_DOCUMENTS`、`grantUriPermissions`；不申请所有文件访问权限。
- 按钮使用 `ACTION_VIEW` + 根 URI + `vnd.android.document/root` 打开文件管理界面；不把 `ACTION_OPEN_DOCUMENT_TREE` 的授权选择器当作文件管理器。无兼容 Activity 时复制路径并提示选择“FlatWorld MOD”。
- 支持目录/文件创建、读写、重命名、递归删除；API 24+ 声明并实现同 Provider 复制/移动。跨来源文件复制、移动由文件管理器通过文件流和创建/删除完成。
- 根目录不可重命名、删除、复制、移动；文档 ID 采用根内相对路径，拒绝越界及符号链接。重名自动加序号，复制失败清理新目标；移动到自身或子目录被拒绝。
- Provider 只通知系统文件列表变更，不扫描、启停或重载 MOD。返回游戏后仍手动刷新；原有启用与重启流程归 MOD 菜单所有。
- 自定义 Gradle 模块继承 `unityLibrary` 的 SDK/build tools，consumer ProGuard 保留 JNI 类与公开入口；不改主 Gradle 模板。

## 验证边界

- 原生能力不能由编辑态 Lua 规则替代。MOD 发现仍按 `modding` 系统规则回归；不为验证按钮而开放测试反射或重复加载算法。
- Android 构建核对 Java 编译、最终 Manifest authority/权限、DEX 与 Release 混淆后 `openModsDirectory(Activity,String):boolean` 的保留。
- Android 11+ 实机核验：首次空目录、中文包名/目录、从下载目录复制完整包到入口、复制移出、重命名、删除、取消返回，再点游戏“刷新”确认发现结果。
- 补测同源复制/移动、重名保留原文件、拒绝目录移入自身；厂商文件管理器不支持该入口时检查提示及剪贴板回退。
- 默认只构建，不安装。插件 AAR/最小验证 APK 仅证明 Android 插件打包与 Manifest 合并，不能替代完整游戏 IL2CPP 构建和真机文件操作验证。

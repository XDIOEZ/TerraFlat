# Lua 直接访问 C# 示例

将整个目录复制到游戏的 `persistentDataPath/Mods/` 下，在主菜单 MOD 管理页启用，再进入世界。

本包只需一个清单和两份 Lua：`main.lua` 订阅正式玩家入世事件，`scripts/speech.lua` 获取已有的自言自语组件并调用 `Say`。没有新增业务 C# 或 MOD API。

`RegisterCleanup` 在 Lua 虚拟机释放前执行退订；不要省略退订。台词只在本地显示，不代表新增了跨客户端聊天广播。`Say` 仍受正式气泡显示与优先级规则约束，返回 false 不应被当成已经显示成功。

API v2 开放 CS，外部脚本视为受信任代码；Lua 修改世界时仍需遵循服务端权威规则。运行过脚本的 MOD 请返回主菜单重载，不支持世界内候选回滚。Android 的最终支持范围以 IL2CPP 真机验证为准。

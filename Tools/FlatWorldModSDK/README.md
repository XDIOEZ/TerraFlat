# FlatWorld MOD SDK

## 创作流程

1. 在 Unity 执行 `FlatWorld/MOD/创建示例 MOD` 查看完整运行包。
2. 在项目 `Assets` 下建立作者源目录，并编写 `manifest.json`、Defs、Patches、Localization、Settings、Lua。
3. Bundle 资源在 Inspector 填写 AssetBundle 名称，名称必须对应 `manifest.bundles[].id`。
4. 执行 `FlatWorld/MOD/创作与打包工具`，选择作者源目录，先校验再构建安装，并可导出 ZIP 分发包。
5. 启动游戏后按 `F10` 查看 MOD 管理器；启停、排序或修改需重载的设置后，在主菜单重载内容。

## 运行包结构

```text
MyMod/
  manifest.json
  Defs/items.json
  Patches/balance.json
  Localization/zh-CN.json
  Localization/en.json
  Settings/settings.json
  Lua/main.lua
  Bundles/windows.bundle
```

运行包禁止 `.dll`、`.exe`、`.cs`、PowerShell/批处理等可执行内容；只允许数据、Lua 和 Unity AssetBundle。

## 稳定协议

- MOD ID：小写字母、数字、`.`、`_`、`-`。
- 内容 ID：`mod.id:definition_id`。
- 当前 `apiVersion`：`1`。
- 加载顺序：硬依赖 → `loadBefore/loadAfter` → `loadOrder` → 玩家软顺序 → ID。
- Patch 顺序：最终 MOD 顺序 → `patchFiles` 顺序 → 文件内顺序。
- Patch 操作：`set`、`replace`、`merge`、`add`、`remove`、`test`，可使用 `expect` 做冲突保护。
- 设置作用域：`client`、`world`、`server`；Lua 只能修改 `client` 设置。
- 联机必须拥有完全一致的 MOD API、加载顺序、版本、内容哈希和权威设置。

## Lua 生命周期

主入口返回 table，可实现：

- `OnLoad(api)`
- `OnUpdate(api, deltaTime)`
- `OnEvent(api, eventName, payloadJson)`
- `OnContentReady(api, payloadJson)`
- `OnWorldEntered(api, payloadJson)`
- `OnWorldExiting(api, payloadJson)`
- `OnPlayerEntered(api, payloadJson)`
- `OnItemSpawned(api, payloadJson)`
- `OnItemDespawning(api, payloadJson)`
- `OnSceneLoaded(api, payloadJson)`
- `OnSave(api, stateJson)`
- `OnLoadSave(api, stateJson)`
- `OnUnload(api)`

物品 Lua 模块支持 `OnLoad`、`OnUpdate`、`OnAct`、`OnSave`。

## 安全和兼容

- 不支持外部 C# DLL、Harmony、私有字段反射或运行时 IL Patch。
- 不支持进入世界后的运行中卸载。
- 上次加载失败时，下次启动自动进入一次安全模式。
- 存档记录精确 MOD 集合；不匹配时拒绝读档，防止静默损坏。

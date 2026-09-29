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

普通内容包支持数据、Lua 和 Unity AssetBundle。C# MOD 另外通过 `managed` 显式声明入口 DLL 和依赖 DLL，并需要玩家确认代码指纹后才加载；未声明的 DLL、`.exe`、`.cs`、PowerShell/批处理仍禁止进入运行包。

## C# 与 Harmony MOD

桌面 Mono 构建支持 `IManagedGameMod` 托管入口。作者工程放在 `Assets` 外，避免补丁被 Unity 编入游戏本体；完整示例位于项目根目录 `ModSDK/Examples/HarmonyMachines/`。

```json
{
  "apiVersion": 1,
  "id": "example.harmony.machines",
  "version": "1.0.0",
  "managed": {
    "entryAssembly": "lib/Example.HarmonyMachines.dll",
    "entryType": "Example.HarmonyMachines.ModEntry",
    "dependencies": ["lib/0Harmony.dll"]
  }
}
```

生命周期为 `Initialize(context)` → `ContentReady()` → `Dispose()`。新增整类机器逻辑使用 `MachineLogicRegistry.Register`，注册租约交给 `context.Track`；Harmony 补丁由 MOD 自己创建，并在 `Dispose` 中只撤销自己的补丁 ID。`FurnaceLogic`、`WorkbenchLogic`、`RecipeProcessor` 和机械解算保留托管具名入口，不要求每台机器都存在 GameObject。

编译和打包使用示例工程；作者自行提供兼容的 Harmony 2 程序集及其显式依赖。编辑器菜单 `FlatWorld/MOD/授权 C# MOD 代码版本` 只负责检查并记录用户确认的 SHA256，不执行代码。代码更新后重新授权；进程已经加载的同名 DLL 不能换字节热替换，需要重启游戏。

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

## JSON Buff

Buff 写在 `definitionFiles` 的 `buffs` 数组中，不再使用 Buff ScriptableObject：

```json
{
  "buffs": [
    {
      "id": "my.mod:burning",
      "displayName": "燃烧",
      "durationSeconds": 10,
      "tickIntervalSeconds": 1,
      "stackMode": "refresh_duration",
      "effects": [
        { "phase": "tick", "typeId": "core:true_damage", "value": 2 }
      ]
    }
  ]
}
```

加载时会把 `typeId` 解析为 C# Handler 并缓存；运行 Tick 不再进行字符串字典查询。完整字段和内建效果 ID 见 `Schemas/items.schema.json`。

## 安全和兼容

- C# / Harmony MOD 拥有游戏进程权限，不是 Lua 沙箱；只加载玩家已经信任的包。反射及 Harmony 不限于推荐机器入口，但不能把原生代码或 Burst 代码当成普通托管 IL。
- 当前托管 DLL 加载器不支持 IL2CPP；Android 的 IL2CPP 配置不因 MOD 支持而改变，JSON/资源/Lua 路径保持独立。
- 不支持进入世界后的运行中卸载。
- 已启用 C# / Harmony 的会话不执行隔离 F5 热替换，避免候选补丁提前影响真实世界；返回主菜单重载，修改过的程序集还需重启进程。
- 上次加载失败时，下次启动自动进入一次安全模式。
- 存档要求仍安装其中实际引用的 MOD ID；同 ID 内容更新使用当前定义。联机继续严格校验当前 MOD 集合与内容哈希，不允许客户端自行结算机器扣料和产物。

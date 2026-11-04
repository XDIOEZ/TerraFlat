# FlatWorld MOD SDK

## 创作流程

1. 在 Unity 执行 `FlatWorld/其他工具/MOD/创建示例 MOD` 查看完整运行包；纯 Lua 可直接参考 `ModSDK/Examples/LuaDirectCs/`。
2. 在项目 `Assets` 下建立作者源目录，并编写 `manifest.json`、Defs、Patches、Localization、Settings、Lua。
3. Bundle 资源在 Inspector 填写 AssetBundle 名称，名称必须对应 `manifest.bundles[].id`。
4. 执行 `FlatWorld/其他工具/MOD/创作与打包工具`，选择作者源目录，先校验再构建安装，并可导出 ZIP 分发包。纯 Lua 包不需要构建 AssetBundle。
5. 把包放进游戏的 `Mods/<modId>/` 目录，在主菜单打开“MOD 管理”并启用，可拖拽条目调整顺序。Lua 在主菜单重载；C# 代码变化需要重启游戏。F10 旧窗口已删除。

## 运行包结构

```text
MyMod/
  manifest.json
  preview.png
  Defs/items.json
  Patches/balance.json
  Localization/zh-CN.json
  Localization/en.json
  Settings/settings.json
  Lua/main.lua
  Bundles/windows.bundle
```

普通内容包支持数据、Lua 和 Unity AssetBundle。C# MOD 另外通过 `managed` 显式声明入口 DLL 和依赖 DLL，启用后在游戏启动时加载；未声明的 DLL、`.exe`、`.cs`、PowerShell/批处理仍禁止进入运行包。

展示图通过 `manifest.json` 的 `previewImage` 声明，例如 `"previewImage": "preview.png"`。路径必须位于 MOD 包内，支持 PNG / JPG / JPEG；图片缺失或损坏只会显示默认占位，不影响 MOD 本身启停。建议使用横向图片，单张不超过 8 MB，宽高均不超过 4096 像素。

普通合成的 `Defs/recipes.json` 直接列出物品或标签所需的总数量，例如末影箱示例的材料是 `{"match":"exact_item","itemId":"Plank","amount":8}` 与 `{"match":"exact_item","itemId":"StoneSlab","amount":1}`。同一种材料合并成一条；`amount: 0` 表示需要但不消耗的工具。普通合成不写 `slot`、`gridWidth`、`gridHeight`、`allowMirror` 或动作 `slotIndex`；热加工仍可用位置规则。完整配方见 `ModSDK/Examples/EnderChest/Defs/recipes.json`。

## C# 与 Harmony MOD

桌面 Mono 构建支持 `IManagedGameMod` 托管入口。作者工程放在 `Assets` 外，避免补丁被 Unity 编入游戏本体；完整示例位于项目根目录 `ModSDK/Examples/HarmonyMachines/`。

```json
{
  "apiVersion": 2,
  "id": "example.harmony.machines",
  "version": "1.0.0",
  "previewImage": "preview.png",
  "managed": {
    "entryAssembly": "lib/Example.HarmonyMachines.dll",
    "entryType": "Example.HarmonyMachines.ModEntry"
  }
}
```

生命周期为 `Initialize(context)` → `ContentReady()` → `Dispose()`。新增整类机器逻辑使用 `MachineLogicRegistry.Register`，注册租约交给 `context.Track`；Harmony 补丁由 MOD 自己创建，并在 `Dispose` 中只撤销自己的补丁 ID。`FurnaceLogic`、`WorkbenchLogic`、`RecipeProcessor` 和机械解算保留托管具名入口，不要求每台机器都存在 GameObject。

编译和打包使用示例工程。桌面 Mono 发行包在 `StreamingAssets/ManagedModRuntime/0Harmony.dll` 提供 Harmony 2.3.3，运行时按需加载；开发时引用同一文件，MOD 包不附带它，也不在 `managed.dependencies` 声明它。主菜单 MOD 页不显示代码 SHA256，也不要求额外授权。代码更新后重启游戏；进程已经加载的同名 DLL 不能换字节热替换。

## 稳定协议

- MOD ID：小写字母、数字、`.`、`_`、`-`。
- 内容 ID：`mod.id:definition_id`。
- 当前 `apiVersion`：`2`。v1 业务 ModApi/ModItemApi 已删除；旧包须迁移脚本并声明 v2，不能只改版本号。
- 加载顺序：硬依赖 → `loadBefore/loadAfter` → `loadOrder` → 玩家软顺序 → ID。
- Patch 顺序：最终 MOD 顺序 → `patchFiles` 顺序 → 文件内顺序。
- Patch 操作：`set`、`replace`、`merge`、`add`、`remove`、`test`，可使用 `expect` 做冲突保护。
- 设置作用域：`client`、`world`、`server`；`ModSettingsRegistry.SetClientValue` 仍只写 client 设置。
- 联机必须拥有完全一致的 MOD API、加载顺序、版本、内容哈希和权威设置。

## Lua 生命周期

主入口返回 table，只保留 `OnLoad(mod)`、`OnContentReady(mod)`、`OnUpdate(mod, deltaTime)`、`OnSave(mod, stateJson)`、`OnLoadSave(mod, stateJson)`、`OnUnload(mod)`。旧的 `OnEvent` 与 OnWorldEntered/OnPlayerEntered 等 JSON 转发回调已移除；游戏事件直接订阅现有 C# 事件，不再维护第二套事件总线。

`mod` 是精简的 `ModLuaContext`，只提供 `ModId`、`CanModifyWorld`、`Log`、`GetGlobalState/SetGlobalState`、`RegisterCleanup(Action)`。也可直接访问全局 `mod`。玩法不再逐项包装：

```lua
local verbose = CS.ModSettingsRegistry.GetBool(mod.ModId, 'verbose', true)
local text = CS.ModLocalizationRegistry.Translate(mod.ModId .. ':greeting', '你好')
-- 已取得角色上的 CharacterSoliloquyController 后：
speech:Say(text, CS.FlatWorld.Dialogue.CharacterSpeechPriority.Player, 5.0, mod.ModId)
```

物品 Lua 的 `OnLoad/OnUpdate/OnAct/OnSave(item, stateJson, deltaTime, mod)` 直接接收真实 `Item`，例如 `item.itemData.IDName`、`item.itemData:AddDurability(...)`、`item:GetComponent(typeof(CS.Mod_Mover_AI))`。返回字符串仍保存进原 `Ex_ModData.BitData`，全局状态仍通过原 MOD 存档管线保存。

每个 MOD 独立 LuaEnv；`require('scripts.helper')` 从当前包的 `scripts/helper.lua` 加载并缓存，不共享其他 MOD 的模块缓存。包内模块正常返回 table，无需编写额外 C#。

订阅 C# 事件后必须保留同一个 Lua 函数并登记退订：

```lua
local function entered(player) mod:Log(player.itemData.IDName) end
CS.GameManager.Event_PlayerEnterWorld('+', entered)
mod:RegisterCleanup(function()
    CS.GameManager.Event_PlayerEnterWorld('-', entered)
end)
```

仅登记过的清理动作由运行时逆序执行；直接 CS 创建的事件订阅、协程和对象不会被自动发现并回滚。跨帧持有 Item 时须注意卸载、对象池复用及 RuntimeGeneration，旧引用不等于原来的物品。客户端不能假定调用 CS 就能获得服务端权威，修改世界前检查 `mod.CanModifyWorld` 并遵循正式系统的权威入口。

## 构建与调用边界

普通游戏类默认通过 xLua 反射访问，不为每个业务方法生成新的 C# 包装文件。`ModLuaLinkerPreservation` 合并在现有 `ModAuthoringWindow.cs` 中，构建时从玩家程序集的 `Assets/5_Scripts/` 来源自动生成临时 link.xml，保留这些程序集和 xLua 委托桥。新增游戏方法不需要修改 ModApi；已有 Unity 类型包装仍由原 xLua 配置维护。

这是以一定代码裁剪收益换自由度：反射调用有开销，尤其不适合每帧逐个遍历大量实体。高频操作优先调用一次完整的 C# 服务。第三方程序集、复杂泛型、接口代理以及含值类型/ref/out 参数的特殊委托仍需针对性绑定与 AOT 验证；不是所有 public 成员都保证无需桥接即可在 Android 运行。保留代码不能让旧版游戏获得从未编译进去的新 C# 类型。

没有新增通用 Hook 框架；优先直接调用既有方法、订阅既有事件。完整自言自语例子见 `ModSDK/Examples/LuaDirectCs/`。

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

- C# / Harmony 与开放 CS 的 Lua 都按受信任代码处理，不是安全沙箱；清空 io/os 也不能阻止经 C# 访问系统能力。只安装可信 MOD。反射及 Harmony 不限于推荐机器入口，但不能把原生代码或 Burst 代码当成普通托管 IL。
- 当前托管 DLL 加载器不支持 IL2CPP；Android 的 IL2CPP 配置不因 MOD 支持而改变，JSON/资源/Lua 路径保持独立。
- 不支持进入世界后的运行中卸载。
- 已运行外部 Lua 或启用 C# / Harmony 的会话不执行世界内候选热替换；候选阶段也禁止首次执行新 Lua，避免未提交脚本修改真实世界。返回主菜单重载，修改过的程序集还需重启进程；纯数据候选机制保留。
- 上次加载失败时，下次启动自动进入一次安全模式。
- 存档要求仍安装其中实际引用的 MOD ID；同 ID 内容更新使用当前定义。联机继续严格校验当前 MOD 集合与内容哈希，不允许客户端自行结算机器扣料和产物。

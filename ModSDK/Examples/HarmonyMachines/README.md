# 机器世界 Harmony 示例

示例针对当前工程的托管 C# 机器逻辑，不依赖每台机器都有 GameObject。源码不放进 Assets，避免 Unity 将 MOD 补丁编进本体。

## 编译和安装

先让当前游戏代码编译成功，再自行准备兼容 Unity Mono 的 Harmony 2 托管程序集。本目录不附带第三方 DLL。

```powershell
dotnet build -c Release `
  -p:GameManagedDirectory="D:\_Unity\_UnityProject\FlatWorld\Library\ScriptAssemblies" `
  -p:HarmonyDllPath="D:\ModLibraries\0Harmony.dll"
```

编译目标将本 MOD DLL、Harmony DLL 和 manifest 放到 `package/`。只把 `package` 的内容安装到 `Application.persistentDataPath/Mods/example.harmony.machines/`，不要把整个作者工程复制进去；运行目录禁止 C# 源文件和未声明的可执行文件。

本示例假定所选 Harmony 发行包的托管依赖已经合并进 0Harmony.dll。若发行包另含必需托管依赖，须一并复制并逐项加入 `managed.dependencies`，不能通过目录扫描自动执行其它 DLL。不要复制 Unity、GamePlay 或系统程序集进 MOD 包。

Unity 菜单 `FlatWorld/MOD/授权 C# MOD 代码版本` 可以选择安装后的包并查看代码 SHA256。确认信任后才授权，再返回主菜单重载资源。代码变化须重新授权；进程中已加载的同名 DLL 换字节后须重启游戏，不支持 F5 隔离替换全局 Harmony 补丁。

这个授权工具只写入用户配置，不执行 DLL。本次仓库修改没有安装或授权示例 MOD，也没有执行 Harmony 运行验收。

## 补丁边界

`ModEntry.Initialize` 在内容加载前执行；`ContentReady` 在正式 MOD 内容发布后执行；退出资源会话时调用 `Dispose`。示例补丁给 `FurnaceLogic.CalculateMaximumTemperature` 的返回值加 100，卸载只撤销当前 MOD ID 的补丁。

常用托管入口：

| 目标 | 用途 |
| --- | --- |
| `MachineWorld.CalculateWorkAmount` | 修改机械工作量 |
| `RecipeProcessor.Advance / Commit` | 修改加工推进或提交 |
| `WorkbenchLogic.PerformWork` | 修改工作台人工制作 |
| `FurnaceLogic.Tick / ConsumeFuel / HeatAndProcess` | 修改炉体运行 |
| `FurnaceLogic.CalculateMaximumTemperature` | 修改炉温上限 |
| `MachineCombatBridge.ApplyDamage` | 修改机器受伤，包括按条件免伤 |
| `MechanicalNetworkGraph.Solve` | 修改整网动力解算 |

这些只是推荐入口，不是白名单。可信 C# MOD 仍可使用 Harmony/反射操作本体其它托管实现；关键入口避免内联并在 link.xml 保留。不要把 Unity 原生方法、Burst 机器码或 IL2CPP 代码当成普通 IL 方法承诺可补丁。

本次支持目标是桌面 Mono 托管构建。当前 Android 使用 IL2CPP，托管 DLL 加载器会明确拒绝，不会静默改成 Mono。JSON/资源/Lua MOD 不因此禁用。

联机仍要求双方 MOD 集合与内容哈希一致；有世界副作用的补丁应遵守本体的权威端检查，不要让客户端独立扣料或产物翻倍。

## 不用 Harmony 也能扩展

`MachineLogicRegistry.Register(id, factory, authoringType, replace)` 返回注销租约，可用 `context.Track(...)` 在 MOD 卸载时清理。新增设施可以注册整类 `MachineLogic`，再注册 `MachineDefinition`，非传动设施使用 `Ports = "none"`。替换内置领域工厂时明确设置 `replace: true`；熔炉、加工台等保持粗粒度，不必拆出一套微型模块框架。

注册自定义鼓风来源使用 `MachineWorld.RegisterAirflow`，同样把返回租约交给 `context.Track`。所有内容注册与游戏对象访问放在主线程生命周期中。

## 官方资料

- Harmony 基础与撤销补丁：https://harmony.pardeike.net/v2/articles/basics.html
- Prefix / Postfix / Transpiler：https://harmony.pardeike.net/v2/articles/patching.html
- 内联、泛型、原生方法限制：https://harmony.pardeike.net/v2/articles/patching-edgecases.html

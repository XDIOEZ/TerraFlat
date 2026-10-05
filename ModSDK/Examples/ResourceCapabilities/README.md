# 资源 ECS 能力示例

这个 C# MOD 给苹果树增加一份树脂状态，每 0.25 秒批次推进一次，并随资源快照保存树脂量。示例没有额外的 Module Prefab，也不创建树木 GameObject；树脂还没有采集 UI 或掉落命令。

`ModEntry.Initialize` 先注册 `example.resourcecapabilities:resin`，JSON 再通过 `modules.resin.prefab` 引用这个稳定地址。这个字段沿用现有格式，但注册过的资源扩展不需要实际 Prefab。只支持声明 `entityRuntime: "resource"` 的定义。

入口代码见 `ModEntry.cs`：

- `CompileResin` 每个定义编译一次，读取 JSON 配置，返回初始化、捕获、释放回调。
- `context.SetState/GetState<T>` 在同一个 Entity 上按稳定模块名保存普通 C# 状态。快照使用本体 `Ex_ModData`，不需要给游戏的 MemoryPack Union 注册 MOD 数据类型。
- `Initialize` 恢复存档并登记实例，`Capture` 保存当前值，`Release` 移除登记。F5 更换定义会先释放旧能力，再按新配置恢复状态；初始化失败会清理已开始的能力。
- 可选 `runtimeFactory` 每种能力在当前共享 World 里只创建一个运行器。示例用普通 C# 批次处理；主线程有结构变化、保存或退出时，Job 运行器必须实现自己的 `CompleteEntityJobs`。普通系统派生自 `SystemBase` 时应在 `OnUpdate` 里合并当轮 Job 依赖。

MOD 修改生命、位置、外观等本体组件之后，调用 `NaturalEntityEcsService.NotifyChanged(handle)`；`context.NotifyChanged()` 是同一入口。自己扩展状态的内部数值变化无需反复刷新本体表现。

普通 C# MOD DLL 在游戏启动后加载，Unity Entities 1.3.8 已初始化的类型表不能自动认识 DLL 中新声明的原生组件。因此示例使用本体已编译的 `ResourceEntityExtensionState` 承载 MOD 普通对象。`ResourceEntityCapability(ComponentType[], ...)` 供已被 Unity 注册的组件使用；这条接口不会重新初始化 TypeManager。大规模热数据需要原生组件时，应将类型作为 Unity 插件随项目编译，并按当前游戏程序集配置自己的 Job/Burst 工具链。

构建时引用当前桌面 Mono 游戏 `FlatWorld_Data/Managed` 目录：

```powershell
dotnet build .\ResourceCapabilities.csproj -c Release -p:GameManagedDirectory="D:\Games\FlatWorld\FlatWorld_Data\Managed"
```

把生成的 `package` 目录复制到游戏 MOD 文件夹，在管理界面启用并重启。示例新增定义 `example.resourcecapabilities:resin_tree`，可以通过 GM 创建。`GamePlayDllPath`、`NewtonsoftJsonPath`、`UnityEngineCorePath` 可单独覆盖，用于 Unity 开发目录的静态编译；游戏依赖 DLL 不应打进 MOD 包。

注册返回的租约必须在 MOD 释放时 Dispose。注销影响后续定义编译，当前实体仍持有冻结回调与运行器，直到实体/World 释放。C# 代码启停沿用现有重启规则；禁止在活跃世界里覆盖同名运行器。

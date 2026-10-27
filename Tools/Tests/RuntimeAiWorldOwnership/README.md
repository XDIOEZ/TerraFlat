# AI 逻辑世界索引离线回归

该项目直接编译游戏中的 `ItemMgr.RuntimeEntities.cs`，并用轻量宿主替代 Unity 对象和世界上下文。
它验证同场景跨世界迁移、目标暂不可用、旧世界卸载注销和伪空对象清理；不能代替 Unity 场景或真实 AI 运行验证。

在仓库根目录执行：

```powershell
dotnet build Tools/Tests/RuntimeAiWorldOwnership/RuntimeAiWorldOwnership.csproj -p:BaseOutputPath=../../../Temp/RuntimeAiWorldOwnership/bin/ -p:BaseIntermediateOutputPath=../../../Temp/RuntimeAiWorldOwnership/obj/
dotnet Temp/RuntimeAiWorldOwnership/bin/Debug/net8.0/RuntimeAiWorldOwnership.dll
```

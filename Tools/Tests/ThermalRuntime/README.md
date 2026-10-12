# 环境热传导离线回归

在仓库根目录运行：

```powershell
dotnet run --project Tools/Tests/ThermalRuntime/ThermalRuntime.Tests.csproj
```

需要 .NET 8 SDK。编译输出和中间文件统一写入 `Temp/Tests/ThermalRuntime/`，不启动或暂停 Unity。

测试直接编译生产 `ThermalRuntime/ThermalInfluenceRuntime`、`TemperatureMgr`、`ItemMatterReaction`、`WorldItemMatterRules`、`ItemMgr.Matter` 和 `DroppedItemRuntime.Matter`。覆盖环境温差指数趋近、目标温度距离衰减、雪地/沙漠、重叠热源取最大值、供电比例和独立冷源，也检查不同 Tick、角色情况、物品湿度、岩浆直接接触加热、燃点与水接触熄灭。

两个实际分帧调度实现额外覆盖首轮时间基线、九秒补算、后加入物品的独立时间、192 项跨帧处理、非法帧时间和收敛后的通知次数；每个物品按其实际最后结算时刻核对，待处理队列尚未到期时不算成公式误差。

Unity、定义目录、液体、注册/添加入口和周边系统使用最小替身；不证明真实场景里的玩家/动物模块装配、注册事件订阅、冷热 Buff 伤害、空间热源表现、实际网络同步或运行性能。这些仍由游戏内验收确认。

# 温室与冰雪离线回归

执行 `python Tools/Tests/GreenhouseThaw/verify.py`，需要本地 .NET SDK 和已导入过项目的 Unity 编译响应文件。

直接编译当前 WorldModel 和正式温室/融雪规则；检查 CO₂ 分压基准、增温回落与上限、连续雪量与分步一致、暖融后天然雪不复活、冰融进度、咸淡水和真实底床恢复，以及已融冰水域的天然取水基线。

输出全部写入 `Temp/GreenhouseThawValidation`，不连接、刷新、编译或控制正在运行的 Unity。此入口没有运行天气队列、网络、磁盘存档或画面验收；对应 Lua 入口为环境系统的 `environment_greenhouse`、`environment_surface_thaw`。

可加 `--gameplay-assembly <外部编译好的GamePlay.dll>`，额外调用完整程序集的真实大气创建/抽排、PlanetData、MemoryPack 和雪冰快照接口；依赖 DLL 从现有 Unity 编译响应文件读取。新 Lua 使用项目实际 XLua 动态库编译语法并检查登记路由，此步骤不执行 Lua 断言。

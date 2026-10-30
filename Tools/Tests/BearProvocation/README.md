# 熊连续驻留与仇恨

运行 `python Tools/Tests/BearProvocation/verify.py --build`，不会刷新 Unity、切换 PlayMode 或加载世界。

- 独立 .NET 进程原样编译 `AIProximityHatredLedger.cs`；项目实际 `xlua.dll` 加载完整 `AI/rules.lua` 检查语法，并只执行熊的四个账本 Lua 场景及其原有业务断言。
- 覆盖严格超过 60 秒、120 初始仇恨、每秒减 1、同一驻留不反复补满、离开重计、多人独立、重复候选去重、每次命中加 30、分帧衰减、非法时间、失效身份移除和回池清空。
- 原样抽取通用 `AnimalTerritorialHatredCapability` 和目标筛选规则，环境替身只提供活体、场景、视野和玩家注册表；检查零伤命中、角色归属、换持有者、完整代际/世界/维度身份、最高仇恨、环境拒绝、8 米边界与外部范围上限，以及被遮挡仍驻留、重新可见才锁敌。
- `--build` 根据当前 asmdef 收集 Unity 尚未导入的新源文件，离线构建 `FlatWorld.GameTest.csproj` 及其生产依赖；输出在忽略目录 `.codex-tmp/bear-provocation-tests/`。

Unity 快速规则还登记 `ai/bear_definition_and_hit_credit`：读取正式 Manifest 继承后的熊定义，按真实模块原型和参数注册 AI/检测/生命/移动/伤害模块，再调用实际受击和决策钩子，检查能力身份、移除狼咬/狼叫/食物模块、贴图地址、角色归属及遮挡驻留。这个夹具没有走真实伤害接收器派发事件，也未推进正式导航或物理命中。

这是离线规则与类型验证，不代表 Unity 现场已验收。真实出生、地形视线、攻击伤害窗口、多人网络和摇摆画面仍需编辑器正常重编译后运行相关 PlayMode Lua。

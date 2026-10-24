# 散物运动离线规则测试

运行：`dotnet run --project Tools/Tests/LooseItemMotion/LooseItemMotion.Tests.csproj`。

直接编译正式 `LooseItemMotion.Core.cs`、`ConveyorPath.cs` 和 Lua 使用的采样夹具；仅替代 Unity 数学和载荷类型，不复制移动算法。适合用户正在 PlayMode 时验证纯规则，不启动、不刷新、不暂停 Unity。

覆盖同向叠加、反向抵消、逆推、横向偏移、分步一致、弯带/反转、岸线、阻挡、缺失地形、环绕、完全沉没外力门禁、非法输入、来源组合/撤销/自撤销。

这不是 Unity 场景测试，不证明 Physics2D 实际碰撞、联机、入箱事务或表现插值已经验收。正式 Lua 断言位于 `GameplayTests/Systems/Mechanical/rules.lua`。

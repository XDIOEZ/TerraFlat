# 地表接触与太空物理边界离线回归

在仓库根目录运行：

```powershell
dotnet run --project Tools/Tests/ShipContacts/ShipContacts.Tests.csproj -c Release
```

本项目只编译生产的地表接触代码和领域模型，不启动或暂停 Unity。当前入口覆盖：

- 太空玩家、太空物品和巡航船体绕过旧扫掠/冲量算法，旧入口不能修改其速度。
- 不同地表和不相交高度跳过接触，同地表同高度继续阻挡物品。
- 太空碎片查询地表船时不解码、不计重、不建立旧碰撞整体。

以前的自研太空碰撞案例保留在源文件中供历史参考，不再从 Main 执行。原生太空碰撞实际由 SpacePhysicsWorld 的 Dynamic Rigidbody2D、Collider2D 和独立 PhysicsScene2D 完成，不能由这套 .NET 替身确认。

Unity 手动验收应覆盖：物品互撞、物品与自由玩家互撞、船体及残骸反作用、跨星区接触、视图未加载时继续模拟、大气与无大气的漂移、W 进出和手动降落。运行验收由用户执行。

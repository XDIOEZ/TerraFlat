# 气泵逐零件机械运动回归

```powershell
dotnet run --project Tools/Tests/MachineArticulation/MachineArticulation.Tests.csproj --configuration Release
python Tools/Tests/MachineArticulation/verify_assets.py
```

独立 .NET 进程直接编译正式 `MachineArticulatedKinematics` 和 Lua 共用采样，不连接 Unity。检查双缸上下分支整圈销轴、固定杆长、活塞轴线、交替行程、确定性、非法几何拒绝、实际工作资格门禁、变速反转与停机重启的连续相位，并向 `Temp/Tests/MachineArticulation/bin/net8.0/production-pump-poses.json` 导出正式销轴采样。

资源检查器随后核验八层透明拆件的像素尺寸、锚点、连杆端点、GUID、PPU、无降采样导入设置及 Addressables 登记。它使用该正式采样，把实际裁片两端销轴经固定缩放和旋转后逐帧对齐关节；前后壳拼合与裁片只允许清单明确声明的低 Alpha 噪点清理，其余像素严格相等。

对应 Lua：`GameplayTests/Systems/Mechanical/machine_articulation.lua`，快速规则入口选择 `mechanical`。用户处于 PlayMode 时仅运行本目录离线检查；Unity 导入、Lua 执行、BRG/船体表现、实际气体事务反馈与画面验收仍需之后执行，离线通过不能代替这些结论。

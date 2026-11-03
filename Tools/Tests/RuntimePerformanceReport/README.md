# 打包版一键性能报告回归

在仓库根目录执行：

    dotnet run --project Tools/Tests/RuntimePerformanceReport/RuntimePerformanceReport.Tests.csproj -c Release

直接编译正式 RuntimePerformanceReportRecorder.cs，并用 Unity/GM/性能点位替身模拟帧更新；不进入 Unity PlayMode，不修改用户场景。
覆盖空缓存和非法时段、最近 30 秒的真实样本范围、慢帧分位数、ZIP 附件、后台发布与剪贴板路径、缓存不足时的部分范围标记，以及 32768 帧环形缓存回绕后的范围筛选。
该回归不能代替 Unity 编辑器编译、GM 布局验收、真实 Player 的 IO/系统剪贴板或 Android 文件管理器测试。

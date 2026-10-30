# 宏观水文基础高度共享回归

```powershell
python Tools/Tests/SharedHydrologyHeightSampling/verify.py
```

编译真实生产程序集，再调用正式宏观水文计算和完整区块生成。独立 CLR 缺少 Unity 原生运行时，仅在 Temp 源副本中将四个 Profiler 作用域改为空操作、将 Burst 函数指针切换成同一数学核的托管执行。`expected.json` 固定保存优化前 `0d1a75d04a8697980e925f85a934c835fd21f09d` 的区域签名；逐项比较坐标、原始高度、流量、终点、下游、可见性和主上游的顺序与原始位。覆盖两种地形算法、无限世界、环绕接缝和跨度不能整除水文网格；同时读取生产诊断对比相邻区域实际高度计算次数。

附加测试使用生产采样上下文与缓存，覆盖采样一部分后取消、旧纪元迟到发布、每组 32768 条和 4 组容量、并发写入及与正式高度函数的逐位一致。它不会启动或修改 Unity，不能代替编辑器快速规则、真实世界画面与运行时性能验证。

原始基线可先用 `HydrologyRaster/verify.py --revision <优化前SHA> --label baseline` 构建，再使用 `--assembly <路径> --write-baseline` 重建签名。直接传当前版本的 `--assembly` 时，该程序集须同样适配独立 CLR。

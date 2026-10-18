# 河道与河漫滩栅格回归

```powershell
python Tools/Tests/HydrologyRaster/verify.py
```

读取 Unity 当前 Bee 的 `FlatWorld.WorldModel.rsp`，把生产程序集重新编译到忽略的 `Temp/HydrologyRasterValidation/current/`，随后通过反射调用生产 `RenderMacroCenters`。需要项目已有一次 Unity 编译和本地 .NET 8；不刷新、不切换、不暂停正在运行的 Unity。

独立 CLR 不能调用 Unity 原生 Profiler，因此只在 Temp 源副本里把四个 `ProfilerMarker` 初始化改成 `default`；河道、河漫滩、地形采样算法均来自生产源码。它不调用完整生成链，也不证明 PlayMode 卡顿或画面改善。

`expected.json` 来自优化前版本 `bf441b313811906fe09cc8dba15fbb6f20084c07` 的生产程序集，已用 `--revision` 再次独立重建并确认 13 个场景一致。按**原字典插入顺序**比较每个坐标、全部河道属性以及河漫滩强度的 double 原始位。覆盖高重叠、负坐标、环绕接缝、窄世界、大外圈、超大窗口稀疏回退、最大半径、海洋拒绝、禁用、空结果、调用前取消、填充后取消及取消后重跑。取消请求没有可提交结果，只验证抛出取消和后续重用不污染数据。

算法只缓存半径 0～96 的精确圆形偏移与衰减，线程安全共享；请求内用池化窗口数组记录首次高度资格和最大强度，仍按首次出现顺序写回字典。窗口超过 1,048,576 格时使用原稀疏字典，限制单次池化 double 缓冲最多 8 MiB；所有退出路径由 `using` 归还缓冲。新诊断 `river.local_refine.floodplain_height_queries` 记录冲积格真实高度查询次数，原候选/合并次数语义保持不变。

结果和本机 CLR 的中位耗时、线程分配量、高度访问次数保存在 `results.json`。时间受同时运行的 Unity 等进程影响，应优先看同输入的精确结果、访问次数与分配量；真实游戏耗时需要同场景重新抓帧。

如果保存了另一份完整 `results.json`，可做同时包含计时的对照：

```powershell
python Tools/Tests/HydrologyRaster/verify.py --label compared --baseline Temp/HydrologyRasterValidation/baseline/results.json
```

也可以指定优化前的 Git SHA 重建基线，脚本通过 `git cat-file --batch` 把该版本的编译源复制到 Temp 并重写编译参数，不切换工作区文件。程序集依赖仍使用本机现有 Unity 版本；该版本需要已有同名 `RenderMacroCenters` 入口。

```powershell
python Tools/Tests/HydrologyRaster/verify.py --revision <优化前SHA> --label baseline
python Tools/Tests/HydrologyRaster/verify.py --label current --baseline Temp/HydrologyRasterValidation/baseline/results.json
```

游戏内快速规则在 `Systems/Terrain/rules.lua` 的 `river_diagnostic_aggregation`：纯陆地河漫滩应每个唯一候选格只查询一次高度，并且重叠合并次数大于高度查询次数。用户正在 PlayMode 时不启动此规则。

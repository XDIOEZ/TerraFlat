# 天气、湿度与地表积分

## 冻结档案与区域引擎

- 生成时汇总固定 airHumidity、最终 precipitation、windX/Y、每格风模长和地理温度；ChunkTerrainData.WeatherProfile 冻结要素 Probability/Intensity/DurationSeconds。生成/调度共用 ChunkWeatherConfiguration；Elements 按稳定字符串 ID 扩展 rain/wind/thunder/fog。
- RegionSizeInChunks 默认 8（128×128格），首次登记冻结代表块档案，各成员共用概率/强度/时限，每区每步推进/复制一次，积分发给全部加载成员。环绕以 Min 分区，窄余边并末区；雷击在实际边界内选点，普通 Client 只应用权威。
- regional-weather.json：各空闲要素每 CheckIntervalSeconds=300 游戏秒独立抽签，ActivationProbability=0.1，可组合；IntensityDistribution/DurationDistribution 的 Values/Probabilities 各十档等概率，强度0.1～1、时长300～1440游戏秒，两表独立抽且事件中不重抽。直接改 JSON，不运行时生成曲线；Associations 仅显式启用，默认 RecoveryProbabilityMultiplier=1/RecoverySeconds=0，不抑制下一轮。
- 固定游戏秒步进，跳时有界补已有事件；新区块不补未加载浇水/雪，首次/读档不播旧雷。回拨平移独立时限，复制以 Revision 为主；PlanetData.RegionalWeatherStates 保存档案/事件。云量由雨/雷派生，雾用地理温度/固定湿度，不做耗湿或雨冷反馈；限时手动雾优先，到期回持续自然雾。
- 静态 precipitation 按水源距离/地理温度：海洋层供河湖生成，河湖层供最终生态/天气。PrecipitationWeight 权重 rain=1、thunder=0.7、wind=0.3，不输送临时水汽。
- TryGetGeographicAirHumidity 读固定层；TryGetAirHumiditySupply 从恢复后的真 Liquid/深度/距离得 B，背景仅 airHumidity.background；TryGetAirHumidity=clamp(B-D)。缺邻区不可用，不生成地形，缓存依 Terrain/LiquidRevision/Epoch。

## 地表账目与批量路径

- 每块仅一份降水积分，筛实际地面/待融雪格后 TrySampleAmbientChunk 同批冻结温度并分雨雪；无降水/天气雪/玩家雪跳过，不逐格查邻区天气、不跨帧借数组。
- 原步长积分逐条排队，MaintainSurfaceWork 每渲染帧 2ms 软预算消费完整块；入队冻结温度标量/源数值、复用不可变权重，出队才逐格合成，等待不改相态。短导航租约护数据，解绑只冲该块。
- 同步保存/退出/回拨结旧账；自动保存最终载荷阶段用短天气推进屏障预算排空，再冻核心/区块字节，取消/异常释放。屏障不冻结游戏秒/输入/表现，释放后有界追赶；旧世界/epoch/星球队列仅释放。到 MaximumCatchUpSteps 暂缓新步，不能集中冲刷或少结雨量。
- SupplySurfaceWaterBatch 每块共用目录/水肥只读视图，满水跳过，规范格直接查占地；先批写水/湿度层及存档/权威差量再通知，湿地 BRG 合并上传。ChunkEnvironmentRenderer 只在最终显隐改变且提交成功后记缓存，重绑/Owner重建/配置变重采，液体覆盖/抽干仍更新。

## 本机表现与事件

- 雨雪用完整 _precipitationTransitionSeconds（默认12真实秒）SmoothStep，风/云光 _weatherTransitionSeconds 默认3秒；跨块从当前表现续接，不按低强度缩短。雨滴/水花随强度，雨声同值，停雨让旧粒子落完。
- SetPrecipitationIntensity(intensity,durationSeconds=60,transitionSeconds=-1) 只改本块：0强度停、0过渡立即、-1默认；普通客户端 false。PresentedRainIntensity/PresentedSnowIntensity 供表现，浇水/雪仍读真实天气。
- 雨粒发射率/Transform 只在值变时提交，存活粒子容量仍收缩，重启/配置变重提；同帧水花共用视口框/WeatherMgr，逐点天气/地形和随机序列保留。
- thunder 期间按强度产新序号，默认最短20游戏秒，384格内共享间隔，事件开始先等；近期记录从存档恢复，距离用环绕最短值。权威保存/复制落点/强度，LightningStrikeSystem 同事件伤害一次；F12/StrikeLightningAt 可立即走真伤害链。
- 雷束/电弧/闪屏共用近远雷 Cue MinDistance/MaxDistance 线性衰减（8格全、192格零），主束向上24格、Shader程序折线/分叉/光晕；雷声延迟照旧。
- DenseFogRendererFeature 按玩家逻辑位置合成本地主相机，清晰半径 clearRadius/强度、柔边默认20格、外全遮。DenseFogPresentationTransition 从屏幕最远角外以完整30真实秒收拢/散开，浓度改变续当前边界；零强度散尽关，玩家/世界/Scene变重建，退出/抑制维度清理。
- WeatherMgr.Audio 通过 AudioService 平滑当地雨/风循环，雨声读实时雨雪分流；雷声仅新 LightningOccurred，共落点、环绕最近副本、近远音色/传播延迟，LateUpdate SetWorldPosition，首次不补播，退出/抑制清待播/句柄。

回归：`environment/rain_visual_batch`；地表队列/天气行为按 catalog 选对应断言。

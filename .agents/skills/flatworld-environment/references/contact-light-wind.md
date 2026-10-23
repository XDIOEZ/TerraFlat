# 接触、积雪、污染、光与风

## 接触和地表状态

- Mod_TileEffectReceiver 的 Ground/Liquid 各有运行器；Liquid 读真实深度，深水有体力时 LiquidFloating 仅暂停 Ground，浮沉边界一次 Exit/Enter。Ground 比 Terrain引用/TopTileId，仅重进才建实例 TileData。
- 临时停用先退冰/水/雪，已加载模块重启按当前位置恢复；PrepareForWorldTransition 撤恢复资格，等就绪显式 RefreshCurrentTileEffects。新代际只重门禁，正常 Tick 再读，不在模块装配中施效果。
- snow.depth（天然）、snow.weather.depth、snow.player.edited/depth/season 分离；天气雪随 SnowCells 保存、暖融补土壤水。WorldSnowInteraction 铲整格每层一 Snowball，铺回耗真物加一层；玩家编辑可超十层，空格留编辑标记，显示钳制不改产物。铲/铺转玩家厚度并清天气基线，取走的雪不融水。
- ContaminationSystem 唯一权威写，指标 flatworld.contamination.<definitionId>，保留服务端/差量/通知；脏污与具体病原负荷分指标。
- 单机散物水态 DroppedItemRuntime.Water：陆地静物订本格变化，未知格限量重试、水中低频查流；替换先备载荷/共享视觉再提交整组数量，水花属实际世界Scene，浮力阈值与视觉浸没分离。

## 光照

- TimeSystem.prefab 的 DayTimeSystem+Light2D 拥全局光，FixedLighting 为上限、SuppressWeather 关天气；GetLighting/GetSunLighting 外层云量只衰减一次，不改局部灯。
- 太阳/接触阴影透明度读纯 GetSunLighting，时间仅轨迹。月相 TotalDays+CurrentTime/DayLength，月光为夜间下限后叠采光率/维度上限；MoonlightIntensity 管亮度、MoonAppearance 管出现，退出清 Shader。
- LightLayerMgr 按需读 URP 活跃登记，代理分类/Transform 用弱引用，动态光参数实时、单次查询共拓扑；无玩法查询不定时扫 Scene，URP 适配失败才按需低频回退。

## 风推动

- 玩法按物体所在地，PlanetData.WindStrength/GPU 风仅镜头投影。WindTransitionWidth 默认32格（边界内外各16），TryGetWindBlend 用连续坐标 SmoothStep 混最多四区，支持拐角/环绕/客户端；缺区无风且不创建状态。风声/设施读混合风，移动先各区强度转推速再混合。
- WindEnvironmentRules/SampleWindVelocity：等效质量=总质量×天气星球重力/9.81；完整 ItemDefinitions 单件最轻 B、最重 A，C=(A+B)/2，B～C 三段起吹级8/9/10，≥C不推。风强向下取整，未达级速度0，达到/高一级/高两级为0.06/0.2/0.5单位/秒，同档不插值；抛掷未完/完全沉没不推。
- RegionalWindPushField 每规范区分别平滑三起吹档向量：起停/改档/反向从当前值用完整 _weatherTransitionSeconds（默认3真实秒）SmoothStep，再混四区。Time.timeAsDouble 不随日历跳，暂停冻结；客户端同场，每帧复用，同帧 WindQueryRevision 变补同步。世界/代际/拓扑/禁天气清，首次从零起、停风尾段仍 HasWindPush。
- WorldMotionSystem.TryCreateWindSamplingContext 每批冻目录质量范围、星球重力、活动拓扑/ChunkMgr；每件真质量/当地四区仍采，小步阻挡/移动后检测仍实时。质量范围依 ItemDefinitionRevision/引用缓存；单件/批量共场，直接读区域风字段/BuildWind，不为风组整份雨雷快照。
- 全域无有效推动时单件直接零、全零区不采；HasWindPush 包含停风尾段，不能按原始瞬时风强跳过。

回归：`environment/tile_effect_reenable`、`environment/light_registry`、`environment/light_sampling`、`environment/wind_push_transition`。

# 宇宙与太空玩法 Lua 测试模块

入口清单：`GameplayTests/universe-suite.json`。测试中心按四个主题组显示，可以全选或只勾选一个组；各项真实玩法检查仍自行准备前置条件。

Lua 编写、截图上限和通用执行/报告契约统一见 [flatworld-lua-testing 导航 Skill](../../../../.agents/skills/flatworld-lua-testing/SKILL.md)。

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json --cases universe_content sector_rules
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json --cases space_gameplay
```

运行前退出自己的 PlayMode，在测试期间保持 Game 视图可渲染。工具复用已经打开的 Unity，不停止用户正在玩的世界。整轮自动创建隔离存档；编译、EditMode 校验和真实 PlayMode 结果分别以实际运行报告为准。

## 覆盖清单

原来的 27 项逻辑检查整合为四个主题用例、四份 Lua，原断言与采样内容保留。独立入场 Lua 已删除，这份清单的入场检查直接放在宇宙内容组中。

| 主题用例 / Lua 文件 | 包含的子检查 |
| --- | --- |
| `universe_content` / `universe_content.lua` | 入场、全部星体目录、公转自转、宇宙快照、全部冷地表 |
| `sector_rules` / `sector_rules.lua` | 双精度坐标、跨界接触、四向/角落租约、折返、多观察者 |
| `ship_rules` / `ship_rules.lua` | 结构、对接、气密、质量、推进、接触、快照、引擎安装、燃料、光纤 |
| `space_gameplay` / `space_gameplay.lua` | 真实会话、模板、星体表现、起飞导航降落、五向越界折返、船体接触 |

`--cases` 现在选择上表的主题 ID。下面保留原子检查标识作为覆盖对照，不再把它们当成独立文件或可勾选的用例。五个跨区方向使用同一段循环，报告步骤标签仍区分方向。

每组内的独立代码块隔离局部辅助函数；每个 `test.space` 子检查仍有自己的中文标签、原始采样和 Lua 错误位置。组内首个失败会停止本组后续步骤，清单的 `continueAfterFailure: true` 继续下一个主题组。前三组以数据断言为主，可为 0 张截图；真实玩法由 AI 在关键命令后选择插入最多 3 次 `test.screenshot`，不再强制结束截图，也不为每个旧子检查创建用例文件夹。

| 子检查标识 | 自动检查的范围 | 使用的数据与入口 |
| --- | --- | --- |
| `enter_world` | 世界、玩家、真实地表区块就绪 | 正式世界入口 |
| `universe_catalog` | 全部配置星区和星体、身份、父子关系、Prefab/Profile/Planet 映射、环境、生成稳定性、空域 | 动态遍历 SpaceCatalog，不写死星体数量 |
| `universe_orbits` | 椭圆近日/远日点、周期、父子组合、速度导数、正逆自转、零自转、高偏心率和无效输入 | 独立 UniverseSimulation |
| `universe_coordinates` | 正负边界、角落、多区穿越、极大编号亚米精度、身份与场景键、溢出、相邻星区接触、空域惯性 | 双精度坐标与正式扫掠 |
| `universe_streaming` | 四向预加载、角落、穿界保留、折返、清退、多观察者与整数极值 | 独立 SpaceSectorStreaming |
| `universe_snapshots` | 星区、身份、时钟、种子和相位保存恢复、当前目录重新绑定、非法数据拒绝 | 正式快照解析器 |
| `universe_surface` | 全部星体冷生成地形、温压、液体/支撑、负坐标、环绕、重复生成确定性 | 独立 WorldRuntime + 正式生成器；不修改活跃星球 |
| `ship_structure` | 四邻接、地板支撑、拆桥分裂、原身份保留、无地板残骸、旋转占地 | 正式船体领域规则 |
| `ship_docking` | 双方确认、对接组、断开、动量及远距离拒绝 | 正式对接领域事务 |
| `ship_atmosphere` | 密闭舱、真实气体填充/抽取/供给、门通行与气密分离、破口泄漏、修复不返旧气 | 正式气密和流体库存 |
| `ship_mass` | 真实载荷、来源去重、重心、惯量、世界点与点速度守恒、非法质量 | 正式质量汇总 |
| `ship_propulsion` | 四向喷嘴、供油比例、转向、松键惯性、侧漂、反向制动、无动力超速、不同步长导航 | 正式推进与导航领域规则 |
| `ship_contacts` | 乘员相撞、质量动量、分离、远区排除、星体扫掠、船体自由经过星体、正负跨区 | 正式乘员/星体领域规则 |
| `ship_snapshots` | 船、飞行、导航、乘员、散落物、速度、缩放及稳定快照 | 正式保存模型 JSON 往返 |
| `ship_engine_placement` | 四向外缘、喷口净空、地板阻挡、管道与缺支撑 | 正式引擎安装规则 |
| `ship_fuel` | 氢氧比例、真实扣量、能量、超额请求原子失败、杂气/单气拒绝 | 正式燃料库存结算 |
| `ship_optical` | 控制台到导航器的光纤连接、断线与修复 | 正式光纤图 |
| `live_session` | 真实固定步时钟、会话身份、正式捕获快照 | 活跃隔离会话；不手动推进固定步 |
| `live_templates` | 全部正式测试船型、展开部件、资源注册、控制台/引擎/地板 | 正式固定结构目录 |
| `live_catalog` | 实际进入全部配置星区、全部 PlanetData 登记、真实星体对象及位置投影、UI 根持久归属和连续性、统一太空时间与采光 | 正式太空迁移、星区租约与表现 |
| `live_flight` | 每个正式船型真实生成/补给、进入控制台、起飞迁移、实际耗油和出力、惯性、旋转、到点制动、船体跨区的乘员支撑与相机连续性、降落 | 正式船型、机器、会话固定步与降落入口 |
| `live_transition_right/left/up/down/corner` | 玩家自然越界、预加载、双侧实际场景、过渡带清退、自然折返、玩家/相机/UI 根连续性、统一太空时间与采光、无维度加载 | 正式太空入场准备速度，后续由真实固定步推进 |
| `live_collision` | 相邻星区船体接触冲量、总动量、远区排除、足够的实际模拟推进时间 | 正式地板增添/提交事务构造自有夹具，真实会话固定步解算 |

## Lua 接口与职责

```lua
test.space("catalog", function(data)
    assert(#data.sectors > 0, "没有配置星区")
    -- 这里遍历实际数据并编写业务断言。
    return "已检查全部配置星区"
end, "宇宙目录")

test.space("sector_transition", function(data)
    assert(data.crossed.spaceView, "没有进入太空")
end, "向右跨区", "right")
test.screenshot("向右跨区并折返后的太空现场")
```

C# 仅提供白名单操作、准备独立夹具、采样正式状态并等待实际流程；业务断言保留在 Lua。数据转换使用 Lua 原生表，不开放 `CS`、`require` 或任意代码执行。每次 Lua 编排或断言回调有 100 万条指令预算。星区 `long` 编号返回字符串，位置/速度返回 double，不经过 float；Lua 不能把巨大星区编号转成普通浮点再比较。

`test.screenshot("中文阶段说明")` 与其他命令一起预先排队，每份 Lua **0～3 次**，循环展开后的次数合并计数，预检与运行都拒绝超限。截图应紧跟要取证的真实 `test.space` 命令，不要插入其验证回调，也不要先排其他命令再拍太空现场；纯采样数据无需附地表截图。

每个操作的原始实际采样保存在用例 JSON 的 `steps` 中，命令为 `space_sample`，即使后面的 Lua 断言失败也会保留，可据此查看输入、轨道位置、速度、燃料或星区租约。

通用等待/移动仍受原来的单步 30 秒、累计 180 秒限制。`test.space` 的真实迁移操作使用各自的有界等待；物理采样按正式会话的 `SimulationSeconds` 等待并保留真实时间超时，编辑器卡顿不能算作已推进物理。整轮仍受测试场景的总超时限制；未来大量增加船型或星区后，应同步调整场景总时间上限。

领域用例使用独立对象，不推进当前宇宙；真实会话用例会准备管理员无敌条件，并在结束或异常后恢复原无敌状态、释放租约和夹具。跨场景后的玩家从正式 ItemMgr 重新取得，不能继续使用迁移前的引用。

## 测试夹具与覆盖边界

`live_flight` 的松键惯性环节把本用例刚生成的船体预置到空星区，保留实际推进后的速度，再等待至少半秒正式模拟，检查速度、位移、引擎停推和燃料不再消耗；之后恢复起飞星区继续转向、导航和降落。星体引力范围内存在正式引力与大气阻力，不能直接用速度不变断言惯性。船体跨区环节只把自有船体预置到空星区边缘并设置起始速度，之后由正式固定步完成跨区和场景过渡。它检查跨区机制，不把夹具预置计作从星球一直导航至边界的完整航程。`live_collision` 使用正式地板物品生成自有船体，提高这些夹具的耐久以隔离冲量规则；不把这一环节当作正常材料耐久/战斗毁船验收。

每个真实迁移操作完成采样与 Lua 断言后先保留现场，允许紧随其后的截图命令取图；下一个非截图命令之前或脚本结束时才恢复起始测试世界并清理。清理复用中立乘员迁移事务，不把清理传送记作降落成功。`live_flight` 的降落断言来自真实 RequestLanding 流程，截图记录该命令全部执行后的实际现场，不能替代起飞/巡航的过程断言。

本模块覆盖上表已实现的单机内容与行为入口，不宣称代码行或所有组合路径达到 100%。以下仍需专项或人工验收：多人网络与两端画面、GPU 星体外观/星空/浮动原点视觉、完整 UI 点击与相机手感、性能与设备表现、磁盘退出重载、活跃冷地表差量、断电缺油救船/海上落船/重生/战斗毁船和散落物回收的完整场景链。领域断言或一张截图不能替代这些验收。

## 每轮报告、截图与测试包

```text
Library/FlatWorldGameplayTests/Runs/<运行ID>/
├─ report.md
├─ report.json
├─ pipeline.json
└─ cases/<用例ID>/
   ├─ report.json
   └─ screenshot-01.png … screenshot-03.png（按实际命令生成，可为 0 张）
```

截图只来自实际执行的 `test.screenshot` 命令，写盘完成后才继续下一步；每份 Lua 最多 3 张，不自动补结尾或失败现场。新报告 `schemaVersion: 5`，每用例的 `screenshots` 数组按命令顺序记录序号、阶段标签、路径、错误、场景、UTC 时间、耗时和玩家位置。阅读报告按 Lua 与截图序号展示，流水线/CLI 的 `screenshotPaths` 只列实际存在图片。

失败前已拍的图保留，失败后的排队命令不执行；禁用、跳过、未执行或不含截图命令的 Lua 没有图片。已尝试取图但无画面或 PlayMode 被中断时保留失败原因；帧末截图在切到 Scene 视图时可能不执行，取图有独立 8 秒超时。流水线以同一运行 ID 关联玩法报告，不附入上轮结果；历史报告与旧图片不修改、不补造现场。

结果文件夹直接作为测试包，不额外保存 ZIP；每轮收尾只保留最近 10 份文件夹包体（包含本轮），自动删除更旧的包并清理历史压缩副本。测试包是本轮结果证据，不是游戏发布包，也不复制正式玩家存档。独立 Player 的结果根目录为 `Application.persistentDataPath/FlatWorldGameplayTests`。

官方参考：[Unity Test Framework 的结果接口](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-test-runner-api.html)、[帧末截图限制](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/WaitForEndOfFrame.html)。

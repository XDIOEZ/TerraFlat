# 宇宙与太空玩法 Lua 测试模块

入口清单：`GameplayTests/universe-suite.json`。测试中心会自动列出这份清单，可以全选或只勾选一个用例；每个用例自行准备前置条件。

```powershell
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json --cases universe_catalog universe_orbits
python Tools/GameplayTesting/run_suite.py GameplayTests/universe-suite.json --cases live_flight live_transition_right
```

运行前退出自己的 PlayMode，在测试期间保持 Game 视图可渲染。工具复用已经打开的 Unity，不停止用户正在玩的世界。整轮自动创建隔离存档；本次实现没有主动编译或执行测试，下述用例是否通过由实际运行报告决定。

## 覆盖清单

清单共 27 个用例：一个入场前置用例、六个宇宙领域用例、十个飞船领域用例、十个真实会话用例。

| 用例 | 自动检查的范围 | 使用的数据与入口 |
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
| `live_catalog` | 实际进入全部配置星区、全部 PlanetData 登记、真实星体对象及位置投影 | 正式太空迁移、星区租约与表现 |
| `live_flight` | 每个正式船型真实生成/补给、进入控制台、起飞迁移、实际耗油和出力、惯性、旋转、到点制动、船体跨区、降落 | 正式船型、机器、会话固定步与降落入口 |
| `live_transition_right/left/up/down/corner` | 玩家自然越界、预加载、双侧实际场景、过渡带清退、自然折返、玩家连续性、无维度加载 | 正式太空入场准备速度，后续由真实固定步推进 |
| `live_collision` | 相邻星区船体接触冲量、总动量、远区排除 | 正式地板增添/提交事务构造自有夹具，真实会话固定步解算 |

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
```

C# 仅提供白名单操作、准备独立夹具、采样正式状态并等待实际流程；业务断言保留在 Lua。数据转换使用 Lua 原生表，不开放 `CS`、`require` 或任意代码执行。每次 Lua 编排或断言回调有 100 万条指令预算。星区 `long` 编号返回字符串，位置/速度返回 double，不经过 float；Lua 不能把巨大星区编号转成普通浮点再比较。

每个操作的原始实际采样保存在用例 JSON 的 `steps` 中，命令为 `space_sample`，即使后面的 Lua 断言失败也会保留，可据此查看输入、轨道位置、速度、燃料或星区租约。

通用等待/移动仍受原来的单步 30 秒、累计 180 秒限制。`test.space` 的真实迁移操作使用各自的有界等待，整轮仍受测试场景的总超时限制；未来大量增加船型或星区后，应同步调整场景总时间上限。

领域用例使用独立对象，不推进当前宇宙；真实会话用例会准备管理员无敌条件，并在结束或异常后恢复原无敌状态、释放租约和夹具。跨场景后的玩家从正式 ItemMgr 重新取得，不能继续使用迁移前的引用。

## 测试夹具与覆盖边界

`live_flight` 的船体跨区环节只把本用例刚生成的船体预置到空星区边缘并设置起始速度，之后由正式固定步完成跨区和场景过渡。它检查跨区机制，不把夹具预置计作从星球一直导航至边界的完整航程。`live_collision` 使用正式地板物品生成自有船体，提高这些夹具的耐久以隔离冲量规则；不把这一环节当作正常材料耐久/战斗毁船验收。

每个迁移用例正常结束后恢复起始测试世界。清理复用中立乘员迁移事务，不把清理传送记作降落成功。`live_flight` 的降落断言来自真实 RequestLanding 流程。

本模块覆盖上表已实现的单机内容与行为入口，不宣称代码行或所有组合路径达到 100%。以下仍需专项或人工验收：多人网络与两端画面、GPU 星体外观/星空/浮动原点视觉、完整 UI 点击与相机手感、性能与设备表现、磁盘退出重载、活跃冷地表差量、断电缺油救船/海上落船/重生/战斗毁船和散落物回收的完整场景链。领域断言或一张截图不能替代这些验收。

## 每轮报告、截图与测试包

```text
Library/FlatWorldGameplayTests/Runs/<运行ID>/
├─ report.md
├─ report.json
├─ pipeline.json
├─ screenshots/final.png
└─ cases/<用例ID>/
   ├─ report.json
   └─ end.png

Library/FlatWorldGameplayTests/Runs/<运行ID>.zip
```

每个执行过的用例在结束时截图，整轮结束后再截图，截图写盘后才发布完成状态。测试失败也保存结果包；禁用、跳过、无画面环境或 PlayMode 被中断时，报告明确记录缺图原因。普通帧末截图在切到 Scene 视图时可能不执行，因此设置独立 8 秒超时；截图缺失不会伪造图片。流水线以同一运行 ID 关联玩法报告，不能附入上轮结果。

测试包是本轮结果证据，不是游戏发布包，也不复制正式玩家存档。独立 Player 的结果根目录为 `Application.persistentDataPath/FlatWorldGameplayTests`。

官方参考：[Unity Test Framework 的结果接口](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-test-runner-api.html)、[帧末截图限制](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/WaitForEndOfFrame.html)。

# FlatWorld MCP 实机游玩问题记录

这份文档持续记录 **GamePlayMCP 操作真实 Unity Play Mode** 时遇到的问题。只写实际操作及可复现证据；将“已确认视觉异常”和“待排除 MCP 操作差异的疑似问题”分开，不把工具返回成功误当成游戏行为成功。

## 第 1 轮：2026-10-17

### 测试环境与现场

- 入口：`GameStartScene` → `gameplay_session(create_world)` → 隔离世界 `MCP_Playtest_20261017`。
- 存档、世界名与种子：`MCP_Playtest_20261017`；拓扑：`Wrapped`；隔离保存目录：`Library/FlatWorldGameplayMCP/Saves/`。**未操作正式存档**。
- 游戏：Unity Editor Play Mode，GameView 截图约 `2294×1088`（画面内部 UI 坐标空间 `2560×1213.5`）；GamePlayMCP 协议 `0.10.4`。
- 角色：出生点约 `(599.5, -222.5)`；测试区为沙滩、浅海；测试会话将生命设为 `20000/20000`，不能用于验证正常受伤、生存平衡。
- 方法：`gameplay_observe/query` 对照 `gameplay_act/ui` 结果，使用 `manage_scene(screenshot)` 保存实际 GameView；测试过移动、寻路、自动拾取、背包、菜单、攻击输入、浅海移动。
- 当前轮次未改游戏代码；问题状态均为**待处理**。截图文件保存在本文同级的 `截图/` 目录。

### 问题 01：黄色状态条遮挡快捷栏槽位

- **状态：已确认视觉问题｜优先级：P2｜领域：HUD 布局**。
- **现象**：屏幕底部中央一条不透明黄色长条横跨快捷栏中间多个槽位，盖住槽位内部内容；背包打开和关闭后仍存在。
- **复现**：进入测试世界，无须执行动作，查看屏幕底部快捷栏；按 `B` 打开背包再次观察。
- **结构化依据**：语义 UI 树中 `UI_Slider` 矩形为 `[1041.23, 103.93, 477.53, 73.03]`；`UI_HotBar` 矩形为 `[746.28, 61.8, 1067.43, 124.73]`。两者区域重叠，和截图一致。
- **期望**：状态条与快捷栏分层或错位摆放，不遮挡物品图标、数量或选择边框。
- **截图**：[首次入世 HUD](截图/MCP游玩_20261017_快捷栏遮挡.png)、[背包打开时的 HUD](截图/MCP游玩_20261017_背包.png)。
- **排查建议**：先定位实际 `UI_Slider` 绑定及其锚点，再检查 `UI_HotBar` 的安全区与 Canvas 排序；不要简单关闭状态条。

### 问题 02：部分常驻按钮被 MCP 点击后没有触发预期面板

- **状态：疑似问题，待人工点击交叉验证｜优先级：P2（暂定）｜领域：UI 输入/自动化协议**。
- **现象**：语义树把快捷栏 `背包` 和 `制作` 按钮标为 `clickable=true, interactable=true`；MCP `gameplay_ui(click)` 返回 `handled=true`，等待约 `0.35s` 后 UI 树仍保持基础 HUD（约 40 个节点），未出现目标面板。`设置` 按钮点击后也没有观察到对应面板。
- **对照**：`gameplay_act(press_key, inputAction="B")` 可正常打开 `UI_Bag`，语义树由约 40 个节点增至约 68 个节点；背包内 `关闭` 按钮通过**同一个** `gameplay_ui(click)` 可以关闭背包。`菜单` 按钮通过 MCP 点击也会改变 UI，随后按 `ESC` 可以关闭。这说明不能简单认定所有 MCP UI 点击都失效。
- **复现**：在世界 HUD 显示时，取当前 `gameplay_ui(tree)` 的按钮 ID（本次背包 `-196972`、制作 `-196944`、设置 `-196888`），分别使用 `gameplay_ui(click)`；等待一个以上界面更新并重新读取树；对照按 `B` 的结果。**运行时节点 ID 会变化，不能作为长期常量。**
- **期望**：点击可交互的常驻按钮应与等价键盘入口产生一致的游戏 UI 结果。
- **风险与下一步**：当前仅证明 **MCP 注入的点击与预期效果不一致**；仍需鼠标/触屏实际点击确认是否属于游戏 UI Bug，或是 `gameplay_ui` 的事件模拟、射线命中/遮挡问题。不可在未验证前认定玩家真实点击失败。
- **截图**：[通过键盘 B 成功打开背包](截图/MCP游玩_20261017_背包.png)。

### 观察 03：浅海松开移动后角色持续向岸边漂移

- **状态：待确认设计预期，暂不判为 Bug｜优先级：待定｜领域：水体/角色移动**。
- **现场**：角色从岸边向西进入 `core:sea_water` 浅海；`water.inWater=true`、`immersion≈0.1`、`floating=false`、氧气 `100/100`。执行 `gameplay_act(move, x=-1, y=0, seconds=8)` 后位置约 `(574.402,-222.737)`；未再发出移动命令而 `wait 6s` 后为 `(577.541,-222.948)`，速度约 `(0.539,-0.035)`。继续运行后回到岸边约 `(584.002,-223.348)`，此时速度归零。
- **复现**：在相同隔离世界沿沙滩向西进入浅海，保持向西移动后停止所有移动输入，连续观察世界坐标与速度；与陆地静止对照。
- **疑点**：松开输入仍有明显位移；但这**可能是有意设计的海水表层流动或水体推力**，仅凭位移不能定性为控制失效。`gameplay_hydrology_debug(status)` 显示 `riverCells=0/flowingCells=0`，这个河流指标不等于海流数据。
- **下一步**：核查海流设计、液体力和外部输入释放后的运动状态；必要时用相同世界位置重复测试。
- **截图**：[浅海位置及水体表现](截图/MCP游玩_20261017_浅海.png)。

### 本轮正常链路与未覆盖范围

- **通过**：隔离建世界并入世、导航至石头附近、接触自动拾取 2 个 `Ore_Stone`、物品出现在快捷栏、快捷栏选择、`B` 打开背包、背包关闭、菜单打开与 `ESC` 关闭、移动与基础瞄准/攻击输入。
- **符合规则而非 Bug**：地面石头是 `pickup=true/interactable=false`，因此对其调用 `gameplay_act(interact, targetGuid=...)` 返回 `interacted=false`；角色靠近后自动拾取正常。随后尝试 `inventory_to_hotbar(Ore_Stone)` 提示背包不存在该物品，因为石头已经自动放在快捷栏中。
- **日志**：本轮 `read_console` 抽查未得到 Error/Warning 记录；这不代表整个 Play Mode 或其他系统均无异常。
- **未覆盖**：制作产出、装备伤害、建造、存档恢复、联机、太空、深水游泳；不能据此宣称这些系统通过。

### 后续处理顺序

1. 修复 **问题 01** 的 HUD 遮挡，随后同 GameView 尺寸截图复验。
2. 用真实鼠标/触摸与 MCP 点击对照复验 **问题 02**，先判定责任属于游戏 UI 还是自动化工具，再修根因。
3. 对照海水表层流动设计决定是否将 **观察 03** 升级为正式 Bug。

> 下一轮继续在本文件按日期追加，保留已有复现证据，不将待确认项擅自写成已修复。

## 修复记录：2026-10-17（静态修改）

### 问题 01：体力条与快捷栏遮挡

- **定位**：黄色条为 `Mod_Stamina` 创建的 `UI_Slider`。原 Prefab 的底部锚点和固定 Y 偏移不感知手机快捷栏缩放、移动和安全区位置，所以两个独立 HUD 重叠。
- **改动**：只在 `Mod_Stamina` 内根据快捷栏实际四角计算上沿，将体力条抬到其上方并留出 12 参考单位间隔；在 `Canvas.willRenderCanvases` 检查真实位置变化，卸载时注销回调。保留原有颜色、进度、Prefabs 和非交互属性。
- **状态**：代码已调整，**待 Play Mode 重载后同尺寸截图复测**（同时测试手机快捷栏负底部间距和安全区变化）。

### 问题 02：MCP 点击手机按钮后面板未打开

- **根因**：MCP 获取玩法控制租约后，`Mod_GameController.IsGameplayInputAllowed` 有意拒绝没有 Agent 专用 Usage 的普通虚拟手机设备。背包按钮的 `MobileInputButton` 通过此设备发出输入，因此即使 `gameplay_ui(click)` 的 Pointer 事件被处理，玩法 Action 仍可能被过滤。此外原 MCP 点击在同一帧按下并松开，排队设备事件不保证跨过 Input System 更新。
- **改动**：GamePlayMCP 点击手机 HUD 时改为异步按下、跨一帧松开（普通面板保持即时点击）；点击正式 `UI_MobileControls` 子树时，且当前 MCP 已持有唯一租约，才对手机虚拟设备短暂添加现有外部控制 Usage，等待后续真实帧消费输入，最后 `finally` 撤销。没有放宽玩家控制器的外部租约规则，也没有直接调用面板业务回调；工具同时返回 `clicked` 事件结果。
- **状态**：代码已调整，**待重载后以 MCP 租约复测背包、制作、设置、交互与菜单**，并核对面板状态而不仅是 `handled`；再对照一次无租约的普通触屏/鼠标点击。

### 观察 03：浅海无输入时向岸漂移

- **结论**：**符合当前设计，暂不改动。** `ChunkMgr.TryGetRuntimeWaterCurrent` 对 Ocean 群系取世界风场的单位方向和流量系数 1；`WorldMotionSystem.SampleWaterVelocity` 将该表层海流转换为外部位移。`water-current.json` 的重量规则会决定玩家实际推动速度。河流诊断的 `flowingCells=0` 不涵盖海洋流动，不能用于否定洋流存在。
- **后续**：如果设计目标改成“浅水不推人”，应专门确认浸没深度阈值及乘船、掉落物的区别后再改，避免把正常海流误修为静水。

### 校验说明

- 本轮仅做静态修改和差异检查；检测到 Unity 仍处于 **Play Mode 主菜单**，按项目规则未停止、暂停或重进测试世界，因此不把编译、MCP 点击与截图复测写成已通过。
- 后续可回到隔离存档 `MCP_Playtest_20261017`，按上面的原步骤复测；失败时记录新的 Console、语义树和 GameView 画面。

## 第 2 轮：2026-10-17（MCP 实机游玩与续档验证）

### 测试现场

- **使用 GamePlayMCP 真实 Play Mode**：从 `GameStartScene` 续入上一轮隔离存档 `MCP_Playtest_20261017`，再以种子 `MCP_Playtest_20261017_R2` 创建新的 `Wrapped` 隔离世界；两份隔离存档都保存在 `Library/FlatWorldGameplayMCP/Saves/`，未写正式存档。
- 先使用 `gameplay_control(acquire)`，之后用 `gameplay_act / gameplay_ui / gameplay_observe / gameplay_query` 实际操作，`manage_scene(screenshot)` 保存 GameView。用于复核的原始 UI 语义树保留在同级 `截图/` 文件夹。
- 本轮 Editor GameView 实际约 `1286×262`，属于非常扁的编辑器测试视口；不把因此产生的整体 UI 缩小直接认定为游戏布局 Bug。
- **范围**：连续保存退出/续档、重新创建世界、手机 HUD 按钮、手工制作界面、移动、自动拾取、快捷栏切换和物品拖放尝试。本轮只记录，不修游戏代码。

### 上轮修复复核

- **问题 01：回归通过（当前视口）**。语义矩形：`UI_HotBar` 为 `[527.55, 13.37, 230.90, 26.98]`，`UI_Slider` 为 `[591.35, 44.00, 103.30, 15.80]`；两者垂直分离，GameView 图像中黄色体力条位于快捷栏上方。尚未验证所有分辨率与用户自定义间距。
- **问题 02：MCP 路径回归通过**。在有控制租约时，`gameplay_ui(click)` 点击 `背包` 得到 `clicked=true` 且实际出现 `UI_Bag`；点击 `制作` 后出现 `UI_HandCraftTable`；点击 `设置` 后出现 `UI_ActionList`。对应关闭按钮也能关闭面板，不能仅凭 MCP 的 `handled` 判定成功。
- **观察 03：沿用已确认的海流设计结论**，本轮没有修改水流规则。

### 问题 04：保存成功但续档时玩家坐标回到世界原点

- **状态：已稳定复现（两份不同的隔离世界）｜优先级：P1｜领域：玩家位置存档/世界恢复**。
- **现象**：`gameplay_session(save_exit)` 实际返回 `saved=true`，下一次 `continue_save(isolated=true)` 返回 `ready=true / resumedIsolated=true`，背包和快捷栏选择状态保留，但**玩家坐标并未恢复**，变为 `(0,0)` 附近。

| 测试存档 | 保存退出前位置（`gameplay_observe`） | 同档续档后位置 | 其他状态 |
| --- | --- | --- | --- |
| `MCP_Playtest_20261017` | `(47.008, -16.912)` | `(0.005, -0.002)` | `Ore_Stone ×2` 保留 |
| `MCP_Playtest_20261017_R2` | `(101.423, -460.583)` | `(0.000, 0.000)` | `Ore_Stone ×2`、`Stick_Wood ×1`、选中槽 1 均保留 |

- **复现步骤**：在隔离世界移动至远离原点的位置 → `observe` 记录坐标和库存 → `act(stop)` → `control(release)` → `session(save_exit)` → 在相同 Play Mode 主菜单执行 `session(continue_save, saveName=原存档, isolated=true)` → 再次 `observe`。两个不同的隔离世界均出现坐标归零。
- **排除延迟**：第一个世界的原点处为深海，读取后再 `wait 2s`，玩家移动到 `(10.872,-4.003)` 并持续受海流推动，**并非随后才回到 `(47.008,-16.912)`**。这里的海流位移和续档时坐标归零是两个不同现象。
- **期望**：续档后恢复保存时的权威世界坐标，不应在玩家背包成功恢复时将角色重新放到原点。
- **影响**：原点在海里时，玩家会落入陌生水域；位置归零还会导致采集、设施、地形探索进度难以接续。
- **可能涉及的环节（尚未定位根因）**：角色坐标快照的写入、读档重建时的数据应用、世界拓扑/表现坐标映射，或会话恢复后的初始摆放。**不得仅凭本轮证据断言具体是哪一个模块出错。**
- **证据**：[续档到深海的 GameView](截图/MCP_20261017_R2_reload-origin.png)、[新世界原本的岸边地形](截图/MCP_20261017_R2_new-world.png)。

### 问题 05：同一 Play Mode 多次切换世界会累积旧快捷栏，甚至阻断物品拖拽

- **状态：已稳定复现｜优先级：P1｜领域：玩家 HUD 生命周期 / UI 射线层级**。
- **现象**：从一个隔离世界 `save_exit` 返回菜单后，再续档或创建新隔离世界，前一个世界的常驻 HUD 没有全部释放。新世界出现多个**同时处于屏幕内**的快捷栏和多个旧手持物 UI。
- **具体现场**：复用同一个 Play Mode，先续入旧世界 → 保存退出 → 再续入旧世界 → 保存退出 → 新建 `MCP_Playtest_20261017_R2`。`gameplay_ui(tree)` 的 `PanelRoot/SafeAreaRoot` 下同时有 `UI_HotBar`、`UI_HotBar_1`、`UI_HotBar_2`（三个都 `onScreen=true`），以及 `UI_Hand`、`UI_Hand_1`、`UI_Hand_2`。再保存并续入 R2 世界后变成 `UI_HotBar` 到 `UI_HotBar_3` **四份快捷栏**，且全部在屏幕内。
- **选中状态不同步**：新世界 `gameplay_act(select_hotbar,index=1)` 返回 `selected=true/heldItem=Stick_Wood`，新的 `UI_HotBar_2` 选中框 X 约 `559.72`；两个旧快捷栏的选中框仍在 X 约 `542.62`，并非仅有无害的同一外观重绘。
- **实际交互阻断**：尝试将新世界 `UI_HotBar_2` 第 0 格的石头拖到手工制作 `输入_1`（`gameplay_ui(drag, deltaX=-0.77, dragDeltaY=132.73)`），工具直接拒绝 `ui_target_not_draggable`，报告**顶层命中对象为旧 `UI_HotBar_1/Slot_UI(Clone)`**，而不是本次玩家新快捷栏。可复现旧 HUD 夺走新世界的射线命中；手工制作材料转移因此未能继续验证。
- **期望**：同一时间每个本地玩家只存在一套有效快捷栏及手持 UI；切换存档或世界时清理/解绑旧面板，不得残留可命中旧玩家数据的 UI 节点。
- **调查方向（非已确认根因）**：检查 `save_exit` 和 `Player` 卸载链、`Mod_HotBar/Inventory` UI 释放、`UIManager` 常驻 `PanelRoot` 注册与重用；修复应从生命周期所有权入手，不能只隐藏重复窗口或降低旧窗口射线优先级。
- **证据**：[四份快捷栏时的画面](截图/MCP_20261017_R2_four-hotbars.png)、[快捷栏选中时的画面](截图/MCP_20261017_R2_stale-hotbar-selection.png)、[三份 HUD 的完整 UI 语义树](截图/MCP_20261017_R2_UI-tree-3-hotbars.json)、[四份 HUD 的完整 UI 语义树](截图/MCP_20261017_R2_UI-tree-4-hotbars.json)。

### 已完成的正常玩法与覆盖限制

- **正常**：新隔离世界进入地表沙滩 `(135.5,-493.5)`；`move_to` 路径导航四段均 `reached=true`，经过沙滩、草地及森林边缘，接触自动拾取石头和木棍；最终有 `Ore_Stone ×2 / Stick_Wood ×1`。快捷栏选择 `Stick_Wood` 与结构化库存一致。
- **正常**：手工制作面板能打开，存在四个材料输入格、两个输出格和配方滚动区；本轮**没有实际完成配方或产出验证**，因为问题 05 的旧快捷栏占用物品拖放的顶层射线位置。不能把未完成制作流程另行认定为制作系统 Bug。
- **诊断**：本轮 `read_console` 对 Error/Warning 的抽查返回空数组，但未执行长期整轮日志审计；不代表游戏其他系统无异常。
- **暂未覆盖**：正常武器伤害、建筑放置、复杂配方、联机、太空、深水游泳及跨程序冷启动后的存档恢复。问题 04 目前证明了同一 Play Mode 的保存/续档异常，尚未做 Editor 重启后的同一坐标对照。
- **下一轮优先级**：先处理问题 04 的位置恢复与问题 05 的旧 HUD 清理；解决后重新验证旧场景存档、物品拖入制作面板及各类 HUD 输入。
- **收尾状态**：`control(status)` 返回 `owned=false`，`gameplay_session(save_exit)` 返回 `saved=true / code=saved_and_exited`。Unity 保留在原有 Play Mode 的 `GameStartScene` 菜单，未停止或暂停 Editor；隔离存档和全部问题截图已保留。

## 修复记录：2026-10-17（问题 04 / 05）

### 问题 04：续档坐标回到原点

- **处理状态：已修改代码，待真实续档复测。** `ItemMgr.CreatePlayer` 原先把已保存的玩家数据也在 `(0,0)` 实例化，后续 `Player.Load` 才修改 Transform；这使刚体或运行时注册阶段存在以原点位置覆盖存档的风险。
- **修复**：实例化前读取 `Data_Player.transform`，按 `WorldLocalPresentation.ProjectPosition` 投影至当前世界，再作为正式 `ItemMgr.InstantiateItem` 初始位置；`Player.Load` 同时同步 Rigidbody2D 的位置和零初速度，保证物理投影不以原点写回。玩家创建时留下的临时 `Players` 空对象也对称销毁。
- **回归断言**：`save/isolated_roundtrip` 新增非原点玩家坐标的正式磁盘备份和当前版本一致性校验。该断言只能证明写盘/读取的坐标保持；**实际 `save_exit → continue_save` 的场景重建仍须单独复测**。

### 问题 05：旧快捷栏和手部库存 UI 跨世界堆积

- **处理状态：已修改代码，待真实跨世界复测。** `Mod_HotBar`、`Mod_Hand`、`Mod_Inventory` 原本仅解绑输入/数据，未释放 `Inventory.basePanel` 创建的跨场景 UI。
- **修复**：在 `Inventory` 建立统一、可重复调用的 `ReleaseRuntimePanel`，由三个所属模块的 `OnUnload` 调用，清理面板与运行时 UI 引用；`UIManager.DestroyPanel(BasePanel)` 统一按实际实例注销并销毁，不依赖命名后缀能否正确反查注册项。不处理其他玩家面板，不用隐藏或禁止射线掩盖旧 UI。
- **回归断言**：`core_lifecycle/live_entry` 现在检查当前 UI 根仅有一个 `UI_HotBar` 和一个 `UI_Hand`，且它们确实属于当前本地玩家；仍需通过连续两次存档切换复验，不将单次进世界断言当成全部通过。

### 本次验证边界

- 仅完成代码与 Lua 断言的静态修改；Unity 当前正处于 **Play Mode 的 `GameStartScene` 菜单**，按项目规则未退出、暂停或再次进入隔离世界，因此尚无新的运行时测试结论。
- 待允许重建/重新游玩时：保存于非原点 → 退出 → 续档确认位置；重复 2 次跨世界切换确认 UI 数量始终为 1；最后将材料从当前快捷栏拖入手工制作格并检查库存实际变化。

## 第 3 轮：2026-10-17（续档复查、蜂巢战斗、战利品拾取）

### 现场与验证边界

- **入口**：通过 HomePC2 的 GamePlayMCP，在已有的 Unity Editor **同一个 Play Mode** 中，从 `GameStartScene` 续入 `MCP_Playtest_20261017_R2`；`continue_save(isolated=true)` 返回 `resumedIsolated=true / ready=true`。没有碰正式存档。
- **重要环境限制**：本次 Play Mode 从上轮一直保持运行，进入前主菜单已经有 **4 个 `UI_HotBar`、4 个 `UI_Hand`** 的历史残留。上一轮针对问题 04/05 的源代码修改可能仍未经过 Editor 退出 Play Mode 后重新编译与生命周期重建；**本轮复现不等于证明最新提交 `6c6ba954a` 已生效后仍有 Bug**。本轮只进行游玩和记录，不进行代码修改、暂停或重启 Unity。
- **记录方法**：正式 `gameplay_control(acquire)` 租约、`gameplay_act` 移动/攻击/选择/按键、`gameplay_query` 读取实体与 ECS 掉落物、`gameplay_ui(tree/click/drag)` 对照实际操作效果，配合 GameView 截图与 Console 抽查。当前 GameView 仍约 `1286×262`，仅作本视口视觉证据。

### 问题 04 复查：坐标归零再次出现（旧运行会话）

- **结论：在未重载的 Play Mode 中仍能复现｜最新修复状态：待新 Play Mode 验证。**
- `gameplay_act(move_to)` 从 `(0,0)` 导航到 `(2.5,22.5)`，再前往 `(22.2,26.2)`，均报告 `reached=true`。保存退出前 `gameplay_observe.player.position=(22.813,25.806)`、库存 `Ore_Stone×2 + Stick_Wood×1`、选中槽 `1`。
- 依次执行 `act(stop) → control(release) → session(save_exit)`，返回 `saved=true / saved_and_exited`；再 `session(continue_save, saveName="MCP_Playtest_20261017_R2", isolated=true)` 返回 `ready=true / resumedIsolated=true`，但 `player.position=(0,0)`。石头、木棍及选中槽均正确保留。
- 这再次证明**当前运行环境**的续档路径仍未恢复非原点坐标；不能区分未应用新程序集与新修复本身失败。下轮必须允许退出并重新进入 Play Mode 后复测，且保留一个保存前非原点、带库存的隔离档做严格对照。

### 问题 05 复查：旧 HUD 继续阻挡新玩家（旧运行会话）

- **结论：当前运行环境继续复现｜优先级 P1｜最新修复状态：待新 Play Mode 验证。**
- UI 数量实际为：本轮主菜单开始时 `HotBar=4/Hand=4`；首次续档后 `5/5`；本轮再次保存退出后菜单仍 `5/5`；再次续档后变成 `6/6`；最终安全保存退出返回菜单时仍为 `6/6`。当前玩家快捷栏归属 `.../UI_MobileControls/快捷栏锚点/UI_HotBar_5`，旧 `UI_HotBar_4` 仍接收上层射线。
- **拖放复现**：打开 `UI_HandCraftTable_2`，把当前快捷栏第 0 格石头拖至 `输入_1`，`gameplay_ui(drag, deltaX=-0.77, dragDeltaY=132.73)` 返回 `ui_target_not_draggable`，顶层命中 `UIManager/PanelRoot/SafeAreaRoot/UI_HotBar_4/Slot_UI(Clone)`；库存石头仍为 2 个，制作输入未被实际转移。
- **额外验证**：点击当前快捷栏第 2 格蜂蜡时 `gameplay_ui(click)` 返回 `ui_target_blocked`，顶层同样命中旧 `UI_HotBar_4/Slot_UI(Clone)`。而 `gameplay_act(select_hotbar,index=2)` 可让当前玩家正确装备 `Beeswax`，证明问题集中在旧 UI 的射线遮挡，而非权威库存选择。
- **证据**：[续档后六份 HUD](截图/MCP_20261017_R3_02_6hud.png)、[制作拖拽场景](截图/MCP_20261017_R3_03_craft-drag.png)、[完整 6 份 HUD 语义树](截图/MCP_20261017_R3_UI-tree-6hotbars.json)。

### 观察 06：近身木棍攻击蜜蜂，没有实际扣血

- **状态：疑似战斗命中问题，尚未确认是否属于飞行单位设计｜暂定 P2。**
- 在蜂巢 `(13.197,-14.521)` 附近、玩家约 `(13.806,-14.667)` 时，3 只 `Bee`（GUID `-1309954476`、`1880599708`、`862583764`）贴近玩家，距离约 `0.729～0.733`，初始 HP 全为 `6/6`。
- 持有正式动画近战武器 `Stick_Wood`，先对 `targetGuid=-1309954476` 执行一次 `gameplay_act(attack,seconds=0.5)`，再重复 4 次约 `0.53s` 的攻击；MCP 均返回动作 `ok=true`，玩家体力发生变化，但 3 只蜜蜂的 HP 始终为 `6/6`。
- **正向对照**：同一木棍对约 `2.232` 单位外的 `Beehive` 执行正式 `attack(targetGuid=1994862324)`，蜂巢 HP 逐次稳定减少 `2`，最终从 `40/40` 降至摧毁。因此武器/攻击链总体可工作，但不证明对所有飞行目标有效。
- **待查**：蜜蜂 Actor 配置为飞行高度 `0.8`、微型 Trigger Collider；需要用正常武器近战、投射物和空中目标碰撞/伤害层做对照。可能只是有意的飞行单位命中限制，不应在没有对照时宣称“蜜蜂无敌”。
- **证据**：[蜂群战斗画面](截图/MCP_20261017_R3_05_bee-combat.png)。

### 观察 07：世界内 AI 调试标签遮挡物品与背包 UI

- **状态：视觉观察，需确认是否启用了开发诊断开关｜暂定 P3。**
- 蜂群追击时，GameView 显示大块黑色诊断标签，例如“状态：巢毁复仇 / 饱食 / 愤怒 / 目标 Player / 蜂巢 GUID”；打开背包后标签覆盖在背包中段，明显影响阅读。
- **复现**：当前测试视口靠近蜂巢并攻击后打开背包，可观察多个 AI 诊断条遮挡游戏 UI；截图见 [蜂蜡入包与诊断标签](截图/MCP_20261017_R3_04_beeswax-inventory.png)、[蜂群追击 HUD](截图/MCP_20261017_R3_05_bee-combat.png)。
- **待查**：这些文字可能是开发阶段主动启用的 GM/AI 调试显示；必须先确认正式玩家模式默认是否启用，不能直接把有意调试表现归类为发布版本 Bug。窄 GameView 放大了遮挡。

### 观察 08：入世过程出现脚下区块显示等待超过 12 秒

- **状态：性能线索，尚未独立复测｜暂定 P2。**
- `read_console` 抽查得到两条 `[GameManager] 玩家脚下区块表现超过 12 秒，继续保持加载页` 警告，分别报告区块 `Generating/terrain=null` 和 `Ready/terrain=ready` 但 `binding.queued=true/pendingChunk=true/view=none`，地址均为 `surface:0,0`。同一隔离存档的 `continue_save` 最终仍 `ready=true`。
- **待查**：要在冷启动与热续档分别测资源准备、TerrainReady 到 ChunkView 实际创建的时间，不以完整 MCP 往返时间代替帧耗；当前 Editor/调试与极窄 GameView 不能代表打包版加载性能。仅此两条日志不能证明永久卡死。

### 本轮真实通过的玩法

- **导航**：在草地区移动，`move_to` 从 `(0,0)` 到 `(2.5,22.5)` 再到 `(22.2,26.2)` 均 `reached=true`；追逐野猪时实体会移动，不把“目标走开”误判为导航失败。
- **蜂巢战斗**：`Stick_Wood` 正式攻击每次造成 2 点蜂巢伤害；20 次左右连续命中后摧毁 `40 HP` 蜂巢。蜜蜂受到攻击后追击并蜇刺玩家，玩家生命实际下降，说明 AI 敌对/伤害至少部分正常。
- **战利品**：蜂巢死亡生成 3 份 ECS `Beeswax`。刚落地时 `pickable=false`，等待约 3 秒后均 `pickable=true`；靠近后先捡到 1 份，再捡到 2 份；库存最终 `Beeswax×3 / Ore_Stone×2 / Stick_Wood×1`，ECS 蜂蜡掉落数量变为零。**短暂不可拾取有正常时间窗，不判为 Bug。**
- **输入/库存**：背包按钮、制作按钮正常打开各自 UI；背包按 `B` 能正常关闭；在 UI 树总量 138 个节点时关闭按钮出现在 `offset=128` 的第二页，属于工具分页而非按钮丢失。当前玩家通过权威 `select_hotbar` 切换槽位可正常更新装备。
- **未完成/不可结论**：制作合成实际产出仍因问题 05 无法执行正常拖拽；蜜蜂能否由合法空中武器伤害、其他武器战斗、建造、联机、太空以及冷重启后的续档仍未验收。

### 收尾

- 本轮只修改了**本 Markdown 记录及证据文件**，未修改游戏生产代码或 Skill。保存退出前隔离玩家约 `(0.676,0.309)`、HP `18690/20000`、库存 `Beeswax×3 / Ore_Stone×2 / Stick_Wood×1`；执行 `act(stop) → control(release) → session(save_exit)` 后返回 `saved=true / code=saved_and_exited`，Unity 保留在 **Play Mode 主菜单**。
- 下轮若要判断问题 04/05 的最新修复是否真正生效，**必须在用户允许时重建 Play Mode** 再复测，旧运行状态下重复续档只能证明本会话继续异常，不是可靠的新提交回归结论。

## 修复记录：2026-10-17（第三轮问题处理，静态修改）

### 问题 04：续档坐标丢失

- **状态：前次代码修复保留，等待干净 Play Mode 实机复测。** 本次复查未发现足以推翻上次 `ItemMgr.CreatePlayer` 按存档位置实例化以及 `Player.Load` 同步刚体的源代码证据。第三轮的 `(22.813,25.806) → (0,0)` 发生在原有 Play Mode，不能据此认定新代码仍失败。为避免用猜测增加补丁，本轮不重复修改玩家位置存档路径。

### 问题 05：旧 HUD 叠加及制作拖拽被挡

- **状态：补齐其他面板所有权清理，待重建后实测。** 之前已修 `Mod_HotBar`、`Mod_Hand`、`Mod_Inventory` 的常驻面板释放；进一步发现 `Mod_HandCraftTable` 自身创建的常驻制作面板没有在 `OnUnload` 销毁，`Mod_SettingCanvas` 仅在 `OnDestroy` 解绑而未归还跨场景设置窗口。
- **本次修改**：在手工制作、玩家设置和手动模块入口的所属模块 `OnUnload` 归还窗口；营养 HUD 通过 `UIManager.DestroyPanel` 注销注册身份；Manager 销毁面板前立即停用，避免本帧仍接收射线。材料/制作进度仍走原模块的正式存档逻辑，不清除玩家库存。
- **回归要求**：干净 Play Mode 下，连续两个 `save_exit → continue_save` 后确保快捷栏/手持物始终各 1 份、制作/设置未打开时不留旧实例；切换快捷栏和拖材料均实际命中当前玩家 UI，并验证正确的材料数量变化。

### 观察 06：MCP 攻击蜜蜂未扣血

- **状态：发现并修复 MCP 目标瞄准坐标问题；实际伤害效果待测试。** `gameplay_act(attack,targetGuid)` 原先直接把飞行单位 `Item.transform.position`（地面根坐标）作为瞄准点，而鸟/蜜蜂的正式 `Mod_AI_Bird` 受击盒位于随 `BirdLift` 升起的独立运动学 Rigidbody2D 上。瞄地面可能导致木棍动画攻击盒与真正飞行受击盒错开。
- **本次修改**：GamePlayMCP 优先读取启用且有效的 `Mod_DamageReceiver` Collider2D 中心，找不到时再回退根坐标；攻击结果增加 `data.aimPoint`，以便检查真实瞄准点。**并未降低受击防御、扩大碰撞体或改变伤害概率。**
- **保留注意事项**：蜜蜂内置体型命中系数为 `0.10`，正常健康状态下即使几何触碰，逻辑命中率也可能明显低于大型目标；此前 5 次不掉血不足以证明永远不能命中。下次需对照飞行目标实际命中盒、攻击次数、逻辑落空事件和生命变化。

### 观察 07：AI 诊断标签覆盖制作和背包

- **状态：已按调试界面让位原则修改，待视觉复测。** 动物参数及蜂巢参数确实是 GM 主动开关的持久化调试显示，默认关闭，不能强行删除调试能力。
- **本次修改**：统一通过 `AI_DebugOverlay.CanDrawWorldLabels` 判断是否有已打开的导航面板、阻断面板或手机菜单抽屉；普通背包、手工制作及设置窗口打开时，动物/蜜蜂/蜂巢头顶 IMGUI 暂时不绘制，面板关闭后继续遵守原 GM 开关。蜜蜂绘制还限制为 IMGUI Repaint，避免布局事件重复构造文字。
- **断言**：`ui_settings/settings_locale_open` 增加正式设置窗口打开时 `debugWorldLabelsSuppressed` 检查，但尚未运行。

### 观察 08：入世脚下区块超过 12 秒

- **状态：证据不足，继续保留待性能采样项，不做无依据修改。** 现有警告带 `Generating/Ready`、队列与绑定等待上下文，世界最终 `ready=true`；它并不等于永久卡死。此前续档坐标错误使角色在 `(0,0)` 加载陌生区块，可能影响这条等待日志，仍不能断言是唯一原因。
- **验证条件**：代码实际生效后，在正确保存坐标和干净 Play Mode 下分别测冷进入、热续档、`ChunkMgr` 基础生成和 ChunkView 绑定耗时；确有异常再定向优化，不延长 12 秒阈值掩盖问题。

### 本次测试边界

- 本轮只有静态代码、测试断言、Skill 和问题记录变更；Unity 仍处于 **Play Mode 主菜单**，未停止、暂停、清空旧 HUD 或进入世界。因此**本次不宣称任何代码已通过编译或实机回归**。第三轮的历史截图与 JSON 继续保留。

## 第 4 轮补充现场（2026-10-17，游玩进行中被修复任务中断）

- 隔离存档 `MCP_Playtest_20261017_R2` 续档后本地玩家仍位于 `(0,0)`；`gameplay_ui(tree)` 记录到 **7 份 `UI_HotBar` 和 7 份 `UI_Hand`**。这是原 Play Mode 持续运行的旧会话，最新代码是否已生效未知，不能据此判定刚提交的修复失败。
- 经正式 `move_to` 自动拾取，材料从 `Stick_Wood×1 / Ore_Stone×2 / Beeswax×3` 增加到 `Stick_Wood×2 / Log×2 / Ore_Stone×2 / Beeswax×3`，足够满足 `Bonfire_Summoner` 的原木与木棍材料需求；尚未成功完成合成，不把材料凑齐视为制作通过。
- `gameplay_ui(click)` 能打开当前手工制作面板，但从 `UI_HotBar_6` 第 0 格向 `输入_1` 拖动 `(deltaX=-0.77, dragDeltaY=132.73)` 失败，报 `ui_target_not_draggable`，顶层命中仍是旧 `UI_HotBar_5/Slot_UI(Clone)`；这是问题 05 的又一次复现，不另立重复问题。
- 营养状态中碳水与饮水均降至零，因长时间测试与世界时间流逝可能属于正常生存消耗。还没有同难度、同时间尺度与食物来源覆盖的对照数据，暂不认定为数值 Bug。
- 用户随后改为本次修复任务；上述世界操作未在本补充中宣称已安全保存退出，不覆盖前轮的成功存档记录。

## 修复记录：2026-10-17（退出保存时序与成功语义）

### 问题 04：角色坐标及退出保存可信性

- **新的代码修复**：发现 `GameManager.BackToHelloScene_Coroutine` 原先先将 `IsInGameWorld=false` 并触发所有 `Event_GameWorldExit` 订阅者，然后才调用 `ItemMgr.SavePlayer`。这意味着退出事件可以先解绑载具/控制、释放任务运行态等，再让玩家模块保存。现已改为**退出前预检并采集玩家与模块状态 → 发布世界退出事件 → 完成世界/区块状态写盘 → 释放玩家**，降低退出清理抢先改变玩家位置的风险。由于存档数据仍为活跃引用，这不代表消除了所有可能的后续数据修改；先前的按存档坐标实例化与刚体同步逻辑保留。
- **成功状态纠正**：原 `gameplay_session(save_exit)` 只要协程回到 `GameStartScene` 就给 `saved=true`，即使 `SaveToDisk` 实际返回失败也会误报成功。现在 `SaveDataMgr` 返回写盘结果，`GameManager.LastExitSaveSucceeded` 保存本轮结果，MCP 仅在**退出完成且实际写盘成功**时返回 `saved=true`；否则区分 `save_write_failed` 与 `exit_timeout`。写盘失败仍需人工检查现场是否已经退出，不能假设自动回滚。
- **状态**：源代码已修改，仍需干净 Play Mode 中在非原点保存、检查 `saved` 与磁盘时间戳，随后 `continue_save` 实测位置是否恢复；绝不能把静态检查当成该问题已验收。

### 其他遗留问题状态

- **问题 05**：上一轮已补齐快捷栏、手持、制作、设置和营养 UI 的卸载所有权，仍需干净 Play Mode 连续两次存档切换与制作拖放验证；本次没有用自动隐藏旧 UI 来掩盖根因。
- **观察 06/07**：飞行受击盒瞄准与调试标签让位已有源代码修改，需在新程序集下对照 `aimPoint`、真实血量和实际截图。
- **观察 08**：脚下区块超过 12 秒的现有日志不足以决定优化点，保留性能调查项；不抬高警告阈值或减少必要的首屏就绪条件。
- **本次验证边界**：Unity 仍在 Play Mode，未主动停止、暂停或重建会话。本轮仅完成代码和静态检查，正式编译、完整读写盘及 UI 回归仍待执行。

## 第 4 轮：2026-10-17（推进实际生存：制工具、建篝火、进食）

### 运行现场及已达成进度

- **方式**：通过 HomePC2 + GamePlayMCP 在已有的 Unity **同一 Play Mode** 续入隔离存档 `MCP_Playtest_20261017_R2`；`continue_save(isolated=true)` 返回 `ready=true`。只操作隔离玩家，不进入正式存档，不修改生产代码或停止 Unity。
- **进入状态**：玩家坐标 `(0,0)`，库存 `Ore_Stone×2 / Stick_Wood×1 / Beeswax×3`，碳水 `0/100`、水分 `0/150`；常驻快捷栏及手持 UI 各 **7 份**。这仍是旧运行会话，不能证明提交 `6c6ba954a / b6e564eb8` 的改动已重新编译生效。
- **采集**：通过真实 `move_to` 接触地面 ECS 掉落，先在 `(-4.484,2.525)` 拾取一根木棍，再在 `(-18.537,-5.598)`、`(-21.424,-12.465)` 各拾取一根原木；库存达到 `Stick_Wood×2 / Log×2 / Ore_Stone×2`，导航结果均为 `reached=true`。
- **制作打制石器**：将 `Ore_Stone×2` 拖到 `UI_HandCraftTable` 的 `输入_1` → 配方列表显示「打制石器」→ 选择并点击 **6 次**「开始制作」→ `输出_1` 出现 1 件（图标 `icons_sheet_98`）→ 拖至当前快捷栏第 4 格；库存实际获得 `ChippedTool×1`，石头被扣除。证据：[投入材料](截图/MCP_20261017_R4_02_stones-in-crafting.png)、[打制石器产出](截图/MCP_20261017_R4_04_chipped-in-output.png)。
- **制作和建造篝火**：将 `Stick_Wood×2` 与 `Log×2` 分别拖入制作输入槽，选择「篝火」，点击制作 6 次，输出 `Bonfire_Summoner×1`；拖入快捷栏第 0 格，`look_at(-19.4,-12.4)` 后执行 `gameplay_act(use)`，召唤器被消耗。随后 `gameplay_act(interact)` 返回 `interacted=true`，实际出现正式 `UI_Bonfire`（包含燃料、输入、输出与处理按钮），证明放置后的建筑能够交互。证据：[篝火制作完成](截图/MCP_20261017_R4_05_bonfire-output.png)、[篝火放置现场](截图/MCP_20261017_R4_06_bonfire-placed.png)。**尚未通过重新载档验证建筑持久化**。
- **燃料**：在 `(-38.493,-19.575)` 新拾取一根原木，返回篝火，打开 `UI_Bonfire`，通过真实拖放将 `Log×1` 从快捷栏放入 `燃料_1`，槽位显示数量 1。没有烹饪食材，所以未声称实际炼制完成。
- **进食**：`Beeswax` 物品描述确认可直接食用。选中第 2 格并调用正式 `gameplay_act(use)`，等待 2 秒后库存 `3→2`；碳水约 `0→54.864/100`、维生素约 `47.507→72.504/100`，脂肪 `0→2/100`，水分仍为 `0/150`，这条进食与扣料链路正常。

### 问题 05 再观察：旧 HUD 阻断制作，被正常设置临时绕开

- **状态：旧 Play Mode 环境仍可复现｜P1｜最新源代码修复仍待干净重载验证**。
- 在快捷栏处于默认位置时，向手工制作槽拖放石头，`gameplay_ui(drag)` 返回 `ui_target_not_draggable`，**顶层命中旧 `UI_HotBar_5/Slot_UI(Clone)`**。本轮当前玩家的快捷栏实际上是 `UI_HotBar_6`，旧 UI 阻止真实射线到达当前库存；[七份 HUD 语义树](截图/MCP_20261017_R4_UI-7hotbars.json)。
- **不修改游戏代码的测试绕行**：通过正式游戏内 `设置 → UI设置 → 快捷栏底部间距`，记录原值 `28`，用 `gameplay_ui(drag,deltaX=500)` 临时改为 `124`，当前快捷栏矩形上移到约 `[527.55,42.53,230.90,26.98]`，避开旧槽位。之后石头、木棍、原木、两个制作产出、篝火燃料均能实际拖放；这**只证明绕行成功，不等于修复了 HUD 生命周期**。
- **清理确认**：保存退出前用正式 UI 将该设置从 `124` 还原为原始 **`28`**，语义树确认 `value=28.0`；未永久改变用户的快捷栏偏好。

### 问题 09：篝火 UI 缺少温度文本绑定

- **状态：已观察到 Console 警告｜疑似 P2｜未修复**。
- **复现**：放置 `Bonfire_Summoner` 后，指向建筑执行 `gameplay_act(interact)`；工具返回 `interacted=true`，场景 UI 树出现 `UI_Bonfire`，但 `read_console` 出现 `未找到名为 FWUI_FurnaceTemperatureValue 的文本组件`。UI 语义树存在 `熔炼进度条` 和 `燃料显示条`，未发现上述温度文本节点。
- **预期**：若火堆处理面板设计要求显示当前温度，应提供正式引用并按真实炉温更新；如果已取消温度文本，则应移除无效查找，不能持续发警告。需要先核对 Prefab 和控制器契约，不因一次缺文本日志便断言温控结算失效。
- **证据**：[篝火交互界面](截图/MCP_20261017_R4_07_bonfire-ui.png)。

### 观察 10：草地上停止输入后仍持续漂移

- **状态：待确认风力设计与正常速度边界，暂不判为 Bug**。
- **复现数据**：草地上 `moving=false`、`water.inWater=false`，玩家速度约 `(0.494,-0.078)`；执行 `gameplay_act(stop)` 后连续 `wait 2s`：位置从约 `(-5.746,-12.744)` → `(-4.750,-12.901)` → `(-3.762,-13.056)`，持续约 0.50 单位/秒；停止输入并未消除这笔外部位移。
- **判断边界**：`WorldMotionSystem/Mod_Mover` 已设计风力等外部速度，不能把 `moving=false` 等同于绝对速度 0。下次需同时采样风场、当前体重与 `DrivenVelocity/ExternalVelocity`，确认是否符合玩家抗风预期；目前不建议直接清零刚体速度。

### 观察 11：打制石器的近战能力需确认

- **状态：内容/设计待确认，暂不判为 Bug**。
- `ChippedTool` 的配置位于 `GameConfig/Items/shells/weapons.json`，带有伤害模块且 `DamageValues.Physical=6`；但真正选中该物品时，`gameplay_observe.player.equippedWeapon` 返回 `held=ChippedTool / runtimeWeapon=false / backend=none / canAttack=false`。它也可能只是制作工具，而非可以挥动的武器；需要核对物品设计和模块装配，尚未通过合法命中链验证伤害。

### 观察 12：直接采摘与取水路线未完成

- 靠近已加载自然橘子树 `OrangeTree` 于 `(-25.5,-9.5)`，玩家到树约 `1.432`，`look_at` 并尝试普通 `interact`，实际 `interacted=false`、库存未增加。自然果树存在成长/自然落果机制，不能仅凭这一结果断言“无法采摘”是 Bug。
- 当前周围已加载区域查询 `CoconutTree / Coconut_Green / Water` 均无匹配；这说明本轮就近没有通过这些路径找到可饮用资源，并不证明整个世界没有水。角色水分始终 `0/150`，是下一次生存探索优先目标。

### 本轮保存与覆盖边界

- **最终状态**：实际 `act(stop) → control(release) → gameplay_session(save_exit)` 返回 `saved=true / code=saved_and_exited`，隔离存档仍为 `MCP_Playtest_20261017_R2`。保存前玩家约 `(25.817,-17.717)`、HP `19168/20000`、碳水 `49.156/100`、水分 `0/150`；背包为 `Beeswax×2 / ChippedTool×1 / Stick_Wood×1`，篝火已放置、燃料槽已有一根原木。证据：[生存 HUD 与库存](截图/MCP_20261017_R4_08_eating-status.png)。
- Unity **维持 Play Mode 主菜单**、`timeScale=1`，未停止、暂停或重启；除隔离世界存档和暂时后已还原的 UI 设置外，没有修改正式游戏代码。本轮不声称已重新载档验证篝火/角色坐标，或已完成实际烹饪。
- **下轮建议**：首先找饮水来源，再猎取或采集可烹饪的食物，用已建立的篝火完成一轮正式加工；若要判断问题 04/05 最新代码是否生效，需在允许时开始干净 Play Mode，不应继续把旧会话结果误判为新提交回归失败。

## 第 5 轮：2026-10-17（河流饮水、生存补给、骨钩与火把制作）

### 测试环境与关键进度

- **真实游玩**：通过 HomePC2 + GamePlayMCP，在**上轮同一个 Play Mode** 的 `GameStartScene` 续入已有隔离存档 `MCP_Playtest_20261017_R2`；`continue_save(isolated=true)` 返回 `ready=true / resumedIsolated=true`。本轮没有修改游戏生产代码、重启或暂停 Unity，也未接触正式存档。
- **再访篝火：成功**。虽然续档后玩家又从保存位置恢复到 `(0,0)`，通过 `move_to(-19.4,-12.4)` 走回篝火附近，指向建筑执行正式 `interact` 返回 `interacted=true`，能重新打开 `UI_Bonfire`；`燃料_1` 仍显示原木数量 `1`，证明本轮**建筑及内部燃料槽均实际跨存档保留**。`gameplay_query(source=runtime, query=Bonfire)` 没查到结果，不等同建筑不存在，因为该查询只覆盖特定运行时 Item 而不代表全部建造系统。证据：[再次打开的篝火](截图/MCP_20261017_R5_01_bonfire_revisit.png)。
- **找到淡水河**：在 `(-43,-59)` 一带通过 `gameplay_query(source=terrain, layerId=riverFloodplain)` 定位到河道，前往约 `(-48,-72)`；实际液体层为 `core:dirty_water`、水深约 `0.6`，河流方向约 `(0.887,-0.462)`、流量 `0.42`。中途流动将停止主动输入的角色推入深水，出现 `oxygen=0/100, drowning=true`，但经 `move_to(-26.5,-82.5)` 回岸后氧气恢复 `100/100`。这说明水体深浅与流速值得谨慎探索，**暂不将河流推力和深水溺水认定为 Bug**。证据：[河流环境](截图/MCP_20261017_R5_02_river-floodplain.png)。
- **饮水：通过正式玩家输入链**。在浅水中将光标指向角色附近水格，执行 `gameplay_act(press_key, inputAction="E", seconds=3.2)`，实际按玩家当前改键 `<Keyboard>/f` 持续交互，水分 `0 → 约 75/150`；之后站在 **干燥沙地** `(-13.523,-65.320)`，指向邻接脏水格 `(-13.5,-66.5)` 再长按 `4.3s`，水分 `0 → 约 99.55/150`，角色 `inWater=false / oxygen=100/100`。正常玩法支持**站在岸上饮用临近的河水**，无需游入深水。`core:dirty_water` 配置每次补水 `25`、有 `20%` 感染概率；本轮未验证是否实际感染，不能把补水成功写作无副作用。证据：[岸边饮水位置](截图/MCP_20261017_R5_07_drink-from-shore.png)。
- **野外补给**：到 `(-15.033,-60.533)` 接触 ECS 掉落后，自动拾到 `Meat_Dehydrate×1 / Bone×1 / Fat×1`。选中肉干后第一次 `use` 只推进进食，第二次使用才消耗该件（`Max_EatingProgress=3`）；蛋白质从约 `20/100` 上升至接近 `100/100`，脂肪约 `0→30/100`，水分略有增加。进食进度与数量符合食物配置，不判为异常。证据：[进食后野外 HUD](截图/MCP_20261017_R5_04_survival-food.png)。
- **制作骨质鱼钩：通过**。将快捷栏的 `Bone×1` 拖入随身制作 `输入_1` → 候选显示「骨质鱼钩」→ 点击候选、点击正式合成按钮 **6 次** → `输出_1` 得到 `FishingHook_Bone×1` → 拖到当前快捷栏第 3 格；库存增加骨钩，骨头正确消耗。
- **制作火把：通过**。将 `Fat×1` 与 `Stick_Wood×1` 分别拖入 `输入_1/输入_2` → 候选「火把」→ 点击合成按钮 **6 次** → 输出格出现图标名 `Torch_Spent`，但**转入快捷栏后的权威物品 ID 为 `Torch_Summoner×1`**。物品定义原本就以 `Torch_Spent` 图片作为未点燃外观，不应将图标名误认为产出错误。证据：[完成火把制作的输出槽](截图/MCP_20261017_R5_05_torch-crafted.png)。
- **火把放置边界**：选择 `Torch_Summoner`，瞄准邻近草地执行一次 `gameplay_act(use)`，物品没消耗、也未观测到落地火把；随后检查正式 `Mod_Building.RequiresPlacementRequest=true`，表示需先**显式进入放置模式**，故目前不能判为建筑放置失败。下轮需要通过正常面板/正式输入找到开启放置模式的入口再验证。

### 问题 13：MCP 持续交互命令未覆盖环境喝水（协议能力缺口）

- **状态：可复现、已找到接口差异｜优先级 P2｜领域：GamePlayMCP 能力，非游戏喝水 Bug｜待处理**。
- **现象**：在 `core:dirty_water` 水体内、水深 `0.3`、氧气 `100/100` 的浅水位置，`look_at` 指向身边有效水面后调用 `gameplay_act(action=interact_hold, seconds=2.2)` 返回 `ok=true / interacted=false / seconds=0`，水分仍为 `0/150`；而同一环境的正式虚拟键盘 `press_key(inputAction="E", seconds=3.2)` 能正确结算三次补水。
- **根因范围**：`GameplayMcpRuntime.InteractHoldAsync` 当前只调用 `Mod_InteractSender.TryBeginHeldInteraction()`（实体 `IInteractable` 持续交互），若无实体则立即返回 false；正常 `Mod_InteractSender.OnInteractPressed` 还会在实体交互未命中时调用 `BeginEnvironmentActionHold()`，因此 MCP 的专用长按动作没有覆盖水体环境行为。**游戏玩家通过改键后 E/F 正常能喝水**。
- **期望**：MCP 通用持续交互可以覆盖正常交互键支持的环境动作，或在能力文档中明确要求使用 `press_key(inputAction="E")` 替代环境长按；不要以返回的 `ok=true` 代替业务成功。
- **当前可用绕行**：将光标瞄准附近真实水格、保持距离在交互上限内，使用 `press_key(inputAction="E",seconds=...)`；对照 `nutrition.water` 前后数值。**本轮仅记录，不修改 MCP 代码。**

### 问题 04 / 05 再观察：续档原点及旧 HUD

- **仍在旧 Play Mode 复现，不等于新提交失败**：玩家续档再次回到 `(0,0)`，本轮当前 HUD 叠加变为 `UI_HotBar×8 / UI_Hand×8`；完整语义树留存在 [8 份 HUD 完整节点数据](截图/MCP_20261017_R5_08hud_full_ui_tree.json)。上一轮修复的生产代码尚无干净 Play Mode 验收，必须区分旧程序集的历史现场与新版回归失败。
- **临时绕行（正常游戏设置操作）**：制作前打开 `设置 → UI设置 → 快捷栏底部间距`，确认原值 `28`，用游戏正式滑条暂改到 `124`，使当前 `UI_HotBar_7` 避开旧面板射线。输入材料、合成、拖回快捷栏均能通过正式 UI 完成。全部制作结束后又通过同一滑条**确认恢复原始值 `28.0`**，没有永久改变该设备级玩家设置；这不能当作 HUD 堆积修复。

### 本轮结果与后续目标

- **结论**：本轮实际完成了篝火及燃料跨档复验、河流饮水、野外补给、肉干进食、骨质鱼钩制作、火把制作；**尚未完成正式生肉烹饪、钓鱼竿制作、鱼类捕捞和火把地面安装**。这些属于下一轮玩法目标，未完成不等于已发现 Bug。
- **隔离存档收尾**：在干燥岸边 `( -13.523, -65.320 )` 记录最后状态：HP 约 `19708/20000`、碳水 `47.542/100`、蛋白质 `90.943/100`、水分 `91.959/150`。背包为 `Torch_Summoner×1 / FishingHook_Bone×1 / ChippedTool×1 / Beeswax×1`；篝火燃料原木已有一根。执行 `act(stop) → control(release) → session(save_exit)` 返回 `saved=true / code=saved_and_exited`，同一隔离 `.bytes` 文件的修改时间发生变化且大小由 `889270` 变为 `898123` 字节；这证实**本次写盘实际更新了文件**，但还没重进检查骨钩/火把最终恢复。
- Unity 仍处于 **Play Mode 的 `GameStartScene` 主菜单**，`timeScale=1`；未停止、暂停或重启，也没有修改任何游戏代码。

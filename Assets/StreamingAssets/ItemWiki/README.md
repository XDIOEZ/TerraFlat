# FlatWorld 物品字典、科技树、行为树与电路实验室

这是直接读取当前项目配置的 HTML 工具，无需安装前端依赖或导出一份物品副本。

## 内置数值调节

档案浏览页选择物品后，在基础数值下方直接使用「数值调节」面板。点字段旁边的「↕ 微调」可直接跳转定位，也可以在调参器内搜索/按武器、护甲、食物等分组筛选。

- 按「偏强 · 减小」「偏弱 · 增大」每次小幅调整；连续相同方向逐渐加速，反馈反转时减小步长，不会像二分查找一样最终锁死。
- 每项独立保存步长、方向、历史趋势，支持手动数值输入及撤销最近一次调整。原有字段双击输入数值仍然可用，并计入调参历史。
- 所有数值写入都复用原有 Item JSON 的**指纹冲突检测、配置校验、备份和原子保存**，只覆盖当前子物品对应字段，不修改父级模板。
- 历史记录保存于项目 Library/FlatWorldItemWiki/balance-history.json，第一次使用时自动尝试迁移旧 Unity 工作台 BalanceTuningDatabase.asset 中的 JSON 物品调参记录，**不会修改、删除旧文件**。历史不进入游戏资源与公开分享。
- 编辑器正在单机 Play Mode 时，ItemWikiBalanceLiveBridge 会接收本机成功保存事件，直接更新允许原位更新的已加载 Item，并合并触发已有的 GameRes.RequestResourceReload()；已冷缓存的内容可能需要资源重载或重新生成实例。网页显示 Unity 的实际回执，不把“已保存”当成“已实时生效”。联机期间拒绝单边运行态修改。
- 公开分享继续只读；**Prefab-only 条目没有 JSON 权威定义，不经 ItemWiki 写入**。

Unity 内亦可由菜单「FlatWorld → 物品百科 → 打开 ItemWiki」启动。本地网页是编辑器，游戏运行包不会包含 Python 服务和编辑器桥接脚本。

科技树画布上方的「铺满网页」会隐藏导航、筛选和详情，只保留可拖拽、缩放的画布。按 Esc 或点击右上角「退出铺满」返回，浏览位置和选择会保留。

科技树默认隐藏连线，选择物品后只高亮直接上下游；「只看所选依赖链」筛选全部上下游物品，连线仍聚焦所选物品。缩放低于 35% 时卡片只显示大号名称，暂停加载画布图标；指向或选中的物品会在画布左下显示不随缩放变小的完整名字，放大后恢复完整卡片。

悬停或键盘聚焦连线时，右下显示关系、配方路线、候选及附加条件；点击连线会选中并定位另一端物品，再点击同一关系可跳回。线段周围有固定屏幕宽度的点击区域，在线上拖拽仍可平移画布。

## 启动

双击本目录的 `打开物品Wiki.cmd`。需要 Python 3，默认打开：

`http://127.0.0.1:8765/Assets/StreamingAssets/ItemWiki/`

启动器会复用同项目、同读写模式的健康服务；没有服务时在后台启动。端口被其他程序占用时会报错，不会结束对方进程。浏览器关闭后后台服务仍可复用。日志位于项目的 `Library/FlatWorldItemWiki/server-8765.out.log` 和 `.err.log`，不应提交。

在项目根目录也可执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Assets/StreamingAssets/ItemWiki/Start-ItemWiki.ps1
```

脚本支持 `-NoBrowser`、`-Port 8767`、`-PublicReadOnly`。也可以直接用 Python 启动：

```powershell
python -B Assets/StreamingAssets/ItemWiki/wiki_server.py --port 8765 --ensure-running
```

`-B` 防止 Python 在 Unity 的 Assets 中生成缓存。需要前台服务并用 Ctrl+C 退出时，去掉 `--ensure-running`。修改 HTML、JS、CSS、JSON 后刷新页面即可；修改 Python 服务代码后需重新启动对应服务。

`公开分享物品Wiki.cmd` 是已有 SakuraFrp 分享入口，使用独立的 8766 只读服务及精确资源白名单。它需要本机 SakuraFrp 配置；本地查看无需启用分享。公共入口只回收本次创建的进程，复用的 Wiki 服务会保留。

## 数据来源

| 内容 | 当前来源 |
| --- | --- |
| 物品和模板 | `../GameConfig/Items/item-manifest.json` 中启用的分包 |
| 生物 | `../GameConfig/Actors/actor-manifest.json` 中启用的分包 |
| 生物行为树与公共子树 | 生物继承后的 `modules.ai.parameters.behaviorTree` 与 Actor Manifest 的 `behaviorSubtrees` |
| 固定制作配方 | `../GameConfig/Recipes/recipe-manifest.json` 中启用的分包 |
| 单料加工 | 每个物品自己的 `processing`，不继承父物品的加工产物 |
| 机器加工与工作站能力 | `Assets/Resources/Config/Mechanical/mechanical-catalog.json` |
| 温变、液体凝固 | 物品 `matter/reactions` 与 `../GameConfig/Liquids/liquids.json` |
| 战利品和 Buff | `../GameConfig/LootTables/loot-tables.json` 与 Buff Manifest |
| 图标 | 当前定义的 Sprite 地址及必要的纹理 `.meta` 切片信息 |

物品继承由 `mechanics.js` 与 Python 写入校验共同对齐游戏加载器：更换模块 Prefab 或声明 `inherit:false` 时整体替换该模块，执行 `removeModules`，不继承显示身份、`formerIds` 或 `processing`。模块说明在 `mechanics-rules.js`，名称与字段解释在 `module-glossary.json`。

本地 Item/Buff 编辑仍通过原来的备份、文件指纹和写入校验流程；单纯查看或切换科技树不会写回游戏配置。Actor 及公开服务保持只读。

## 行为树分页

顶部「07 行为树」可按生物或公共子树浏览，也可直接打开 `#behavior-tree/actor%2FWolf`。目录支持名称、ID、节点搜索；树图从左到右，同级按从上到下的优先顺序显示，可折叠分支、展开子树、拖动及缩放。

点击节点查看本次调用的实际参数；公共子树合并默认值与调用覆盖，并列出递归引用它的生物。Actor 局部同名子树优先于公共定义，能力配置和 JSON 来源保留在详情中。页面只读展示配置结构，不显示运行中节点状态。

`behavior-tree-model.js` 处理继承后目录、子树展开与 `$参数` 替换，`behavior-tree.js` / `.css` 负责浏览；「重新读取」会重新加载当前 JSON，无需导出副本。

## 时代和子阶段如何调整

电路实验室的入口、规则与维护说明见下方「电路实验室」章节。

`tech-tree-eras.json` 只管理物品字典的展示分期，不会增加或改变游戏解锁规则。

- `eras`：各大时代的名称、顺序和颜色。`order` 从小到大排列在左到右；导航按实际时代数量排成单行，空间不足时横向滚动。
- `stages`：每个大时代内的细分阶段，使用 `eraId` 关联大时代，`order` 控制先后。
- `assignments`：按稳定 `itemIds` 指定 `stageId`，`reason` 说明分期依据。调整单个物品时，把它从原组移到目标组，避免重复。
- `specialItems`：内部表现变体、调试工具等特殊说明。
- `stationAliases` / `moduleStations`：经过运行时代码确认的工作站别名和模块工作站映射；不凭名称猜测新的生产关系。

新增到 Manifest 的物品会自动进入字典。未明确分期的新物品会留在“待分期”，补 `assignments` 即可。连线从真实配方、加工、温变等数据生成，时代的先后位置本身不代表它们相互依赖。

当前顺序为：原始采集 → 石器 → 铜器时代 → 青铜时代 → 铁器 → 机械动力时代 → 电力时代 → 化工时代 → 电子信息时代 → 太空时代。机械传动与机械加工归机械动力，发电储能和电网用电归电力，气液管道、泵送、过滤、电解与氢能按用途归化工；电子气压探针归电子信息，船体、船控、导航和光纤仍归太空。

塑料（`Plastic`）归化工材料，硅板（`SiliconPlate`）归半导体材料，两者先作为物品内容占位。科技树不会因为分期而补造生产配方或前置关系；后续添加正式加工路线后会自动显示连线。

自然采集物品在中下方「自然资源」区按已知生物群系组合分块展示，点击导航中的「自然资源」可定位。森林与草原等跨群系资源只展示一次，洞穴、小行星等额外来源在提示和详情中保留；搜索群系名称也可筛选。分组读取启用的自然物生成分包，伴生物沿宿主继承来源，产物沿真实采集与掉落链传播；地块 `groundHarvest` 产物标为「地表采挖」，未映射群系的生物掉落标为「生物产物」，没有来源证据的保留「群系未标注」。这些是候选来源，实际出现仍受地形、气候和世界预设限制。

`stages.layoutBand: "natural"` 明确指定整个阶段进入资源区，洞口、种植或加工路线不会将组内物品拆回时代主线；特殊物品仍留在主线。其他阶段的自然生成物、采矿节点、树木及其直接采集产物自动识别，纯加工或种植产物不会因此被归入自然资源。资源保留原展示分期和筛选规则；树冠果实按 `Module_CanopyFruit.FruitItemId` 的真实掉落来源连线，不把条件裂果当作直接收获。

`tech-tree-model.js` 负责数据关系，`tech-tree.js` 负责浏览和交互；修改显示时应保持两个职责分开。现有游戏机制文档导航仍在 [docs/README.md](docs/README.md)。

右键物品节点可选择在当前页面、新标签页或新窗口打开档案；后两项保留科技树的选中、位置和铺满状态，无须经过侧栏。菜单按 Esc 关闭，再按 Esc 才退出铺满；独立窗口由浏览器按窗口偏好处理。

## 维护时的最低检查

以下检查仅验证工具的语法和数据契约，不代替浏览体验及游戏玩法验收：

```powershell
node Assets/StreamingAssets/ItemWiki/tests/maintenance.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/balance-tuning.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/tech-tree-model.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/tech-tree-layout.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/behavior-tree-model.test.js
node --test Assets/StreamingAssets/ItemWiki/tests/circuit-board-model.test.js
python -B -m unittest discover -s Assets/StreamingAssets/ItemWiki/tests -p test_wiki_maintenance.py
python -B -m unittest discover -s Assets/StreamingAssets/ItemWiki/tests -p test_balance_tuning.py
```

增加浏览器脚本或新的数据源时，同时检查 `wiki_server.py` 的公开白名单；公开模式不能因为新页面而开放整个项目目录。

连线、依赖链清选、远近景图标加载及铺满状态的浏览器回归使用独立测试页面，不依赖 Wiki 服务。首次安装测试工具到已忽略的 Library，再运行：

```powershell
npm install --prefix Library/ItemWikiBrowserValidation --no-audit --no-fund playwright-core
$env:ITEM_WIKI_PLAYWRIGHT_MODULE = (Resolve-Path Library/ItemWikiBrowserValidation/node_modules/playwright-core).Path
node Library/ItemWikiBrowserValidation/node_modules/playwright-core/cli.js install chromium
node --test Assets/StreamingAssets/ItemWiki/tests/tech-tree-browser.test.js
```

已有 Chromium 时可设置 `ITEM_WIKI_CHROMIUM_PATH` 指向其可执行文件，省略浏览器安装。

## 电路实验室

顶部「08 电路实验室」或 `#circuit-board` 打开网格模拟器。无需新增前端依赖；HTML、Canvas、CSS 与纯 JavaScript 模型均内嵌在 ItemWiki。默认示例为电池供电、计时器控制灯泡亮 1 秒灭 1 秒，另有蓄水池自动控制、太阳能充放电、AND 真值实验及 64 位二进制加法器。

- 三层独立数据：设备、电力线、信号线。点击放置、拖动布线，选择模式拖动设备；R 旋转，右键擦除当前层，Delete 删除选中设备，空格/中键拖动视野，滚轮缩放，支持铺满网页。相同层相邻线相连；信号跨线桥横竖分离。
- `circuit-board-model.js` 从当前机械目录的 Electrical 节点创建游戏设备库，名称取 Wiki 已解析的物品；`circuit-board.js/.css` 仅负责编辑、显示、浏览器保存。`app.js` 负责分页生命周期和目录刷新，不在浏览器调用游戏命令或写配置接口。
- 正式物品中的 `ShipFiber` 单独登记为光纤适配：保留真实来源，但在实验中只作为可连普通信号线的 0/1 线路，不复刻船体归属和飞船控制协议。
- 「05 · 64 位二进制加法器」是完整可编辑示例：两个 64 位无符号输入寄存器，通过三条独立的蓝色宽数据总线连接一个 64 位全加器宏和 65 位结果显示器。黄线为电源，绿线为最高进位指示；断电、断总线或多输出冲突时清除无效结果。棋盘下方自动显示加法工作台，可输入十六进制、二进制或十进制大数，查看完整结果和 64 个一位 XOR / AND / OR 进位级的逐位记录；点击 16×4 热图可跳转到任一进位级。默认测试最大 64 位值加 1，完整溢出到第 65 位。
- 为避免铺设大量独立图形门，该示例把 64 个一位全加器组合成**可观察的宏元件**，内部按位传递真实进位、与 JS 大整数结果独立验证；这不是游戏已经实现的 CPU 芯片或原生总线。宽总线是同一信号覆盖层中的独立类型，不能与 0/1 信号线短接。保留版本 1 的原有方案保存结构，旧布置仍能载入。
- 机器目录与科技树共享一次请求，但其他科技树资料加载失败不阻断电路页；无关物品刷新不重置电路进度、选择与撤销。真实电气目录发生变化时保留布置并重置运行。
- 电力以整网功率求解，W 与 J 分离；电压兼容、方向端口、缺电比例分摊、发电优先、充放电速率和整网过载告警沿用游戏当前规则。过载不自动断线，不做电阻压降。双向电机只模拟发电方向，外部机械动力由参数表示。
- 逻辑元件与所有自动化控制口均为「模拟扩展」。同信号网采用 OR，输入悬空为 0，设备控制口不接线则按启用状态运行；每级门读上一拍、统一提交，每拍 0.05 秒。反馈环有真实离散延迟，不递归求稳态。SR 复位优先，计数与翻转只读上升沿。
- 实验水箱通过目标 ID 与水泵/传感器关联，虚线表示抽象水路；仅模拟储量与简化流量，不模拟水源、压力、温度交换、配方、船只推力等完整游戏业务。实验光照、温度由用户设置。
- 布置保存在当前浏览器 `flatworld-circuit-board-v1:<pathname>`，可导入/导出版本 1 JSON；只含布置和初始参数，不含运行进度。导入完整校验后替换，未知元件、非法数值、重复与越界会拒绝；损坏自动保存的原文保留在 `:recovery`。撤销恢复布置并重置运行。编辑、切页、后台时暂停，不补算离线时间。
- 公开模式只新增三个明确脚本/样式白名单条目，不开放新写入接口；修改服务白名单后，已有公开只读服务需在方便时重启。普通本地服务直接刷新即可读取新页面。

模型回归：`node --test Assets/StreamingAssets/ItemWiki/tests/circuit-board-model.test.js`。浏览器回归使用上述 Playwright 环境运行 `tests/circuit-board-browser.test.js`，真实浏览器截图保存在忽略目录 `Library/ItemWikiBrowserValidation/`。

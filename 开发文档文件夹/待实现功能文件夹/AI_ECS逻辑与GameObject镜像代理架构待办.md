# FlatWorld AI：ECS Logic + GameObject Mirror Proxy + BRG 架构待办

## 1. 系统定位

本方案不直接删除现有 GameObject AI，也不要求所有生物彻底脱离 GameObject。

目标是为现有普通 AI 建立一套**并行可体验的 Hybrid ECS 版本**：

- **ECS 负责 AI 逻辑和高频批量计算**。
- **BatchRendererGroup（BRG）负责生物绘制和大规模可见实例批处理**。
- **GameObject 退化为 Mirror Proxy（镜像代理空壳）**，只承担 Unity / 玩家 GameObject 世界与 ECS 之间无法直接消失的连接工作。
- 现有 GameObject AI 版本继续保留，首轮不直接替换；通过 GM / 配置切换和成组生成进行 A/B 体验与性能对比。
- 同一个生物实例只能由一个后端驱动，严禁旧 AI 和 ECS 同时修改状态。

最终结构：

```text
                       ┌──────────────────────────┐
                       │      Actor Definition     │
                       │  物种参数 / 模块组合 / 美术 │
                       └────────────┬─────────────┘
                                    │
                                    ▼
┌──────────────────────────────────────────────────────────────┐
│                         ECS Logic World                      │
│                                                              │
│  PerceptionSystem   MovementSystem   FeedingSystem           │
│  SleepSystem        CombatSystem     ReproductionSystem      │
│  FlightSystem       PackSystem       HiveSystem ...          │
│                                                              │
│             Entity = 纯数据组件组合，不是物种脚本             │
└───────────────┬──────────────────────────────┬───────────────┘
                │                              │
                │ 表现快照                      │ 交互/外部输入
                ▼                              ▼
      ┌──────────────────┐          ┌────────────────────────┐
      │ AI BatchRenderer │          │ GameObject MirrorProxy │
      │ BatchRendererGroup│          │ 极薄连接壳               │
      └────────┬─────────┘          └───────────┬────────────┘
               │                                │
               ▼                                ▼
        Sprite / 动画 / 阴影             Player / Collider / UI
        大规模批量 GPU 绘制              音效 / 特效 / 交互桥
```

一句话定义：

> **ECS 是大脑和模拟层，BRG 是批量身体绘制层，GameObject Mirror Proxy 是 Unity 生态的转接头。**

---

## 2. 当前现状

### 2.1 Actor 统计

当前正式 Actor 共 **11 种**：

- Bird
- Seagull
- Bee
- Chicken
- WildBoar
- Wolf
- Ghost
- Zombie
- Sheep
- Rabbit
- SnowLeopard

其中：

- Zombie 已有正式 AIECS 路线。
- Chicken / WildBoar / Wolf / Ghost / Bird / SnowLeopard 主要仍为专用 GameObject AI。
- Sheep / Rabbit 使用 `Module_AI_BehaviorGraph`。
- Bee 使用 Bird 基础能力 + 蜂群独立行为。
- Seagull 继承 Bird 行为。

现有 GameObject AI **首轮全部保留**，作为对照组。

### 2.2 当前 AIECS 基础

已经存在：

- `AiecsSimulation`
- 原生 Entity World
- `AiecsPerceptionSystem`
- `AiecsDecisionSystem`
- `AiecsBehaviorSystem`
- `AiecsFlowAgent`
- Crowd Steering
- Engagement Slot
- Attack / Damage / Buff 基础框架
- 外部玩家代理
- `IAiecsSimulationStage`
- `AiecsBehaviorProposal`
- 距离脉冲模拟
- 正式生态宿主
- AIECS 动画目录
- `AiecsWorldRenderer`

这些都应继续演进，不另起第三套 ECS 框架。

### 2.3 当前 AI 绘制并非真正 BRG

当前 `AiecsRenderBatch` 的实现是：

- 动态拼接 Mesh。
- 使用 `MeshRenderer`。
- 每批最多 4096 Sprite。
- 每帧上传顶点 / UV / Color 等数据。

项目真正的 Unity `BatchRendererGroup` 已经用于区块表现：

`ChunkBatchRendererGroupService`

因此本任务需要为 AI 建立独立的 **AiecsBatchRendererGroup**，参考区块 BRG 后端的实例缓冲、批次注册、剔除和资源生命周期设计，但不要让 AI 直接依赖区块专用的 `ChunkBatchRendererGroupService`。

---

## 3. 核心架构原则

### 3.1 GameObject 不再是 AI 计算单元

Hybrid ECS 版本中的普通生物 GameObject 禁止承担：

- 感知搜索。
- 状态机 Tick。
- BehaviorGraph Tick。
- 寻路决策。
- 进食搜索。
- 睡眠判断。
- 仇恨计算。
- 群体计算。
- 战斗时序。
- Sprite / Animator 常规绘制。
- 每帧 Y 排序。
- 每帧 AI Update / Coroutine。

这些全部进入 ECS 或 BRG。

### 3.2 GameObject Mirror Proxy 的职责

Mirror Proxy 只保留确实需要连接 Unity GameObject 世界的能力。

允许职责：

- 保存 Entity / CombatIdentity / GUID 的绑定句柄。
- 接收玩家 GameObject 的交互、点击、射线或现有接口调用。
- 把外部事件转换成 ECS Command / Event。
- 把少量需要 Unity Object 的反馈交给音频 / 特效 / UI 服务。
- 在必须存在 Physics2D 接触体时提供极薄 Collider Proxy。
- 作为旧系统仍要求 `GameObject` / `Item` 参数时的过渡适配器。
- 调试时提供 Entity 跳转 / Inspector 状态查看入口。

禁止职责：

- 自己拥有第二份 HP、饥饿、目标、AI 状态。
- 自己判断下一步行为。
- 自己控制常规动画状态机。
- 自己持续查询世界。
- 自己作为普通 SpriteRenderer 绘制主体。

### 3.3 ECS 是权威逻辑状态

Hybrid 版本不能出现：

```text
Proxy.isEating = true
ECS.FeedingState = Idle
```

任何 AI 权威状态只保存一份：

```text
ECS.FeedingState = Eating
```

Proxy 只能读取或转发，不保存竞争状态。

### 3.4 共享能力按数据和更新频率成批处理

不要创建：

```text
ChickenEcsAiSystem
WolfEcsAiSystem
RabbitEcsAiSystem
```

按确实存在的共用行为，逐步建立：

```text
MovementSystem
PerceptionSystem
FeedingSystem
SleepSystem
FleeSystem
ChaseSystem
CombatSystem
ReproductionSystem
FlightSystem
PackSystem
HiveSystem
```

物种通过组件组合得到行为。`Flee / Chase / Pack / Hive` 可以先作为共享规则或少量 Stage 接入现有 `AiecsDecisionSystem`，不必机械地各建一套遍历全部 Entity 的 System。按需要筛选的组件和 60/30/10Hz 脉冲组织查询；低频能力不增加每个基础 Tick 的全量扫描。

当前 `AiecsSimulation` 用一个含身份、脑、攻击、Buff、移动等全部组件的 Archetype，`activeQuery` 也要求整套组件。模块化前先把“所有 Actor 必有”的基础查询与“只有该能力才有”的可选查询分开；Stage 持有当前 World 的能力查询，并只调度已激活的匹配居民。大块运行态适合可选组件，小型常量开关适合放共享定义；不要为每个物种标签制造大量 Archetype 组合，也不要每 Tick 为切换行为增删组件。

---

## 4. ECS 模块模型

### 4.1 基础组件

所有普通 Hybrid ECS Actor 可按需组合：

- `ActorIdentity`
- `ActorTransform`
- `ActorBody`
- `ActorVital`
- `ActorFaction`
- `ActorSimulationPulse`
- `ActorRenderState`

组件命名可继续沿用现有 `Aiecs*` 前缀，不要求机械改名；重点是职责拆分。

### 4.2 移动模块

先复用现有 `AiecsBehaviorIntent`、`AiecsLocalMotion` 和 `AiecsFlowAgent`。以下是职责边界，不代表必须新增五份组件：

- `MovementCapability`
- `MovementIntent`
- `MovementRuntime`
- `GroundMovement`
- `FlightMovement`

由现有 Flow/Crowd 调度链统一批处理。只有地面与飞行的查询、数据生命周期确实不同，才拆成可选组件；不在每次行为切换时反复增删组件。

GameObject Proxy 不直接调用 `Mover_AI`。

流程：

```text
Brain / Feeding / Flee / Chase
           │
           ▼
     MovementIntent
           │
           ▼
  MovementSystem + Flow/Crowd
           │
           ▼
    ECS ActorTransform
           │
     ┌─────┴─────┐
     ▼           ▼
    BRG       Mirror Proxy
```

### 4.3 感知模块

- 所有需要感知的 Entity 共用稀疏空间索引。
- 不允许每只动物一个 `Mod_ItemDetector` 做独立扫描。
- 玩家通过 External Player Proxy 进入感知快照。
- Legacy GameObject AI 与 Hybrid ECS Actor 需要互相测试时，只桥接有限的 GameObject Actor Snapshot。
- LOS 使用现有地形 / 建筑冻结数据，不使用 Physics2D 热路径。

### 4.4 进食模块

建议组件：

- `NutritionState`
- `FeedingCapability`
- `FeedingTarget`
- `FeedingRuntime`

统一 `FeedingSystem`：

- 批量判断哪些实体需要进食。
- 批量搜索合适食物候选。
- 批量维护目标有效性。
- 到达后写入 Eating Intent。
- 完成后统一提交资源消耗 / 营养变化事件。

食物可能是 Item、掉落物或世界资源。Gameplay 桥在冷边界制作带世界、维度、资源身份和版本的候选快照；ECS 只选目标和提出消费请求。Gameplay 在结算点重新校验资源、一次性预留并扣减，再回写成功或失败。多个 Actor 争同一份食物时只能成功一次；不能在 Job 中直接改 Item 或把食物存在 Proxy 中。

鸡、羊、兔、鸟、蜜蜂等使用同一套主干，不再各自写“找食物”循环。

### 4.5 睡眠模块

统一处理：

- 时间条件。
- 疲劳 / 生命条件。
- 受伤唤醒。
- 睡眠期间低频模拟。
- 睡眠动画状态输出。

### 4.6 战斗模块

继续复用：

- `AiecsAttackState`
- HitEvent
- DamageSystem
- BuffSystem
- Engagement Slot

新增特殊技能不要回到逐动物 MonoBehaviour，使用共享 Skill 数据 + `IAiecsSimulationStage`。

### 4.7 繁殖 / 生产模块

通用繁殖：

- 配偶资格。
- 配偶搜索。
- 冷却。
- 后代生成事件。

物种特殊生产：

- Chicken：EggLaying。
- Bee：HiveReproduction。

特殊能力做独立组件 / Stage，不污染通用繁殖系统。

### 4.8 飞行模块

Bird / Seagull / Bee 共用 Flight 主干：

- Ground
- RunUp（需要的物种）
- TakeOff
- Flying
- Landing
- Flight Stamina
- Flight Height

Bird 和 Bee 的差异来自能力组件与定义参数，而不是复制两套飞行核心。

---

## 5. 物种模块组合

首轮建议组合：

| Actor | Hybrid ECS 模块 |
| --- | --- |
| Chicken | Movement + Perception + Nutrition + Feeding + Sleep + Flee + Reproduction + EggLaying |
| Sheep | Movement + Perception + Nutrition + Feeding + Sleep + Flee |
| Rabbit | Movement + Perception + Nutrition + Feeding + Flee |
| WildBoar | Movement + Perception + Nutrition + Feeding + Flee + Combat + Charge |
| Wolf | Movement + Perception + Chase + Combat + Flee + Pack |
| SnowLeopard | Movement + Perception + Chase + Combat + Predator |
| Bird | Movement + Flight + Perception + Nutrition + Feeding + Flee + Sleep |
| Seagull | Bird 共享模块 + Seagull 参数 |
| Bee | Flight + Perception + Feeding + HiveMember + Combat |
| Ghost | Movement + Perception + Chase + Combat + Darkness |
| Zombie | Movement + Perception + Chase + Combat |

`Predator / Charge / Pack / EggLaying / HiveMember / Darkness` 属于少量扩展能力；不要为了这些能力创建完整物种 System。

---

## 6. GameObject Mirror Proxy 设计

### 6.1 Proxy 形态

建议建立统一组件：

`AiecsActorMirrorProxy`

其内部只保存：

- Entity 句柄。
- CombatIdentity。
- Actor GUID。
- 绑定代际。
- 必要外部系统引用。

不要复制 ECS 状态。

稳定身份以 `CombatIdentity` / Actor GUID 为准；`Entity` 只在当前 ECS World 内有效。每次绑定还需核对 World、Dimension、Entity Version 和 Proxy 自身绑定代际。解绑时先停用 Collider/旧接口并清除旧回调，再归还对象池；延迟到达的命中、交互和 UI 回调必须因代际不符而丢弃。

### 6.2 Proxy 输入方向

GameObject → ECS：

```text
旧接口确实需要 GameObject 的点击 / 命中 / 交互
        ↓
AiecsActorMirrorProxy
        ↓
ECS Command / HitEvent / InteractionEvent
```

现有 `AiecsGameplayBridge` 已能把玩家武器命中写入 ECS。玩家攻击、纯几何选中和查询优先沿用批量空间索引与战斗桥；只有调用方必须持有 Collider、GameObject 或 `Item` 时才创建 Mirror Proxy，不把它设为所有攻击的必经中转。

### 6.3 Proxy 输出方向

ECS → Unity：

```text
ECS 状态变化
   │
   ├─ BRG：位置 / 朝向 / Sprite帧 / 水中参数 / 阴影
   ├─ Audio：一次性声音事件
   ├─ VFX：一次性特效事件
   ├─ UI：血条 / 名称 / 调试信息
   └─ Proxy：必要 Collider / 外部交互位置同步
```

### 6.4 不要求所有 Entity 永远拥有 Proxy

Proxy 应池化。

推荐：

- 玩家附近、有交互需求：绑定 Proxy。
- 远处普通生物：只保留 Entity + BRG 或甚至只保留 Entity。
- 离开交互范围：Proxy 解绑并回对象池。
- 回到附近：重新取得 Proxy，并绑定同一个 Entity / GUID。

绑定条件以实际交互能力为准，距离只作粗筛；进入和退出范围用不同阈值，避免边界来回创建。按当前世界和维度建立候选集，并给池容量和每帧绑定量设预算。卸载区块、死亡、切维度、退出世界、F5 玩家重建时统一撤销旧绑定。

以下只是容量示意：

```text
世界中 8000 个 AI Entity

可见 1500 个 → BRG 绘制 1500 个
可交互近距 200 个 → 只有约 200 个 Mirror Proxy
远距 6500 个 → 没有 GameObject Proxy
```

具体范围必须通过 Profiler 和玩法需求调整，不写死以上数字。

---

## 7. BRG AI 渲染架构

### 7.1 目标

Hybrid ECS 版本普通 AI 的常规 Sprite 绘制全部进入：

`AiecsBatchRendererGroup`

不再为每只 Hybrid ECS Actor 保留：

- SpriteRenderer。
- Animator。
- 每实体 MeshRenderer。
- 每实体 Y 排序脚本。

### 7.2 复用项目现有 BRG 经验

参考：

`World/WorldModel/Presentation/ChunkBatchRendererGroupService.cs`

可复用的设计思想：

- `BatchRendererGroup` 单后端所有权。
- Mesh / Material 注册缓存。
- GPU Instance Buffer。
- 每实例连续结构体数据。
- 批次容量复用。
- 增量实例更新。
- BRG Culling Callback。
- 生命周期集中 Dispose。
- Development Build 调试统计。

不要直接把 AI 注册进 `ChunkBatchRendererGroupService`，因为区块实例生命周期和 Actor 动态生命周期不同。

项目是 Unity 2022.3。BRG 可提供 `HasSortingPosition` 的实例位置排序，但它没有 SpriteRenderer 的 Sorting Layer / Order 字段；现有区块 BRG 也是用透明队列处理固定层级。因此 AI BRG 接入前，先用正式 2D Renderer、现有光照与世界排序配置做小规模纵切验证：同一只 Zombie 穿过玩家、建筑、草、机械前后时，主体和两类阴影是否正确交错。若仅靠单个实例化 Draw 无法保持这些关系，按排序语义拆绘制范围或增加专用 Renderer Pass，并测量 Draw/CPU 代价；不能直接把 24 行网格批次替换成一个大 Draw 后宣称排序完成。

### 7.3 AI Instance Data

至少需要：

- 世界位置。
- Scale。
- Facing / Flip。
- Sprite / Animation Frame 索引。
- Tint。
- 水体表现参数。
- Flight Height / Visual Offset。
- 阴影参数。
- Y 排序所需世界锚点。

数据结构要按 GPU 上传效率设计，不能每实体传大量冗余状态。图集帧的 UV、局部尺寸和 Pivot 可从共享目录索引，GPU 实例数据只放每帧确实变化的值。先统计移动单位比例和上传字节数，再决定连续脏区上传或整段上传；大量 AI 同时移动时，逐实例 `GraphicsBuffer.SetData` 可能比整段上传更贵。

### 7.4 动画

Animator 不再作为 Hybrid ECS Actor 动画权威。

使用现有 `AiecsAnimationCatalog`：

```text
ECS Behavior / AttackPhase / FlightPhase
                  ↓
          Animation State ID
                  ↓
       ECS Animation Clock
                  ↓
       AiecsAnimationCatalog
                  ↓
             Sprite Frame
                  ↓
                 BRG
```

需要特殊 AnimationEvent 的内容改为 ECS 时间轴事件或 Gameplay Event，不回调每只 Actor 的 Animator。

### 7.5 透明 Y 排序

2D 俯视角必须保留现有动态 Y 排序视觉语义。

- BRG 后端要实现稳定的透明排序策略。
- 同一 Actor 的主体、阴影、特殊附层保持相对顺序。
- 排序不能退化成每实例主线程 `Renderer.sortingOrder` 写入。
- 与建筑、玩家 GameObject、世界 BRG 的相对排序要真实 Play Mode 验证。

### 7.6 Player 仍是 GameObject

玩家暂不要求 ECS 化。

因此需要明确：

- Player Transform → ECS External Player Proxy。
- AI BRG 与 Player SpriteRenderer 的 Y 排序语义一致。
- 玩家攻击 → ECS HitEvent。
- ECS AI 攻击 → Player Combat Bridge。
- AI Proxy 只是桥，不要求玩家改成 ECS。

---

## 8. Legacy GameObject AI 与 Hybrid ECS 并行对比

### 8.1 首轮不替换

以下旧实现暂时保留：

- `AI_Chicken`
- `AI_WildBoar`
- `AI_Wolf`
- `AI_SnowLeopard`
- `AI_Bird`
- `AI_Ghost`
- `Module_AI_BehaviorGraph`
- Bee 旧行为

### 8.2 后端选择

为每个 Actor 提供开发期选择：

```text
LegacyGameObject
HybridEcsProxy
```

正式生态的权威选择已由 `SpawnerConfig.SpawnEntry.RuntimeBackend` 在物种级确定，同一世界同一物种只能选一种后端。开发 A/B 使用独立测试场景或隔离的测试批次，不修改正式路由，也不把对照 Legacy 实例登记为正式生态居民；测试批次须有明确创建、计数和清理所有权。启动 AIECS 开发模拟时遵守现有正式宿主的暂停/恢复协议，不同时驱动两个 World。

正式生态中的后端切换只影响以后创建的实例；现存居民的转换需另做带存档迁移的显式操作，不能仅改开关就把旧实体清空或静默换后端。

同一实例绝不允许：

```text
AI_Wolf.ModUpdate()
+
AIECS Wolf Entity Tick
```

### 8.3 GM A/B 测试入口

在现有 AIECS GM 页增加：

- 当前测试 Actor。
- 生成 Legacy 版本。
- 生成 Hybrid ECS 版本。
- 成对生成 Legacy + Hybrid。
- 清理测试实例。
- 当前 Legacy 数量。
- 当前 ECS Entity 数量。
- 当前 Proxy 数量。
- 当前 BRG Visible 数量。
- CPU AI 时间。
- BRG Upload / Cull / Draw 统计。
- GC Alloc。

成对测试使用相同定义、初始参数、环境和随机种子；双后端互相感知/攻击尚未打通时应隔离对照区，避免两个样本互相影响。

### 8.4 对比维度

体验：

- 行为是否自然。
- 追击 / 逃跑是否一致。
- 进食、睡眠、群体行为是否正确。
- 动画反馈是否及时。
- 玩家攻击反馈是否正常。

性能：

- Main Thread AI。
- Job Worker 时间。
- Rendering CPU。
- Draw / Batch 数。
- GPU Instance Upload。
- GC Alloc。
- 100 / 500 / 1000 / 5000 / 8192 AI 情况。

---

## 9. Hybrid ECS 各系统的数据流

### 9.1 一帧逻辑

```text
① 外部输入快照
Player / Boss / World Data
          ↓
② Perception Systems
          ↓
③ Module Proposal Systems
Feeding / Sleep / Flee / Pack / Hive ...
          ↓
④ Decision
          ↓
⑤ Movement / Flight / Crowd
          ↓
⑥ Attack / Skill
          ↓
⑦ Damage / Buff / Survival Settlement
          ↓
⑧ Render Snapshot + Proxy Event Snapshot
          ↓
   ┌──────┴────────┐
   ▼               ▼
 BRG            MirrorProxy
```

### 9.2 模块不能互相直接操纵 GameObject

例如 Feeding System 想让鸡走向食物：

错误：

```text
FeedingSystem → chicken.transform.position += ...
```

正确：

```text
FeedingSystem → MovementIntent
MovementSystem → ActorTransform
BRG / Proxy → 消费 ActorTransform
```

这样所有移动永远只有一个写入口。

### 9.3 输入、结算和表现的时间边界

- 主线程先冻结玩家、世界、食物和旧接口事件，再启动本次模拟 Job。每条外部命令带世界、维度、目标身份、绑定代际和模拟 Tick；失效命令丢弃，重复命中按事件身份去重。
- Job 只产生命中、消耗、生成、音效等纯数据事件；完成依赖后由 Gameplay 桥按固定顺序提交给 Item、玩家和存档系统。对同一目标的伤害与资源扣减必须保持一次性结算。
- `AiecsDisplayRecord` 是模拟完成后的只读快照。BRG 可以在两次模拟之间插值显示；交互和 Collider 位置必须取已提交的权威位置，不用插值图像反写 ECS。Physics2D 真需要代理时，要明确在物理步之前同步哪一版位置。

### 9.4 身份、存档和世界流送

- 存档以 Actor GUID、物种定义 ID、世界/维度和运行态组件为准，不存 `Entity` 索引、Proxy 实例、BRG 槽位或临时空间索引。恢复时创建新 Entity，再用 GUID 重建目标、蜂巢等跨 Actor 引用；不存在的目标清空。
- 当前正式宿主有运行时 `_actors`/数量登记与 `ResetWorld`，尚没有本架构所需的完整 ECS 居民存档闭环。把存档/读档、死亡掉落一次性状态、世界退出重进列为第一批正式迁移门槛，避免先迁十个物种后再改身份格式。
- 距离脉冲只降低或冻结行为频率，不能代替持久化。区块卸载时由权威世界生命周期决定居民继续存在、休眠或写回存档；Renderer/Proxy 解绑不销毁 Entity。F5 更新定义时保留运行态并明确字段兼容与迁移规则，不重置已存在居民。

---

## 10. 迁移顺序

### P0：Hybrid 基础设施

- [ ] 先用已有 Zombie + 现有玩家战斗桥做纵切，记录同场景 MeshRenderer 基线和两端命中；明确哪些旧交互真的要求 GameObject。
- [ ] 在 Unity 2022.3 正式 2D Renderer 中验证 AI BRG 的动画、水线、光照、两类阴影，以及与玩家、建筑、草、机械的透明 Y 排序。通过后再接入独立 `AiecsBatchRendererGroup`。
- [ ] 建立稳定 Actor GUID ↔ 当前 Entity/CombatIdentity 的世界级注册表，处理死亡、区块卸载、切维度、退出世界和 F5。
- [ ] 完成 ECS 居民存档/恢复的最小闭环，并核对生成器数量、死亡掉落只提交一次。
- [ ] 对确认必须依赖 GameObject 的旧交互建立 `AiecsActorMirrorProxy` 与池化绑定；按需求选配 Collider，不为所有居民预建 Proxy。
- [ ] GM 增加隔离的 Legacy / Hybrid A/B 对照入口，显示 Entity、Proxy、BRG 可见数和上传成本。

### P1：模块化核心

- [ ] 把现有单一 Archetype/`activeQuery` 拆成基础查询与可选能力查询；能力 Stage 只处理有该组件且本 Tick 激活的居民，验证不同组件组合不会漏算。
- [ ] 在现有 Flow/Crowd 上扩展 Movement；只补真实缺少的运动能力。
- [ ] 复用现有 Perception/LOS/双空间索引；补不同物种需要的感知规则。
- [ ] Feeding / Nutrition。
- [ ] Sleep。
- [ ] Flee / Chase。
- [ ] 复用现有 Attack/Damage/Buff 与玩家桥；补能力差异。
- [ ] Reproduction。
- [ ] 通用事件桥。

### P2：简单物种

- [ ] Zombie。
- [ ] Sheep。
- [ ] Rabbit。
- [ ] Chicken。

每个物种都保留 Legacy 版，可随时对比。

### P3：地面战斗物种

- [ ] WildBoar。
- [ ] Wolf。
- [ ] SnowLeopard。

### P4：飞行

- [ ] Flight System。
- [ ] Bird。
- [ ] Seagull。

### P5：蜂群

- [ ] Bee。
- [ ] HiveMember。
- [ ] Hive System。

### P6：特殊移动

- [ ] Ghost。
- [ ] Darkness / Retreat。

### P7：长期收口

等实际体验与 Profiler 证明 Hybrid ECS 明显更合适后，再单独决定是否删除 Legacy GameObject AI。

**本待办不授权 Agent 直接删除 Legacy AI。**

---

## 11. 性能约束

### ECS 热路径禁止

- `FindObjectsOfType`
- 每实体 `GetComponent`
- Physics2D 全局扫描
- 每实体 LINQ
- 每实体托管状态机对象
- 每实体 Coroutine
- 每实体路径对象
- 每帧托管分配

### Mirror Proxy 禁止

- AI `Update()`。
- AI `LateUpdate()`。
- AI `ModUpdate()`。
- 普通 SpriteRenderer / Animator 绘制主体。
- 重复保存 ECS 权威状态。
- 每帧从 EntityManager 拉取大量组件逐项同步。

Proxy 的同步必须批量化：

- ECS 统一输出少量 ProxySnapshot。
- 主线程按当前已绑定 Proxy 数量应用必要位置 / Collider / 交互状态。
- 没有 Proxy 的 Entity 完全不产生 Proxy 同步成本。

### BRG 约束

- 不为每 Actor 创建 Renderer。
- 不每帧重建整个 Mesh。
- 尽量增量上传发生变化的 Instance Data。
- Mesh / Material / Sprite 几何共享注册。
- Culling 不回到逐 GameObject 检查。
- Debug 统计必须可以确认 batch / instance / visible / upload 数。
- 同时记录 CPU 写入次数、上传字节、Culling 耗时和 RenderThread/GPU 成本。大量实例连续移动时比较整段上传与合并脏区上传，不能只数 `SetData` 调用次数。
- 透明排序还须记录 `BatchDrawCommand` 数；当前区块 BRG 的逐实例精确排序会为每个可见实例生成一条命令，不能直接照搬到数千只 AI 而只看 Batch 数。

---

## 12. GameObject 仍需要存在的边界

GameObject 不应为了“纯度”被强行全部删除。

以下情况允许存在 Proxy：

- 玩家鼠标 / 交互系统暂时要求 Collider / GameObject。
- 旧模块接口必须接收 GameObject。
- 近距离需要 Physics2D 接触。
- 特殊一次性特效 / 音频挂点确实需要 Transform。
- GM / Editor 调试需要可选择对象。

但需要持续问：

> 这是玩法必须依赖 GameObject，还是只是旧实现习惯？

如果只是旧实现习惯，应继续迁出 Proxy。

最终 Mirror Proxy 越薄越好。

---

## 13. Agent 并行执行建议

可以并行，但核心文件指定单一 owner。

建议：

- Agent A：Mirror Proxy / Pool / Identity Bridge。
- Agent B：AI BatchRendererGroup / Animation / Sorting / Culling。
- Agent C：Movement + Perception 模块化。
- Agent D：Nutrition + Feeding + Sleep。
- Agent E：Combat + Skill + Player Bridge。
- Agent F：Reproduction + Flight + Hive 扩展设计。
- Agent G：GM A/B 对比与性能统计。

以下核心文件不要多个 Agent 同时直接修改：

- `AiecsSimulation.cs`
- `AiecsComponents.cs`
- `AiecsDefinitionCompiler.cs`
- `AiecsGameplayBridge.cs`
- Actor Definition 公共 schema
- `GMReflectionConsole.Navigation.cs`

如果并行 Agent 需要增加能力，优先新增独立 Component / System / Stage 文件，由核心 owner 最后接入调度链。

---

## 14. 单物种完成标准

一个 Hybrid ECS 物种只有同时满足以下条件才算可与 Legacy 对比：

- [ ] Legacy GameObject 版本仍能正常运行。
- [ ] Hybrid ECS 版本创建的是 Entity + 可选 Mirror Proxy。
- [ ] Hybrid 版本 AI 逻辑不调用旧物种 AI。
- [ ] Hybrid 版本移动由共享 ECS Movement System 处理。
- [ ] 该物种需要的 Feeding / Sleep / Combat 等模块由共享 System 处理。
- [ ] Hybrid 版本主体绘制进入 BRG，不依赖 SpriteRenderer / Animator。
- [ ] 玩家可以正常攻击 Hybrid Actor。
- [ ] Hybrid Actor 可以正常影响玩家。
- [ ] 交互时 Proxy 正确连接，不保存第二份逻辑状态。
- [ ] Proxy 解绑后 Entity 仍能继续模拟。
- [ ] Proxy 重绑定后身份和表现正确恢复。
- [ ] 存档/读档、区块卸载重进、切维度、F5 后 GUID 和运行态仍正确，旧 Proxy 回调不能命中新居民。
- [ ] 多只生物争同一资源时只扣减一次，死亡、战利品和繁殖生成事件只提交一次。
- [ ] Y 排序、动画、阴影、水体表现与 GameObject 版本视觉可比。
- [ ] GM 能分别显示 Legacy / ECS / Proxy / BRG 数量。
- [ ] 无新增 Error / Exception。
- [ ] Profiler 中 Hybrid 版本无明显逐实体 managed 热点。

---

## 15. 整体完成标准

本阶段完成并不意味着删除 Legacy AI。

本阶段的完成定义是：

- [ ] 11 个正式 Actor 都存在可运行的 Hybrid ECS 版本。
- [ ] Legacy GameObject AI 仍保留用于 A/B 对照。
- [ ] ECS 已形成模块化 Movement / Feeding / Sleep / Perception / Combat 等公共处理链。
- [ ] 不同物种主要通过组件与数据组合，而不是复制物种 System。
- [ ] Hybrid 普通 AI 的常规绘制全部进入 `BatchRendererGroup`。
- [ ] Mirror Proxy 已成为极薄的 Unity / Player / ECS 连接层。
- [ ] 远处 Entity 可以完全没有 GameObject Proxy。
- [ ] 玩家仍为 GameObject 时，双方感知、攻击、交互闭环完整。
- [ ] ECS 居民已具备独立存档与恢复路径，表现和 Proxy 的生命周期不改变居民归属。
- [ ] GM 可一键比较 Legacy 与 Hybrid ECS 的体验和性能。
- [ ] 在真实玩法负载下完成至少 100 / 500 / 1000 / 5000 / 8192 AI 分档性能采样。

性能对照使用同一存档、相机、生成范围、物种比例、可见数和玩家路径；分别记录 Editor 与目标设备的 Main Thread、Job 等待/执行、Render Thread、GPU、GC、上传字节和帧时间 P50/P95。记录实际 Active Tick 数，避免降频后把行为缺失误判为优化收益。

完成后再由实际体验和 Profiler 数据决定下一阶段：

1. 继续长期保留双后端；或
2. Hybrid ECS 成为默认，Legacy 只保留特殊 Boss；或
3. 对个别行为复杂的物种继续使用 GameObject AI。

本阶段不要提前替用户做这个取舍。

---

## 16. 最终架构理解

Hybrid ECS 普通生物不是“一个很重的 GameObject 加一点 ECS 优化”，而应该是：

```text
                         玩家 GameObject
                               │
                               │ 外部交互
                               ▼
                      GameObject MirrorProxy
                       （轻量、可池化）
                               │
                      Command / Event Bridge
                               │
                               ▼
┌───────────────────────────────────────────────────────────┐
│                        ECS World                           │
│                                                           │
│ 感知 → 决策 → 移动 → 进食 → 睡眠 → 战斗 → 生存 → 繁殖      │
│              全部按模块对大量 Entity 批量计算              │
└────────────────────────────┬──────────────────────────────┘
                             │ Render Snapshot
                             ▼
                  AiecsBatchRendererGroup
                             │
                             ▼
                   大规模 Sprite 批量绘制
```

因此普通生物的 GameObject 最终只是一个**连接 ECS 与传统 Unity / 玩家 GameObject 系统的临时镜像代理壳**。

真正的大规模成本应集中到：

- Burst / Jobs / ECS 批处理逻辑。
- Shared Flow / Spatial Index。
- BRG GPU Instance Rendering。

而不是分散在几千个 MonoBehaviour、Animator、SpriteRenderer 和独立 AI Module 上。

---

## 17. 后续待规划

- BRG 在本项目 2D Renderer 内能否与现有 Sorting Layer / Order 完整交错，以 Zombie 纵切和 Frame Debugger 结果决定最终绘制分组。
- 哪些旧交互必须持有 Collider / GameObject，以真实调用链决定 Proxy 能否保持为少量可选实例。
- 长距离居民的存档粒度、区块休眠规则和 F5 定义兼容策略，以正式存档格式与生态数量语义共同确定。

# 离散概率表

唯一原表为同目录的 `discrete-probability-tables.json`。`percent` 直接使用百分数，每张表合计100%；`value` 是抽中后交给业务的结果。

```json
{
  "id": "example.amount",
  "entries": [
    { "value": 1, "percent": 10 },
    { "value": 2, "percent": 90 }
  ]
}
```

## 统一链路

`JSON 原表 → 独立运行态 → 修正规则 → 自动归一化 → 不可变快照 → 统一抽样 → 行为消费结果`

- `DiscreteProbabilityTableDefinition`：原表数据模型。
- `DiscreteProbabilityState`：修正规则收到的结构体，`SetWeight/MultiplyWeight` 写时复制；复制结构体后修改也不会串到原表或另一个副本。
- `DiscreteProbabilitySnapshot`：最终只读结果，`GetPercent(index)` 查看归一化后的真实百分比。
- `DiscreteProbability.Sample(table, random01)`：统一抽签，零概率不命中，0和1均合法。
- `SampleWithin`：先限制允许结果范围，再对剩余档位归一化抽签；范围内没有正概率结果时明确报错。

## 难度与扩展入口

在规则注册/卸载阶段订阅和退订 `DiscreteProbabilityConfiguration.Modifying`。规则根据表 ID 修正本次状态，行为系统不判断难度。

```csharp
static void Adjust(string tableId, ref DiscreteProbabilityState state)
{
    if (tableId != "loot.tree.leaf.amount") return;
    for (int i = 0; i < state.Count; i++)
        if (state.GetValue(i) >= 10)
            state.MultiplyWeight(i, 1.2);
}

// 注册一次；所属规则卸载时用 -= 退订。
DiscreteProbabilityConfiguration.Modifying += Adjust;
```

修正不必维持总和100。比如原始10/90，将第一项权重改成30，最终为25%/75%；负数、非有限权重和全部归零都会拒绝。难度定义变化或修正规则注册变化会使最终目录重新构建；源 JSON 不变。

修改 JSON 后重新导入资源并调用 `DiscreteProbabilityConfiguration.Reload()`，下一次 Capture 构建新目录；正式战利品重新加载也会刷新。原表解析和构建在主线程进行，抽样使用调用方传入的随机数，不改 Unity 全局随机状态。

## 已迁移的五类

| 类型 | 表 ID / 结果含义 |
| --- | --- |
| 树叶掉落数量 | `loot.tree.leaf.amount`，1～20片；6张战利品表共享 |
| 地面自然落叶 | `generation.count.surface.forest.leaf`，1～10片；原生成开关保持关闭 |
| 岩浆湖半径 | `generation.lava.radius`，基础格数，应用空间缩放并限制地理允许范围 |
| 河床边缘沉积 | `generation.river.deposit.activation/length`，激活倾向与长度占比 |
| 木棍/仙人掌聚落 | `generation.patch.distance.0`～`10`，按最近归一化距离档读取0=不生成、1=生成的概率；再乘原有总生成率 |

初始表沿用旧分布的数值倾向，运行时不再计算这些概率曲线。聚落空间密度由连续衰减改为11个距离档，边界采用最近档位。天气、雪区半径和地理温度尚未接入本目录。

## 地图冻结与缓存

Profile 创建时将最终 `generation.*` 概率写入已有数字参数，随冻结生成配置保存和恢复，并参与生成/生态指纹。后台线程只消费所属 Profile 的概率；难度调整不改写已生成世界或已经冻结的 Profile。要对新生成任务应用新难度，按正式流程创建新 Profile。

# 食物设置面板 - 快速开始指南

## 概述
食物设置面板是一个编辑器工具，参考武器数值设置面板的设计，用于统一、方便地设计和管理游戏中所有食物的参数。

## 快速开始

### 1. 打开面板
```
菜单: FlatWorld -> 食物数值表
```

### 2. 扫描食物预制体
点击工具栏中的"扫描 Prefab"按钮，自动扫描所有包含 `Mod_Food` 组件的预制体。

### 3. 编辑食物参数
在表格中直接编辑各行数据，修改会自动保存。

### 4. 应用参数
点击对应行的"应用"按钮，或点击"应用全部"批量应用所有参数。

---

## 详细功能说明

### 表格列说明

#### 基础信息
- **Prefab**：预制体引用，点击可定位

#### 营养值（5列）
| 列 | 字段 | 说明 |
|----|------|------|
| 1 | 碳水 | Carbohydrates，主要能量来源 |
| 2 | 脂肪 | Fat，备用能量来源 |
| 3 | 蛋白 | Protein，生命恢复所需 |
| 4 | 水 | Water，生存必需（缺水伤害） |
| 5 | 维生 | Vitamins，健康维持（缺乏伤害） |

#### 食物特性
- **进度**：咀嚼多少次才能完全吃掉（Max_EatingProgress）
- **消耗速度**：营养值消耗的速度倍率

#### 腐败配置
- 启用/禁用标记
- 腐败时间（秒）

### 工具栏功能

#### 扫描 Prefab
自动扫描项目中所有 Mod_Food 组件：
- 读取现有参数到表格
- 按名称排序显示
- 首次使用时必须执行

#### 应用全部
将表格中所有行的数据同时应用到对应预制体：
- 保存Prefab资源
- 支持批量更新

#### 保存表格
保存当前表格配置到 `FoodStatTableConfig.asset`

### 过滤和搜索

#### 搜索框
按预制体名称或路径搜索（不区分大小写）

#### 仅营养
只显示包含营养数据的行

#### 仅腐败
只显示启用腐败配置的行

### 行操作按钮

#### 应用
将该行参数应用到对应预制体

#### 定位
在 Project 面板中定位到该预制体

#### Inspector
打开预制体编辑模式，自动定位到 Mod_Food 组件

### 底部功能

#### 应用选中行
只应用当前选中行的数据

#### 从 Prefab 重新扫描
清空表格并重新扫描所有预制体

#### 清空表格
清空当前表格所有行（需确认）

---

## 使用示例

### 例子 1：修改现有食物参数

1. 打开"食物数值表"
2. 在表格中找到"Apple"食物
3. 编辑 Carbohydrates 值：50 → 100
4. 按Tab键或鼠标点击其他单元格
5. 修改会自动保存到配置文件
6. 点击该行的"应用"按钮，参数应用到预制体

### 例子 2：新增食物到表格

1. 确保新食物预制体（如 "Bread"）包含 `Mod_Food` 组件
2. 点击"扫描 Prefab"按钮
3. 新食物自动出现在表格
4. 编辑其参数（营养值、腐败配置等）
5. 点击"应用"保存到预制体

### 例子 3：批量设置腐败参数

1. 在表格中找到所有需要腐败的食物
2. 对每一行编辑 EnableSpoilage 和 SpoilageIntervalSeconds
3. 点击"应用全部"批量应用
4. 所有参数同时生效

---

## 高级技巧

### 参数对应关系

食物设置表格的参数对应预制体中的数据结构：

```csharp
Mod_Food.FoodModData
├── Food (营养数据)
│   ├── nutrition (Nutrition)
│   │   ├── Carbohydrates / Max_Carbohydrates
│   │   ├── Fat / Max_Fat
│   │   ├── Protein / Max_Protein
│   │   ├── Water / Max_Water
│   │   └── Vitamins / Max_Vitamins
│   ├── Max_EatingProgress (咀嚼次数)
│   ├── nutritionConsumeSpeed (GameValue_float)
│   ├── WaterConsumeSpeedRate
│   └── nutritionConsumeRate
├── EnableSpoilage (是否启用腐败)
├── SpoilageIntervalSeconds (腐败间隔)
└── SpoilageTargetItemID (腐败产物ID)
```

### 保存位置
- 表格配置：`Assets/Editor/FlatWorld/FoodStatTableConfig.asset`
- 行数据类：`Assets/Editor/FlatWorld/FoodStatTableRow.cs`

### 手动调整预制体参数

可以直接在预制体检查器中修改 Mod_Food 组件的参数，然后：
1. 在面板中搜索该食物
2. 点击对应行的"定位"按钮
3. 手动编辑更新

---

## 常见问题

### Q: 为什么我的食物没有出现在表格中？
A: 确保预制体包含 `Mod_Food` 组件，然后点击"扫描 Prefab"按钮重新扫描。

### Q: 我修改了表格数据但预制体没变化？
A: 需要点击该行的"应用"按钮或"应用全部"按钮来应用更改。

### Q: 表格中的数值为 0 是什么意思？
A: 表示预制体中对应的参数未设置或为 0。编辑后点击"应用"即可更新。

### Q: 如何删除表格中的某一行？
A: 暂不支持单行删除。可点击"清空表格"后重新扫描。

### Q: 腐败参数是什么意思？
A: 
- **EnableSpoilage**：是否启用腐败机制
- **SpoilageIntervalSeconds**：多少秒后触发腐败
- **SpoilageTargetItemID**：腐败后变成的物品ID（如腐肉）

### Q: 为什么某些字段编辑后没有保存？
A: 所有编辑都会自动保存到配置文件。确认已按Tab键或鼠标点击其他单元格完成编辑。

---

## 与武器数值表的对比

| 功能 | 武器表 | 食物表 |
|------|-------|-------|
| 扫描对象 | DamageReceiver/Mod_Damage | Mod_Food |
| 主要参数 | MaxHp/Damage/Defense | 营养值/腐败 |
| 应用方式 | 同步 | 同步 |
| UI布局 | 相似设计 | 参考布局 |
| 编辑方式 | 表格编辑 | 表格编辑 |

---

## 文件列表

创建的新文件：
- `FoodStatTableRow.cs` - 食物行数据类
- `FoodStatTableConfig.cs` - 食物配置 ScriptableObject
- `FoodStatTableWindow.cs` - 编辑器窗口主逻辑
- `FoodStatTableWindow_README.md` - 本文档

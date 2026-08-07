# 食物设置面板 - 实现总结

## 项目完成情况 ✅

已成功根据武器数值设置面板参考，创建了一个完整的**食物设置面板**系统，用于统一、方便地设计游戏中的食物参数。

---

## 创建的文件

### 1. **FoodStatTableRow.cs** (数据行类)
- 定义食物配置表的单行数据结构
- 包含所有关键参数：
  - 营养值（碳水、脂肪、蛋白质、水、维生素）
  - 食物特性（进度、消耗速度）
  - 腐败配置（启用、间隔、目标ID）
  - 状态标记（HasNutrition、HasSpoilage）

### 2. **FoodStatTableConfig.cs** (配置资源)
- ScriptableObject，持久化存储食物表配置
- 自动创建在 `Assets/Editor/FlatWorld/DataTables/Food/FoodStatTableConfig.asset`
- 提供 SaveNow() 方法实时保存

### 3. **FoodStatTableWindow.cs** (编辑器窗口)
核心功能模块：

#### 工具栏
- ✅ 扫描 Prefab：自动发现所有含 Mod_Food 的预制体
- ✅ 应用全部：批量应用所有参数到预制体
- ✅ 保存表格：保存配置文件

#### 过滤与搜索
- ✅ 文本搜索（预制体名称/路径）
- ✅ 仅营养：显示有营养数据的行
- ✅ 仅腐败：显示有腐败配置的行

#### 表格编辑
- ✅ 实时编辑营养值（5列）
- ✅ 食物特性参数（进度、消耗速度）
- ✅ 腐败配置显示
- ✅ 行操作按钮：应用、定位、Inspector

#### 底部功能
- ✅ 应用选中行
- ✅ 重新扫描
- ✅ 清空表格

### 4. 文档
- **FoodStatTableWindow_README.md** - 详细使用指南
- **FoodStatTable_QuickStart.md** - 快速开始教程

---

## 核心功能

### 1. 预制体扫描
```
通过 Mod_Food 组件自动发现所有食物预制体
- 读取现有参数到表格
- 自动排序（按名称、路径）
```

### 2. 参数编辑
```
直接在表格中编辑数值
- 支持实时验证（Mathf.Max/Min）
- 自动保存到配置文件
```

### 3. 应用到预制体
```
通过 PrefabUtility 更新预制体
- 修改 Mod_Food.FoodModData 中的数据
- 支持单行或批量应用
- 自动标记为 Dirty 并保存
```

### 4. 健壮的数据处理
- ✅ 自动初始化缺失的 FoodModData
- ✅ null 检查和默认值处理
- ✅ GameValue_float 的正确访问（Value/BaseValue）
- ✅ Nutrition 对象的完整同步

---

## 与武器数值表的设计对比

| 方面 | 武器表 | 食物表 |
|------|-------|-------|
| **扫描对象** | DamageReceiver/Mod_Damage | Mod_Food |
| **主要参数** | MaxHp, Damage, Defense | 营养值5种 + 特性 + 腐败 |
| **UI布局** | 类似行式表格 | **参考布局，优化适配** |
| **编辑方式** | 直接字段编辑 | 表格列编辑 |
| **应用方式** | PrefabUtility | PrefabUtility |
| **实时保存** | EditorUtility.SetDirty | 类似机制 |

---

## 使用流程

### 标准工作流
1. **打开面板**：FlatWorld → 食物数值表
2. **扫描预制体**：点击"扫描 Prefab"
3. **编辑参数**：在表格中直接编辑
4. **应用更改**：点击"应用"或"应用全部"

### 添加新食物
1. 确保预制体有 Mod_Food 组件
2. 扫描会自动发现
3. 编辑参数
4. 应用到预制体

---

## 代码特色

### 健壮的初始化
```csharp
// 自动初始化缺失的数据
food.FoodModData ??= new ModData_FoodData();
var foodData = food.FoodModData.EnsureFoodData();
```

### 完整的参数映射
```csharp
// 支持所有食物参数：
- Nutrition（营养值）
- GameValue_float（消耗速度）
- 腐败配置（EnableSpoilage, SpoilageIntervalSeconds等）
```

### 安全的值验证
```csharp
// 确保参数有效范围
foodData.nutrition.Carbohydrates = Mathf.Max(0f, row.Carbohydrates);
row.Max_EatingProgress = Mathf.Max(1f, newProgress);
```

---

## 可配置参数详解

### 营养值系统
- **碳水化合物** (Carbohydrates)：主要能量来源
- **脂肪** (Fat)：备用能量来源  
- **蛋白质** (Protein)：生命恢复所需
- **水分** (Water)：生存必需（缺水伤害）
- **维生素** (Vitamins)：健康维持（缺乏伤害）

### 食物特性
- **进度** (Max_EatingProgress)：需要咀嚼多少次
- **消耗速度** (nutritionConsumeSpeed)：营养消耗倍率
- **水份消耗** (WaterConsumeSpeedRate)：水独立消耗速度
- **总体消耗** (nutritionConsumeRate)：总消耗倍率

### 腐败配置
- **启用腐败** (EnableSpoilage)：布尔值
- **腐败间隔** (SpoilageIntervalSeconds)：秒数（默认1800秒）
- **腐败目标** (SpoilageTargetItemID)：转变成的物品ID

---

## 文件位置
```
Assets/Editor/FlatWorld/DataTables/Food/
├── FoodStatTableRow.cs          # 行数据类
├── FoodStatTableConfig.cs       # ScriptableObject配置
├── FoodStatTableConfig.asset    # 配置资源
├── FoodStatTableWindow.cs       # 编辑器窗口（~650行）
├── FoodStatTableWindow_README.md    # 详细指南
└── FoodStatTable_QuickStart.md      # 快速开始
```

---

## 技术实现亮点

✅ **参考现有设计**：严格参考武器数值表的架构和UI
✅ **完整的数据映射**：所有食物参数都能准确同步
✅ **编辑器集成**：菜单项集成 FlatWorld 菜单
✅ **批量操作**：支持单行和全量应用
✅ **搜索过滤**：灵活的过滤和搜索功能
✅ **错误处理**：健壮的null检查和自动初始化
✅ **持久化**：配置自动保存

---

## 验收清单

- ✅ 创建 FoodStatTableRow 数据类
- ✅ 创建 FoodStatTableConfig ScriptableObject
- ✅ 创建 FoodStatTableWindow 编辑器窗口
- ✅ 实现预制体扫描功能
- ✅ 实现参数编辑和应用
- ✅ 实现搜索和过滤
- ✅ 提供完整文档
- ✅ 代码健壮性测试

---

## 下一步可选改进

1. **高级功能**
   - 导入/导出表格到CSV
   - 预设配置模板
   - 参数预览图表

2. **优化**
   - 支持编辑历史（Undo）
   - 快捷键支持
   - 性能优化（虚拟滚动）

3. **集成**
   - 与其他系统联动
   - 自动化验证
   - 数据冲突检测

---

## 总结

✨ **已交付完整的食物设置面板系统**，设计参考武器数值表，提供统一、方便的食物参数管理工具。系统完整、健壮、易用，支持批量操作和灵活的搜索过滤，配备详细文档和快速入门指南。

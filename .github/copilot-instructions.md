# FlatWorld 项目编程指南



## 基础脚本
玩家输入控制器 GameController.cs 位于 `Assets/5_Scripts/5-3_GamePlay/Controller/`，负责处理玩家输入和交互 使用新输入系统。
## 库存默认交换对象
Mod_Hand 位于 `Assets/5_Scripts/5-3_GamePlay/Entity/Player/`，是玩家默认的交换对象，玩家的手部插槽

## 代码风格指南

### 基本原则
- ✅ **代码简洁**：避免冗长、重复的逻辑
- ✅ **明确暴露错误**：减少空检查，错误应该立即抛出而不是静默失败
- ✅ **中文注释和交流**：所有注释和变量名应该易于理解
- ✅ **使用#region组织**：提升可读性和可维护性

### #region 规范

**必须遵循**:
```csharp
#region 名称
// #region 下一行添加回车
[内容]
// #endregion 上一行添加回车
#endregion
```

**顶格要求**: `#region` 和 `#endregion` 必须**顶格**，不允许前导空格。

**推荐的#region结构**：按功能进行区分，不再要求固定顺序。

- 不同脚本应根据自身职责设计不同的 `#region` 结构。
- 优先按“业务功能模块”分区，而不是强行套用统一模板。

```

### 字段与属性

- **默认为Public**: 字段如果没有 `private` 关键字，默认应设置为 `public`
- **字段命名**: `PascalCase`（不带下划线前缀），私有字段除外
- **私有字段**: `_camelCase`（带下划线前缀）
- **字段注释**: 所有字段后添加 `//` 双线注释

```csharp
public float eatEnterHungerRate = 0.35f; // 进食触发饥饿阈值
public List<string> edibleTags = new List<string> { "Food" }; // 可食物Tag列表
private float _stateElapsed; // 状态持续时间
```

### 方法注释

- **方法后添加注释**：使用 `///` 标记（带参数列表）或 `//` 标记
- **方法内注释**：为关键步骤添加 `//` 注释


### Debug输出

**禁止**: 仅输出单一字符串  
**推荐**: 输出变量与字符串组合，提供上下文信息

```csharp
// ❌ 不推荐
Debug.Log("Eating");

// ✅ 推荐
Debug.Log($"[Chicken] 开始进食，饥饿度={_hungerRate:F2}, 目标食物={_currentFoodTarget?.itemData.name}");
```

### 成员顺序

在一个类中，按以下顺序组织成员：

1. **嵌套类型** （如枚举、内部类）
2. **常量和配置字段** （带 `[SerializeField]`、`[FoldoutGroup]` 等）
3. **缓存和运行时状态** （私有字段）
4. **属性** （`public` 或 `private` 的属性）
5. **Unity生命周期方法** （Awake, Start, Update, OnDisable, OnDestroy）
6. **公共方法** （按功能分组）
7. **私有方法** （辅助和内部逻辑）

**禁止**: 擅自改变字段或属性的名字，如有建议在注释中说明。

### 性能优化

- 允许（但非必须）进行小幅性能优化
- 优化需要有明确的收益，避免过度设计
- 缓存频繁访问的组件引用

---

## 关键系统说明

### 避免空检查的做法

在明确会出错时，**直接抛出异常而不是空检查**:

```csharp
// ❌ 不推荐 - 静默失败
public void Eat(Item food)
{
    if (food == null) return; // 问题隐藏了
}

// ✅ 推荐 - 明确暴露错误
public void Eat(Item food)
{
    if (food == null) throw new ArgumentNullException(nameof(food));
    // 或使用约束：
    food.GetComponent<Mod_Food>().Consume(1.0f);
}

## 用户偏好

✅ **中文交流**：所有说明、问题和讨论都用中文  
✅ **代码简洁**：避免过度的注释和冗长逻辑  
✅ **#region折叠**：用于提升可读性  
✅ **明确错误**：错误应该立即暴露，不要隐藏  


## 相关文档

<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f2c1
- Active scene:
  - Name: 地球
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender
- Active game object:
  - Name: Chicken(Clone)
  - Tag: Untagged
  - Layer: Default
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
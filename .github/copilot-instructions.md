# FlatWorld 项目编程指南

## 项目概览

**项目名**: FlatWorld  
**引擎**: Unity 2022.3.62f2c1  
**项目类型**: 2D俯视角沙盒游戏  
**主要语言**: C#（支持Lua脚本通过xLua）  

**核心系统**:
- **行为树AI系统** (TheKiwiCoder)：用于NPC和敌人的决策逻辑
- **模组系统** (Module/ModuleData)：管理角色属性（食物、血量、交互等）
- **物品与交互系统**：物品捡起、合成、装备、交互三角互动
- **日夜循环系统**：影响NPC行为和游戏机制
- **存档系统**：使用MemoryPack序列化，支持跨场景数据持久化

**活跃场景标签**: Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore  
**活跃场景图层**: Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReceiver, DamageSender  
**当前编辑对象**: Chicken（小鸡AI）

---

## 目录结构详解

```
Assets/
├── 5_Scripts/
│   └── 5-3_GamePlay/
│       ├── AI/                      # AI逻辑与行为树节点
│       │   ├── AI_Chicken.cs        # 小鸡AI（状态机+行为管理）
│       │   └── [其他NPC文件]
│       ├── Item/                    # 物品和交互系统
│       │   └── Mod_* 系列           # 物品功能模块
│       ├── Controller/              # 玩家控制与交互发送
│       └── [其他游戏逻辑]
├── TheKiwiCoder/
│   └── BehaviourTree/               # 行为树编辑器与运行时
│       ├── Scripts/
│       │   ├── Runtime/             # 行为树核心系统
│       │   ├── Node/                # 节点类型（组合/装饰/动作）
│       │   ├── Actions/             # 具体的动作节点实现
│       │   └── Editor/              # 编辑器UI与工具
│       └── UIBuilder/               # 编辑器界面
├── Plugins/
│   ├── Sirenix/                     # OdinInspector（属性绘制）
│   ├── Pathfinding/                 # A*寻路
│   ├── xLua/                        # Lua脚本支持
│   ├── DOTween/                     # 动画库
│   └── [其他第三方插件]
├── Editor/
│   └── FlatWorld/
│       ├── WindowsCompileNotifier.cs # 编译完成Windows通知
│       └── TodoListWindow.cs         # 待办事项编辑器窗口
├── 6_Art/                           # 美术资源
├── Saves/                           # 存档数据
└── Animations/                      # 动画控制器
```

---

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

**推荐的#region结构**（按此顺序）：

```csharp
public class MyClass : MonoBehaviour
{
#region 序列化字段/配置

    [SerializeField]
    public int MyField; // 说明

#endregion

#region 缓存和运行时状态

    private int _runtimeValue;
    private bool _isInitialized;

#endregion

#region 属性

    public int MyProperty => _runtimeValue;

#endregion

#region Unity生命周期

    private void Awake() { }
    private void Start() { }
    private void Update() { }

#endregion

#region 核心功能

    public void DoSomething() { }

#endregion

#region 辅助方法

    private void Helper() { }

#endregion
}
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

```csharp
/// <summary>
/// 检查是否可以进食
/// </summary>
/// <param name="hungerRate">当前饥饿比例 0-1</param>
/// <returns>是否满足进食条件</returns>
public bool CanEat(float hungerRate)
{
    // 检查饥饿水平是否低于阈值
    if (hungerRate < eatEnterHungerRate)
    {
        // 满足进食条件
        return true;
    }
    return false;
}
```

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

### 1. 模组系统 (Module Pattern)

每个游戏对象（Item）可以拥有多个 `Module`，用于管理特定的功能模块。

```csharp
public abstract class Module : MonoBehaviour
{
    public abstract ModuleData _Data { get; set; }
    public virtual void Load() { }
    public virtual void Save() { }
    public virtual void ModUpdate(float deltaTime) { }
}
```

**常见模块**:
- `Mod_Food`: 管理食物属性（腹饥度、水分、营养）
- `DamageReceiver`: 管理血量和伤害
- `Mod_ItemDetector`: 检测范围内的物品
- `Mod_AnimatorController`: 动画状态管理
- `Mod_InteractSender`: 交互发起方

### 2. 行为树AI系统

使用 `TheKiwiCoder` 的行为树框架。节点分为：

- **CompositeNode**（组合节点）：Selector（选择器）、Sequencer（顺序器）
- **DecoratorNode**（装饰节点）：条件、反转等
- **ActionNode**（动作节点）：具体的AI行为，如移动、进食、逃跑

**示例** (`Hunting.cs`):
```csharp
[NodeMenu("ActionNode/行动/狩猎")]
public class Hunting : ActionNode
{
    public List<string> ItemType = new List<string>();

    protected override State OnUpdate()
    {
        Item targetItem = FindTargetItem();
        if (targetItem == null) return State.Failure;
        context.mover.TargetPosition = targetItem.transform.position;
        return State.Success;
    }
}
```

### 3. 存档系统

使用 `MemoryPack` 序列化和 `Module._Data` 模式：

```csharp
[Serializable]
[MemoryPackable]
public partial class AI_ChickenSaveData
{
    public ChickenState State = ChickenState.Idle;
    public float EggTimer = 0f;
    public float Fatigue01 = 0f;
}
```

**保存/读取流程**:
- `Module.Load()`: 从 SaveData 读取
- `Module.Save()`: 写入 SaveData
- `GameManager.SaveGame()`: 序列化所有模块数据

### 4. 物品与交互

- **IInteractable**: 交互接口（含 `OnInteractStart` 和 `OnInteractCancel`）
- **Mod_InteractSender**: 玩家发起交互的模块
- **Item**: 游戏世界中的物品基类

---

## 常见编码模式

### 状态机 (AI_Chicken例)

```csharp
public enum ChickenState { Idle, Move, Forage, Eat, Sleep, Mate, LayEgg, Flee }

private ChickenState _currentState = ChickenState.Idle;

private void UpdateState(float deltaTime)
{
    // 转换逻辑
    switch (_currentState)
    {
        case ChickenState.Idle:
            HandleIdle();
            break;
        case ChickenState.Eat:
            HandleEat();
            break;
        // ...
    }
}
```

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
```

### 缓存组件引用

```csharp
#region CachedModules

[SerializeField, ReadOnly]
private Mover_AI _mover;

[SerializeField, ReadOnly]
private Mod_Food _food;

#endregion

private void Awake()
{
    _mover = GetComponent<Mover_AI>();
    _food = GetComponent<Mod_Food>();
}
```

---

## 工作流建议

### 添加新的AI行为
1. 在 `Assets/5_Scripts/5-3_GamePlay/AI/` 创建新的 ActionNode 类
2. 使用 `[NodeMenu("ActionNode/分类/名称")]` 标记
3. 在行为树编辑器中拖入新节点
4. 在节点参数中配置行为参数

### 修改现有代码
1. **检查#region结构**：是否符合规范
2. **补充缺失注释**：尤其是方法和重要字段
3. **调整成员顺序**：按推荐顺序重新组织
4. **优化Debug输出**：包含变量上下文
5. **移除无用代码**：清理临时变量和测试代码

### 代码复查检查清单
- [ ] 所有方法都有注释（`///` 或 `//`）？
- [ ] Debug.Log 包含上下文信息？
- [ ] #region 顶格且格式正确？
- [ ] 字段后有双线注释？
- [ ] 没有空检查隐藏的错误？
- [ ] 成员顺序符合规范？

---

## API参考

### 常用OdinInspector属性

```csharp
[FoldoutGroup("分组名")]      // 折叠组
[PropertyOrder(10)]           // 属性顺序
[LabelText("标签")]          // 自定义标签
[SuffixLabel("单位", true)]  // 后缀标签
[Range(0f, 1f)]              // 范围约束
[ReadOnly]                   // 只读（编辑器）
[SerializeField]             // 序列化字段
```

### 常用模块查询

```csharp
// 获取物品上的特定模块
var food = item.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);

// 获取物品检测器中的物品
var itemsInRange = detector.CurrentItemsInArea;

// 检查物品标签
if (item.itemData.Tags.ContainsTag("Food")) { }
```

---

## 用户偏好

✅ **中文交流**：所有说明、问题和讨论都用中文  
✅ **代码简洁**：避免过度的注释和冗长逻辑  
✅ **#region折叠**：用于提升可读性  
✅ **明确错误**：错误应该立即暴露，不要隐藏  

---

## 相关文档

- Unity 2022.3 LTS 文档: https://docs.unity3d.com/2022.3/Documentation/
- TheKiwiCoder 行为树: 项目内 `Assets/TheKiwiCoder/BehaviourTree/`
- OdinInspector 文档: https://odininspector.com/
- MemoryPack: 二进制序列化库
<!-- UNITY CODE ASSIST INSTRUCTIONS START -->
- Project name: FlatWorld
- Unity version: Unity 2022.3.62f2c1
- Active scene:
  - Tags:
    - Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController, Entity, MapCore
  - Layers:
    - Default, TransparentFX, Ignore Raycast, Water, UI, Collider, DamageReciver, DamageSender
- Active game object:
  - Name: Chicken
  - Tag: Untagged
  - Layer: Default
<!-- UNITY CODE ASSIST INSTRUCTIONS END -->
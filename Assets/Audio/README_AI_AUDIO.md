# FlatWorld AI Audio 工作流

## 新增音效

1. 把生成的 WAV 放入 `Assets/Audio/Generated/`。
2. 使用文件名：`事件ID__变体.wav`。
3. 在 Unity 执行 `Tools > FlatWorld > Audio > Rebuild AI Audio Catalog`。

示例：

```text
ui.click__01.wav
ui.click__02.wav
item.axe.swing__01.wav
item.axe.hit.wood__01.wav
ambient.forest.day__01.wav
```

同一事件 ID 的多个文件会自动成为随机变体。前缀会自动决定声道：

- `ui.*` → UI
- `music.*` → Music
- `ambient.*` → Ambient
- `voice.*` → Voice
- 其他 → Sfx

## 业务调用

全局或 UI：

```csharp
AudioService.Instance.Play("ui.click");
AudioService.Instance.PlayAt("world.tree.fall", transform.position);
AudioService.Instance.PlayAttached("item.torch.loop", transform);
```

Item/Module：

1. 在 Item 子物体添加 `Mod_AudioEmitter`。
2. 添加映射，例如模块事件 `hit` → Cue ID `item.axe.hit.wood`。
3. 其他 Module 调用：

```csharp
item.itemMods
    .GetMod_ByID<Mod_AudioEmitter>(Mod_AudioEmitter.ModuleId)
    ?.PlayEvent("hit");
```

需要跨存档恢复的循环声勾选 `Persist While Saved`；一次性音效不要保存。

## 战斗音效分层

一次武器命中由两层声音组成：

- `combat.weapon.*.attack`：武器挥动/攻击动作，只在攻击开始时播放。
- `combat.impact.*`：确认造成伤害后，根据受击对象材质播放。

常见组合可以覆盖材质层，例如：

- `combat.impact.knife.foliage`
- `combat.impact.knife.stone`
- `combat.impact.axe.wood`
- `combat.impact.pickaxe.stone`

武器在 `Mod_Damage` 中可指定分类、动作 Cue 和材质覆盖；受击对象在
`DamageReceiver` 中可指定材质或对象专属 Cue。保持 `Auto` 时会根据
Item 的 ID、名称和标签自动识别，新增预制体通常无需改伤害代码。

## 约定

- 业务代码只引用稳定事件 ID，不直接引用 WAV 或 `AudioClip`。
- ID 使用小写点号层级：`领域.对象.动作.材质`。
- 新变体只新增文件，不修改业务代码。
- 全局音量属于用户设备设置，保存在 `PlayerPrefs`；Item 循环播放状态才进入 Module 存档。

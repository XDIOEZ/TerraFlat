# FlatWorld AI Audio 工作流

## UI 候选素材库

- 手工制作或 LMMS 导出的 UI 候选音效放在 `Assets/Audio/UI/LMMS_Clicks/`，按风格分文件夹，使用 `UI_风格_用途_声音特征_变体编号.wav` 详细命名。
- 候选素材在 Project 中直接试听；选定风格后再绑定现有 AudioCue。此目录不参与下面的自动 Catalog 扫描，避免不同风格被混成同一事件的随机变体。
- 原始工程与离线试听页位于 `SoundDesign/UI_Click_LMMS/`；目录内说明文件提供完整音效名称索引。
- 背包物品取放素材放在 `Assets/Audio/UI/InventoryItems_LMMS/`，按拿起/背包/箱子及材质分目录，再独立区分重量、体积；`InventoryAudioLibrary.json` 提供 72 个 WAV 的路径、GUID 和分类索引。工程与可拖拽试听页位于 `SoundDesign/Inventory_Items_LMMS/`。

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
- `weather.*` → Ambient
- `voice.*` → Voice
- 其他 → Sfx

## 天气音效

- 雨声 `weather.rain.loop` 已替换为 LMMS MCP 合成的 31.7 秒立体声循环，保留原 WAV 与 Cue 的 GUID。
- 风声 `weather.wind.loop` 为 43.7 秒阵风循环；近雷 `weather.thunder` 和远雷 `weather.thunder.far` 各三条真实录音变体，均经 LMMS MCP 编排导出。
- 雨风由本地天气强度控制音量；雷声从已发布的真实落点空间发声，64 格内选近雷，64–192 格选远雷，传播延迟为环绕最短距离 / 64。两组 Cue 均以线性衰减在 192 格处静音，玩家移动时更新最近副本方位。均走 Ambient 音量设置，退出或抑制天气的维度会停止。
- 原始可编辑工程、制作说明与母带裁切脚本在 `SoundDesign/Weather_LMMS/`。雨声不再属于 `Generate Default AI SFX` 的生成条目，避免覆盖新素材。
- 真实雷声来自 Freesound 的 CC0 录音；作者、来源、裁切范围、公共预览散列与处理参数记录在 `SoundDesign/Weather_LMMS/sources.json` 和 `THIRD_PARTY_NOTICES.md`。

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
`Mod_DamageReceiver` 中可指定材质或对象专属 Cue。保持 `Auto` 时会根据
Item 的 ID、名称和标签自动识别，新增预制体通常无需改伤害代码。

## 约定

- 业务代码只引用稳定事件 ID，不直接引用 WAV 或 `AudioClip`。
- ID 使用小写点号层级：`领域.对象.动作.材质`。
- 新变体只新增文件，不修改业务代码。
- 全局音量属于用户设备设置，保存在 `PlayerPrefs`；Item 循环播放状态才进入 Module 存档。

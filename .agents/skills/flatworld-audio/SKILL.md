---
name: flatworld-audio
description: "Use when: 定位或修改 FlatWorld 的音频服务、AudioCue、声源池、战斗/实体/UI 音效、音量、淡入淡出或音频资源。关键词：AudioService、AudioCatalog、AudioCue、Mod_AudioEmitter、CombatAudioRouter。"
---

# FlatWorld 音频

## 入口

- 核心：`Assets/5_Scripts/5-6_Audio/Runtime/{AudioService,AudioCatalog,AudioCue,AudioRuntimeConfig,AudioTypes}.cs`
- 实体/战斗：`Assets/5_Scripts/5-3_GamePlay/Presentation/Audio/Mod_AudioEmitter.cs`、`Entities/Combat/CombatAudioRouter.cs`
- UI：`Assets/5_Scripts/5-5_UI/Audio/`
- 资源与工具：`Assets/Resources/Audio/`、`Assets/5_Scripts/5-6_Audio/Editor/`
- LMMS UI 候选素材：`Assets/Audio/UI/LMMS_Clicks/`；原始工程与离线试听：`SoundDesign/UI_Click_LMMS/`
- 物品取放素材：`Assets/Audio/UI/InventoryItems_LMMS/`；工程与交互试听：`SoundDesign/Inventory_Items_LMMS/`，JSON 索引按操作/材质/重量/体积选音。

## 不变量

- 业务系统通过稳定 Cue ID 发声，不自行管理临时 `AudioSource`。
- UI 候选音效按风格分目录，用 `UI_风格_用途_声音特征_变体编号.wav` 命名；不放入自动 Catalog 扫描的 `Assets/Audio/Generated/`，选定风格后再绑定 Cue，避免三套风格被混成随机变体。再导出用 `build_preview.py --unity .` 同步 WAV 并保留已有 GUID。
- 物品取放候选按 `物品交互_材质_操作_重量_体积_变体.wav` 命名；重量 kg 与体积 L 独立划档，按真正转移数量计算。背包/箱子用不同接触音色，拿起只在点击取物或开始拖拽时播放一次，放入音只跟随成功事务；候选索引不等于运行时已接入。
- `AudioService` 负责跨场景生命周期、池化、并发、优先级、淡入淡出与音量。
- 世界音效使用 `At/Attached`、完整空间混音和线性衰减，`MinDistance` 内正常音量、`MaxDistance` 处静音；不要混入不衰减的 2D 分量。`Mod_Cam` 绑定本机玩家与已有 `AudioListener`，服务将声源投影到监听器 XY 平面，排除相机高度、前探和跟随偏移；退出时按目标身份解绑，避免旧玩家清掉新玩家的绑定。范围外短音不占池或冷却，循环音保留句柄以便靠近后恢复；Catalog Builder 重建也必须保持这些默认值。
- Catalog/Config 移动时同步 Resources 加载常量；循环 Cue 必须有明确停止和回收路径。
- AI 生成音效以事件 ID 结尾的 `.loop` 作为循环语义；`player.*` 属于带空间衰减的世界 SFX，角色循环音效必须绑定角色 Transform 并在状态失效时停止。
- 战斗音效联动 `flatworld-combat`，UI 音效联动 `flatworld-ui`；角色台词和气泡属于 `flatworld-dialogue`。
- 音量和静音设置由 `AudioService` 直接实现 `ISettingsProvider`，通过 `SettingsProviderRegistry` 暴露 Slider/Toggle；`FlatWorld.Audio` 只依赖 `Data` 中的设置契约，不依赖 UI 或 GamePlay。
- 音量页六路滑块使用公共 `UI_SliderControl` 嵌套实例；保留 `MasterVolume/MusicVolume/SfxVolume/UIVolume/AmbientVolume/VoiceVolume` 及对应 `_数值` 节点名，外观封装不能改变 Provider 绑定契约。回归测试临时写入音量后必须恢复玩家原值。

## 验证

- 静态检查 Cue 是否可解析、总线/循环配置是否正确、停止后声源是否回池；听感仅作最终人工确认。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。

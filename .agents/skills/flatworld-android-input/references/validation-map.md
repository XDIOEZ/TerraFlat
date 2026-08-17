# Android 输入验证导航图

## 默认验证门槛

1. 检查目标 diff 和 `git diff --check`，确认没有覆盖用户已有修改、空白错误或生成文件被手改。
2. 等待 Unity 完成本次脚本导入，检查 Console 为零新增编译错误和相关异常。
3. 执行菜单 `FlatWorld/Validation/Compile Android Player Scripts`，确认 Android Player 脚本编译成功；入口为 `Assets/Editor/FlatWorld/Automation/AndroidScriptCompileValidator.cs`。
4. 涉及 HUD、Prefab、安全区或射线时执行菜单 `FlatWorld/Validation/Validate Mobile Controls Layout`；入口为 `Assets/Editor/FlatWorld/Automation/MobileControlsLayoutValidator.cs`。
5. 未经用户明确要求，不调用 Unity Test Runner、`run_tests`、测试脚本或 Golden Path。

## 已有自动化入口

| 覆盖 | 文件/操作 |
|---|---|
| 虚拟设备、方向、攻击按住/松开、输入锁和设备切换 | `Assets/GameTest/PlayerInteraction/MobileControlsInputTests.cs` |
| 真实单人移动端主路径 | `player.mobile-controls` |
| Golden Path 场景实现 | `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.MobileControls.cs` |
| 操作注册 | `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathOperations.cs` |
| 测试统一入口 | `.agents/skills/flatworld-test-automation/scripts/run_unity_tests.py` |

只有用户明确要求运行时，才按 `flatworld-test-automation` 与 `flatworld-golden-path` 的流程执行；不要绕过其清理、结果和 Console 检查。

## 定向人工验收

- 同时按住左摇杆和右侧指向，确认移动与朝向互不抢占。
- 按下攻击摇杆但不拖出死区，确认立即沿普通朝向攻击；拖动后实时改向并保留为普通朝向；松开可靠停止，玩家移动时准线仍保持相对位置跟随。
- 分别点击交互、使用、建造和功能按钮，确认不产生攻击事件、不误拆建筑且不改变普通朝向。
- 同时操作移动、指向、攻击和按钮，确认每个触点只控制自己的控件。
- 按住键盘 Shift 奔跑时同时拖动普通/攻击摇杆并按虚拟按钮，确认键盘只改变奔跑，摇杆方向和按钮状态不中断。
- 在 Unity Device Simulator 中按住 Shift，再用鼠标左键点击/拖动虚拟摇杆和虚拟按钮，确认模拟器 Touchscreen UI 射线仍能命中且不进入手柄虚拟光标模式。
- 在攻击/移动按住时打开模态面板、锁定输入、切后台、失焦或旋转横屏方向，确认没有残留移动或攻击。
- 检查 16:9、20:9、左右刘海与两个横屏方向，确认安全区、快捷栏、摇杆和按钮不重叠。
- 用键鼠与手柄回归移动、攻击、交互、UI 焦点和虚拟光标，确认移动端设备没有触发手柄模式。

## Android 配置核对

- 包名：`com.icetilapia.flatworld`。
- 方向：仅横屏左/右。
- 后端/架构：IL2CPP、ARM64。
- 最低 API：22；目标 SDK 自动。
- 运行目标：High、`Application.targetFrameRate = 60`、`QualitySettings.vSyncCount = 0`。
- 不把未生成 APK 的编辑器结果表述为真机多点触控或持续 60 FPS 已验证。

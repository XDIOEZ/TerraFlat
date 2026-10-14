# UI与设置

快速入口：`rules.lua`；操作：`ui_settings/mobile_size、navigation_policy、panel_lifecycle`。

已编写的规则覆盖：正式 UIUserSettings 控件倍率净化；停用独立 Selectable 的手柄焦点策略（设置滑块、显示条、滚动条、快捷栏）；停用独立 BasePanel 的关闭初始化、重复开关事件幂等、视觉透明度和交互状态。临时排序计数在结束时恢复，不注册测试面板到 UIManager。

关键流程待补：真实面板打开/关闭/销毁、模态输入锁、设置保存/放弃、SafeArea和移动布局、手柄顶层焦点、语言变化后的布局。独立控件查询不改当前 UI 或 PlayerPrefs。

本轮仅创建静态测试代码，未运行。后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

# 生物摇摆模式静态检查

运行 `python Tools/Tests/ActorSway/verify.py`，不会暂停、刷新或编译正在运行的 Unity。

检查正式 Actor Manifest、继承后的动画能力 ID/启用状态/SingleSprite 模式、初始 Sprite 的文件和切片、Addressables 构建地址，以及初始化代码没有回到 Animator 首帧采样。雪豹和共用狼外壳的僵尸另查物种首帧，新增正式物种自动纳入。

这是静态检查，不替代 Unity 编译、资源加载、真实出生、对象池复用或画面验收。正式加载器与 Sprite API 的 Lua 回归在 `ai/single_sprite_catalog`；单图动作和生命周期在 `item_runtime/single_sprite_animation`。Unity 未处于用户 PlayMode 时运行 `python Tools/GameplayTesting/run_suite.py --systems ai item_runtime`。

2026-10-24：静态检查12种全部通过，Unity只读查询12种Sprite切片均可加载。提交基线加本轮修改的GamePlay与Assembly-CSharp-Editor隔离编译通过；整工作区试编译遇到另一项弹反输入接口缺失，未修改或提交该功能。未暂停用户PlayMode，也未执行Lua或真实出生回归。

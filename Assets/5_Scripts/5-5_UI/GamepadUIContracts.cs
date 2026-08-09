using UnityEngine;

/// <summary>
/// 手柄次要操作契约。实现者应在当前选中的 UI 对象上打开上下文菜单或执行次要动作。
/// </summary>
public interface IGamepadContextActionHandler
{
    bool HandleGamepadContextAction();
}

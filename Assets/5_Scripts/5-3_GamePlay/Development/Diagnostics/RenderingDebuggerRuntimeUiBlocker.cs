using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 禁止 URP Rendering Debugger 的运行时调试菜单，避免误触调试快捷键后再次显示左侧 Display Stats 面板。
/// 仅在 Unity 编辑器或 Development Build 中生效，不影响正式发布包的运行逻辑。
/// </summary>
internal static class RenderingDebuggerRuntimeUiBlocker
{
#region 生命周期

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>在场景加载前关闭运行时调试菜单及其输入监听。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void DisableRuntimeDebugger()
    {
        DebugManager debugManager = DebugManager.instance;
        debugManager.enableRuntimeUI = false;
        debugManager.displayRuntimeUI = false;
        debugManager.displayPersistentRuntimeUI = false;
    }
#endif

#endregion
}

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>安全区纯数据回归与显式干净编译入口；不打开或修改玩家存档，也不自动修改任何 Prefab。</summary>
public static class SafeAreaRegressionDiagnostics
{
    #region 安全区回归

    [MenuItem("FlatWorld/诊断/验证安全区屏幕切换")]
    public static void Validate()
    {
        Rect full = new Rect(0, 0, 1240, 888);
        Rect notch = new Rect(48, 24, 1144, 864);
        Check(SafeAreaRectController.ResolveSafeArea(full, 1240, 888) == full, "完整屏幕保持不变");
        Check(SafeAreaRectController.ResolveSafeArea(notch, 1240, 888) == notch, "有效刘海与底部边距保持不变");
        Check(SafeAreaRectController.ResolveSafeArea(new Rect(140, 63, 2495, 1221), 1240, 888) == full,
            "模拟器旧分辨率矩形不产生屏外锚点");
        Check(SafeAreaRectController.ResolveSafeArea(new Rect(0, 0, 888, 1240), 1240, 888) == full,
            "旋转过渡帧安全区不越界");
        Check(SafeAreaRectController.ResolveSafeArea(new Rect(float.NaN, 0, 1, 1), 1240, 888) == full,
            "NaN 安全区被拒绝");
        Check(SafeAreaRectController.ResolveSafeArea(default, 1240, 888) == full, "空安全区不会隐藏 UI");
        Check(SafeAreaRectController.ResolveSafeArea(new Rect(-1, 0, 1241, 888), 1240, 888) == full,
            "负向越界安全区被拒绝");
        Check(SafeAreaRectController.ResolveSafeArea(new Rect(-0.2f, 0, 1240.4f, 888), 1240, 888) == full,
            "半像素误差归一到屏幕边界");
        Rect hotbar = new Rect(-445, 0, 890, 104);
        Matrix4x4 below = Matrix4x4.TRS(new Vector3(0, -35), Quaternion.identity, Vector3.one * 0.68f);
        Check(Mathf.Approximately(SafeAreaRectController.ResolveBottomCorrection(hotbar, below, 0), 35),
            "负间距只补齐越界部分，兼容快捷栏缩放");
        Matrix4x4 above = Matrix4x4.Translate(new Vector3(0, 28));
        Check(SafeAreaRectController.ResolveBottomCorrection(hotbar, above, 0) == 0,
            "有效正间距不被修改");
        Check(SafeAreaRectController.ResolveBottomCorrection(hotbar, Matrix4x4.identity, 12) == 12,
            "系统手势底部边距继续生效");
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/SafeAreaRegression.txt", "PASS 11 assertions\n");
        Debug.Log("[SafeAreaRegression] PASS 11 assertions");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    #endregion

    #region 编译注册恢复

    /// <summary>由 Unity 自己重建 source-gen 与程序集注册，禁止手工替换 ScriptAssemblies 或靠禁用 Burst 掩盖异常。</summary>
    [MenuItem("FlatWorld/诊断/请求干净脚本编译")]
    public static void RequestCleanCompilation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先停止 Play，再执行干净编译。");
        CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
    }

    #endregion
}

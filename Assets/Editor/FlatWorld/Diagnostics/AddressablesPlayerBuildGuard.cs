using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.Build;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>资源构建失败时终止 Player 打包，避免生成缺少语言包和游戏资源的安装包。</summary>
    internal static class AddressablesPlayerBuildGuard
    {
        #region 资源构建结果

        [InitializeOnLoadMethod]
        private static void Register()
        {
            BuildScript.buildCompleted -= RejectFailedPlayerContent;
            BuildScript.buildCompleted += RejectFailedPlayerContent;
        }

        private static void RejectFailedPlayerContent(AddressableAssetBuildResult result)
        {
            if (!(result is AddressablesPlayerBuildResult) || string.IsNullOrEmpty(result.Error))
                return;

            // Addressables 1.22 默认仅记录错误，必须显式阻止缺资源的 Player 继续打包。
            throw new BuildFailedException(
                $"Addressables 资源构建失败，已终止打包，避免生成缺资源的安装包。请先解决资源构建错误：{result.Error}");
        }

        #endregion
    }
}

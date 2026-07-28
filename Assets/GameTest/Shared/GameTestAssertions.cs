using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Shared
{
    /// <summary>
    /// 系统冒烟测试共享断言：校验关键脚本、资源目录与资产入口仍然可被 Unity 解析。
    /// </summary>
    public static class GameTestAssertions
    {
        #region 脚本与类型

        public static void AssertScriptType(string scriptPath, string expectedTypeName)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            Assert.That(script, Is.Not.Null, $"缺少关键脚本：{scriptPath}");

            Type type = script.GetClass();
            Assert.That(type, Is.Not.Null, $"关键脚本未解析出类型，可能存在编译或类名问题：{scriptPath}");
            Assert.That(type.Name, Is.EqualTo(expectedTypeName), $"关键脚本类型与约定不一致：{scriptPath}");
        }

        #endregion

        #region 资源与目录

        public static void AssertAssetExists(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            Assert.That(asset, Is.Not.Null, $"缺少关键资产：{assetPath}");
        }

        public static void AssertFolderExists(string folderPath)
        {
            Assert.That(AssetDatabase.IsValidFolder(folderPath), Is.True, $"缺少关键目录：{folderPath}");
        }

        public static void AssertFolderContainsAsset(string folderPath, string filter)
        {
            AssertFolderExists(folderPath);
            string[] guids = AssetDatabase.FindAssets(filter, new[] { folderPath });
            Assert.That(guids, Is.Not.Empty, $"目录中没有符合 '{filter}' 的资产：{folderPath}");
        }

        #endregion
    }
}

using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace FlatWorld.Tests.EditMode
{
    /// <summary>编辑态先校验正式测试清单、文件和场景，失败时禁止进入 PlayMode。</summary>
    public sealed class GameplayManifestEditModeTests
    {
        #region 清单及资源约束
        [Test]
        public void Suite_UsesProductionParser()
        {
            string selected = GameplayTestSelection.ResolveSuitePath(GameplayTestSelection.QuickSuitePath);
            string path = GameplayTestSelection.GetAbsoluteSuitePath(selected);
            Assert.That(File.Exists(path), Is.True, "待执行清单不存在：" + path);
            GameplayTestSuite suite = GameplayTestSuite.Parse(File.ReadAllText(path));
            Assert.That(suite.Cases.Exists(test => test.Enabled), Is.True);
        }

        [Test]
        public void Suite_AllEnabledLuaScriptsExistAndCompile()
        {
            string selected = GameplayTestSelection.ResolveSuitePath(GameplayTestSelection.QuickSuitePath);
            GameplayTestSuite suite = GameplayTestSuite.Parse(
                File.ReadAllText(GameplayTestSelection.GetAbsoluteSuitePath(selected)));
            foreach (GameplayTestCase entry in suite.Cases)
            {
                if (!entry.Enabled) continue;
                string path = Path.Combine(UnityEngine.Application.streamingAssetsPath,
                    entry.LuaPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.Exists(path), Is.True, entry.Id + " 找不到 Lua 脚本");
                Assert.That(new FileInfo(path).Length, Is.InRange(1L, 1024L * 1024L),
                    entry.Id + " 的 Lua 文件为空或超出测试执行器的 1MB 限制");
                string source = File.ReadAllText(path);
                Assert.That(string.IsNullOrWhiteSpace(source), Is.False,
                    entry.Id + " 的 Lua 内容为空");
                Assert.DoesNotThrow(() => GameplayTestLuaRunner.ValidateScript(source, "@" + entry.LuaPath),
                    entry.Id + " 的 Lua 预编译失败，不应进入 PlayMode。");
            }
        }

        [Test]
        public void Scene_ContainsRealGameplayTestRunner()
        {
            const string path = "Assets/3_Scenes/Development/GameplayTestScene.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
            string yaml = File.ReadAllText(path);
            Assert.That(yaml, Does.Contain("m_Name: GameplayTestRunner"));
            Assert.That(yaml, Does.Contain("testSuitePath:"));
        }

        [Test]
        public void Suite_RejectsInvalidPathsAndUnknownFields()
        {
            Assert.Throws<InvalidDataException>(() =>
                GameplayTestSelection.NormalizeSuitePath("GameplayTests/../GameStartScene.unity"));
            Assert.Throws<InvalidDataException>(() =>
                GameplayTestSuite.Parse("{\"schemaVersion\":1,\"unknown\":true,\"cases\":[]}"));
        }
        #endregion
    }
}

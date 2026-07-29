using System.IO;
using System.Linq;
using FlatWorld.Networking;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Networking
{
    /// <summary>联机基础冒烟测试：保护启动器、网络管理器、玩家 Prefab 与测试场景。</summary>
    public sealed class NetworkingSmokeTests
    {
        [Test]
        [Category("Networking.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameBootstrap.cs", "NetworkGameBootstrap");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-4_Networking/Gameplay/FlatWorldGameNetworkManager.cs", "FlatWorldGameNetworkManager");
            GameTestAssertions.AssertAssetExists("Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab");
            GameTestAssertions.AssertAssetExists("Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NetworkMode.prefab");
            GameTestAssertions.AssertAssetExists("Assets/3_Scenes/NetworkTest.unity");
        }

        [Test]
        [Category("Networking.Smoke")]
        public void NetworkPanelUsesGameResPrefabInsteadOfRuntimeVisualTree()
        {
            const string scriptPath = "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkModePanelView.cs";
            string source = File.ReadAllText(scriptPath);

            Assert.That(source, Does.Contain("GameRes.Instance.InstantiatePrefab(NetworkPanelKey"));
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("CreateImage("));
            Assert.That(source, Does.Not.Contain("CreateText("));
        }

        [Test]
        [Category("Networking.Smoke")]
        public void NetworkPlayerNameLabelIsAuthoredInPrefab()
        {
            const string prefabPath = "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少联机玩家 Prefab：{prefabPath}");

            Transform label = prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "玩家名称");
            Assert.That(label, Is.Not.Null, "联机玩家名称必须在 Prefab 中可视化编辑。");
            Assert.That(label.GetComponent("TextMeshPro"), Is.Not.Null, "玩家名称节点缺少 TextMeshPro。 ");

            string source = File.ReadAllText(
                "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWorldPlayer.cs");
            Assert.That(source, Does.Not.Contain("new GameObject(\"玩家名称\")"));
            Assert.That(source, Does.Not.Contain("AddComponent<TextMeshPro>"));
        }

        [TestCase("127.0.0.1", 7777, "127.0.0.1", 7777)]
        [TestCase("kcp://tunnel.example.com", 24567, "tunnel.example.com", 24567)]
        [TestCase("tunnel.example.com:24567", 7777, "tunnel.example.com", 24567)]
        [TestCase("udp://tunnel.example.com:24567", 7777, "tunnel.example.com", 24567)]
        [TestCase("kcp://[2001:db8::1]:24567", 7777, "2001:db8::1", 24567)]
        [Category("Networking.Smoke")]
        public void TunnelEndpointUsesEmbeddedPort(
            string value,
            ushort fallbackPort,
            string expectedHost,
            ushort expectedPort)
        {
            bool parsed = NetworkConnectionEndpoint.TryParse(
                value,
                fallbackPort,
                out NetworkConnectionEndpoint endpoint,
                out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(endpoint.Host, Is.EqualTo(expectedHost));
            Assert.That(endpoint.Port, Is.EqualTo(expectedPort));
        }

        [TestCase("tcp://tunnel.example.com:24567")]
        [TestCase("http://tunnel.example.com:24567")]
        [TestCase("tunnel.example.com:70000")]
        [TestCase("tunnel.example.com/path")]
        [Category("Networking.Smoke")]
        public void TunnelEndpointRejectsUnsupportedValues(string value)
        {
            bool parsed = NetworkConnectionEndpoint.TryParse(
                value,
                7777,
                out _,
                out string error);

            Assert.That(parsed, Is.False);
            Assert.That(error, Is.Not.Empty);
        }
    }
}

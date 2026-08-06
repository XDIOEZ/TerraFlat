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
            GameTestAssertions.AssertScriptType(
                "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWeatherStateCoordinator.cs",
                "NetworkWeatherStateCoordinator");
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
        public void GameplayAndMapGenerationProtocolsUseNewVersionsAndRejectOldPeers()
        {
            System.Reflection.Assembly gameplayAssembly = System.Reflection.Assembly.Load(
                "FlatWorld.Networking.Gameplay");
            System.Type gameplayProtocol = gameplayAssembly.GetType(
                "FlatWorld.Networking.Gameplay.NetworkGameplayProtocol",
                throwOnError: true);
            System.Type mapProtocol = gameplayAssembly.GetType(
                "FlatWorld.Networking.Gameplay.NetworkMapGenerationProtocol",
                throwOnError: true);

            int gameplayVersion = (int)gameplayProtocol.GetField("CurrentVersion").GetRawConstantValue();
            int mapVersion = (int)mapProtocol.GetField("CurrentVersion").GetRawConstantValue();
            Assert.That(gameplayVersion, Is.EqualTo(10));
            Assert.That(mapVersion, Is.EqualTo(4));

            System.Reflection.MethodInfo calculateHash = mapProtocol.GetMethod(
                "CalculateSettingsHash",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(calculateHash, Is.Not.Null);
            object[] settings = { 12345, 2048, 0.01f, true, 100, 100, WorldTopologyMode.Wrapped };
            uint first = (uint)calculateHash.Invoke(null, settings);
            uint repeated = (uint)calculateHash.Invoke(null, settings);
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(
                (uint)calculateHash.Invoke(null, new object[] { 12346, 2048, 0.01f, true, 100, 100, WorldTopologyMode.Wrapped }),
                Is.Not.EqualTo(first));
            Assert.That(
                (uint)calculateHash.Invoke(null, new object[] { 12345, 2048, 0.01f, true, 100, 100, WorldTopologyMode.Infinite }),
                Is.Not.EqualTo(first));

            System.Type snapshotType = gameplayAssembly.GetType(
                "FlatWorld.Networking.Gameplay.NetworkWorldSnapshot",
                throwOnError: true);
            Assert.That(snapshotType.GetField("TopologyMode"), Is.Not.Null);

            string managerSource = File.ReadAllText(
                "Assets/5_Scripts/5-4_Networking/Gameplay/FlatWorldGameNetworkManager.cs");
            Assert.That(managerSource, Does.Contain("hello.Version != NetworkGameplayProtocol.CurrentVersion"));
            Assert.That(managerSource, Does.Contain("snapshot.GenerationProtocol != NetworkMapGenerationProtocol.CurrentVersion"));
        }

        [Test]
        [Category("Networking.Smoke")]
        public void ServerMovementValidationAcceptsLegalSeamCrossingAndClampsIllegalJump()
        {
            System.Reflection.Assembly gameplayAssembly = System.Reflection.Assembly.Load(
                "FlatWorld.Networking.Gameplay");
            System.Type playerType = gameplayAssembly.GetType(
                "FlatWorld.Networking.Gameplay.NetworkWorldPlayer",
                throwOnError: true);
            System.Reflection.MethodInfo validate = playerType.GetMethod(
                "CalculateAcceptedPosition",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.That(validate, Is.Not.Null);

            var planet = new PlanetData
            {
                Radius = 16,
                ChunkSize = new Vector2Int(16, 16),
                TopologyMode = WorldTopologyMode.Wrapped
            };
            var accepted = new Vector3(15f, 0f, 0f);

            Vector3 legal = (Vector3)validate.Invoke(
                null,
                new object[] { accepted, new Vector3(-15f, 0f, 0f), 3f, planet });
            Assert.That(legal, Is.EqualTo(new Vector3(-15f, 0f, 0f)),
                "A two-unit seam crossing must not be rejected as a thirty-unit teleport.");

            Vector3 clamped = (Vector3)validate.Invoke(
                null,
                new object[] { accepted, new Vector3(0f, 0f, 0f), 3f, planet });
            Assert.That(WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds), Is.True);
            Assert.That(bounds.ShortestDelta(accepted, clamped).magnitude, Is.EqualTo(3f).Within(0.0001f));

            string playerSource = File.ReadAllText(
                "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWorldPlayer.cs");
            string streamingSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.Networking.cs");
            Assert.That(playerSource, Does.Contain("WorldTopologyRuntime.ShortestDelta"));
            Assert.That(playerSource, Does.Contain("WorldTopologyRuntime.NormalizePosition"));
            Assert.That(streamingSource, Does.Contain("NormalizeChunkPosition"));
            Assert.That(streamingSource, Does.Contain("WorldTopologyRuntime.ShortestDelta"));
        }

        [Test]
        [Category("Networking.Smoke")]
        public void NetworkPlayerNameLabelIsAuthoredInPrefab()
        {
            const string prefabPath = "Assets/Resources/Networking/FlatWorldNetworkPlayer.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少联机玩家 Prefab：{prefabPath}");
            Assert.That(
                prefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty,
                "FlatWorldNetworkPlayer 只能作为无碰撞网络代理，实体碰撞由核心 Player Prefab 负责。");

            Transform label = prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "玩家名称");
            Assert.That(label, Is.Not.Null, "联机玩家名称必须在 Prefab 中可视化编辑。");
            Assert.That(label.GetComponent("TextMeshPro"), Is.Not.Null, "玩家名称节点缺少 TextMeshPro。 ");

            string source = File.ReadAllText(
                "Assets/5_Scripts/5-4_Networking/Gameplay/NetworkWorldPlayer.cs");
            Assert.That(source, Does.Contain("ItemMgr.Instance.LoadNetworkPlayer"),
                "本地与远程联机角色必须继续复用正式核心 Player Prefab。");
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
            int fallbackPort,
            string expectedHost,
            int expectedPort)
        {
            bool parsed = NetworkConnectionEndpoint.TryParse(
                value,
                checked((ushort)fallbackPort),
                out NetworkConnectionEndpoint endpoint,
                out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(endpoint.Host, Is.EqualTo(expectedHost));
            Assert.That(endpoint.Port, Is.EqualTo(checked((ushort)expectedPort)));
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

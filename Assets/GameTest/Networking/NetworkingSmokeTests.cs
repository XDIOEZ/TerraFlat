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
        [Category("Smoke")]
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
                "Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.Networking.cs");
            Assert.That(playerSource, Does.Contain("WorldTopologyRuntime.ShortestDelta"));
            Assert.That(playerSource, Does.Contain("WorldTopologyRuntime.NormalizePosition"));
            Assert.That(streamingSource, Does.Contain("NormalizeChunkPosition"));
            Assert.That(streamingSource, Does.Contain("WorldTopologyRuntime.ShortestDelta"));
        }



    }
}

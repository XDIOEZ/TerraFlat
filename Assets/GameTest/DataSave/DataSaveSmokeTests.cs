using System.Reflection;
using System.Linq;
using FlatWorld.Gameplay.Progress;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.DataSave
{
    /// <summary>数据存档基础冒烟测试：保护存档入口与权威数据链脚本。</summary>
    public sealed class DataSaveSmokeTests
    {

        [Test]
        [Category("DataSave.Player")]
        public void InitialPlayerPlacementUsesProfileCreationStateInsteadOfSavedPosition()
        {
            MethodInfo requiresInitialPlacement = typeof(GameManager).GetMethod(
                "RequiresInitialPlayerPlacement",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(requiresInitialPlacement, Is.Not.Null);

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Player/Player.prefab");
            Assert.That(playerPrefab, Is.Not.Null);

            GameObject playerObject = Object.Instantiate(playerPrefab);
            playerObject.name = "PlayerPositionPersistenceTest";
            Player player = playerObject.GetComponent<Player>();
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Data, Is.Not.Null);

            try
            {
                player.Data.transform.position = Vector3.zero;
                player.SetProfileContext(
                    localProfile: true,
                    profileDataWasCreated: false,
                    runtimeProfileName: "原角色");
                Assert.That(
                    requiresInitialPlacement.Invoke(null, new object[] { player }),
                    Is.False,
                    "旧玩家保存在世界原点时也必须原样恢复。");
                player.Data.Name_User = "管理员";
                Assert.That(
                    player.ProfileName,
                    Is.EqualTo("原角色"),
                    "显示名或临时管理员身份变化不能改变存档角色键。");

                player.Data.transform.position = new Vector3(17f, -9f, 0f);
                player.SetProfileContext(localProfile: true, profileDataWasCreated: true);
                Assert.That(
                    requiresInitialPlacement.Invoke(null, new object[] { player }),
                    Is.True,
                    "新玩家判定必须来自档案创建状态，而不是坐标值。");
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        [Category("DataSave.Player")]
        public void MainWorldSpawnRoundTripsInsidePlayerSpecialDataWithoutOverwritingOtherNamespaces()
        {
            var playerData = new Data_Player
            {
                ItemSpecialData = "{\"flatworld.tutorial\":{\"stage\":2}}"
            };
            Vector3 expectedPosition = new Vector3(12.5f, -4.5f, 0f);

            Assert.That(
                PlayerMainWorldSpawnStore.SetMainWorldSpawn(
                    playerData,
                    "地球__dimension__cave",
                    expectedPosition),
                Is.True);
            Assert.That(
                PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(
                    playerData,
                    out Vector3 restoredPosition,
                    out string restoredWorldKey),
                Is.True);
            Assert.That(restoredPosition, Is.EqualTo(expectedPosition));
            Assert.That(restoredWorldKey, Is.EqualTo("地球"));
            Assert.That(
                ItemSpecialDataJsonStore.ReadRoot(playerData.ItemSpecialData)
                    .Value<Newtonsoft.Json.Linq.JObject>("flatworld.tutorial")
                    .Value<int>("stage"),
                Is.EqualTo(2));

            Assert.That(
                PlayerMainWorldSpawnStore.EnsureMainWorldSpawn(
                    playerData,
                    "其他星球",
                    new Vector3(99f, 88f, 0f)),
                Is.True,
                "已有出生点时 Ensure 只能保留原值，不能被后续加载覆盖。");
            PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(
                playerData,
                out restoredPosition,
                out restoredWorldKey);
            Assert.That(restoredPosition, Is.EqualTo(expectedPosition));
            Assert.That(restoredWorldKey, Is.EqualTo("地球"));

            var saveContainer = new Ex_ModData_MemoryPackable();
            saveContainer.WriteData<ItemData>(playerData);
            Data_Player restoredPlayerData = saveContainer.GetData<ItemData>() as Data_Player;
            Assert.That(restoredPlayerData, Is.Not.Null);
            Assert.That(
                PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(
                    restoredPlayerData,
                    out restoredPosition,
                    out restoredWorldKey),
                Is.True);
            Assert.That(restoredPosition, Is.EqualTo(expectedPosition));
            Assert.That(restoredWorldKey, Is.EqualTo("地球"));
        }







        [Test]
        [Category("DataSave.Smoke")]
        [Category("Smoke")]
        public void TileStackMapRoundTripKeepsEmptyThroughOverflowCellsAndEnvironment()
        {
            var source = new Data_TileMap
            {
                IDName = "GameTest_TileMap",
                position = new Vector2Int(-8, 12),
                TileLoaded = true
            };
            source.EnsureTileStorage(2, 2);
            source.EnsureEnvironmentStorage(2, 2);
            source.SetEnvironmentAtLocal(1, 1, 0.25f, 12.5f, 0.75f, 0.6f);
            source.SetLightAtLocal(1, 1, 0.4f);
            source.SetWindAtLocal(1, 1, new Vector2(-0.8f, 0.6f));
            Vector2Int oneLayer = source.position + new Vector2Int(1, 0);
            Vector2Int twoLayers = source.position + new Vector2Int(0, 1);
            Vector2Int fourLayers = source.position + new Vector2Int(1, 1);
            source.SetBaseTile(oneLayer, NewTile("one"));
            source.SetBaseTile(twoLayers, NewTile("two_base"));
            source.PushTile(twoLayers, NewTile("two_overlay"));
            source.SetBaseTile(fourLayers, NewTile("four_0"));
            source.PushTile(fourLayers, NewTile("four_1"));
            source.PushTile(fourLayers, NewTile("four_2"));
            source.PushTile(fourLayers, NewTile("four_3"));
            source.TrySetGrassStateAtWorld(fourLayers, GrassCellState.Present);

            var container = new Ex_ModData_MemoryPackable();
            container.WriteData<ItemData>(source);
            ItemData restoredBase = container.GetData<ItemData>();

            Assert.That(restoredBase, Is.TypeOf<Data_TileMap>());
            Data_TileMap restored = (Data_TileMap)restoredBase;
            Assert.That(restored.position, Is.EqualTo(source.position));
            Assert.That(restored.TileLoaded, Is.True);
            Assert.That(restored.Width, Is.EqualTo(2));
            Assert.That(restored.Height, Is.EqualTo(2));
            Assert.That(restored.GetLayerCount(source.position), Is.Zero);
            Assert.That(restored.GetLayerCount(oneLayer), Is.EqualTo(1));
            Assert.That(restored.GetLayerCount(twoLayers), Is.EqualTo(2));
            Assert.That(restored.GetLayerCount(fourLayers), Is.EqualTo(4));
            Assert.That(restored.GetTileAt(fourLayers, 0).ID, Is.EqualTo("four_0"));
            Assert.That(restored.GetTileAt(fourLayers, 3).ID, Is.EqualTo("four_3"));
            Assert.That(restored.CountNonEmptyCells(), Is.EqualTo(3));
            Assert.That(restored.CountOverflowAllocations(), Is.EqualTo(1));
            Assert.That(restored.EnvironmentLayers.IsValidSize(2, 2), Is.True);
            Assert.That(restored.EnvironmentLayers.Temperature[1, 1], Is.EqualTo(0.25f));
            Assert.That(restored.EnvironmentLayers.TemperatureCelsius[1, 1], Is.EqualTo(12.5f));
            Assert.That(restored.EnvironmentLayers.Precipitation[1, 1], Is.EqualTo(0.75f));
            Assert.That(restored.EnvironmentLayers.Height[1, 1], Is.EqualTo(0.6f));
            Assert.That(restored.EnvironmentLayers.Light[1, 1], Is.EqualTo(0.4f));
            Assert.That(restored.EnvironmentLayers.WindX[1, 1], Is.EqualTo(-0.8f).Within(0.000001f));
            Assert.That(restored.EnvironmentLayers.WindY[1, 1], Is.EqualTo(0.6f).Within(0.000001f));
            Assert.That(restored.TryGetGrassStateAtWorld(fourLayers, out GrassCellState grass), Is.True);
            Assert.That(grass, Is.EqualTo(GrassCellState.Present));
        }


        [Test]
        [Category("DataSave.Weather")]
        public void WeatherEventStateRoundTripsThroughMemoryPackContainer()
        {
            var source = new PlanetData
            {
                Name = "测试星球",
                CurrentWeather = WeatherType.Rain,
                WeatherIntensity = 0.65f,
                WeatherDataVersion = WeatherEventScheduler.CurrentDataVersion,
                WeatherPhase = WeatherPhase.RainSteady,
                WeatherPhaseStartedTotalTime = 120f,
                WeatherPhaseEndTotalTime = 360f,
                NextWeatherEventTotalTime = 0f,
                WeatherRandomCursor = 7,
                WeatherEventSequence = 3
            };
            var moduleData = new Ex_ModData_MemoryPackable();
            moduleData.WriteData(source);

            PlanetData restored = new PlanetData();
            moduleData.ReadData(ref restored);

            Assert.That(restored.CurrentWeather, Is.EqualTo(WeatherType.Rain));
            Assert.That(restored.WeatherIntensity, Is.EqualTo(0.65f));
            Assert.That(restored.WeatherPhase, Is.EqualTo(WeatherPhase.RainSteady));
            Assert.That(restored.WeatherPhaseStartedTotalTime, Is.EqualTo(120f));
            Assert.That(restored.WeatherPhaseEndTotalTime, Is.EqualTo(360f));
            Assert.That(restored.WeatherRandomCursor, Is.EqualTo(7));
            Assert.That(restored.WeatherEventSequence, Is.EqualTo(3));
        }

        private static TileData NewTile(string id)
        {
            return new TileData_Universal
            {
                ID = id,
                Name = id,
                IsWalkable = true
            };
        }

        private static int ReadPrivateVersion(string fieldName)
        {
            FieldInfo field = typeof(SaveDataMgr).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (int)field.GetRawConstantValue();
        }

        private static byte[] Prefix(byte[] prefix, byte[] body)
        {
            var payload = new byte[prefix.Length + body.Length];
            System.Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
            System.Buffer.BlockCopy(body, 0, payload, prefix.Length, body.Length);
            return payload;
        }

        private static void AssertIncompatible(MethodInfo method, SaveDataMgr manager, byte[] payload)
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(manager, new object[] { payload }));
            Assert.That(exception.InnerException, Is.TypeOf<SaveVersionIncompatibleException>());
            Assert.That(exception.InnerException.Message, Does.Contain("迁移"));
            Assert.That(exception.InnerException.Message, Does.Contain("覆盖"));
            Assert.That(exception.InnerException.Message, Does.Contain("删除"));
        }
    }
}
